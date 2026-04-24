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

        // --- 2. CÁC DỊCH VỤ CỐT LÕI ---
        private readonly IServiceProvider _serviceProvider;
        private readonly AppSession _appSession;
        private readonly IUserNotificationService _notificationService;
        private readonly NotificationManagerService _notificationManager;
        private readonly ViewTrackerService _viewTracker;

        // --- 3. CÁC VIEWMODEL CON (Persistent - Singleton) ---
        // Giữ tham chiếu cố định để không bị khởi tạo lại hoặc Dispose khi chuyển tab
        public DashboardViewModel DashboardVm { get; }
        public VehicleRegistrationViewModel VehicleRegistrationVm { get; }
        public CustomerViewModel CustomerVm { get; }
        public ProductViewModel ProductVm { get; }
        public WeighingHistoryViewModel WeighingHistoryVm { get; }
        public SettingsViewModel SettingsVm { get; }
        public NotificationHistoryViewModel NotificationHistoryVm { get; }
        
        public NotificationManagerService NotificationManager => _notificationManager;

        // --- 4. CONSTRUCTOR ---
        public MainViewModel(
            IServiceProvider serviceProvider,
            AppSession appSession,
            IUserNotificationService notificationService,
            NotificationManagerService notificationManager,
            ViewTrackerService viewTracker)
        {
            _serviceProvider = serviceProvider;
            _appSession = appSession;
            _notificationService = notificationService;
            _notificationManager = notificationManager;
            _viewTracker = viewTracker;

            // Khởi tạo tham chiếu đến các Singleton ViewModels một lần duy nhất
            DashboardVm = _serviceProvider.GetRequiredService<DashboardViewModel>();
            VehicleRegistrationVm = _serviceProvider.GetRequiredService<VehicleRegistrationViewModel>();
            CustomerVm = _serviceProvider.GetRequiredService<CustomerViewModel>();
            ProductVm = _serviceProvider.GetRequiredService<ProductViewModel>();
            WeighingHistoryVm = _serviceProvider.GetRequiredService<WeighingHistoryViewModel>();
            SettingsVm = _serviceProvider.GetRequiredService<SettingsViewModel>();
            NotificationHistoryVm = _serviceProvider.GetRequiredService<NotificationHistoryViewModel>();

            // Mặc định hiển thị Dashboard khi khởi động
            _currentView = DashboardVm;
            _viewTracker.CurrentView = ViewType.Dashboard;
        }

        // --- 5. CÁC LỆNH ĐIỀU HƯỚNG ---
        [RelayCommand] private void ShowDashboard() => Navigate(DashboardVm);
        [RelayCommand] private void ShowRegistration() => Navigate(VehicleRegistrationVm);
        [RelayCommand] private void ShowCustomer() => Navigate(CustomerVm);
        [RelayCommand] private void ShowProduct() => Navigate(ProductVm);
        [RelayCommand] private void ShowHistory() => Navigate(WeighingHistoryVm);
        [RelayCommand] private void ShowSettings() => Navigate(SettingsVm);
        [RelayCommand] private void ShowNotificationHistory() => Navigate(NotificationHistoryVm);

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
                // ARCHITECTURE FIX: KHÔNG Dispose ViewModel cũ để giữ trạng thái (Keep-Alive)
                CurrentView = viewModel;

                // Cập nhật ViewTracker để BackgroundService biết không cần hiện Toast nếu đang ở tab đó
                if (viewModel is DashboardViewModel) _viewTracker.CurrentView = ViewType.Dashboard;
                else if (viewModel is VehicleRegistrationViewModel) _viewTracker.CurrentView = ViewType.VehicleRegistration;
                else if (viewModel is WeighingHistoryViewModel) _viewTracker.CurrentView = ViewType.WeighingHistory;
                else if (viewModel is SettingsViewModel) _viewTracker.CurrentView = ViewType.Settings;
                else _viewTracker.CurrentView = ViewType.Unknown;

                // Tự động làm mới dữ liệu khi quay lại Dashboard
                if (viewModel is DashboardViewModel dbVm)
                {
                    dbVm.LoadInitialDataAsync().FireAndForgetSafe(ex => 
                        _notificationService.LogError(ex, "Lỗi làm mới dữ liệu Dashboard"));
                }
            }
        }
    }
}