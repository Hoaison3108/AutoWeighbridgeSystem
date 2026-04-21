using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using AutoWeighbridgeSystem.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AutoWeighbridgeSystem.Services
{
    public partial class NotificationManagerService : ObservableObject
    {
        private const int AutoHideSeconds = 6;

        // Danh sách thông báo đang hiển thị (Toasts)
        public ObservableCollection<NotificationMessage> ActiveToasts { get; } = new();

        // Danh sách toàn bộ lịch sử thông báo
        public ObservableCollection<NotificationMessage> History { get; } = new();

        [ObservableProperty]
        private bool _isHistoryVisible = false;

        [RelayCommand]
        public void ToggleHistory() => IsHistoryVisible = !IsHistoryVisible;

        [RelayCommand]
        public void ClearHistory() => History.Clear();

        [RelayCommand]
        public void CloseNotification(NotificationMessage message)
        {
            if (message == null) return;
            ActiveToasts.Remove(message);
        }

        public void Notify(string title, string message, NotificationType type)
        {
            var notification = new NotificationMessage
            {
                Title = title,
                Message = message,
                Type = type
            };

            // Thực hiện trên UI Thread
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Thêm vào hàng đầu của lịch sử
                History.Insert(0, notification);

                // Thêm vào danh sách hiển thị
                ActiveToasts.Add(notification);

                // Giới hạn số lượng Toast hiển thị cùng lúc (nếu muốn)
                if (ActiveToasts.Count > 5)
                {
                    ActiveToasts.RemoveAt(0);
                }
            });

            // Tự động đóng sau X giây
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(AutoHideSeconds));
                Application.Current.Dispatcher.Invoke(() =>
                {
                    if (ActiveToasts.Contains(notification))
                    {
                        ActiveToasts.Remove(notification);
                    }
                });
            });
        }
    }
}
