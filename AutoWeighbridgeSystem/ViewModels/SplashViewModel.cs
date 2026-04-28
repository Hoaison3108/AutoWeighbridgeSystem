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

                    // Timeout 15 giây: tránh treo màn hình khi SQL Server chưa start
                    using var cts = new System.Threading.CancellationTokenSource(TimeSpan.FromSeconds(15));

                    try 
                    {
                        // Kiểm tra nhanh kết nối trước — báo lỗi trong ~3 giây thay vì treo 30+ giây
                        bool canConnect = await db.Database.CanConnectAsync(cts.Token);
                        if (!canConnect)
                        {
                            throw new Exception("SQL Server không phản hồi.");
                        }

                        UpdateStatus("Đang khởi tạo / cập nhật cơ sở dữ liệu...", 25);

                        // MigrateAsync: tự động tạo DB nếu chưa có, cập nhật schema nếu cần
                        await db.Database.MigrateAsync(cts.Token);
                        UpdateStatus("Cơ sở dữ liệu đã sẵn sàng.", 40);
                    }
                    catch (OperationCanceledException)
                    {
                        throw new Exception(
                            "Quá thời gian chờ kết nối SQL Server (15 giây).\n\n" +
                            "HƯỚNG DẪN KHẮC PHỤC:\n" +
                            "1. Mở 'Services' (services.msc) → tìm 'SQL Server (SQLEXPRESS)' → nhấn Start.\n" +
                            "2. Đợi 10-15 giây rồi mở lại phần mềm.\n" +
                            "3. Nếu không có SQL Server, cài đặt 'SQL Server Express' từ Microsoft.");
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[Splash] Lỗi khởi tạo Database");
                        throw new Exception(
                            "Không thể kết nối hoặc khởi tạo CSDL.\n\n" +
                            "HƯỚNG DẪN KHẮC PHỤC:\n" +
                            "1. Kiểm tra dịch vụ 'SQL Server (SQLEXPRESS)' đã được Chạy (Start) chưa.\n" +
                            "2. Đảm bảo SQL Server đã được cài đặt đúng phiên bản.\n" +
                            "3. Kiểm tra tài khoản và mật khẩu trong file appsettings.json.\n\n" +
                            $"Chi tiết lỗi: {ex.Message}");
                    }
                });

                // Bước 1: Chờ database sẵn sàng
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
