using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Common;
using AutoWeighbridgeSystem.Models;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using AutoWeighbridgeSystem.Services;
using AutoWeighbridgeSystem.Services.Protocols;
using System;
using System.Collections.ObjectModel;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Windows;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class SettingsViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly IConfiguration _configuration;
        private readonly IUserNotificationService _notificationService;
        private readonly AppSession _appSession;
        private readonly ScaleService _scaleService;
        private readonly RfidMultiService _rfidService;
        private readonly IScaleProtocolFactory _protocolFactory;

        // --- 0. PHÂN QUYỀN ---
        /// <summary>Trả về true nếu người dùng hiện tại có quyền Admin.</summary>
        public bool IsAdmin => string.Equals(_appSession.Role, "Admin", StringComparison.OrdinalIgnoreCase);

        /// <summary>Danh sách giao thức được hỗ trợ — dùng để bind vào ComboBox trong SettingsView.</summary>
        public IReadOnlyList<string> SupportedProtocols => _protocolFactory.SupportedProtocols;

        // --- 1. QUẢN LÝ DANH SÁCH USER ---
        [ObservableProperty] private ObservableCollection<User> _userList = new();
        [ObservableProperty] private User _gridSelectedUser;

        // --- 2. CẤU HÌNH HỆ THỐNG ---

        // Database
        [ObservableProperty] private string _dbConnectionString;
        [ObservableProperty] private string _backupFolderPath;

        // DB Discovery & Manual Config
        [ObservableProperty] private ObservableCollection<string> _discoveredServers = new();
        [ObservableProperty] private string _selectedSqlServer;
        [ObservableProperty] private string _dbServerName;
        [ObservableProperty] private string _dbName;
        [ObservableProperty] private string _dbUser;
        [ObservableProperty] private string _dbPassword;
        [ObservableProperty] private bool _isScanning;

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
        [ObservableProperty] private int _scaleStabilityDelta;
        [ObservableProperty] private int _scaleRfidCooldown;
        [ObservableProperty] private int _scaleQueueTimeout;
        [ObservableProperty] private int _scaleWatchdog;
        [ObservableProperty] private bool _defaultToAutoMode;
        [ObservableProperty] private bool _defaultToOnePassMode;
        [ObservableProperty] private bool _isOfficeMode;

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

        // Danh sách cổng COM lấy từ Windows
        [ObservableProperty] private ObservableCollection<string> _availableComPorts = new();

        public SettingsViewModel(
            IDbContextFactory<AppDbContext> dbContextFactory,
            IConfiguration configuration,
            IUserNotificationService notificationService,
            AppSession appSession,
            ScaleService scaleService,
            RfidMultiService rfidService,
            IScaleProtocolFactory protocolFactory)
        {
            _dbContextFactory    = dbContextFactory;
            _configuration       = configuration;
            _notificationService = notificationService;
            _appSession          = appSession;
            _scaleService        = scaleService;
            _rfidService         = rfidService;
            _protocolFactory     = protocolFactory;
            
            // Lấy danh sách cổng COM ban đầu
            try 
            { 
                var ports = SerialPort.GetPortNames().OrderBy(p => p).ToList();
                ports.Insert(0, "None");
                AvailableComPorts = new ObservableCollection<string>(ports); 
            } 
            catch { AvailableComPorts = new ObservableCollection<string> { "None" }; }
            
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
                CamRtspPort  = _configuration["CameraSettings:RtspPort"];
                CamUsername  = _configuration["CameraSettings:Username"];
                CamPassword  = _configuration["CameraSettings:Password"];
                CamRtspUrl   = _configuration["CameraSettings:RtspUrl"];

                // Database Settings (Backup)
                BackupFolderPath = _configuration["DatabaseSettings:BackupFolderPath"] ?? @"D:\Backups";

                // Scale
                ScaleProtocol       = _configuration["ScaleSettings:Protocol"] ?? "VishayVT220";
                ScaleComPort        = _configuration["ScaleSettings:ComPort"];
                ScaleBaudRate       = int.TryParse(_configuration["ScaleSettings:BaudRate"],          out int sb)  ? sb  : 2400;
                ScaleDataBits       = int.TryParse(_configuration["ScaleSettings:DataBits"],          out int sdb) ? sdb : 7;
                ScaleParity         = _configuration["ScaleSettings:Parity"]    ?? "Even";
                ScaleStopBits       = _configuration["ScaleSettings:StopBits"]  ?? "One";
                ScaleDefaultProduct = _configuration["ScaleSettings:DefaultProductName"];
                ScaleMinWeight      = int.TryParse(_configuration["ScaleSettings:MinWeightThreshold"],   out int smw) ? smw : 200;
                ScaleStabilityDelta = int.TryParse(_configuration["ScaleSettings:StabilityDelta"],        out int ssd) ? ssd : 50;
                ScaleRfidCooldown   = int.TryParse(_configuration["ScaleSettings:RfidCooldownSeconds"],  out int src) ? src : 3;
                ScaleQueueTimeout   = int.TryParse(_configuration["ScaleSettings:QueueTimeoutSeconds"],  out int sqt) ? sqt : 45;
                ScaleWatchdog       = int.TryParse(_configuration["ScaleSettings:HardwareWatchdogSeconds"], out int swd) ? swd : 60;
                DefaultToAutoMode   = bool.TryParse(_configuration["ScaleSettings:DefaultToAutoMode"],   out bool am) ? am  : true;
                DefaultToOnePassMode= bool.TryParse(_configuration["ScaleSettings:DefaultToOnePassMode"], out bool op) ? op  : true;
                IsOfficeMode        = bool.TryParse(_configuration["IsOfficeMode"],                  out bool om) ? om  : false;

                // Relay
                RelayComPort       = _configuration["RelaySettings:ComPort"];
                RelayBaudRate      = int.TryParse(_configuration["RelaySettings:BaudRate"],     out int rb)  ? rb  : 9600;
                RelayDataBits      = int.TryParse(_configuration["RelaySettings:DataBits"],     out int rdb) ? rdb : 8;
                RelayParity        = _configuration["RelaySettings:Parity"]   ?? "None";
                RelayStopBits      = _configuration["RelaySettings:StopBits"] ?? "One";
                RelayAlarmDuration = int.TryParse(_configuration["RelaySettings:AlarmDurationMs"], out int rad) ? rad : 1500;

                // RFID Scale In
                RfidInPort     = _configuration["RfidSettings:ScaleIn:ComPort"];
                RfidInBaudRate = int.TryParse(_configuration["RfidSettings:ScaleIn:BaudRate"],  out int rib) ? rib : 9600;
                RfidInDataBits = int.TryParse(_configuration["RfidSettings:ScaleIn:DataBits"],  out int rid) ? rid : 8;
                RfidInParity   = _configuration["RfidSettings:ScaleIn:Parity"]   ?? "None";
                RfidInStopBits = _configuration["RfidSettings:ScaleIn:StopBits"] ?? "One";

                // RFID Scale Out
                RfidOutPort     = _configuration["RfidSettings:ScaleOut:ComPort"];
                RfidOutBaudRate = int.TryParse(_configuration["RfidSettings:ScaleOut:BaudRate"], out int rob) ? rob : 9600;
                RfidOutDataBits = int.TryParse(_configuration["RfidSettings:ScaleOut:DataBits"], out int rod) ? rod : 8;
                RfidOutParity   = _configuration["RfidSettings:ScaleOut:Parity"]   ?? "None";
                RfidOutStopBits = _configuration["RfidSettings:ScaleOut:StopBits"] ?? "One";

                // RFID Desk
                RfidDeskPort     = _configuration["RfidSettings:Desk:ComPort"];
                RfidDeskBaudRate = int.TryParse(_configuration["RfidSettings:Desk:BaudRate"],  out int rdb2) ? rdb2 : 9600;
                RfidDeskDataBits = int.TryParse(_configuration["RfidSettings:Desk:DataBits"],  out int rdd2) ? rdd2 : 8;
                RfidDeskParity   = _configuration["RfidSettings:Desk:Parity"]   ?? "None";
                RfidDeskStopBits = _configuration["RfidSettings:Desk:StopBits"] ?? "One";
            }
            catch (Exception ex)
            {
                _notificationService.LogError(ex, "Lỗi Load Config");
            }
        }

        [RelayCommand]
        private async Task SaveSystemConfigAsync()
        {
            try
            {
                string jsonPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                string jsonString = File.ReadAllText(jsonPath);
                var root = JsonNode.Parse(jsonString);

                if (root == null) root = new JsonObject();

                // ==========================================
                // 1. DB & Camera
                // ==========================================
                if (root["ConnectionStrings"] == null) root["ConnectionStrings"] = new JsonObject();
                root["ConnectionStrings"]["DefaultConnection"] = DbConnectionString;

                if (root["DatabaseSettings"] == null) root["DatabaseSettings"] = new JsonObject();
                root["DatabaseSettings"]["BackupFolderPath"] = BackupFolderPath;

                if (root["CameraSettings"] == null) root["CameraSettings"] = new JsonObject();
                root["CameraSettings"]["IpAddress"] = CamIpAddress;
                root["CameraSettings"]["RtspPort"]  = CamRtspPort;
                root["CameraSettings"]["Username"]  = CamUsername;
                root["CameraSettings"]["Password"]  = CamPassword;
                root["CameraSettings"]["RtspUrl"]   = CamRtspUrl;

                // ==========================================
                // 2. Scale Settings
                // ==========================================
                if (root["ScaleSettings"] == null) root["ScaleSettings"] = new JsonObject();
                var scale = root["ScaleSettings"];
                scale["Protocol"]              = ScaleProtocol;
                scale["ComPort"]               = ScaleComPort;
                scale["BaudRate"]              = ScaleBaudRate;
                scale["DataBits"]              = ScaleDataBits;
                scale["Parity"]               = ScaleParity;
                scale["StopBits"]             = ScaleStopBits;
                scale["DefaultProductName"]   = ScaleDefaultProduct;
                scale["MinWeightThreshold"]   = ScaleMinWeight;
                scale["StabilityDelta"]      = ScaleStabilityDelta;
                scale["RfidCooldownSeconds"]  = ScaleRfidCooldown;
                scale["QueueTimeoutSeconds"]  = ScaleQueueTimeout;
                scale["HardwareWatchdogSeconds"] = ScaleWatchdog;
                scale["DefaultToAutoMode"]    = DefaultToAutoMode;
                scale["DefaultToOnePassMode"] = DefaultToOnePassMode;

                root["IsOfficeMode"] = IsOfficeMode;

                // ==========================================
                // 3. Relay Settings
                // ==========================================
                if (root["RelaySettings"] == null) root["RelaySettings"] = new JsonObject();
                var relay = root["RelaySettings"];
                relay["ComPort"]         = RelayComPort;
                relay["BaudRate"]        = RelayBaudRate;
                relay["DataBits"]        = RelayDataBits;
                relay["Parity"]          = RelayParity;
                relay["StopBits"]        = RelayStopBits;
                relay["AlarmDurationMs"] = RelayAlarmDuration;

                // ==========================================
                // 4. RFID Settings
                // ==========================================
                if (root["RfidSettings"] == null) root["RfidSettings"] = new JsonObject();
                var rfid = root["RfidSettings"];

                if (rfid["ScaleIn"] == null)  rfid["ScaleIn"]  = new JsonObject();
                rfid["ScaleIn"]["ComPort"]  = RfidInPort;
                rfid["ScaleIn"]["BaudRate"] = RfidInBaudRate;
                rfid["ScaleIn"]["DataBits"] = RfidInDataBits;
                rfid["ScaleIn"]["Parity"]   = RfidInParity;
                rfid["ScaleIn"]["StopBits"] = RfidInStopBits;

                if (rfid["ScaleOut"] == null) rfid["ScaleOut"] = new JsonObject();
                rfid["ScaleOut"]["ComPort"]  = RfidOutPort;
                rfid["ScaleOut"]["BaudRate"] = RfidOutBaudRate;
                rfid["ScaleOut"]["DataBits"] = RfidOutDataBits;
                rfid["ScaleOut"]["Parity"]   = RfidOutParity;
                rfid["ScaleOut"]["StopBits"] = RfidOutStopBits;

                if (rfid["Desk"] == null) rfid["Desk"] = new JsonObject();
                rfid["Desk"]["ComPort"]  = RfidDeskPort;
                rfid["Desk"]["BaudRate"] = RfidDeskBaudRate;
                rfid["Desk"]["DataBits"] = RfidDeskDataBits;
                rfid["Desk"]["Parity"]   = RfidDeskParity;
                rfid["Desk"]["StopBits"] = RfidDeskStopBits;

                // ==========================================
                // LƯU FILE (Atomic write: temp → rename)
                // ==========================================
                // Ghi ra .tmp trước, sau đó rename — nếu crash giữa chừng file gốc còn nguyên
                var options = new JsonSerializerOptions { WriteIndented = true };
                string outputJson = root.ToJsonString(options);
                string tempPath = jsonPath + ".tmp";
                File.WriteAllText(tempPath, outputJson, System.Text.Encoding.UTF8);
                File.Move(tempPath, jsonPath, overwrite: true); // atomic rename trên NTFS

                // Đồng bộ ngược lại file gốc nếu đang chạy trong Debug
                try
                {
                    string baseDir = AppDomain.CurrentDomain.BaseDirectory;
                    if (baseDir.Contains("bin\\Debug") || baseDir.Contains("bin/Debug"))
                    {
                        var projectRoot = Directory.GetParent(baseDir)?.Parent?.Parent?.Parent?.FullName;
                        if (projectRoot != null)
                        {
                            string sourceJsonPath = Path.Combine(projectRoot, "appsettings.json");
                            if (File.Exists(sourceJsonPath))
                            {
                                string srcTmp = sourceJsonPath + ".tmp";
                                File.WriteAllText(srcTmp, outputJson, System.Text.Encoding.UTF8);
                                File.Move(srcTmp, sourceJsonPath, overwrite: true);
                            }
                        }
                    }
                }
                catch { /* Bỏ qua nếu lỗi phân quyền ghi file hệ thống */ }

                // ==========================================
                // REINITIALIZE HARDWARE (không cần restart app)
                // ==========================================
                await ReinitializeHardwareAsync();
            }
            catch (Exception ex)
            {
                _notificationService.ShowError(UiText.Messages.SaveConfigError(ex.Message), UiText.Titles.Error);
            }
        }

        /// <summary>
        /// Khởi động lại ScaleService và RfidMultiService với thông số mới từ form Settings.
        /// Vì cả 2 là Singleton, event subscription ở ViewModel và Coordinator vẫn còn nguyên.
        /// Phương thức chạy trên background thread để không block UI.
        /// </summary>
        private async Task ReinitializeHardwareAsync()
        {
            await Task.Run(() =>
            {
                bool scaleOk = false;
                bool rfidOk  = false;

                // --- 1. Reinitialize Scale ---
                try
                {
                        if (ScaleComPort == "None")
                        {
                            _scaleService.Close();
                            scaleOk = true;
                        }
                        else
                        {
                            Enum.TryParse(ScaleParity,   ignoreCase: true, out Parity   parity);
                            Enum.TryParse(ScaleStopBits, ignoreCase: true, out StopBits stopBits);

                            IScaleProtocol protocol = _protocolFactory.Create(ScaleProtocol);

                            _scaleService.Reinitialize(
                                ScaleComPort, ScaleBaudRate, ScaleDataBits,
                                parity, stopBits, protocol, ScaleMinWeight, ScaleStabilityDelta);
                            scaleOk = true;
                        }
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "[SETTINGS] Lỗi reinitialize ScaleService");
                }

                // --- 2. Reinitialize RFID ---
                try
                {
                    _rfidService.ReinitializeReaders(
                        RfidInPort,   RfidInBaudRate,
                        RfidOutPort,  RfidOutBaudRate,
                        RfidDeskPort, RfidDeskBaudRate);
                    rfidOk = true;
                }
                catch (Exception ex)
                {
                    Serilog.Log.Error(ex, "[SETTINGS] Lỗi reinitialize RfidMultiService");
                }

                // --- 3. Hiển thị kết quả ---
                string scaleMsg = scaleOk
                    ? $"✅ Đầu cân: {ScaleComPort} ({ScaleBaudRate} bps)"
                    : "⚠️ Đầu cân: lỗi khởi động (xem Log)";
                string rfidMsg  = rfidOk
                    ? $"✅ RFID: {_rfidService.ActiveReaderCount} đầu đọc"
                    : "⚠️ RFID: lỗi khởi động (xem Log)";

                Application.Current?.Dispatcher.Invoke(() =>
                    _notificationService.ShowInfo(
                        $"Đã lưu cấu hình và khởi động lại phần cứng:\n{scaleMsg}\n{rfidMsg}",
                        "LƯU & KHỞI ĐỘNG LẠI THÀNH CÔNG"));
            });
        }

        [RelayCommand]
        private void RefreshComPorts()
        {
            try
            {
                var ports = SerialPort.GetPortNames().OrderBy(p => p).ToList();
                ports.Insert(0, "None");
                AvailableComPorts = new ObservableCollection<string>(ports);
                _notificationService.ShowInfo($"Đã tìm thấy {ports.Count - 1} cổng COM đang cắm trên hệ thống.", "LÀM MỚI DANH SÁCH COM");
            }
            catch (Exception ex)
            {
                _notificationService.LogError(ex, "Lỗi quét cổng COM");
            }
        }

        [RelayCommand]
        private async Task LoadUsersAsync()
        {
            using var db = _dbContextFactory.CreateDbContext();
            var users = await db.Users.AsNoTracking().OrderBy(u => u.Id).ToListAsync();
            Application.Current?.Dispatcher.Invoke(() => { UserList = new ObservableCollection<User>(users); });
        }

        [RelayCommand]
        private async Task BackupDatabaseAsync()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(BackupFolderPath))
                {
                    _notificationService.ShowWarning("Vui lòng cấu hình thư mục lưu Backup trước.", "CHÚ Ý");
                    return;
                }

                if (!Directory.Exists(BackupFolderPath))
                {
                    Directory.CreateDirectory(BackupFolderPath);
                }

                string fileName = $"AutoWeighbridgeDB_Backup_{DateTime.Now:yyyyMMdd_HHmmss}.bak";
                string fullPath = Path.Combine(BackupFolderPath, fileName);

                using var db = _dbContextFactory.CreateDbContext();
                
                string dbName = db.Database.GetDbConnection().Database;
                string sql = $"BACKUP DATABASE [{dbName}] TO DISK = '{fullPath}'";

                await db.Database.ExecuteSqlRawAsync(sql);

                _notificationService.ShowInfo($"Đã sao lưu thành công CSDL vào:\n{fullPath}", "BACKUP HOÀN TẤT");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Lỗi khi sao lưu: {ex.Message}\n(Lưu ý: SQL Server cần có quyền Write vào thư mục này)", "LỖI BACKUP");
                Serilog.Log.Error(ex, "[BACKUP] Lỗi khi sao lưu bằng tay");
            }
        }

        // =========================================================================
        // TÍNH NĂNG KẾT NỐI DATABASE (SCAN & MANUAL)
        // =========================================================================

        [RelayCommand]
        private async Task DiscoverServersAsync()
        {
            IsScanning = true;
            DiscoveredServers.Clear();
            _notificationService.ShowInfo("Đang quét các máy chủ SQL trong mạng nội bộ...", "ĐANG QUÉT");

            try
            {
                await Task.Run(async () =>
                {
                    using (var udpClient = new UdpClient())
                    {
                        udpClient.EnableBroadcast = true;
                        var requestData = new byte[] { 0x02 }; // SQL Browser discovery packet
                        var serverEndPoint = new IPEndPoint(IPAddress.Broadcast, 1434);

                        // Gửi gói tin quảng bá
                        await udpClient.SendAsync(requestData, requestData.Length, serverEndPoint);

                        // Chờ phản hồi trong 3 giây
                        var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
                        
                        while (!cts.IsCancellationRequested)
                        {
                            try
                            {
                                // Sử dụng Task.WaitAsync để có timeout
                                var receiveTask = udpClient.ReceiveAsync();
                                var result = await receiveTask.WaitAsync(cts.Token);
                                
                                var response = Encoding.ASCII.GetString(result.Buffer);
                                
                                // Format phản hồi: ServerName;NAME;InstanceName;INST;...;;
                                var parts = response.Split(';');
                                string server = "";
                                string instance = "";
                                
                                for (int i = 0; i < parts.Length - 1; i++)
                                {
                                    if (parts[i].Equals("ServerName", StringComparison.OrdinalIgnoreCase)) server = parts[i + 1];
                                    if (parts[i].Equals("InstanceName", StringComparison.OrdinalIgnoreCase)) instance = parts[i + 1];
                                }

                                if (!string.IsNullOrEmpty(server))
                                {
                                    string fullName = string.IsNullOrEmpty(instance) ? server : $"{server}\\{instance}";
                                    Application.Current.Dispatcher.Invoke(() =>
                                    {
                                        if (!DiscoveredServers.Contains(fullName))
                                            DiscoveredServers.Add(fullName);
                                    });
                                }
                            }
                            catch (OperationCanceledException) { break; }
                            catch { /* Bỏ qua gói tin rác hoặc lỗi nhận */ }
                        }
                    }
                });

                if (DiscoveredServers.Count == 0)
                {
                    _notificationService.ShowWarning("Không tìm thấy máy chủ nào. Hãy đảm bảo dịch vụ 'SQL Server Browser' đã được bật tại máy trạm.", "KẾT QUẢ");
                }
                else
                {
                    _notificationService.ShowInfo($"Tìm thấy {DiscoveredServers.Count} máy chủ SQL Server.", "HOÀN TẤT");
                }
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("Lỗi khi quét máy chủ: " + ex.Message, "LỖI");
            }
            finally
            {
                IsScanning = false;
            }
        }

        [RelayCommand]
        private void BuildConnectionString()
        {
            if (string.IsNullOrWhiteSpace(DbServerName) || string.IsNullOrWhiteSpace(DbName))
            {
                _notificationService.ShowWarning("Vui lòng nhập tên Server và tên Cơ sở dữ liệu.", "THIẾU THÔNG TIN");
                return;
            }

            // Xây dựng connection string theo chuẩn SQL Server Auth (Kèm Timeout 5s chống treo)
            string conn = $"Server={DbServerName};Database={DbName};User Id={DbUser};Password={DbPassword};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5;";
            DbConnectionString = conn;
            _notificationService.ShowInfo("Đã tổng hợp chuỗi kết nối mới. Hãy nhấn 'Lưu toàn bộ' để áp dụng.", "THÀNH CÔNG");
        }

        [RelayCommand]
        private async Task TestConnectionAsync()
        {
            if (string.IsNullOrWhiteSpace(DbServerName) || string.IsNullOrWhiteSpace(DbName))
            {
                _notificationService.ShowWarning("Vui lòng điền đủ thông tin Server và Database để kiểm tra.", "CHÚ Ý");
                return;
            }

            string testConn = $"Server={DbServerName};Database={DbName};User Id={DbUser};Password={DbPassword};Encrypt=True;TrustServerCertificate=True;Connect Timeout=5;";

            try
            {
                var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
                optionsBuilder.UseSqlServer(testConn);

                using var db = new AppDbContext(optionsBuilder.Options);
                bool canConnect = await db.Database.CanConnectAsync();

                if (canConnect)
                    _notificationService.ShowInfo("Kết nối tới Database thành công!", "THÀNH CÔNG");
                else
                    _notificationService.ShowWarning("Không thể kết nối tới Database. Vui lòng kiểm tra lại IP/Tên máy, User/Pass và tường lửa.", "THẤT BẠI");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("Lỗi kết nối: " + ex.Message, "LỖI");
            }
        }
    }
}