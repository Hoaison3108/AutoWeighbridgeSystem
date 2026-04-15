using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Common;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;
        private readonly ScaleService _scaleService;
        private readonly RfidMultiService _rfidService;
        private readonly DashboardWorkflowService _dashboardWorkflow;
        private readonly DashboardSaveService _dashboardSaveService;
        private readonly DashboardDataService _dashboardDataService;
        private readonly HardwareWatchdogService _hardwareWatchdogService;
        private readonly IUserNotificationService _notificationService;
        private readonly AlarmService _alarmService;
        private readonly AppSession _appSession;

        // --- CẤU HÌNH & TRẠNG THÁI ---
        private int _queueTimeoutSeconds = 45;
        private int _hardwareWatchdogSeconds = 15;

        private bool _isProcessingSave = false;

        public Unosquare.FFME.MediaElement VideoPlayer { get; set; }

        // --- PROPERTIES BINDING ---
        [ObservableProperty] private string _weightDisplay = "0";
        [ObservableProperty] private bool _isScaleStable = false;

        [ObservableProperty] private string _licensePlate = "";
        [ObservableProperty] private string _customerName = "";
        [ObservableProperty] private string _productName = "";
        [ObservableProperty] private string _rfidInput = "";
        [ObservableProperty] private string _rfidLocationLabel = "Mã thẻ RFID (Đang chờ...):";

        [ObservableProperty] private bool _isAutoMode = true;
        [ObservableProperty] private bool _isOnePassMode = false;
        [ObservableProperty] private Uri _cameraUri;
        [ObservableProperty] private string _cameraStatus = "Camera Online";

        [ObservableProperty] private ObservableCollection<WeighingTicket> _recentTickets = new();
        [ObservableProperty] private WeighingTicket _selectedRecentTicket;

        [ObservableProperty] private ObservableCollection<Product> _productList = new();
        [ObservableProperty] private Product _selectedProduct;
        [ObservableProperty] private ObservableCollection<Vehicle> _vehicleList = new();
        [ObservableProperty] private ObservableCollection<Customer> _customerList = new();
        [ObservableProperty] private Vehicle _selectedVehicle;
        [ObservableProperty] private Customer _selectedCustomer;

        [ObservableProperty] private decimal _lockedWeight = 0;
        [ObservableProperty] private bool _isWeightLocked = false;

        // === TIMER CẬP NHẬT UI ===
        private DispatcherTimer _uiRefreshTimer;
        private const int RefreshIntervalMs = 40;

        public DashboardViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            IConfiguration configuration,
            ScaleService scaleService,
            RfidMultiService rfidService,
            DashboardWorkflowService dashboardWorkflow,
            DashboardSaveService dashboardSaveService,
            DashboardDataService dashboardDataService,
            HardwareWatchdogService hardwareWatchdogService,
            IUserNotificationService notificationService,
            AlarmService alarmService,
            AppSession appSession)
        {
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
            _scaleService = scaleService;
            _rfidService = rfidService;
            _dashboardWorkflow = dashboardWorkflow;
            _dashboardSaveService = dashboardSaveService;
            _dashboardDataService = dashboardDataService;
            _hardwareWatchdogService = hardwareWatchdogService;
            _notificationService = notificationService;
            _alarmService = alarmService;
            _appSession = appSession;

            LoadConfiguration();

            _scaleService.WeightChanged += OnScaleWeightChanged;
            _rfidService.CardRead += OnRfidCardRead;

            InitializeCamera();
            InitializeUiRefreshTimer();

            _ = LoadRecentTicketsAsync();
            _ = LoadInitialDataAsync();
            StartHardwareWatchdog();
        }

        private void InitializeUiRefreshTimer()
        {
            _uiRefreshTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(RefreshIntervalMs),
                IsEnabled = true
            };
            _uiRefreshTimer.Tick += UiRefreshTimer_Tick;
        }

        private void UiRefreshTimer_Tick(object sender, EventArgs e)
        {
            if (IsWeightLocked) return;
            WeightDisplay = _scaleService.CurrentWeight.ToString("N0");
            IsScaleStable = _scaleService.IsScaleStable;
        }

        private void LoadConfiguration()
        {
            if (int.TryParse(_configuration["ScaleSettings:QueueTimeoutSeconds"], out int qt)) _queueTimeoutSeconds = qt;
            if (int.TryParse(_configuration["ScaleSettings:HardwareWatchdogSeconds"], out int wd)) _hardwareWatchdogSeconds = wd;

            if (bool.TryParse(_configuration["ScaleSettings:DefaultToAutoMode"], out bool isAuto)) IsAutoMode = isAuto;
            if (bool.TryParse(_configuration["ScaleSettings:DefaultToOnePassMode"], out bool isOnePass)) IsOnePassMode = isOnePass;
        }

        partial void OnSelectedVehicleChanged(Vehicle value)
        {
            if (value == null) return;
            LicensePlate = value.LicensePlate;
            if (value.Customer != null)
            {
                SelectedCustomer = CustomerList.FirstOrDefault(c => c.CustomerId == value.CustomerId);
                CustomerName = value.Customer.CustomerName;
            }
        }

        partial void OnSelectedCustomerChanged(Customer value)
        {
            if (value != null) CustomerName = value.CustomerName;
        }

        partial void OnSelectedProductChanged(Product value)
        {
            if (value != null) ProductName = value.ProductName;
        }

        private async Task ProcessAndSaveWeighingAsync(decimal finalWeight)
        {
            if (_isProcessingSave) return;
            _isProcessingSave = true;

            try
            {
                LockedWeight = finalWeight;
                IsWeightLocked = true;
                WeightDisplay = LockedWeight.ToString("N0");

                var request = new DashboardSaveRequest(
                    LicensePlate,
                    SelectedVehicle?.VehicleId,
                    VehicleList,
                    CustomerName,
                    ProductName,
                    LockedWeight,
                    IsOnePassMode);
                var result = await _dashboardSaveService.ExecuteSaveAsync(request);

                if (result.IsSuccess)
                {
                    ShowCameraMessage($"🔒 ĐÃ CHỐT: {result.FinalWeight:N0} KG\n{result.Message}", false);
                    await LoadRecentTicketsAsync();

                    await Task.Delay(2000);
                    ResetForm();
                }
                else
                {
                    if (!result.HasException)
                        _notificationService.ShowWarning(result.Message);
                    IsWeightLocked = false;
                }
            }
            catch (Exception)
            {
                IsWeightLocked = false;
            }
            finally
            {
                _isProcessingSave = false;
            }
        }

        private void OnScaleWeightChanged(decimal weight, bool isStable)
        {
            _hardwareWatchdogService.NotifyScaleDataReceived();
            var decision = _dashboardWorkflow.EvaluateScaleEvent(weight, isStable, IsAutoMode, _isProcessingSave, IsWeightLocked);
            if (!decision.ShouldClearPendingAndReset && !decision.ShouldSave) return;

            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                if (decision.ShouldClearPendingAndReset)
                {
                    ResetForm();
                    ShowCameraMessage(decision.CameraMessage);
                    return;
                }

                if (_isProcessingSave || IsWeightLocked) return;

                LicensePlate = decision.PendingVehicle.LicensePlate;
                CustomerName = decision.PendingVehicle.CustomerName;
                ProductName = decision.PendingVehicle.ProductName;
                await ProcessAndSaveWeighingAsync(decision.WeightToSave);
            });
        }

        private void OnRfidCardRead(string readerRole, string cardId)
        {
            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                if (_dashboardWorkflow.ShouldIgnoreRfidRead(readerRole)) return;

                if (readerRole == ReaderRoles.Desk) return;

                RfidInput = cardId;
                RfidLocationLabel = $"Mã thẻ RFID (Nhận từ {readerRole}):";

                var selectedProductName = SelectedProduct?.ProductName ?? "Hàng hóa";
                var decision = await _dashboardWorkflow.EvaluateRfidEventAsync(
                    cardId,
                    selectedProductName,
                    IsAutoMode,
                    IsScaleStable,
                    _scaleService.CurrentWeight);

                if (decision.ShouldShowMessage)
                    ShowCameraMessage(decision.CameraMessage, decision.MessageAutoHide);

                if (decision.ShouldStartPendingTimeout)
                    StartPendingTimeout();

                if (decision.ShouldSave)
                {
                    LicensePlate = decision.PendingVehicle.LicensePlate;
                    CustomerName = decision.PendingVehicle.CustomerName;
                    ProductName = decision.PendingVehicle.ProductName;
                    await ProcessAndSaveWeighingAsync(decision.WeightToSave);
                }
            });
        }

        [RelayCommand]
        private void ToggleWeightLock()
        {
            if (!IsWeightLocked)
            {
                if (!IsScaleStable) { _notificationService.ShowWarning(UiText.Messages.UnstableScaleWarning, UiText.Titles.Warning); return; }
                if (_scaleService.CurrentWeight < _dashboardWorkflow.MinWeightThreshold) return;

                LockedWeight = _scaleService.CurrentWeight;
                IsWeightLocked = true;
                ShowCameraMessage($"🔒 ĐÃ CHỐT: {LockedWeight:N0} KG", false);
            }
            else { IsWeightLocked = false; LockedWeight = 0; }
        }

        [RelayCommand]
        private async Task ManualSaveAsync()
        {
            if (IsAutoMode) { _notificationService.ShowWarning(UiText.Messages.ManualModeDisableAuto); return; }
            if (string.IsNullOrWhiteSpace(LicensePlate)) { _notificationService.ShowWarning(UiText.Messages.EnterLicensePlate); return; }
            if (!IsWeightLocked || LockedWeight <= 0) { _notificationService.ShowWarning(UiText.Messages.LockWeightBeforeSave); return; }

            if (!_notificationService.Confirm(UiText.Messages.SaveTicketConfirm(LicensePlate, LockedWeight), UiText.Titles.Confirm)) return;
            await ProcessAndSaveWeighingAsync(LockedWeight);
        }

        [RelayCommand]
        private async Task CancelTicketAsync()
        {
            if (SelectedRecentTicket == null || SelectedRecentTicket.IsVoid) return;
            if (!_notificationService.Confirm(UiText.Messages.CancelTicketConfirm, UiText.Titles.Cancel)) return;

            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var ticket = await db.WeighingTickets.IgnoreQueryFilters().FirstOrDefaultAsync(t => t.TicketID == SelectedRecentTicket.TicketID);
                if (ticket != null)
                {
                    ticket.IsVoid = true;
                    ticket.VoidReason = "Hủy thủ công";
                    db.WeighingTickets.Update(ticket);
                    await db.SaveChangesAsync();
                    ShowCameraMessage($"ĐÃ HỦY: {ticket.TicketID}");
                    await LoadRecentTicketsAsync();
                }
            }
            catch (Exception ex) { _notificationService.LogError(ex, "Lỗi hủy phiếu"); }
        }

        [RelayCommand]
        private async Task TriggerManualAlarmAsync() => await _alarmService.TriggerAlarmAsync();

        [RelayCommand]
        private void RefreshForm()
        {
            ResetForm();
            ShowCameraMessage("♻️ ĐÃ LÀM SẠCH BIỂU MẪU");
        }

        private async Task LoadInitialDataAsync()
        {
            try
            {
                var initialData = await _dashboardDataService.LoadInitialDataAsync();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    VehicleList = new ObservableCollection<Vehicle>(initialData.Vehicles);
                    CustomerList = new ObservableCollection<Customer>(initialData.Customers);
                    ProductList = new ObservableCollection<Product>(initialData.Products);

                    string defProd = initialData.DefaultProductName;
                    var prod = ProductList.FirstOrDefault(p => p.ProductName.Equals(defProd, StringComparison.OrdinalIgnoreCase));
                    if (prod != null) { SelectedProduct = prod; ProductName = prod.ProductName; }
                    else ProductName = defProd;
                });
            }
            catch (Exception ex) { _notificationService.LogError(ex, "Lỗi tải danh mục"); }
        }

        public async Task LoadRecentTicketsAsync()
        {
            try
            {
                var tickets = await _dashboardDataService.LoadRecentTicketsAsync();
                Application.Current?.Dispatcher.Invoke(() => RecentTickets = new ObservableCollection<WeighingTicket>(tickets));
            }
            catch (Exception ex) { _notificationService.LogError(ex, "Lỗi tải nhật ký"); }
        }

        private void InitializeCamera()
        {
            string url = _configuration["CameraSettings:RtspUrl"];
            if (!string.IsNullOrEmpty(url)) CameraUri = new Uri(url);
        }

        private void StartHardwareWatchdog()
        {
            _hardwareWatchdogService.StartHardwareWatchdog(
                _hardwareWatchdogSeconds,
                () => ShowCameraMessage("⚠️ MẤT TÍN HIỆU ĐẦU CÂN!", false));
        }

        private void ResetForm()
        {
            LicensePlate = ""; CustomerName = ""; RfidInput = "";
            SelectedVehicle = null; SelectedCustomer = null;
            IsWeightLocked = false; LockedWeight = 0;
            _dashboardWorkflow.ClearPendingData();
            _hardwareWatchdogService.CancelPendingTimeout();
        }

        private void StartPendingTimeout()
        {
            _hardwareWatchdogService.StartPendingTimeout(
                _queueTimeoutSeconds,
                () => _dashboardWorkflow.ClearPendingData());
        }

        private void ShowCameraMessage(string msg, bool autoHide = true)
        {
            _notificationService.ShowCameraStatus(s => CameraStatus = s, msg, autoHide);
        }

        public void Dispose()
        {
            _hardwareWatchdogService.StopAll();
            _uiRefreshTimer?.Stop();
            _scaleService.WeightChanged -= OnScaleWeightChanged;
            _rfidService.CardRead -= OnRfidCardRead;
        }
    }
}