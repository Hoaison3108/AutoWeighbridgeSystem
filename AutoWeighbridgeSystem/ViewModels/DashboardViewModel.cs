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
        private readonly Microsoft.EntityFrameworkCore.IDbContextFactory<AutoWeighbridgeSystem.Data.AppDbContext> _dbContextFactory;

        // =========================================================================
        // TRẠNG THÁI NỘI BỘ
        // =========================================================================

        /// <summary>
        /// SemaphoreSlim thay cho bool thông thường — đảm bảo thread-safe khi
        /// Scale event và RFID event có thể cùng kích hoạt ProcessAndSave.
        /// </summary>
        private readonly SemaphoreSlim _saveLock = new SemaphoreSlim(1, 1);

        // === FFME VideoPlayer — set từ code-behind ===
        public Unosquare.FFME.MediaElement VideoPlayer { get; set; }

        // =========================================================================
        // OBSERVABLE PROPERTIES (UI Binding)
        // =========================================================================

        // --- Hiển thị cân ---
        [ObservableProperty] private string _weightDisplay = "0";
        [ObservableProperty] private bool _isScaleStable = false;
        [ObservableProperty] private decimal _lockedWeight = 0;
        [ObservableProperty] private bool _isWeightLocked = false;

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

        // --- Danh sách lịch sử ---
        [ObservableProperty] private ObservableCollection<WeighingTicket> _recentTickets = new();
        [ObservableProperty] private WeighingTicket _selectedRecentTicket;

        // --- Danh mục (nguồn gốc) ---
        [ObservableProperty] private ObservableCollection<Product> _productList = new();
        [ObservableProperty] private ObservableCollection<Vehicle> _vehicleList = new();
        [ObservableProperty] private ObservableCollection<Customer> _customerList = new();
        [ObservableProperty] private Product _selectedProduct;
        [ObservableProperty] private Vehicle _selectedVehicle;
        [ObservableProperty] private Customer _selectedCustomer;

        // --- CollectionView cho Autocomplete (filter-as-you-type) ---
        /// <summary>View đã lọc dùng cho ComboBox Biển số — binding thay thế VehicleList.</summary>
        public ICollectionView VehicleView { get; private set; }
        /// <summary>View đã lọc dùng cho ComboBox Khách hàng — binding thay thế CustomerList.</summary>
        public ICollectionView CustomerView { get; private set; }
        /// <summary>View đã lọc dùng cho ComboBox Hàng hóa — binding thay thế ProductList.</summary>
        public ICollectionView ProductView { get; private set; }

        // --- Text đang gõ trong từng ComboBox để thực hiện filter ---
        [ObservableProperty] private string _vehicleFilterText = "";
        [ObservableProperty] private string _customerFilterText = "";
        [ObservableProperty] private string _productFilterText = "";

        /// <summary>Ngược của IsAutoMode — dùng để bind IsEnabled cho ComboBox.</summary>
        public bool IsManualMode => !IsAutoMode;

        // =========================================================================
        // UI REFRESH (Event-driven realtime)
        // =========================================================================

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
            Microsoft.EntityFrameworkCore.IDbContextFactory<AutoWeighbridgeSystem.Data.AppDbContext> dbContextFactory)
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
            _dbContextFactory = dbContextFactory;

            LoadUiConfiguration();
            InitializeCamera();
            SubscribeToScaleEvent();
            SubscribeToCoordinatorEvents();

            // Khởi động Coordinator — truyền Func<> để Coordinator đọc state VM khi cần
            _coordinator.Start(
                getIsAutoMode: () => IsAutoMode,
                getIsWeightLocked: () => IsWeightLocked,
                getIsProcessingSave: () => _saveLock.CurrentCount == 0,
                getSelectedProductName: () => SelectedProduct?.ProductName ?? "Hàng hóa");

            LoadInitialDataAsync().FireAndForgetSafe(ex =>
                _notificationService.LogError(ex, "Lỗi tải danh mục lúc khởi động"));
            LoadRecentTicketsAsync().FireAndForgetSafe(ex =>
                _notificationService.LogError(ex, "Lỗi tải nhật ký phiếu cân lúc khởi động"));
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
            if (IsWeightLocked) return;
            // Ép luồng UI cập nhật ngay tức thì (Priority cao hơn Timer)
            Application.Current?.Dispatcher.BeginInvoke(new Action(() =>
            {
                WeightDisplay = weight.ToString("N0");
                IsScaleStable = isStable;
            }), DispatcherPriority.DataBind);
        }

        /// <summary>Đăng ký lắng nghe tất cả events từ DashboardEventCoordinator.</summary>
        private void SubscribeToCoordinatorEvents()
        {
            _coordinator.AutoSaveRequested += OnAutoSaveRequested;
            _coordinator.FormResetRequested += OnFormResetRequested;
            _coordinator.CameraMessageRequested += OnCameraMessageRequested;
            _coordinator.RfidCaptured += OnRfidCaptured;
            _coordinator.PendingTimeoutStartRequested += OnPendingTimeoutStartRequested;
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

        // =========================================================================
        // CẬP NHẬT FORM KHI CHỌN COMBO BOX
        // =========================================================================

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
                VehicleFilterText = "";
                CustomerFilterText = "";
                ProductFilterText = "";
            }
        }

        /// <summary>Lọc danh sách xe theo text đang gõ trong ComboBox Biển số.</summary>
        partial void OnVehicleFilterTextChanged(string value)
        {
            VehicleView?.Refresh();
        }

        /// <summary>Lọc danh sách khách hàng theo text đang gõ trong ComboBox Khách hàng.</summary>
        partial void OnCustomerFilterTextChanged(string value)
        {
            CustomerView?.Refresh();
        }

        /// <summary>Lọc danh sách hàng hóa theo text đang gõ trong ComboBox Hàng hóa.</summary>
        partial void OnProductFilterTextChanged(string value)
        {
            ProductView?.Refresh();
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
            if (!VehicleList.Any(v => v.LicensePlate.Equals(LicensePlate, StringComparison.OrdinalIgnoreCase)))
            {
                var vm = new QuickVehicleRegisterViewModel(LicensePlate, _dbContextFactory, _notificationService, _scaleService);
                var window = new Views.QuickVehicleRegisterWindow { DataContext = vm };
                window.ShowDialog();

                if (!vm.IsRegisteredAndSaved)
                {
                    return; // Người dùng Hủy ngang cửa sổ thiết bị
                }

                // Nếu đăng ký thành công, load lại DB và chọn xe mới
                await LoadInitialDataAsync();
                var newVehicle = VehicleList.FirstOrDefault(v => v.LicensePlate.Equals(LicensePlate, StringComparison.OrdinalIgnoreCase));
                if (newVehicle != null) SelectedVehicle = newVehicle;
            }

            if (!_notificationService.Confirm(UiText.Messages.SaveTicketConfirm(LicensePlate, LockedWeight), UiText.Titles.Confirm))
                return;

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
            var matchedVehicle = VehicleList.FirstOrDefault(v => v.LicensePlate.Equals(LicensePlate, StringComparison.OrdinalIgnoreCase));
            if (matchedVehicle != null) SelectedVehicle = matchedVehicle;

            CustomerName = SelectedRecentTicket.CustomerName;
            var matchedCustomer = CustomerList.FirstOrDefault(c => c.CustomerName.Equals(CustomerName, StringComparison.OrdinalIgnoreCase));
            if (matchedCustomer != null) SelectedCustomer = matchedCustomer;

            ProductName = SelectedRecentTicket.ProductName;
            var matchedProduct = ProductList.FirstOrDefault(p => p.ProductName.Equals(ProductName, StringComparison.OrdinalIgnoreCase));
            if (matchedProduct != null) SelectedProduct = matchedProduct;
        }

        // =========================================================================
        // TẢI DỮ LIỆU
        // =========================================================================

        private async Task LoadInitialDataAsync()
        {
            var initialData = await _dashboardDataService.LoadInitialDataAsync();

            Application.Current?.Dispatcher.Invoke(() =>
            {
                VehicleList = new ObservableCollection<Vehicle>(initialData.Vehicles);
                CustomerList = new ObservableCollection<Customer>(initialData.Customers);
                ProductList = new ObservableCollection<Product>(initialData.Products);

                // Khởi tạo CollectionView với predicate filter — hỗ trợ autocomplete khi gõ
                VehicleView = CollectionViewSource.GetDefaultView(VehicleList);
                CustomerView = CollectionViewSource.GetDefaultView(CustomerList);
                ProductView = CollectionViewSource.GetDefaultView(ProductList);

                VehicleView.Filter = item =>
                {
                    if (string.IsNullOrWhiteSpace(VehicleFilterText)) return true;
                    return item is Vehicle v &&
                           v.LicensePlate.Contains(VehicleFilterText, StringComparison.OrdinalIgnoreCase);
                };

                CustomerView.Filter = item =>
                {
                    if (string.IsNullOrWhiteSpace(CustomerFilterText)) return true;
                    return item is Customer c &&
                           c.CustomerName.Contains(CustomerFilterText, StringComparison.OrdinalIgnoreCase);
                };

                ProductView.Filter = item =>
                {
                    if (string.IsNullOrWhiteSpace(ProductFilterText)) return true;
                    return item is Product p &&
                           p.ProductName.Contains(ProductFilterText, StringComparison.OrdinalIgnoreCase);
                };

                // Thông báo UI biết có View mới
                OnPropertyChanged(nameof(VehicleView));
                OnPropertyChanged(nameof(CustomerView));
                OnPropertyChanged(nameof(ProductView));

                string defProd = initialData.DefaultProductName;
                var prod = ProductList.FirstOrDefault(p => p.ProductName.Equals(defProd, StringComparison.OrdinalIgnoreCase));
                if (prod != null) { SelectedProduct = prod; ProductName = prod.ProductName; }
                else ProductName = defProd;
            });
        }

        public async Task LoadRecentTicketsAsync()
        {
            var tickets = await _dashboardDataService.LoadRecentTicketsAsync();
            Application.Current?.Dispatcher.Invoke(() =>
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
            SelectedVehicle = null;
            SelectedCustomer = null;
            IsWeightLocked = false;
            LockedWeight = 0;
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
            _coordinator.AutoSaveRequested -= OnAutoSaveRequested;
            _coordinator.FormResetRequested -= OnFormResetRequested;
            _coordinator.CameraMessageRequested -= OnCameraMessageRequested;
            _coordinator.RfidCaptured -= OnRfidCaptured;
            _coordinator.PendingTimeoutStartRequested -= OnPendingTimeoutStartRequested;
            _coordinator.Dispose();
            _saveLock.Dispose();
        }
    }
}