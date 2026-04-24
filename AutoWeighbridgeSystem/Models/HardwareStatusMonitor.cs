using AutoWeighbridgeSystem.Common;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AutoWeighbridgeSystem.Models
{
    public partial class HardwareStatusMonitor : ObservableObject
    {
        [ObservableProperty] private HardwareConnectionStatus _scale = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _rfidIn = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _rfidOut = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _rfidDesk = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _camera = HardwareConnectionStatus.Offline;
        [ObservableProperty] private HardwareConnectionStatus _alarm = HardwareConnectionStatus.Offline;

        public void UpdateStatus(string device, HardwareConnectionStatus status)
        {
            switch (device)
            {
                case "Scale": 
                    Scale = status; 
                    break;
                case ReaderRoles.ScaleIn: 
                    RfidIn = status; 
                    break;
                case ReaderRoles.ScaleOut: 
                    RfidOut = status; 
                    break;
                case ReaderRoles.Desk: 
                    RfidDesk = status; 
                    break;
                case "Camera": 
                    Camera = status; 
                    break;
                case "Alarm": 
                    Alarm = status; 
                    break;
            }
        }
    }
}
