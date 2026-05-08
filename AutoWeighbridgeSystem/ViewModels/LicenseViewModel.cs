using System;
using System.IO;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using AutoWeighbridgeSystem.Services;
using System.Windows;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class LicenseViewModel : ObservableObject
    {
        private readonly LicenseService _licenseService;
        private readonly IUserNotificationService _notificationService;
        private readonly AppSession _appSession;

        [ObservableProperty] private string _hardwareId = string.Empty;
        [ObservableProperty] private string _expiryDate = string.Empty;
        [ObservableProperty] private string _remainingDays = string.Empty;
        [ObservableProperty] private string _licenseStatus = string.Empty;
        [ObservableProperty] private string _customerName = "Chưa đăng ký";
        [ObservableProperty] private bool _isAdmin;

        public LicenseViewModel(LicenseService licenseService, IUserNotificationService notificationService, AppSession appSession)
        {
            _licenseService = licenseService;
            _notificationService = notificationService;
            _appSession = appSession;
            RefreshLicenseInfo();
            IsAdmin = _appSession.Role == "Admin";
        }

        [RelayCommand]
        private void RefreshLicenseInfo()
        {
            HardwareId = _licenseService.MachineId;
            RemainingDays = _licenseService.GetRemainingDays();
            
            if (_licenseService.CurrentStatus == AutoWeighbridgeSystem.Services.LicenseStatus.Valid && _licenseService.CurrentLicense != null)
            {
                ExpiryDate = _licenseService.CurrentLicense.ExpiryDate.ToString("dd/MM/yyyy");
                CustomerName = _licenseService.CurrentLicense.CustomerName;
                LicenseStatus = "ĐÃ KÍCH HOẠT";
            }
            else
            {
                ExpiryDate = "N/A";
                CustomerName = "Chưa đăng ký";
                LicenseStatus = "CHƯA KÍCH HOẠT / HẾT HẠN";
            }
        }

        [RelayCommand]
        private void CopyHardwareId()
        {
            Clipboard.SetText(HardwareId);
            _notificationService.ShowInfo("Đã sao chép Mã máy vào bộ nhớ tạm.", "THÔNG BÁO");
        }

        [RelayCommand]
        private void ActivateLicense()
        {
            var openFileDialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "License file (license.dat)|license.dat|All files (*.*)|*.*",
                Title = "Chọn tệp bản quyền license.dat"
            };

            if (openFileDialog.ShowDialog() == true)
            {
                string licensePath = openFileDialog.FileName;
                string dir = Path.GetDirectoryName(licensePath) ?? "";
                string keyPath = Path.Combine(dir, "key.dat");

                if (!File.Exists(keyPath))
                {
                    _notificationService.ShowError("Không tìm thấy tệp key.dat trong cùng thư mục với file bản quyền.", "THIẾU FILE CHÌA KHÓA");
                    return;
                }

                string scrambledKey = File.ReadAllText(keyPath).Trim();
                string secretKey = _licenseService.Descramble(scrambledKey);
                var status = _licenseService.ValidateLicenseFromFile(licensePath, secretKey);
                
                if (status == AutoWeighbridgeSystem.Services.LicenseStatus.Valid)
                {
                    _licenseService.ImportLicense(licensePath, keyPath);
                    RefreshLicenseInfo();
                    _notificationService.ShowInfo("Kích hoạt bản quyền thành công! Hệ thống đã được mở khóa.", "THÀNH CÔNG");
                }
                else
                {
                    ShowErrorMessage(status);
                }
            }
        }

        [RelayCommand]
        private void DeactivateLicense()
        {
            bool confirm = _notificationService.Confirm(
                "Bạn có chắc chắn muốn hủy bản quyền trên máy tính này không? \nThao tác này sẽ khóa các tính năng chính của hệ thống.",
                "XÁC NHẬN HỦY BẢN QUYỀN");

            if (confirm)
            {
                try
                {
                    string appDir = AppDomain.CurrentDomain.BaseDirectory;
                    string licensePath = Path.Combine(appDir, "license.dat");
                    string keyPath = Path.Combine(appDir, "key.dat");

                    if (File.Exists(licensePath)) File.Delete(licensePath);
                    if (File.Exists(keyPath)) File.Delete(keyPath);

                    RefreshLicenseInfo();
                    _notificationService.ShowWarning("Đã hủy bản quyền. Vui lòng kích hoạt lại để sử dụng.", "THÔNG BÁO");
                }
                catch (Exception ex)
                {
                    _notificationService.ShowError("Lỗi khi xóa file bản quyền: " + ex.Message);
                }
            }
        }

        private void ShowErrorMessage(AutoWeighbridgeSystem.Services.LicenseStatus status)
        {
            switch (status)
            {
                case AutoWeighbridgeSystem.Services.LicenseStatus.NoLicense:
                    _notificationService.ShowError("Tệp tin chọn không hợp lệ hoặc không tồn tại.", "LỖI FILE");
                    break;
                case AutoWeighbridgeSystem.Services.LicenseStatus.Expired:
                    _notificationService.ShowError("Bản quyền này đã hết hạn sử dụng.", "HẾT HẠN");
                    break;
                case AutoWeighbridgeSystem.Services.LicenseStatus.MachineMismatch:
                    _notificationService.ShowError("Mã máy trong tệp bản quyền không khớp với máy này.", "LỖI PHẦN CỨNG");
                    break;
                case AutoWeighbridgeSystem.Services.LicenseStatus.InvalidSignature:
                    _notificationService.ShowError("Tệp bản quyền bị hỏng hoặc chữ ký không hợp lệ.", "LỖI TỆP");
                    break;
            }
        }
    }
}
