using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Services;
using AutoWeighbridgeSystem.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using static System.Windows.Forms.VisualStyles.VisualStyleElement.StartPanel;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class LoginViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly AppSession _appSession;
        private readonly IServiceProvider _serviceProvider;

        [ObservableProperty]
        private string _username = "";

        [ObservableProperty]
        private string _errorMessage = "";

        [ObservableProperty]
        private bool _isLoading = false;

        public Action CloseAction { get; set; }

        public LoginViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            AppSession appSession,
            IServiceProvider serviceProvider) // Dùng để gọi MainWindow sau khi login
        {
            _dbContextFactory = dbContextFactory;
            _appSession = appSession;
            _serviceProvider = serviceProvider;
        }

        [RelayCommand]
        private async Task LoginAsync(object parameter)
        {
            if (IsLoading) return;

            // Lấy mật khẩu từ PasswordBox truyền vào (để bảo mật, không binding trực tiếp Password)
            var passwordBox = parameter as PasswordBox;
            string password = passwordBox?.Password ?? "";

            if (string.IsNullOrWhiteSpace(Username) || string.IsNullOrWhiteSpace(password))
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
                    if (user == null || user.Password != password)
                    {
                        ErrorMessage = "Tài khoản không tồn tại, bị khóa hoặc sai mật khẩu!";
                        return;
                    }

                    // Đăng nhập thành công -> Lưu vào Session
                    _appSession.SetUser(user.Id, user.FullName, user.Role);
                    Log.Information("Người dùng {User} đã đăng nhập thành công", user.Username);

                    OpenMainWindow();

                    // Đoạn này tôi để thông báo lỗi tạm thời nếu bạn chưa setup bảng Users
                    ErrorMessage = "Chức năng kết nối bảng Users đang được comment lại.";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Lỗi khi đăng nhập");
                ErrorMessage = "Lỗi kết nối cơ sở dữ liệu!";

                // BỔ SUNG MESSAGEBOX ĐỂ HIỆN LỖI CHI TIẾT KỸ THUẬT
                string detailedError = $"Chi tiết lỗi: {ex.Message}";
                if (ex.InnerException != null)
                {
                    detailedError += $"\n\nNội dung gốc: {ex.InnerException.Message}";
                }

                MessageBox.Show(detailedError, "Lỗi Hệ Thống - SQL Connection",
                                MessageBoxButton.OK, MessageBoxImage.Error);
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