using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using System.Windows;

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
                // Bước 0: Khởi tạo cơ bản
                UpdateStatus("Đang khởi tạo cấu hình...", 5);
                await Task.Delay(200);

                // Chạy song song 3 tác vụ nặng nhất
                var dbTask = Task.Run(() => 
                {
                    using var scope = _serviceProvider.CreateScope();
                    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    db.Database.Migrate();
                });

                var scaleTask = Task.Run(() => 
                {
                    _serviceProvider.GetRequiredService<ScaleService>();
                });

                var rfidTask = Task.Run(() => 
                {
                    _serviceProvider.GetRequiredService<RfidMultiService>();
                });

                // Cập nhật progress giả lập trong khi chờ (vì các task trên không report progress được chi tiết)
                // Hoặc đơn giản là đợi Task.WhenAll
                
                await Task.WhenAll(dbTask, scaleTask, rfidTask);

                UpdateStatus("Hệ thống đã sẵn sàng!", 100);
                await Task.Delay(500);
                
                CloseAction?.Invoke();
            }
            catch (Exception ex)
            {
                UpdateStatus($"Lỗi: {ex.Message}", Progress);
                MessageBox.Show($"Lỗi nghiêm trọng khi khởi động: {ex.Message}", "Lỗi Hệ Thống", MessageBoxButton.OK, MessageBoxImage.Error);
                Application.Current.Shutdown();
            }
        }

        private void UpdateStatus(string msg, int progress)
        {
            Application.Current.Dispatcher.Invoke(() =>
            {
                Status = msg;
                Progress = progress;
            });
        }
    }
}
