using AutoWeighbridgeSystem.Services;
using AutoWeighbridgeSystem.Common;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Windows;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class MainViewModel : ObservableObject
    {
        // --- 1. QUẢN LÝ TRẠNG THÁI GIAO DIỆN ---

        [ObservableProperty] private object _currentView;
        [ObservableProperty] private bool _isSidebarExpanded = true;

        // --- 2. CÁC INSTANCE VIEWMODEL CON (Quản lý bởi DI Container) ---

        private readonly DashboardViewModel _dashboardVm;
        private readonly VehicleRegistrationViewModel _registrationVm;
        private readonly CustomerViewModel _customerVm;
        private readonly ProductViewModel _productVm;
        private readonly WeighingHistoryViewModel _historyVm; // Bổ sung module Lịch sử
        private readonly SettingsViewModel _settingsVm;
        private readonly AppSession _appSession;
        private readonly IUserNotificationService _notificationService;

        // --- 3. CONSTRUCTOR (Đồng nhất qua Injection) ---

        public MainViewModel(
            DashboardViewModel dashboardVm,
            VehicleRegistrationViewModel registrationVm,
            CustomerViewModel customerVm,
            ProductViewModel productVm,
            WeighingHistoryViewModel historyVm, // Inject module Lịch sử
            SettingsViewModel settingsVm,
            AppSession appSession,
            IUserNotificationService notificationService)
        {
            _dashboardVm = dashboardVm;
            _registrationVm = registrationVm;
            _customerVm = customerVm;
            _productVm = productVm;
            _historyVm = historyVm;
            _settingsVm = settingsVm;
            _appSession = appSession;
            _notificationService = notificationService;

            // Mặc định hiển thị Dashboard khi khởi động
            _currentView = _dashboardVm;
        }

        // --- 4. CÁC LỆNH ĐIỀU HƯỚNG ---

        [RelayCommand]
        private void ShowDashboard() => Navigate(_dashboardVm);

        [RelayCommand]
        private void ShowRegistration() => Navigate(_registrationVm);

        [RelayCommand]
        private void ShowCustomer() => Navigate(_customerVm);

        [RelayCommand]
        private void ShowProduct() => Navigate(_productVm);

        [RelayCommand]
        private void ShowHistory() => Navigate(_historyVm); // Lệnh hiển thị Lịch sử Phiếu cân

        [RelayCommand]
        private void ShowSettings() => Navigate(_settingsVm);

        [RelayCommand]
        private void ToggleSidebar() => IsSidebarExpanded = !IsSidebarExpanded;

        [RelayCommand]
        private void Logout()
        {
            if (_notificationService.Confirm(UiText.Messages.LogoutConfirm, UiText.Titles.Confirm))
            {
                Application.Current.Shutdown();
            }
        }

        // --- 5. HÀM TRỢ GIÚP ĐIỀU HƯỚNG ---

        private void Navigate(object viewModel)
        {
            if (CurrentView != viewModel)
            {
                CurrentView = viewModel;
            }
        }
    }
}