using AutoWeighbridgeSystem.Services;
using AutoWeighbridgeSystem.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;
using System;
using Microsoft.Extensions.DependencyInjection;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // --- 1. QUẢN LÝ TRẠNG THÁI GIAO DIỆN ---
        [ObservableProperty] private object _currentView;
        [ObservableProperty] private bool _isSidebarExpanded = true;

        // --- 2. CÁC DỊCH VỤ CỐT LÕI (Inject luôn) ---
        private readonly IServiceProvider _serviceProvider;
        private readonly AppSession _appSession;
        private readonly IUserNotificationService _notificationService;
        private readonly NotificationManagerService _notificationManager;

        // --- 3. CÁC INSTANCE VIEWMODEL CON (Lazy Loading) ---
        private DashboardViewModel _dashboardVm;
        private VehicleRegistrationViewModel _registrationVm;
        private CustomerViewModel _customerVm;
        private ProductViewModel _productVm;
        private WeighingHistoryViewModel _historyVm;
        private SettingsViewModel _settingsVm;
        private NotificationHistoryViewModel _notificationHistoryVm;

        // Bộc lộ cho View (Sử dụng Getter để Lazy Load)
        public DashboardViewModel DashboardVM => _dashboardVm ??= _serviceProvider.GetRequiredService<DashboardViewModel>();
        public VehicleRegistrationViewModel RegistrationVM => _registrationVm ??= _serviceProvider.GetRequiredService<VehicleRegistrationViewModel>();
        public CustomerViewModel CustomerVM => _customerVm ??= _serviceProvider.GetRequiredService<CustomerViewModel>();
        public ProductViewModel ProductVM => _productVm ??= _serviceProvider.GetRequiredService<ProductViewModel>();
        public WeighingHistoryViewModel HistoryVM => _historyVm ??= _serviceProvider.GetRequiredService<WeighingHistoryViewModel>();
        public SettingsViewModel SettingsVM => _settingsVm ??= _serviceProvider.GetRequiredService<SettingsViewModel>();
        public NotificationHistoryViewModel NotificationHistoryVM => _notificationHistoryVm ??= _serviceProvider.GetRequiredService<NotificationHistoryViewModel>();
        
        public NotificationManagerService NotificationManager => _notificationManager;

        // --- 4. CONSTRUCTOR ---
        public MainViewModel(
            IServiceProvider serviceProvider,
            AppSession appSession,
            IUserNotificationService notificationService,
            NotificationManagerService notificationManager)
        {
            _serviceProvider = serviceProvider;
            _appSession = appSession;
            _notificationService = notificationService;
            _notificationManager = notificationManager;

            // Mặc định hiển thị Dashboard khi khởi động (Dashboard sẽ được khởi tạo tại đây)
            _currentView = DashboardVM;
        }

        // --- 5. CÁC LỆNH ĐIỀU HƯỚNG ---
        [RelayCommand] private void ShowDashboard() => Navigate(DashboardVM);
        [RelayCommand] private void ShowRegistration() => Navigate(RegistrationVM);
        [RelayCommand] private void ShowCustomer() => Navigate(CustomerVM);
        [RelayCommand] private void ShowProduct() => Navigate(ProductVM);
        [RelayCommand] private void ShowHistory() => Navigate(HistoryVM);
        [RelayCommand] private void ShowSettings() => Navigate(SettingsVM);
        [RelayCommand] private void ShowNotificationHistory() => Navigate(NotificationHistoryVM);

        [RelayCommand] private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

        [RelayCommand]
        private void Logout()
        {
            if (_notificationService.Confirm(UiText.Messages.LogoutConfirm, UiText.Titles.Confirm))
            {
                Application.Current.Shutdown();
            }
        }

        // --- 6. HÀM TRỢ GIÚP ĐIỀU HƯỚNG ---
        private void Navigate(object viewModel)
        {
            if (CurrentView != viewModel)
            {
                CurrentView = viewModel;

                // Tự động làm mới danh sách gợi ý khi quay lại màn hình Dashboard
                if (viewModel is DashboardViewModel dbVm)
                {
                    dbVm.LoadInitialDataAsync().FireAndForgetSafe(ex => 
                        _notificationService.LogError(ex, "Lỗi làm mới dữ liệu Dashboard"));
                }
            }
        }
    }
}