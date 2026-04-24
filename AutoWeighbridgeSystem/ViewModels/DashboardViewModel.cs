using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Data;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AutoWeighbridgeSystem.Common;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class DashboardViewModel : ObservableObject, IDisposable
    {
        // =========================================================================
        // DEPENDENCIES
        // =========================================================================
        private readonly IConfiguration _configuration;
        private readonly ScaleService _scaleService;
        private readonly DashboardEventCoordinator _coordinator;
        private readonly DashboardSaveService _dashboardSaveService;
        private readonly DashboardDataService _dashboardDataService;
        private readonly WeighingBusinessService _weighingBusiness;
        private readonly IUserNotificationService _notificationService;
        private readonly AlarmService _alarmService;
        private readonly AppSession _appSession;
        private readonly BackgroundAutomationService _automationService;
        
        public SystemClockService Clock { get; }
        private readonly Func<string, QuickVehicleRegisterViewModel> _quickRegisterVmFactory;

        // =========================================================================
        // TRẠNG THÁI NỘI BỘ
        // =========================================================================
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);
        private readonly Action _updateWeightDisplayAction;
        private decimal _pendingDisplayWeight;
        private volatile bool _pendingDisplayStable;

        // =========================================================================
        // OBSERVABLE PROPERTIES
        // =========================================================================
        [ObservableProperty] private string _weightDisplay = "0";
        [ObservableProperty] private bool _isScaleStable = false;
        [ObservableProperty] private decimal _lockedWeight = 0;

        private volatile bool _isWeightLockedVolatile = false;
        public bool IsWeightLocked
        {
            get => _isWeightLockedVolatile;
            set { if (SetProperty(ref _isWeightLockedVolatile, value)) OnPropertyChanged(nameof(IsManualMode)); }
        }

        [ObservableProperty] private string _licensePlate = "";
        [ObservableProperty] private string _customerName = "";
        [ObservableProperty] private string _productName = "";
        [ObservableProperty] private string _rfidInput = "";
        [ObservableProperty] private string _rfidLocationLabel = "Mã thẻ RFID (Đang chờ...):";

        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(CanManualSave))]
        [NotifyPropertyChangedFor(nameof(IsManualMode))]
        private bool _isAutoMode = true;
        [ObservableProperty] private bool _isOnePassMode = false;

        [ObservableProperty] private Uri _cameraUri;
        [ObservableProperty] private string _cameraStatus = "Camera Online";
        [ObservableProperty] private string _cameraMessage = "";
        [ObservableProperty] [NotifyPropertyChangedFor(nameof(CanManualSave))] private bool _isSaving = false;

        [ObservableProperty] private HardwareConnectionStatus _scaleConnectionStatus = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _rfidInStatus = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _rfidOutStatus = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _rfidDeskStatus = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _cameraConnectionStatus = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _alarmStatus = HardwareConnectionStatus.Offline;

        [ObservableProperty] private ObservableCollection<WeighingTicket> _recentTickets = new();
        [ObservableProperty] private WeighingTicket _selectedRecentTicket;

        public AutocompleteProvider<string> VehicleAutocomplete { get; } = new(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));
        public AutocompleteProvider<string> CustomerAutocomplete { get; } = new(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));
        public AutocompleteProvider<string> ProductAutocomplete { get; } = new(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));

        public bool IsManualMode => !IsAutoMode;
        public bool CanManualSave => !IsAutoMode && !IsSaving;

        // =========================================================================
        // CONSTRUCTOR
        // =========================================================================
        public DashboardViewModel(
            IConfiguration configuration,
            ScaleService scaleService,
            DashboardEventCoordinator coordinator,
            DashboardSaveService dashboardSaveService,
            DashboardDataService dashboardDataService,
            WeighingBusinessService weighingBusiness,
            IUserNotificationService notificationService,
            AlarmService alarmService,
            AppSession appSession,
            SystemClockService clock,
            BackgroundAutomationService automationService,
            Func<string, QuickVehicleRegisterViewModel> quickRegisterVmFactory)
        {
            _configuration = configuration;
            _scaleService = scaleService;
            _coordinator = coordinator;
            _dashboardSaveService = dashboardSaveService;
            _dashboardDataService = dashboardDataService;
            _weighingBusiness = weighingBusiness;
            _notificationService = notificationService;
            _alarmService = alarmService;
            _appSession = appSession;
            Clock = clock;
            _automationService = automationService;
            _quickRegisterVmFactory = quickRegisterVmFactory;

            _updateWeightDisplayAction = () => {
                WeightDisplay = _pendingDisplayWeight.ToString("N0");
                IsScaleStable = _pendingDisplayStable;
            };

            LoadUiConfiguration();
            InitializeCamera();
            
            _scaleService.WeightChanged += OnScaleWeightChangedUiUpdate;
            
            // Subscribe events từ Coordinator
            _coordinator.FormResetRequested += OnFormResetRequested;
            _coordinator.CameraMessageRequested += OnCameraMessageRequested;
            _coordinator.RfidCaptured += OnRfidCaptured;
            _coordinator.PendingTimeoutStartRequested += OnPendingTimeoutStartRequested;
            _coordinator.HardwareStatusChanged += OnHardwareStatusChanged;
            _alarmService.HardwareStatusChanged += OnAlarmHardwareStatusChanged;

            // Lắng nghe tín hiệu khi Service ngầm lưu xong dữ liệu
            _automationService.DataChanged += OnBackgroundDataChanged;

            _coordinator.Start(
                getIsAutoMode: () => IsAutoMode,
                getIsWeightLocked: () => IsWeightLocked,
                getIsProcessingSave: () => _saveLock.CurrentCount == 0,
                getSelectedProductName: () => !string.IsNullOrEmpty(ProductName) ? ProductName : "Hàng hóa",
                getIsOnePassMode: () => IsOnePassMode);

            _ = LoadInitialDataAsync();
            _ = LoadRecentTicketsAsync();
            _alarmService.Initialize();
        }

        private void LoadUiConfiguration()
        {
            if (bool.TryParse(_configuration["ScaleSettings:DefaultToAutoMode"], out bool isAuto)) IsAutoMode = isAuto;
            if (bool.TryParse(_configuration["ScaleSettings:DefaultToOnePassMode"], out bool isOnePass)) IsOnePassMode = isOnePass;
        }

        private void InitializeCamera()
        {
            string url = _configuration["CameraSettings:RtspUrl"];
            if (!string.IsNullOrEmpty(url)) CameraUri = new Uri(url);
        }

        private void OnScaleWeightChangedUiUpdate(decimal weight, bool isStable)
        {
            if (IsWeightLocked) return;
            _pendingDisplayWeight = weight;
            _pendingDisplayStable = isStable;
            Application.Current?.Dispatcher.BeginInvoke(_updateWeightDisplayAction, DispatcherPriority.DataBind);
        }

        private void OnBackgroundDataChanged()
        {
            _ = LoadRecentTicketsAsync();
            
            // Đảm bảo làm sạch form khi dịch vụ ngầm đã lưu xong phiếu (đặc biệt cho Auto Mode)
            // Phải chạy trên UI Thread vì ResetForm thay đổi các thuộc tính Observable
            Application.Current?.Dispatcher.InvokeAsync(() => {
                ResetForm();
            });
        }

        private void OnFormResetRequested(string message) { ResetForm(); ShowCameraMessage(message); }
        private void OnCameraMessageRequested(string message, bool autoHide) => ShowCameraMessage(message, autoHide);
        private void OnRfidCaptured(string cardId, string locationLabel) { RfidInput = cardId; RfidLocationLabel = locationLabel; }
        private void OnPendingTimeoutStartRequested() => _coordinator.RequestPendingTimeout();
        private void OnHardwareStatusChanged(string device, HardwareConnectionStatus status)
        {
            switch (device)
            {
                case "Scale": ScaleConnectionStatus = status; break;
                case ReaderRoles.ScaleIn: RfidInStatus = status; break;
                case ReaderRoles.ScaleOut: RfidOutStatus = status; break;
                case ReaderRoles.Desk: RfidDeskStatus = status; break;
            }
        }
        private void OnAlarmHardwareStatusChanged(HardwareConnectionStatus status) => AlarmStatus = status;
        public void NotifyCameraStatus(HardwareConnectionStatus status) => CameraConnectionStatus = status;

        partial void OnIsAutoModeChanged(bool value)
        {
            if (value) { VehicleAutocomplete.ClearFilter(); CustomerAutocomplete.ClearFilter(); ProductAutocomplete.ClearFilter(); }
        }

        private async Task ProcessAndSaveWeighingAsync(decimal finalWeight)
        {
            if (!await _saveLock.WaitAsync(0)) return;
            try
            {
                IsSaving = true;
                LockedWeight = finalWeight;
                IsWeightLocked = true;
                WeightDisplay = LockedWeight.ToString("N0");

                var request = new DashboardSaveRequest(LicensePlate, CustomerName, ProductName, LockedWeight, IsOnePassMode);
                var result = await _dashboardSaveService.ExecuteSaveAsync(request);

                if (result.IsSuccess)
                {
                    ShowCameraMessage($"🔒 ĐÃ CHỐT: {result.FinalWeight:N0} KG\n{result.Message}", false);
                    await LoadRecentTicketsAsync();
                    
                    // Giảm thời gian chờ xuống 1 giây (thay vì 2 giây) để thao tác nhanh hơn
                    await Task.Delay(1000);
                    ResetForm();
                }
                else
                {
                    if (result.HasException) _notificationService.ShowError("Lỗi hệ thống không xác định.");
                    else _notificationService.ShowWarning(result.Message);
                    IsWeightLocked = false;
                }
            }
            catch (Exception ex) { IsWeightLocked = false; Log.Error(ex, "Lỗi lưu phiếu"); }
            finally 
            { 
                IsSaving = false;
                _saveLock.Release(); 
            }
        }

        [RelayCommand]
        private void ToggleWeightLock()
        {
            if (!IsWeightLocked)
            {
                if (!IsScaleStable) { _notificationService.ShowWarning(UiText.Messages.UnstableScaleWarning); return; }
                if (_scaleService.CurrentWeight < _coordinator.MinWeightThreshold) return;
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

            // Chống spam click
            if (IsSaving) return;

            try
            {
                IsSaving = true;

                // Tính năng mới: Tự động chốt khi nhấn Lưu
                if (!IsWeightLocked)
                {
                    // Chờ cân ổn định vô hạn định
                    if (!IsScaleStable)
                    {
                        ShowCameraMessage("⏳ ĐANG CHỜ CÂN ỔN ĐỊNH ĐỂ LƯU...", false);
                        while (!IsScaleStable)
                        {
                            if (IsAutoMode) return;
                            if (string.IsNullOrWhiteSpace(LicensePlate)) return; // Bị hủy bằng nút Làm mới
                            
                            // Thoát chờ nếu xe lùi ra khỏi cân
                            if (_scaleService.CurrentWeight < _coordinator.MinWeightThreshold)
                            {
                                ShowCameraMessage("⚠️ XE ĐÃ RỜI CÂN. HỦY CHỜ", true);
                                _notificationService.ShowWarning("Xe đã rời khỏi cân trước khi ổn định. Hệ thống đã hủy lệnh lưu.");
                                return;
                            }

                            await Task.Delay(200);
                        }
                    }
                    
                    if (_scaleService.CurrentWeight < _coordinator.MinWeightThreshold) 
                    {
                        _notificationService.ShowWarning($"Trọng lượng quá nhỏ (dưới mức tối thiểu {_coordinator.MinWeightThreshold} kg).");
                        ShowCameraMessage("", true);
                        return;
                    }

                    LockedWeight = _scaleService.CurrentWeight;
                    IsWeightLocked = true;
                    ShowCameraMessage($"🔒 ĐÃ CHỐT: {LockedWeight:N0} KG", false);
                }

                if (LockedWeight <= 0) { _notificationService.ShowWarning(UiText.Messages.LockWeightBeforeSave); return; }

                if (!VehicleAutocomplete.Items.Any(plate => plate.Equals(LicensePlate, StringComparison.OrdinalIgnoreCase)))
                {
                    var existingVehicle = await _dashboardDataService.GetVehicleByPlateAsync(LicensePlate);
                    if (existingVehicle != null) await LoadInitialDataAsync();
                    else
                    {
                        var vm = _quickRegisterVmFactory(LicensePlate);
                        var window = new Views.QuickVehicleRegisterWindow { DataContext = vm };
                        window.ShowDialog();
                        if (!vm.IsRegisteredAndSaved) return;
                        await LoadInitialDataAsync();
                    }
                }
                
                await ProcessAndSaveWeighingAsync(LockedWeight);
            }
            finally
            {
                IsSaving = false;
            }
        }

        [RelayCommand]
        private async Task CancelTicketAsync()
        {
            if (SelectedRecentTicket == null || SelectedRecentTicket.IsVoid) return;
            if (!_notificationService.Confirm(UiText.Messages.CancelTicketConfirm, UiText.Titles.Confirm)) return;
            var (isSuccess, message) = await _weighingBusiness.VoidTicketAsync(SelectedRecentTicket.TicketID);
            if (isSuccess) { ShowCameraMessage($"ĐÃ HỦY: {message}"); await LoadRecentTicketsAsync(); }
            else _notificationService.ShowWarning(message);
        }

        [RelayCommand] private async Task TriggerManualAlarmAsync() => await _alarmService.TriggerAlarmAsync();

        [RelayCommand] private void RefreshForm() { ResetForm(); ShowCameraMessage("♻️ ĐÃ LÀM SẠCH BIỂU MẪU"); }

        [RelayCommand]
        private void CopyDataFromTicket()
        {
            if (IsAutoMode || SelectedRecentTicket == null) return;
            LicensePlate = SelectedRecentTicket.LicensePlate;
            CustomerName = SelectedRecentTicket.CustomerName;
            ProductName = SelectedRecentTicket.ProductName;
        }

        public async Task LoadInitialDataAsync()
        {
            var initialData = await _dashboardDataService.LoadInitialDataAsync();
            Application.Current?.Dispatcher?.Invoke(() => {
                VehicleAutocomplete.UpdateItems(initialData.Vehicles);
                CustomerAutocomplete.UpdateItems(initialData.Customers);
                ProductAutocomplete.UpdateItems(initialData.Products);
                ProductName = ProductAutocomplete.Items.FirstOrDefault(p => p.Equals(initialData.DefaultProductName, StringComparison.OrdinalIgnoreCase)) ?? initialData.DefaultProductName;
            });
        }

        public async Task LoadRecentTicketsAsync()
        {
            var tickets = await _dashboardDataService.LoadRecentTicketsAsync();
            Application.Current?.Dispatcher?.Invoke(() => RecentTickets = new ObservableCollection<WeighingTicket>(tickets));
        }

        private void ResetForm()
        {
            LicensePlate = ""; CustomerName = ""; RfidInput = "";
            IsWeightLocked = false; LockedWeight = 0;
            CameraStatus = "Camera Online";
            _coordinator.ClearPendingData();
            _coordinator.CancelPendingTimeout();
        }

        private void ShowCameraMessage(string msg, bool autoHide = true) => _notificationService.ShowCameraStatus(s => CameraStatus = s, msg, autoHide);

        public void Dispose()
        {
            _scaleService.WeightChanged -= OnScaleWeightChangedUiUpdate;
            _coordinator.FormResetRequested -= OnFormResetRequested;
            _coordinator.CameraMessageRequested -= OnCameraMessageRequested;
            _coordinator.RfidCaptured -= OnRfidCaptured;
            _coordinator.PendingTimeoutStartRequested -= OnPendingTimeoutStartRequested;
            _coordinator.HardwareStatusChanged -= OnHardwareStatusChanged;
            _alarmService.HardwareStatusChanged -= OnAlarmHardwareStatusChanged;
            _automationService.DataChanged -= OnBackgroundDataChanged;
            _saveLock.Dispose();
        }
    }
}