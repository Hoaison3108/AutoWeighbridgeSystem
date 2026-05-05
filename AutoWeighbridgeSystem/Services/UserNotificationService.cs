using System;
using System.Threading.Tasks;
using System.Windows;
using AutoWeighbridgeSystem.Common;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Interface định nghĩa các phương thức thông báo đến người dùng.
    /// Dùng interface để dễ mock khi unit test và tách biệt implementation khỏi tầng ViewModel.
    /// </summary>
    public interface IUserNotificationService
    {
        /// <summary>Hiển thị hộp thoại cảnh báo (màu vàng — Warning).</summary>
        void ShowWarning(string message, string title = UiText.Titles.WarningUpper);

        /// <summary>Hiển thị hộp thoại thông tin (màu xanh — Information).</summary>
        void ShowInfo(string message, string title = UiText.Titles.Info);

        /// <summary>Hiển thị hộp thoại lỗi (màu đỏ — Error).</summary>
        void ShowError(string message, string title = UiText.Titles.Error);

        /// <summary>
        /// Hiển thị hộp thoại xác nhận (Yes/No) và trả về <c>true</c> nếu người dùng chọn Yes.
        /// </summary>
        bool Confirm(string message, string title, MessageBoxButton buttons = MessageBoxButton.YesNo, MessageBoxImage image = MessageBoxImage.Question);

        /// <summary>
        /// Cập nhật trạng thái trên màn hình camera thông qua delegate <paramref name="setStatus"/>.
        /// Nếu <paramref name="autoHide"/> = <c>true</c>, thông báo tự ẩn sau 3 giây.
        /// </summary>
        /// <param name="setStatus">Action gán giá trị cho property CameraStatus của ViewModel.</param>
        /// <param name="message">Nội dung thông báo.</param>
        /// <param name="autoHide">Có tự ẩn về trạng thái "Camera Online" sau 3 giây không.</param>
        void ShowCameraStatus(Action<string> setStatus, string message, bool autoHide = true);

        /// <summary>Ghi log lỗi qua Serilog (không hiện hộp thoại).</summary>
        void LogError(Exception exception, string messageTemplate);

        /// <summary>Ghi log thông tin qua Serilog (không hiện hộp thoại).</summary>
        void LogInformation(string messageTemplate, params object[] propertyValues);
    }

    /// <summary>
    /// Implementation của <see cref="IUserNotificationService"/> dùng <c>MessageBox</c> của WPF.
    /// Trạng thái camera được cập nhật qua delegate để giữ tách biệt service khỏi ViewModel.
    /// </summary>
    public sealed class UserNotificationService : IUserNotificationService
    {
        private readonly NotificationManagerService _notificationManager;
        private const string DefaultCameraStatus    = UiText.Camera.OnlineStatus;
        private const int    CameraStatusAutoHideMs = 3000;

        public UserNotificationService(NotificationManagerService notificationManager)
        {
            _notificationManager = notificationManager;
        }

        /// <inheritdoc/>
        public void ShowWarning(string message, string title = UiText.Titles.WarningUpper)
        {
            _notificationManager.Notify(title, message, Models.NotificationType.Warning);
        }

        /// <inheritdoc/>
        public void ShowInfo(string message, string title = UiText.Titles.Info)
        {
            _notificationManager.Notify(title, message, Models.NotificationType.Info);
        }

        /// <inheritdoc/>
        public void ShowError(string message, string title = UiText.Titles.Error)
        {
            _notificationManager.Notify(title, message, Models.NotificationType.Error);
        }

        /// <inheritdoc/>
        public bool Confirm(string message, string title, MessageBoxButton buttons = MessageBoxButton.YesNo, MessageBoxImage image = MessageBoxImage.Question)
        {
            // Vẫn giữ MessageBox cho việc xác nhận vì cần chặn để lấy kết quả (Yes/No)
            return MessageBox.Show(message, title, buttons, image) == MessageBoxResult.Yes;
        }

        /// <inheritdoc/>
        public void ShowCameraStatus(Action<string> setStatus, string message, bool autoHide = true)
        {
            setStatus(message);
            if (!autoHide) return;

            // Chạy nền để tự reset về "Camera Online" sau 3 giây
            _ = Task.Run(async () =>
            {
                await Task.Delay(CameraStatusAutoHideMs);
                var dispatcher = Application.Current?.Dispatcher;
                if (dispatcher != null)
                    dispatcher.Invoke(() => setStatus(DefaultCameraStatus));
                else
                    setStatus(DefaultCameraStatus);
            });
        }

        /// <inheritdoc/>
        public void LogError(Exception exception, string messageTemplate)
        {
            Log.Error(exception, messageTemplate);
        }

        /// <inheritdoc/>
        public void LogInformation(string messageTemplate, params object[] propertyValues)
        {
            Log.Information(messageTemplate, propertyValues);
        }
    }
}
