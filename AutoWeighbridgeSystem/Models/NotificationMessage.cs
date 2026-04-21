using System;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoWeighbridgeSystem.Models
{
    public partial class NotificationMessage : ObservableObject
    {
        public Guid Id { get; } = Guid.NewGuid();
        public string Title { get; set; }
        public string Message { get; set; }
        public NotificationType Type { get; set; }
        public DateTime Timestamp { get; } = DateTime.Now;

        [ObservableProperty]
        private bool _isClosed = false;

        // Dùng để xác định màu sắc hiển thị trên UI
        public string Color => Type switch
        {
            NotificationType.Success => "#00E676", // Xanh lục
            NotificationType.Error   => "#FF5252", // Đỏ
            NotificationType.Warning => "#FFD600", // Vàng
            _                        => "#2196F3"  // Xanh dương (Info)
        };

        public string Icon => Type switch
        {
            NotificationType.Success => "CheckCircle",
            NotificationType.Error   => "AlertCircle",
            NotificationType.Warning => "Alert",
            _                        => "Information"
        };
    }
}
