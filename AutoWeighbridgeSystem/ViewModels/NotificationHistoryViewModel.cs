using AutoWeighbridgeSystem.Services;
using AutoWeighbridgeSystem.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System.Collections.ObjectModel;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class NotificationHistoryViewModel : ObservableObject
    {
        private readonly NotificationManagerService _notificationManager;

        public ObservableCollection<NotificationMessage> History => _notificationManager.History;

        public NotificationHistoryViewModel(NotificationManagerService notificationManager)
        {
            _notificationManager = notificationManager;
        }

        [RelayCommand]
        private void ClearAllHistory()
        {
            _notificationManager.ClearHistory();
        }

        [RelayCommand]
        private void CloseHistoryPanel()
        {
            // Có thể dùng để quay lại Dashboard hoặc trang trước đó
        }
    }
}
