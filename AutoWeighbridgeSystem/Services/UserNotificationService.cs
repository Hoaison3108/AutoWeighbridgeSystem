using System;
using System.Threading.Tasks;
using System.Windows;
using AutoWeighbridgeSystem.Common;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    public interface IUserNotificationService
    {
        void ShowWarning(string message, string title = UiText.Titles.WarningUpper);
        void ShowInfo(string message, string title = UiText.Titles.Info);
        void ShowError(string message, string title = UiText.Titles.Error);
        bool Confirm(string message, string title, MessageBoxButton buttons = MessageBoxButton.YesNo, MessageBoxImage image = MessageBoxImage.Question);
        void ShowCameraStatus(Action<string> setStatus, string message, bool autoHide = true);
        void LogError(Exception exception, string messageTemplate);
        void LogInformation(string messageTemplate, params object[] propertyValues);
    }

    public sealed class UserNotificationService : IUserNotificationService
    {
        private const string DefaultCameraStatus = UiText.Camera.OnlineStatus;
        private const int CameraStatusAutoHideMs = 3000;

        public void ShowWarning(string message, string title = UiText.Titles.WarningUpper)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        public void ShowInfo(string message, string title = UiText.Titles.Info)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Information);
        }

        public void ShowError(string message, string title = UiText.Titles.Error)
        {
            MessageBox.Show(message, title, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        public bool Confirm(string message, string title, MessageBoxButton buttons = MessageBoxButton.YesNo, MessageBoxImage image = MessageBoxImage.Question)
        {
            return MessageBox.Show(message, title, buttons, image) == MessageBoxResult.Yes;
        }

        public void ShowCameraStatus(Action<string> setStatus, string message, bool autoHide = true)
        {
            setStatus(message);
            if (!autoHide) return;

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

        public void LogError(Exception exception, string messageTemplate)
        {
            Log.Error(exception, messageTemplate);
        }

        public void LogInformation(string messageTemplate, params object[] propertyValues)
        {
            Log.Information(messageTemplate, propertyValues);
        }
    }
}
