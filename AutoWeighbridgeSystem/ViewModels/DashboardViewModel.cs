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
        
        public SystemClockService Clock { get; }

        /// <summary>
        /// Factory delegate được inject từ DI — tạo <see cref="QuickVehicleRegisterViewModel"/>
        /// với biển số xe được truyền vào, mà không cần DashboardViewModel
        /// phải biết cách tạo VM con hoặc cần <c>IDbContextFactory</c>.
        /// </summary>
        private readonly Func<string, QuickVehicleRegisterViewModel> _quickRegisterVmFactory;

        // =========================================================================
        // TRẠNG THÁI NỘI BỘ
        // =========================================================================

        /// <summary>
        /// SemaphoreSlim thay cho bool thông thường — đảm bảo thread-safe khi
        /// Scale event và RFID event có thể cùng kích hoạt ProcessAndSave.
        /// </summary>
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        // VideoPlayer control được quản lý hoàn toàn ở code-behind của DashboardView.xaml.cs


        // =========================================================================
        // OBSERVABLE PROPERTIES (UI Binding)
        // =========================================================================

        // --- Hiển thị cân ---
        [ObservableProperty] private string _weightDisplay = "0";
        [ObservableProperty] private bool _isScaleStable = false;
        [ObservableProperty] private decimal _lockedWeight = 0;

        /// <summary>
        /// <c>true</c> khi trọng lượng đã được chốt thủ công.
        /// <para>
        /// Dùng <c>volatile</c> backing field để SerialPort thread có thể đọc
        /// an toàn không cần lock trong <see cref="OnScaleWeightChangedUiUpdate"/>.
        /// </para>
        /// </summary>
        private volatile bool _isWeightLockedVolatile = false;
        public bool IsWeightLocked
        {
            get => _isWeightLockedVolatile;
            set
            {
                if (_isWeightLockedVolatile != value)
                {
                    _isWeightLockedVolatile = value;
                    OnPropertyChanged();
                }
            }
        }

        // --- Thông tin phiếu cân ---
        [ObservableProperty] private string _licensePlate = "";
        [ObservableProperty] private string _customerName = "";
        [ObservableProperty] private string _productName = "";
        [ObservableProperty] private string _rfidInput = "";
        [ObservableProperty] private string _rfidLocationLabel = "Mã thẻ RFID (Đang chờ...):";

        // --- Chế độ hoạt động ---
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(IsManualMode))]
        private bool _isAutoMode = true;
        [ObservableProperty] private bool _isOnePassMode = false;

        // --- Camera ---
        [ObservableProperty] private Uri _cameraUri;
        [ObservableProperty] private string _cameraStatus = "Camera Online";

        // --- Trạng thái kết nối phần cứng (Offline mặc định → Online khi port mở thành công) ---
        [ObservableProperty] private HardwareConnectionStatus _scaleConnectionStatus   = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _rfidInStatus            = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _rfidOutStatus           = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _rfidDeskStatus          = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _cameraConnectionStatus  = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _alarmStatus             = HardwareConnectionStatus.Offline;

        // --- Danh sách lịch sử ---
        [ObservableProperty] private ObservableCollection<WeighingTicket> _recentTickets = new();
        [ObservableProperty] private WeighingTicket _selectedRecentTicket;

        // --- CollectionView cho Autocomplete (filter-as-you-type) ---
        public AutocompleteProvider<string> VehicleAutocomplete { get; } 
            = new AutocompleteProvider<string>(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));
        
        public AutocompleteProvider<string> CustomerAutocomplete { get; } 
            = new AutocompleteProvider<string>(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));
            
        public AutocompleteProvider<string> ProductAutocomplete { get; } 
            = new AutocompleteProvider<string>(Array.Empty<string>(), (item, text) => item.Contains(text, StringComparison.OrdinalIgnoreCase));

        /// <summary>Ngược của IsAutoMode — dùng để bind IsEnabled cho ComboBox.</summary>
        public bool IsManualMode => !IsAutoMode;

        // =========================================================================
        // UI REFRESH — Cache delegate và pending fields để tránh tạo object mới mỗi frame
        // =========================================================================

        /// <summary>Trọng lượng đang chờ hiển thị lên UI — viết bởi Serial thread, đọc bởi UI thread.</summary>
        private decimal _pendingDisplayWeight;
        /// <summary>Ấn nập trạng thái ổn định — viết bởi Serial thread, đọc bới UI thread.</summary>
        private volatile bool _pendingDisplayStable;
        /// <summary>Cached Action delegate — khởi tạo một lần trong constructor, tái sử dụng mỗi frame.</summary>
        private readonly Action _updateWeightDisplayAction;

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
            Func<string, QuickVehicleRegisterViewModel> quickRegisterVmFactory)
        {
            _configuration          = configuration;
            _scaleService           = scaleService;
            _coordinator            = coordinator;
            _dashboardSaveService   = dashboardSaveService;
            _dashboardDataService   = dashboardDataService;
            _weighingBusiness       = weighingBusiness;
            _notificationService    = notificationService;
            _alarmService           = alarmService;
            _appSession             = appSession;
            Clock                   = clock;
            _quickRegisterVmFactory = quickRegisterVmFactory;

            // Khởi tạo cached action một lần duy nhất — tái sử dụng mỗi frame (không tạo object mới 30 lần/giây)
            _updateWeightDisplayAction = () =>
            {
                WeightDisplay = _pendingDisplayWeight.ToString("N0");
                IsScaleStable = _pendingDisplayStable;
            };

            LoadUiConfiguration();
            InitializeCamera();
            SubscribeToScaleEvent();
            SubscribeToCoordinatorEvents();
            SubscribeToAlarmEvent();

            // Khởi động Coordinator — truyền Func<> để Coordinator đọc state VM khi cần
            _coordinator.Start(
                getIsAutoMode: () => IsAutoMode,
                getIsWeightLocked: () => IsWeightLocked,
                getIsProcessingSave: () => _saveLock.CurrentCount == 0,
                getSelectedProductName: () => !string.IsNullOrEmpty(ProductName) ? ProductName : "Hàng hóa");

            LoadInitialDataAsync().FireAndForgetSafe(ex =>
                _notificationService.LogError(ex, "Lỗi tải danh mục lúc khởi động"));
            LoadRecentTicketsAsync().FireAndForgetSafe(ex =>
                _notificationService.LogError(ex, "Lỗi tải nhật ký phiếu cân lúc khởi động"));

            // Kiểm tra trạng thái chuông ngay lúc khởi động
            _alarmService.Initialize();
        }

        // =========================================================================
        // KHỞI TẠO
        // =========================================================================

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

        private void SubscribeToScaleEvent()
        {
            _scaleService.WeightChanged += OnScaleWeightChangedUiUpdate;
        }

        private void OnScaleWeightChangedUiUpdate(decimal weight, bool isStable)
        {
            // Nhận: Serial thread — giá trị so sánh IsWeightLocked là volatile read (an toàn)
            if (IsWeightLocked) return;

            // Ghi giá trị cần hiển thị trước khi có hàng rào bộ nhớ (BeginInvoke tạo happens-before)
            _pendingDisplayWeight  = weight;
            _pendingDisplayStable  = isStable;

            // Dùng cached delegate thay vì new Action mỗi lần → không tạo GC pressure
            Application.Current?.Dispatcher.BeginInvoke(
                _updateWeightDisplayAction,
                DispatcherPriority.DataBind);
        }

        /// <summary>Đăng ký lắng nghe tất cả events từ DashboardEventCoordinator.</summary>
        private void SubscribeToCoordinatorEvents()
        {
            _coordinator.AutoSaveRequested             += OnAutoSaveRequested;
            _coordinator.FormResetRequested             += OnFormResetRequested;
            _coordinator.CameraMessageRequested         += OnCameraMessageRequested;
            _coordinator.RfidCaptured                   += OnRfidCaptured;
            _coordinator.PendingTimeoutStartRequested   += OnPendingTimeoutStartRequested;
            _coordinator.HardwareStatusChanged          += OnHardwareStatusChanged;
        }

        // Được xử lý qua OnScaleWeightChangedUiUpdate

        // =========================================================================
        // COORDINATOR EVENT HANDLERS — phản ứng với quyết định từ Coordinator
        // =========================================================================

        private async Task OnAutoSaveRequested(decimal weight, PendingVehicleData vehicleData)
        {
            LicensePlate = vehicleData.LicensePlate;
            CustomerName = vehicleData.CustomerName;
            ProductName = vehicleData.ProductName;
            await ProcessAndSaveWeighingAsync(weight);
        }

        private void OnFormResetRequested(string message)
        {
            ResetForm();
            ShowCameraMessage(message);
        }

        private void OnCameraMessageRequested(string message, bool autoHide)
        {
            ShowCameraMessage(message, autoHide);
        }

        private void OnRfidCaptured(string cardId, string locationLabel)
        {
            RfidInput = cardId;
            RfidLocationLabel = locationLabel;
        }

        private void OnPendingTimeoutStartRequested()
        {
            _coordinator.RequestPendingTimeout();
        }

        /// <summary>
        /// Cập nhật property tương ứng khi Coordinator báo trạng thái phần cứng thay đổi.
        /// Chạy trên UI thread (Coordinator đã BeginInvoke trước khi raise event này).
        /// </summary>
        private void OnHardwareStatusChanged(string device, HardwareConnectionStatus status)
        {
            switch (device)
            {
                case "Scale":   ScaleConnectionStatus = status; break;
                case ReaderRoles.ScaleIn:   RfidInStatus  = status; break;
                case ReaderRoles.ScaleOut:  RfidOutStatus = status; break;
                case ReaderRoles.Desk:      RfidDeskStatus = status; break;
            }
        }

        private void SubscribeToAlarmEvent()
        {
            _alarmService.HardwareStatusChanged += OnAlarmHardwareStatusChanged;
        }

        private void OnAlarmHardwareStatusChanged(HardwareConnectionStatus status)
        {
            AlarmStatus = status;
        }

        /// <summary>
        /// Gọi từ code-behind khi camera mở thành công hoặc thất bại.
        /// </summary>
        public void NotifyCameraStatus(HardwareConnectionStatus status)
            => CameraConnectionStatus = status;

        // =========================================================================
        // AUTOCOMPLETE FILTER HANDLERS
        // =========================================================================

        /// <summary>
        /// Khi IsAutoMode thay đổi → xóa filter, thông báo IsManualMode thay đổi.
        /// Việc đổi sang Auto xóa trắng filter để tránh nhầm lẫn.
        /// </summary>
        partial void OnIsAutoModeChanged(bool value)
        {
            if (value) // Chuyển sang Auto — xóa filter
            {
                VehicleAutocomplete.ClearFilter();
                CustomerAutocomplete.ClearFilter();
                ProductAutocomplete.ClearFilter();
            }
        }

        // =========================================================================
        // LOGIC LƯU PHIẾU — điều phối UI state xung quanh một lần gọi service
        // =========================================================================

        private async Task ProcessAndSaveWeighingAsync(decimal finalWeight)
        {
            // SemaphoreSlim(1,1): chỉ cho 1 luồng thực thi cùng lúc, không block — bỏ qua nếu đang bận
            if (!await _saveLock.WaitAsync(0)) return;

            try
            {
                LockedWeight = finalWeight;
                IsWeightLocked = true;
                WeightDisplay = LockedWeight.ToString("N0");

                var request = new DashboardSaveRequest(
                    LicensePlate,
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
                    if (result.HasException)
                        _notificationService.ShowError("Lỗi hệ thống không xác định. Vui lòng kiểm tra Logs.", "LỖI NGHIÊM TRỌNG");
                    else
                        _notificationService.ShowWarning(result.Message);

                    IsWeightLocked = false;
                }
            }
            catch (Exception ex)
            {
                IsWeightLocked = false;
                Log.Error(ex, "[DASHBOARD] Lỗi không xác định khi lưu phiếu cân, trọng lượng: {Weight}", finalWeight);
            }
            finally
            {
                _saveLock.Release();
            }
        }

        // =========================================================================
        // COMMANDS — hành động người dùng thực hiện
        // =========================================================================

        [RelayCommand]
        private void ToggleWeightLock()
        {
            if (!IsWeightLocked)
            {
                if (!IsScaleStable)
                {
                    _notificationService.ShowWarning(UiText.Messages.UnstableScaleWarning, UiText.Titles.Warning);
                    return;
                }
                if (_scaleService.CurrentWeight < _coordinator.MinWeightThreshold) return;

                LockedWeight = _scaleService.CurrentWeight;
                IsWeightLocked = true;
                ShowCameraMessage($"🔒 ĐÃ CHỐT: {LockedWeight:N0} KG", false);
            }
            else
            {
                IsWeightLocked = false;
                LockedWeight = 0;
            }
        }

        [RelayCommand]
        private async Task ManualSaveAsync()
        {
            if (IsAutoMode) { _notificationService.ShowWarning(UiText.Messages.ManualModeDisableAuto); return; }
            if (string.IsNullOrWhiteSpace(LicensePlate)) { _notificationService.ShowWarning(UiText.Messages.EnterLicensePlate); return; }
            if (!IsWeightLocked || LockedWeight <= 0) { _notificationService.ShowWarning(UiText.Messages.LockWeightBeforeSave); return; }

            // Bổ sung luồng chặn xử lý Đăng ký xe vãng lai
            if (!VehicleAutocomplete.Items.Any(plate => plate.Equals(LicensePlate, StringComparison.OrdinalIgnoreCase)))
            {
                // Kiểm tra chéo dưới DB xem thực tế xe đã tồn tại chưa (phòng trường hợp vừa đăng ký ở tab khác)
                var existingVehicle = await _dashboardDataService.GetVehicleByPlateAsync(LicensePlate);
                if (existingVehicle != null)
                {
                    // Xe đã có trong DB, chỉ cần refresh lại danh sách gợi ý và chạy tiếp
                    await LoadInitialDataAsync();
                }
                else
                {
                    // Thực sự là xe mới -> Mở cửa sổ đăng ký nhanh
                    var vm = _quickRegisterVmFactory(LicensePlate);
                    var window = new Views.QuickVehicleRegisterWindow { DataContext = vm };
                    window.ShowDialog();

                    if (!vm.IsRegisteredAndSaved)
                    {
                        return;
                    }

                    await LoadInitialDataAsync();
                }
            }

            await ProcessAndSaveWeighingAsync(LockedWeight);
        }

        [RelayCommand]
        private async Task CancelTicketAsync()
        {
            if (SelectedRecentTicket == null || SelectedRecentTicket.IsVoid) return;
            if (!_notificationService.Confirm(UiText.Messages.CancelTicketConfirm, UiText.Titles.Cancel)) return;

            var (isSuccess, message) = await _weighingBusiness.VoidTicketAsync(SelectedRecentTicket.TicketID);

            if (isSuccess)
            {
                ShowCameraMessage($"ĐÃ HỦY: {message}");
                await LoadRecentTicketsAsync();
            }
            else
            {
                _notificationService.ShowWarning(message);
            }
        }

        [RelayCommand]
        private async Task TriggerManualAlarmAsync() => await _alarmService.TriggerAlarmAsync();

        [RelayCommand]
        private void RefreshForm()
        {
            ResetForm();
            ShowCameraMessage("♻️ ĐÃ LÀM SẠCH BIỂU MẪU");
        }

        [RelayCommand]
        private void CopyDataFromTicket()
        {
            if (IsAutoMode) return; // Chỉ có tác dụng trong chế độ Manual
            if (SelectedRecentTicket == null) return;

            // Đẩy dữ liệu Text ngược lên form (Hỗ trợ nhập tay tự do)
            LicensePlate = SelectedRecentTicket.LicensePlate;
            CustomerName = SelectedRecentTicket.CustomerName;
            ProductName = SelectedRecentTicket.ProductName;
        }

        // =========================================================================
        // TẢI DỮ LIỆU
        // =========================================================================

        public async Task LoadInitialDataAsync()
        {
            var initialData = await _dashboardDataService.LoadInitialDataAsync();

            Application.Current?.Dispatcher?.Invoke(() =>
            {
                VehicleAutocomplete.UpdateItems(initialData.Vehicles);
                CustomerAutocomplete.UpdateItems(initialData.Customers);
                ProductAutocomplete.UpdateItems(initialData.Products);

                // Gán tên hàng hóa mặc định
                string defProd = initialData.DefaultProductName;
                var matched = ProductAutocomplete.Items.FirstOrDefault(p => p.Equals(defProd, StringComparison.OrdinalIgnoreCase));
                if (matched != null) ProductName = matched;
                else ProductName = defProd;
            });
        }

        public async Task LoadRecentTicketsAsync()
        {
            var tickets = await _dashboardDataService.LoadRecentTicketsAsync();
            Application.Current?.Dispatcher?.Invoke(() =>
                RecentTickets = new ObservableCollection<WeighingTicket>(tickets));
        }

        // =========================================================================
        // HELPERS
        // =========================================================================

        private void ResetForm()
        {
            LicensePlate = "";
            CustomerName = "";
            RfidInput = "";
            IsWeightLocked = false;
            LockedWeight = 0;
            CameraStatus = "Camera Online"; // Xóa chữ chốt cân trên overlay camera
            _coordinator.ClearPendingData();
            _coordinator.CancelPendingTimeout();
        }

        private void ShowCameraMessage(string msg, bool autoHide = true)
        {
            _notificationService.ShowCameraStatus(s => CameraStatus = s, msg, autoHide);
        }

        // =========================================================================
        // DISPOSE
        // =========================================================================
        public void Dispose()
        {
            _scaleService.WeightChanged -= OnScaleWeightChangedUiUpdate;
            _coordinator.AutoSaveRequested             -= OnAutoSaveRequested;
            _coordinator.FormResetRequested             -= OnFormResetRequested;
            _coordinator.CameraMessageRequested         -= OnCameraMessageRequested;
            _coordinator.RfidCaptured                   -= OnRfidCaptured;
            _coordinator.PendingTimeoutStartRequested   -= OnPendingTimeoutStartRequested;
            _coordinator.HardwareStatusChanged          -= OnHardwareStatusChanged;
            _alarmService.HardwareStatusChanged         -= OnAlarmHardwareStatusChanged;
            _coordinator.Dispose();
            _saveLock.Dispose();
        }
    }
}