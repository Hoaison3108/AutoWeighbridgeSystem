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

            // Dùng InvokeAsync (Fire & Forget) để background thread không bị chặn,
            // đảm bảo true auto — gửi xong là đi tiếp, không chờ UI thread xử lý.
            Application.Current.Dispatcher.InvokeAsync(() =>
            {
                History.Insert(0, notification);
                ActiveToasts.Add(notification);

                if (ActiveToasts.Count > 5)
                    ActiveToasts.RemoveAt(0);
            });

            // Tự động đóng sau X giây — cũng dùng InvokeAsync
            _ = Task.Run(async () =>
            {
                await Task.Delay(TimeSpan.FromSeconds(AutoHideSeconds));
                Application.Current.Dispatcher.InvokeAsync(() =>
                {
                    if (ActiveToasts.Contains(notification))
                        ActiveToasts.Remove(notification);
                });
            });
        }
    }
}
