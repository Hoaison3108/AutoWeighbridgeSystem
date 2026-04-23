using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;
using Serilog;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class SplashViewModel : ObservableObject
    {
        private readonly IServiceProvider _serviceProvider;

        [ObservableProperty] private int _progress = 0;
        [ObservableProperty] private string _status = "Đang bắt đầu...";

        public Action CloseAction { get; set; }

        public SplashViewModel(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task StartInitAsync()
        {
            try
            {
                // Bước 0: Khởi tạo cấu hình
                UpdateStatus("Đang kiểm tra kết nối hệ thống...", 5);
                await Task.Delay(200);

                // Task 1: Kiểm tra và khởi tạo Database
                var dbTask = Task.Run(async () => 
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    
                    UpdateStatus("Đang kiểm tra kết nối CSDL...", 15);
                    
                    try 
                    {
                        // MigrateAsync sẽ tự động tạo Database nếu chưa tồn tại
                        // và cập nhật cấu trúc bảng nếu đã có.
                        await db.Database.MigrateAsync();
                        UpdateStatus("Cơ sở dữ liệu đã sẵn sàng.", 40);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Splash] Lỗi khởi tạo Database");
                        throw new Exception("Không thể kết nối hoặc khởi tạo CSDL.\n\n" +
                            "HƯỚNG DẪN KHẮC PHỤC:\n" +
                            "1. Kiểm tra dịch vụ 'SQL Server (SQLEXPRESS)' đã được Chạy (Start) chưa.\n" +
                            "2. Đảm bảo SQL Server đã được cài đặt đúng phiên bản.\n" +
                            "3. Kiểm tra tài khoản và mật khẩu trong file appsettings.json.");
                    }
                });

                // Bước 1: Kiểm tra và khởi tạo Database
                await dbTask;

                UpdateStatus("Hệ thống đã sẵn sàng!", 100);
                await Task.Delay(500);
                
                Application.Current?.Dispatcher?.Invoke(() =>
                {
                    CloseAction?.Invoke();
                });
            }
            catch (Exception ex)
            {
                UpdateStatus("LỖI KHỞI ĐỘNG", Progress);
                MessageBox.Show($"{ex.Message}", "Lỗi Hệ Thống", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current?.Shutdown();
            }
        }

        private void UpdateStatus(string msg, int progress)
        {
            Application.Current?.Dispatcher?.Invoke(() =>
            {
                Status = msg;
                Progress = progress;
            });
        }
    }
}
