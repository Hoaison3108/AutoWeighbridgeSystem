using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Windows;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;

        // --- 1. QUẢN LÝ DANH SÁCH USER ---
        [ObservableProperty] private ObservableCollection<User> _userList = new();
        [ObservableProperty] private User _gridSelectedUser;

        // --- 2. CẤU HÌNH HỆ THỐNG ---

        // Database
        [ObservableProperty] private string _dbConnectionString;

        // Camera Settings
        [ObservableProperty] private string _camIpAddress;
        [ObservableProperty] private string _camRtspPort;
        [ObservableProperty] private string _camUsername;
        [ObservableProperty] private string _camPassword;
        [ObservableProperty] private string _camRtspUrl;

        // Scale Settings (13 thông số)
        [ObservableProperty] private string _scaleProtocol;
        [ObservableProperty] private string _scaleComPort;
        [ObservableProperty] private int _scaleBaudRate;
        [ObservableProperty] private int _scaleDataBits;
        [ObservableProperty] private string _scaleParity;
        [ObservableProperty] private string _scaleStopBits;
        [ObservableProperty] private string _scaleDefaultProduct;
        [ObservableProperty] private int _scaleMinWeight;
        [ObservableProperty] private int _scaleRfidCooldown;
        [ObservableProperty] private int _scaleQueueTimeout;
        [ObservableProperty] private int _scaleWatchdog;
        [ObservableProperty] private bool _defaultToAutoMode;
        [ObservableProperty] private bool _defaultToOnePassMode;

        // Relay Settings
        [ObservableProperty] private string _relayComPort;
        [ObservableProperty] private int _relayBaudRate;
        [ObservableProperty] private int _relayDataBits;
        [ObservableProperty] private string _relayParity;
        [ObservableProperty] private string _relayStopBits;
        [ObservableProperty] private int _relayAlarmDuration;

        // RFID Settings - Scale In (5 thông số)
        [ObservableProperty] private string _rfidInPort;
        [ObservableProperty] private int _rfidInBaudRate;
        [ObservableProperty] private int _rfidInDataBits;
        [ObservableProperty] private string _rfidInParity;
        [ObservableProperty] private string _rfidInStopBits;

        // RFID Settings - Scale Out (5 thông số)
        [ObservableProperty] private string _rfidOutPort;
        [ObservableProperty] private int _rfidOutBaudRate;
        [ObservableProperty] private int _rfidOutDataBits;
        [ObservableProperty] private string _rfidOutParity;
        [ObservableProperty] private string _rfidOutStopBits;

        // RFID Settings - Desk (5 thông số)
        [ObservableProperty] private string _rfidDeskPort;
        [ObservableProperty] private int _rfidDeskBaudRate;
        [ObservableProperty] private int _rfidDeskDataBits;
        [ObservableProperty] private string _rfidDeskParity;
        [ObservableProperty] private string _rfidDeskStopBits;

        public SettingsViewModel(IDbContextFactory<AppDbContext> dbContextFactory, IConfiguration configuration)
        {
            _dbContextFactory = dbContextFactory;
            _configuration = configuration;
            LoadConfig();
            _ = LoadUsersAsync();
        }

        private void LoadConfig()
        {
            try
            {
                DbConnectionString = _configuration.GetConnectionString("DefaultConnection");

                // Camera
                CamIpAddress = _configuration["CameraSettings:IpAddress"];
                CamRtspPort = _configuration["CameraSettings:RtspPort"];
                CamUsername = _configuration["CameraSettings:Username"];
                CamPassword = _configuration["CameraSettings:Password"];
                CamRtspUrl = _configuration["CameraSettings:RtspUrl"];

                // Scale
                ScaleProtocol = _configuration["ScaleSettings:Protocol"] ?? "VishayVT220";
                ScaleComPort = _configuration["ScaleSettings:ComPort"];
                ScaleBaudRate = int.Parse(_configuration["ScaleSettings:BaudRate"] ?? "2400");
                ScaleDataBits = int.Parse(_configuration["ScaleSettings:DataBits"] ?? "7");
                ScaleParity = _configuration["ScaleSettings:Parity"] ?? "Even";
                ScaleStopBits = _configuration["ScaleSettings:StopBits"] ?? "One";
                ScaleDefaultProduct = _configuration["ScaleSettings:DefaultProductName"];
                ScaleMinWeight = int.Parse(_configuration["ScaleSettings:MinWeightThreshold"] ?? "200");
                ScaleRfidCooldown = int.Parse(_configuration["ScaleSettings:RfidCooldownSeconds"] ?? "3");
                ScaleQueueTimeout = int.Parse(_configuration["ScaleSettings:QueueTimeoutSeconds"] ?? "45");
                ScaleWatchdog = int.Parse(_configuration["ScaleSettings:HardwareWatchdogSeconds"] ?? "60");
                DefaultToAutoMode = bool.Parse(_configuration["ScaleSettings:DefaultToAutoMode"] ?? "true");
                DefaultToOnePassMode = bool.Parse(_configuration["ScaleSettings:DefaultToOnePassMode"] ?? "true");

                // Relay
                RelayComPort = _configuration["RelaySettings:ComPort"];
                RelayBaudRate = int.Parse(_configuration["RelaySettings:BaudRate"] ?? "9600");
                RelayDataBits = int.Parse(_configuration["RelaySettings:DataBits"] ?? "8");
                RelayParity = _configuration["RelaySettings:Parity"] ?? "None";
                RelayStopBits = _configuration["RelaySettings:StopBits"] ?? "One";
                RelayAlarmDuration = int.Parse(_configuration["RelaySettings:AlarmDurationMs"] ?? "1500");

                // RFID Scale In
                RfidInPort = _configuration["RfidSettings:ScaleIn:ComPort"];
                RfidInBaudRate = int.Parse(_configuration["RfidSettings:ScaleIn:BaudRate"] ?? "9600");
                RfidInDataBits = int.Parse(_configuration["RfidSettings:ScaleIn:DataBits"] ?? "8");
                RfidInParity = _configuration["RfidSettings:ScaleIn:Parity"] ?? "None";
                RfidInStopBits = _configuration["RfidSettings:ScaleIn:StopBits"] ?? "One";

                // RFID Scale Out
                RfidOutPort = _configuration["RfidSettings:ScaleOut:ComPort"];
                RfidOutBaudRate = int.Parse(_configuration["RfidSettings:ScaleOut:BaudRate"] ?? "9600");
                RfidOutDataBits = int.Parse(_configuration["RfidSettings:ScaleOut:DataBits"] ?? "8");
                RfidOutParity = _configuration["RfidSettings:ScaleOut:Parity"] ?? "None";
                RfidOutStopBits = _configuration["RfidSettings:ScaleOut:StopBits"] ?? "One";

                // RFID Desk
                RfidDeskPort = _configuration["RfidSettings:Desk:ComPort"];
                RfidDeskBaudRate = int.Parse(_configuration["RfidSettings:Desk:BaudRate"] ?? "9600");
                RfidDeskDataBits = int.Parse(_configuration["RfidSettings:Desk:DataBits"] ?? "8");
                RfidDeskParity = _configuration["RfidSettings:Desk:Parity"] ?? "None";
                RfidDeskStopBits = _configuration["RfidSettings:Desk:StopBits"] ?? "One";
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi Load Config: " + ex.Message);
            }
        }

        [RelayCommand]
        private void SaveSystemConfig()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                string jsonString = File.ReadAllText(jsonPath);
                var root = JsonNode.Parse(jsonString);

                // Đảm bảo Root không bị null (Phòng trường hợp file JSON trống trơn)
                if (root == null) root = new JsonObject();

                // ==========================================
                // 1. DB & Camera
                // ==========================================
                if (root["ConnectionStrings"] == null) root["ConnectionStrings"] = new JsonObject();
                root["ConnectionStrings"]["DefaultConnection"] = DbConnectionString;

                if (root["CameraSettings"] == null) root["CameraSettings"] = new JsonObject();
                root["CameraSettings"]["IpAddress"] = CamIpAddress;
                root["CameraSettings"]["RtspPort"] = CamRtspPort;
                root["CameraSettings"]["Username"] = CamUsername;
                root["CameraSettings"]["Password"] = CamPassword;
                root["CameraSettings"]["RtspUrl"] = CamRtspUrl;

                // ==========================================
                // 2. Scale Settings
                // ==========================================
                if (root["ScaleSettings"] == null) root["ScaleSettings"] = new JsonObject();
                var scale = root["ScaleSettings"];
                scale["Protocol"] = ScaleProtocol;
                scale["ComPort"] = ScaleComPort;
                scale["BaudRate"] = ScaleBaudRate;
                scale["DataBits"] = ScaleDataBits;
                scale["Parity"] = ScaleParity;
                scale["StopBits"] = ScaleStopBits;
                scale["DefaultProductName"] = ScaleDefaultProduct;
                scale["MinWeightThreshold"] = ScaleMinWeight;
                scale["RfidCooldownSeconds"] = ScaleRfidCooldown;
                scale["QueueTimeoutSeconds"] = ScaleQueueTimeout;
                scale["HardwareWatchdogSeconds"] = ScaleWatchdog;
                scale["DefaultToAutoMode"] = DefaultToAutoMode;
                scale["DefaultToOnePassMode"] = DefaultToOnePassMode;

                // ==========================================
                // 3. Relay Settings
                // ==========================================
                if (root["RelaySettings"] == null) root["RelaySettings"] = new JsonObject();
                var relay = root["RelaySettings"];
                relay["ComPort"] = RelayComPort;
                relay["BaudRate"] = RelayBaudRate;
                relay["DataBits"] = RelayDataBits;
                relay["Parity"] = RelayParity;
                relay["StopBits"] = RelayStopBits;
                relay["AlarmDurationMs"] = RelayAlarmDuration;

                // ==========================================
                // 4. RFID Settings
                // ==========================================
                if (root["RfidSettings"] == null) root["RfidSettings"] = new JsonObject();
                var rfid = root["RfidSettings"];

                // Nhánh Scale In
                if (rfid["ScaleIn"] == null) rfid["ScaleIn"] = new JsonObject();
                rfid["ScaleIn"]["ComPort"] = RfidInPort;
                rfid["ScaleIn"]["BaudRate"] = RfidInBaudRate;
                rfid["ScaleIn"]["DataBits"] = RfidInDataBits;
                rfid["ScaleIn"]["Parity"] = RfidInParity;
                rfid["ScaleIn"]["StopBits"] = RfidInStopBits;

                // Nhánh Scale Out
                if (rfid["ScaleOut"] == null) rfid["ScaleOut"] = new JsonObject();
                rfid["ScaleOut"]["ComPort"] = RfidOutPort;
                rfid["ScaleOut"]["BaudRate"] = RfidOutBaudRate;
                rfid["ScaleOut"]["DataBits"] = RfidOutDataBits;
                rfid["ScaleOut"]["Parity"] = RfidOutParity;
                rfid["ScaleOut"]["StopBits"] = RfidOutStopBits;

                // Nhánh Desk (Bàn làm việc)
                if (rfid["Desk"] == null) rfid["Desk"] = new JsonObject();
                rfid["Desk"]["ComPort"] = RfidDeskPort;
                rfid["Desk"]["BaudRate"] = RfidDeskBaudRate;
                rfid["Desk"]["DataBits"] = RfidDeskDataBits;
                rfid["Desk"]["Parity"] = RfidDeskParity;
                rfid["Desk"]["StopBits"] = RfidDeskStopBits;

                // ==========================================
                // LƯU FILE
                // ==========================================
                var options = new JsonSerializerOptions { WriteIndented = true };
                File.WriteAllText(jsonPath, root.ToJsonString(options));

                MessageBox.Show("Lưu cấu hình thành công! Hãy khởi động lại ứng dụng.", "Thông báo", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Lỗi lưu cấu hình: " + ex.Message, "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        [RelayCommand]
        private async Task LoadUsersAsync()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync();
            Application.Current?.Dispatcher.Invoke(() => { UserList = new ObservableCollection<User>(users); });
        }
    }
}