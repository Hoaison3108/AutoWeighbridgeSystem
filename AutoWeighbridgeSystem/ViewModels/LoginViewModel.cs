using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Common;
using AutoWeighbridgeSystem.Services;
using AutoWeighbridgeSystem.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly AppSession _appSession;
        private readonly IServiceProvider _serviceProvider;
        private readonly IUserNotificationService _notificationService;

        [ObservableProperty]
        private string _username = "";

        [ObservableProperty]
        private string _password = "";

        [ObservableProperty]
        private string _errorMessage = "";

        [ObservableProperty]
        private bool _isLoading = false;

        public Action CloseAction { get; set; }

        public LoginViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            AppSession appSession,
            IServiceProvider serviceProvider,
            IUserNotificationService notificationService) // Dùng để gọi MainWindow sau khi login
        {
            _dbContextFactory = dbContextFactory;
            _appSession = appSession;
            _serviceProvider = serviceProvider;
            _notificationService = notificationService;
        }

        [RelayCommand]
        private async Task LoginAsync()
        {
            if (IsLoading) return;

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(Password))
            {
                ErrorMessage = "Vui lòng nhập đầy đủ Tài khoản và Mật khẩu.";
                return;
            }

            ErrorMessage = "";
            IsLoading = true;

            try
            {
                using (var db = _dbContextFactory.CreateDbContext())
                {
                    // TÌM NGƯỜI DÙNG TRONG DATABASE
                    // Lưu ý: Tên bảng (Users) có thể khác tùy thuộc vào AppDbContext của bạn
                    // Tìm user có Username khớp và tài khoản chưa bị khóa
                    var user = await db.Users.FirstOrDefaultAsync(u => u.Username == Username && u.IsActive);

                    // Đối chiếu mật khẩu (Lưu ý: Tương lai nên dùng hàm Hash để so sánh)
                    if (user == null || user.Password != Password)
                    {
                        ErrorMessage = "Tài khoản không tồn tại, bị khóa hoặc sai mật khẩu!";
                        return;
                    }

                    // Đăng nhập thành công -> Lưu vào Session
                    _appSession.SetUser(user.Id, user.FullName, user.Role);
                    _notificationService.LogInformation("Người dùng {User} đã đăng nhập thành công", user.Username);

                    OpenMainWindow();

                    // Đoạn này tôi để thông báo lỗi tạm thời nếu bạn chưa setup bảng Users
                    ErrorMessage = "Chức năng kết nối bảng Users đang được comment lại.";
                }
            }
            catch (Exception ex)
            {
                _notificationService.LogError(ex, "Lỗi khi đăng nhập");
                ErrorMessage = "Lỗi kết nối cơ sở dữ liệu!";

                // BỔ SUNG MESSAGEBOX ĐỂ HIỆN LỖI CHI TIẾT KỸ THUẬT
                string detailedError = $"Chi tiết lỗi: {ex.Message}";
                if (ex.InnerException != null)
                {
                    detailedError += $"\n\nNội dung gốc: {ex.InnerException.Message}";
                }

                _notificationService.ShowError(detailedError, UiText.Messages.SqlConnectionErrorTitle());
            }
            finally
            {
                IsLoading = false;
            }
        }

        private void OpenMainWindow()
        {
            // Yêu cầu DI Container tạo MainWindow
            var mainWindow = _serviceProvider.GetRequiredService<MainWindow>();
            mainWindow.Show();

            // Đóng cửa sổ Login
            CloseAction?.Invoke();
        }
    }
}