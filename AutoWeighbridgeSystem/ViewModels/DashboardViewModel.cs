using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;
        private readonly ScaleService _scaleService;
        private readonly RfidMultiService _rfidService;
        private readonly RfidBusinessService _rfidBusiness;
        private readonly AlarmService _alarmService;
        private readonly WeighingBusinessService _weighingBusiness;
        private readonly AppSession _appSession;

        // --- CẤU HÌNH & TRẠNG THÁI ---
        private decimal _minWeightThreshold = 200;
        private int _rfidCooldownSeconds = 3;
        private int _queueTimeoutSeconds = 45;
        private int _hardwareWatchdogSeconds = 15;

        private DateTime _lastScaleDataReceivedTime = DateTime.Now;
        private readonly System.Collections.Generic.Dictionary<string, DateTime> _rfidCooldowns = new();
        private CancellationTokenSource _pendingTimeoutCts;
        private CancellationTokenSource _watchdogCts;
        private bool _isProcessingSave = false;

        // --- HÀNG CHỜ PENDING ---
        private string _pendingLicensePlate;
        private string _pendingCustomerName;
        private string _pendingProductName;
        private int _pendingVehicleId;
        private bool _hasPendingVehicle = false;

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
            RfidBusinessService rfidBusiness,
            AlarmService alarmService,
            WeighingBusinessService weighingBusiness,
            AppSession appSession)
        {
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
            _scaleService = scaleService;
            _rfidService = rfidService;
            _rfidBusiness = rfidBusiness;
            _alarmService = alarmService;
            _weighingBusiness = weighingBusiness;
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
            if (decimal.TryParse(_configuration["ScaleSettings:MinWeightThreshold"], out decimal pw)) _minWeightThreshold = pw;
            if (int.TryParse(_configuration["ScaleSettings:RfidCooldownSeconds"], out int cd)) _rfidCooldownSeconds = cd;
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

                int vId = _hasPendingVehicle ? _pendingVehicleId : (SelectedVehicle?.VehicleId ?? 0);
                if (vId == 0 && !string.IsNullOrEmpty(LicensePlate))
                {
                    var matched = VehicleList.FirstOrDefault(v => v.LicensePlate.Equals(LicensePlate, StringComparison.OrdinalIgnoreCase));
                    if (matched != null) vId = matched.VehicleId;
                }

                var result = await _weighingBusiness.ProcessWeighingAsync(
                    LicensePlate, vId, CustomerName, ProductName, LockedWeight, IsOnePassMode);

                if (result.IsSuccess)
                {
                    ShowCameraMessage($"🔒 ĐÃ CHỐT: {LockedWeight:N0} KG\n{result.Message}", false);
                    _ = _alarmService.TriggerAlarmAsync();
                    await LoadRecentTicketsAsync();

                    await Task.Delay(2000);
                    ResetForm();
                }
                else
                {
                    MessageBox.Show(result.Message, "Cảnh Báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                    IsWeightLocked = false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Lỗi thực thi giao dịch cân");
                IsWeightLocked = false;
            }
            finally
            {
                _isProcessingSave = false;
            }
        }

        private void OnScaleWeightChanged(decimal weight, bool isStable)
        {
            _lastScaleDataReceivedTime = DateTime.Now;

            if (weight < _minWeightThreshold)
            {
                if (_hasPendingVehicle && !IsWeightLocked)
                {
                    Application.Current?.Dispatcher.InvokeAsync(() =>
                    {
                        ClearPendingData();
                        ResetForm();
                        ShowCameraMessage("CÂN VỀ KHÔNG - HỦY LỆNH CHỜ");
                    });
                }
                return;
            }

            if (IsAutoMode && isStable && _hasPendingVehicle && !_isProcessingSave && !IsWeightLocked)
            {
                Application.Current?.Dispatcher.InvokeAsync(async () =>
                {
                    if (_isProcessingSave || IsWeightLocked) return;
                    LicensePlate = _pendingLicensePlate;
                    CustomerName = _pendingCustomerName;
                    ProductName = _pendingProductName;
                    ClearPendingData();
                    await ProcessAndSaveWeighingAsync(weight);
                });
            }
        }

        private void OnRfidCardRead(string readerRole, string cardId)
        {
            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                if (_rfidCooldowns.TryGetValue(readerRole, out DateTime lastRead))
                    if ((DateTime.Now - lastRead).TotalSeconds < _rfidCooldownSeconds) return;

                _rfidCooldowns[readerRole] = DateTime.Now;

                if (readerRole == ReaderRoles.Desk) return;

                RfidInput = cardId;
                RfidLocationLabel = $"Mã thẻ RFID (Nhận từ {readerRole}):";

                if (!IsAutoMode) { ShowCameraMessage("CHẾ ĐỘ TAY - BỎ QUA THẺ!"); return; }

                await ProcessWeighbridgeRfidAsync(cardId);
            });
        }

        private async Task ProcessWeighbridgeRfidAsync(string cardId)
        {
            try
            {
                var rfidResult = await _rfidBusiness.ProcessRawCardAsync(cardId);

                if (!rfidResult.IsSuccess || rfidResult.IsNewCard)
                {
                    ShowCameraMessage($"THẺ {rfidResult.CleanCardId} CHƯA ĐĂNG KÝ!");
                    return;
                }

                var vehicle = rfidResult.ExistingVehicle;
                _pendingLicensePlate = vehicle.LicensePlate;
                _pendingCustomerName = vehicle.Customer?.CustomerName ?? "Khách lẻ";
                _pendingProductName = SelectedProduct?.ProductName ?? "Hàng hóa";
                _pendingVehicleId = vehicle.VehicleId;
                _hasPendingVehicle = true;

                if (IsScaleStable && _scaleService.CurrentWeight >= _minWeightThreshold)
                {
                    LicensePlate = _pendingLicensePlate;
                    CustomerName = _pendingCustomerName;
                    ProductName = _pendingProductName;
                    ClearPendingData();
                    await ProcessAndSaveWeighingAsync(_scaleService.CurrentWeight);
                }
                else
                {
                    ShowCameraMessage($"NHẬN XE: {_pendingLicensePlate}. ĐỢI ỔN ĐỊNH...", false);
                    StartPendingTimeout();
                }
            }
            catch (Exception ex) { Log.Error(ex, "Lỗi xử lý RFID"); }
        }

        [RelayCommand]
        private void ToggleWeightLock()
        {
            if (!IsWeightLocked)
            {
                if (!IsScaleStable) { MessageBox.Show("Cân đang dao động, vui lòng đợi ổn định!", "Cảnh báo"); return; }
                if (_scaleService.CurrentWeight < _minWeightThreshold) return;

                LockedWeight = _scaleService.CurrentWeight;
                IsWeightLocked = true;
                ShowCameraMessage($"🔒 ĐÃ CHỐT: {LockedWeight:N0} KG", false);
            }
            else { IsWeightLocked = false; LockedWeight = 0; }
        }

        [RelayCommand]
        private async Task ManualSaveAsync()
        {
            if (IsAutoMode) { MessageBox.Show("Vui lòng tắt chế độ AUTO!"); return; }
            if (string.IsNullOrWhiteSpace(LicensePlate)) { MessageBox.Show("Vui lòng nhập Biển số xe!"); return; }
            if (!IsWeightLocked || LockedWeight <= 0) { MessageBox.Show("Vui lòng chốt số cân!"); return; }

            if (MessageBox.Show($"Lưu phiếu cho xe {LicensePlate} - {LockedWeight:N0} kg?", "Xác nhận", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;
            await ProcessAndSaveWeighingAsync(LockedWeight);
        }

        [RelayCommand]
        private async Task CancelTicketAsync()
        {
            if (SelectedRecentTicket == null || SelectedRecentTicket.IsVoid) return;
            if (MessageBox.Show("Xác nhận HỦY phiếu?", "Hủy", MessageBoxButton.YesNo) != MessageBoxResult.Yes) return;

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
            catch (Exception ex) { Log.Error(ex, "Lỗi hủy phiếu"); }
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
                using var db = _dbContextFactory.CreateDbContext();
                var vehicles = await db.Vehicles.AsNoTracking().Include(v => v.Customer).Where(v => !v.IsDeleted).ToListAsync();
                var customers = await db.Customers.AsNoTracking().ToListAsync();
                var products = await db.Products.AsNoTracking().ToListAsync();

                Application.Current?.Dispatcher.Invoke(() =>
                {
                    VehicleList = new ObservableCollection<Vehicle>(vehicles);
                    CustomerList = new ObservableCollection<Customer>(customers);
                    ProductList = new ObservableCollection<Product>(products);

                    string defProd = _configuration["ScaleSettings:DefaultProductName"] ?? "Đá xô bồ";
                    var prod = ProductList.FirstOrDefault(p => p.ProductName.Equals(defProd, StringComparison.OrdinalIgnoreCase));
                    if (prod != null) { SelectedProduct = prod; ProductName = prod.ProductName; }
                    else ProductName = defProd;
                });
            }
            catch (Exception ex) { Log.Error(ex, "Lỗi tải danh mục"); }
        }

        public async Task LoadRecentTicketsAsync()
        {
            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var tickets = await db.WeighingTickets.IgnoreQueryFilters().AsNoTracking()
                    .OrderByDescending(t => t.TimeIn).Take(15).ToListAsync();
                Application.Current?.Dispatcher.Invoke(() => RecentTickets = new ObservableCollection<WeighingTicket>(tickets));
            }
            catch (Exception ex) { Log.Error(ex, "Lỗi tải nhật ký"); }
        }

        private void InitializeCamera()
        {
            string url = _configuration["CameraSettings:RtspUrl"];
            if (!string.IsNullOrEmpty(url)) CameraUri = new Uri(url);
        }

        private void StartHardwareWatchdog()
        {
            _watchdogCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                while (!_watchdogCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(_hardwareWatchdogSeconds), _watchdogCts.Token);
                    if ((DateTime.Now - _lastScaleDataReceivedTime).TotalSeconds > _hardwareWatchdogSeconds)
                        ShowCameraMessage("⚠️ MẤT TÍN HIỆU ĐẦU CÂN!", false);
                }
            }, _watchdogCts.Token);
        }

        private void ResetForm()
        {
            LicensePlate = ""; CustomerName = ""; RfidInput = "";
            SelectedVehicle = null; SelectedCustomer = null;
            IsWeightLocked = false; LockedWeight = 0;
            ClearPendingData();
        }

        private void ClearPendingData() { _pendingLicensePlate = null; _hasPendingVehicle = false; _pendingTimeoutCts?.Cancel(); }

        private void StartPendingTimeout()
        {
            _pendingTimeoutCts?.Cancel(); _pendingTimeoutCts = new CancellationTokenSource();
            Task.Run(async () =>
            {
                try { await Task.Delay(TimeSpan.FromSeconds(_queueTimeoutSeconds), _pendingTimeoutCts.Token); ClearPendingData(); } catch { }
            }, _pendingTimeoutCts.Token);
        }

        private void ShowCameraMessage(string msg, bool autoHide = true)
        {
            CameraStatus = msg;
            if (autoHide) Task.Run(async () => { await Task.Delay(3000); CameraStatus = "Camera Online"; });
        }

        public void Dispose()
        {
            _watchdogCts?.Cancel();
            _pendingTimeoutCts?.Cancel();
            _uiRefreshTimer?.Stop();
            _scaleService.WeightChanged -= OnScaleWeightChanged;
            _rfidService.CardRead -= OnRfidCardRead;
        }
    }
}