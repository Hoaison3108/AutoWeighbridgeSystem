using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using AutoWeighbridgeSystem.Services.Protocols;
using AutoWeighbridgeSystem.ViewModels;
using AutoWeighbridgeSystem.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.IO;
using System.IO.Ports;
using System.Windows;
using System.Threading; // THÊM THƯ VIỆN NÀY CHO MUTEX
using LibVLCSharp.Shared;

namespace AutoWeighbridgeSystem
{
    public partial class App : Application
    {
        public static IServiceProvider ServiceProvider { get; private set; }
        public IConfiguration Configuration { get; private set; }

        // Biến Mutex toàn cục để kiểm tra app đang chạy
        private static Mutex _mutex = null;

        protected override void OnStartup(StartupEventArgs e)
        {
            // =======================================================
            // 1. CƠ CHẾ CHỐNG MỞ 2 APP CÙNG LÚC (Fix lỗi chiếm cổng COM)
            // =======================================================
            const string appName = "RangDong_AutoWeighbridgeSystem_Unique";
            bool createdNew;
            _mutex = new Mutex(true, appName, out createdNew);

            if (!createdNew)
            {
                MessageBox.Show("Phần mềm Trạm Cân đang được mở rồi!\nVui lòng kiểm tra dưới thanh Taskbar.",
                                "Cảnh báo", MessageBoxButton.OK, MessageBoxImage.Warning);
                Application.Current.Shutdown();
                return; // Ngừng khởi động ngay lập tức
            }

            base.OnStartup(e);

            // Khởi tạo LibVLC
            Core.Initialize();

            // =======================================================
            // 2. KHỞI TẠO LOG & CÁC DỊCH VỤ CỐT LÕI
            // =======================================================
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Debug()
                .WriteTo.File(
                    path: "Logs/TramCanLog_.txt",
                    rollingInterval: RollingInterval.Day,
                    retainedFileCountLimit: 30,
                    outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {Message:lj}{NewLine}{Exception}")
                .CreateLogger();

            Log.Information("=== HỆ THỐNG TRẠM CÂN BẮT ĐẦU KHỞI ĐỘNG ===");

            try
            {
                Configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                var serviceCollection = new ServiceCollection();
                ConfigureServices(serviceCollection);
                ServiceProvider = serviceCollection.BuildServiceProvider();

                // Chế độ tắt máy thủ công để tránh app tự đóng khi SplashWindow đóng
                Current.ShutdownMode = ShutdownMode.OnExplicitShutdown;

                // =======================================================
                // 3. HIỂN THỊ MÀN HÌNH CHỜ (SPLASH) & KHỞI TẠO SONG SONG
                // =======================================================
                var splashVm = ServiceProvider.GetRequiredService<SplashViewModel>();
                var splashWindow = ServiceProvider.GetRequiredService<SplashWindow>();
                splashWindow.DataContext = splashVm;
                splashWindow.ShowDialog(); 

                // KHỞI CHẠY DỊCH VỤ TỰ ĐỘNG HÓA NGẦM (Bất kể View nào)
                ServiceProvider.GetRequiredService<BackgroundAutomationService>();
                
                // KHỞI CHẠY LẬP LỊCH ĐỒNG BỘ GOOGLE SHEETS
                ServiceProvider.GetRequiredService<GoogleSheetsSyncWorker>().Start();
                // =======================================================
                // 4. HIỂN THỊ MÀN HÌNH ĐĂNG NHẬP
                // =======================================================
                // Trả về chế độ đóng app khi hết window
                Current.ShutdownMode = ShutdownMode.OnLastWindowClose;
                
                var loginWindow = ServiceProvider.GetRequiredService<LoginWindow>();
                Current.MainWindow = loginWindow;
                loginWindow.Show();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Lỗi nghiêm trọng khi khởi động ứng dụng!");
                MessageBox.Show("Không thể khởi động hệ thống. Vui lòng kiểm tra Logs.\nChi tiết: " + ex.Message,
                                "Lỗi Hệ Thống", MessageBoxButton.OK, MessageBoxImage.Error);
                Current.Shutdown();
            }
        }

        private void ConfigureServices(IServiceCollection services)
        {
            // (Đoạn này giữ nguyên 100% code cũ của Sơn, không thay đổi logic)
            // --- Nhóm 1: Cấu hình & Database ---
            services.AddSingleton<IConfiguration>(Configuration);
            services.AddDbContextFactory<AppDbContext>(options =>
            {
                options.UseSqlServer(
                    Configuration.GetConnectionString("DefaultConnection"),
                    sqlOptions =>
                    {
                        // Tự động retry khi gặp lỗi SQL transient (timeout, connection reset, deadlock...)
                        // maxRetryCount: thử lại tối đa 3 lần sau lần thất bại đầu tiên
                        // maxRetryDelay: chờ tối đa 10 giây giữa mỗi lần retry (EF Core tự tính exponential backoff)
                        sqlOptions.EnableRetryOnFailure(
                            maxRetryCount: 3,
                            maxRetryDelay: TimeSpan.FromSeconds(10),
                            errorNumbersToAdd: null); // null = dùng danh sách lỗi transient mặc định của SQL Server

                        // Timeout mỗi lệnh SQL tối đa 30 giây (mặc định là 30s, đặt tường minh cho rõ)
                        sqlOptions.CommandTimeout(30);
                    });
            });

            // --- Nhóm 2: Hardware & Session Services (Singleton) ---
            services.AddSingleton<RelayService>();
            services.AddSingleton<AppSession>();
            services.AddSingleton<RfidBusinessService>();
            services.AddSingleton<NotificationManagerService>();
            services.AddSingleton<AlarmService>();
            services.AddSingleton<SignalLightService>();
            services.AddSingleton<WeighingBusinessService>();
            services.AddSingleton<DashboardWorkflowService>();
            services.AddSingleton<DashboardSaveService>();
            services.AddSingleton<DashboardDataService>();
            services.AddSingleton<HardwareWatchdogService>();
            services.AddSingleton<DashboardEventCoordinator>();
            services.AddSingleton<IUserNotificationService, UserNotificationService>();
            services.AddSingleton<SystemClockService>();
            services.AddSingleton<BackgroundAutomationService>();
            services.AddSingleton<ViewTrackerService>();
            services.AddSingleton<CameraService>();
            services.AddSingleton<GoogleSheetsExportService>();
            services.AddSingleton<GoogleSheetsSyncWorker>();

            services.AddSingleton<ScaleService>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var scaleService = new ScaleService();

                try
                {
                    var section = config.GetSection("ScaleSettings");
                    string port = section["ComPort"];

                    if (!string.IsNullOrEmpty(port) && port != "None")
                    {
                        int baud = int.TryParse(section["BaudRate"], out int b) ? b : 2400;
                        int dataBits = int.TryParse(section["DataBits"], out int d) ? d : 7;
                        Enum.TryParse(section["Parity"] ?? "Even", out Parity parity);
                        Enum.TryParse(section["StopBits"] ?? "One", out StopBits stopBits);

                        string protocolName = section["Protocol"] ?? "VishayVT220";

                        var protocolFactory = provider.GetRequiredService<IScaleProtocolFactory>();
                        IScaleProtocol selectedProtocol = protocolFactory.Create(protocolName);

                        decimal minWeight = decimal.TryParse(section["MinWeightThreshold"], out decimal mw) ? mw : 50;
                        decimal stableDelta = decimal.TryParse(section["StabilityDelta"], out decimal sd) ? sd : 50;

                        scaleService.Initialize(port, baud, dataBits, parity, stopBits, selectedProtocol, minWeight, stableDelta);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[APP] Lỗi cấu hình đầu cân");
                }

                return scaleService;
            });

            services.AddSingleton<RfidMultiService>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var rfidService = new RfidMultiService();

                string inPort   = config["RfidSettings:ScaleIn:ComPort"];
                string outPort  = config["RfidSettings:ScaleOut:ComPort"];
                string deskPort = config["RfidSettings:Desk:ComPort"];

                // Mỗi đầu đọc sử dụng BaudRate riêng của mình (không dùng chung)
                int inBaud   = int.TryParse(config["RfidSettings:ScaleIn:BaudRate"],  out int ib) ? ib  : 9600;
                int outBaud  = int.TryParse(config["RfidSettings:ScaleOut:BaudRate"], out int ob) ? ob  : 9600;
                int deskBaud = int.TryParse(config["RfidSettings:Desk:BaudRate"],     out int db) ? db  : 9600;

                if (!string.IsNullOrEmpty(deskPort) && deskPort != "None")
                    rfidService.AddReader(ReaderRoles.Desk,     deskPort, deskBaud);
                if (!string.IsNullOrEmpty(inPort) && inPort != "None")
                    rfidService.AddReader(ReaderRoles.ScaleIn,  inPort,   inBaud);
                if (!string.IsNullOrEmpty(outPort) && outPort != "None")
                    rfidService.AddReader(ReaderRoles.ScaleOut, outPort,  outBaud);

                return rfidService;
            });

            // --- Nhóm 3: ViewModels (Logic nghiệp vụ) ---
            services.AddSingleton<IScaleProtocolFactory, ScaleProtocolFactory>();
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<IExportService, ExcelExportService>();
            services.AddSingleton<DashboardViewModel>();
            // DashboardViewModel nhận factory delegate thay vì IDbContextFactory trực tiếp.
            // Delegate này được DI resolve và truyền các dependency cần thiết vào QuickVehicleRegisterViewModel.
            services.AddSingleton<Func<string, QuickVehicleRegisterViewModel>>(sp => licensePlate =>
                new QuickVehicleRegisterViewModel(
                    licensePlate,
                    sp.GetRequiredService<Microsoft.EntityFrameworkCore.IDbContextFactory<Data.AppDbContext>>(),
                    sp.GetRequiredService<IUserNotificationService>(),
                    sp.GetRequiredService<ScaleService>()));

            services.AddSingleton<VehicleRegistrationViewModel>();
            services.AddTransient<ManualTicketViewModel>();
            services.AddTransient<EditTicketViewModel>();
            services.AddTransient<SplashViewModel>();
            services.AddSingleton<LoginViewModel>();

            // --- Nhóm 4: Views (Giao diện) ---
            services.AddSingleton<MainWindow>();
            services.AddTransient<SplashWindow>();
            services.AddTransient<LoginWindow>();
            services.AddSingleton<DashboardView>();
            services.AddSingleton<VehicleRegistrationView>();
 
            services.AddSingleton<CustomerViewModel>();
            services.AddSingleton<CustomerView>();
            services.AddSingleton<ProductViewModel>();
            services.AddSingleton<ProductView>();
            services.AddSingleton<SettingsViewModel>();
            services.AddSingleton<SettingsView>();
            services.AddSingleton<NotificationHistoryViewModel>();
            services.AddSingleton<NotificationHistoryView>();
            services.AddSingleton<WeighingHistoryViewModel>();
            services.AddSingleton<WeighingHistoryView>();
        }

        protected override void OnExit(ExitEventArgs e)
        {
            Log.Information("=== HỆ THỐNG TRẠM CÂN BẮT ĐẦU ĐÓNG KẾT NỐI ===");

            try
            {
                // =======================================================
                // 3. CHỦ ĐỘNG ĐÓNG CÁC CỔNG COM TRƯỚC TIÊN (Fix lỗi Access Denied)
                // =======================================================
                if (ServiceProvider != null)
                {
                    var rfidService = ServiceProvider.GetService<RfidMultiService>();
                    if (rfidService != null)
                    {
                        rfidService.CloseAll();
                        Log.Information("[APP] Đã ngắt kết nối an toàn các đầu đọc RFID.");
                    }

                    var scaleService = ServiceProvider.GetService<ScaleService>();
                    if (scaleService != null)
                    {
                        scaleService.Close();
                        Log.Information("[APP] Đã ngắt kết nối an toàn với Đầu cân.");
                    }

                    var relayService = ServiceProvider.GetService<RelayService>();
                    if (relayService != null)
                    {
                        relayService.Close();
                        Log.Information("[APP] Đã ngắt kết nối an toàn với mạch Relay (Chuông).");
                    }

                    var signalLightService = ServiceProvider.GetService<SignalLightService>();
                    if (signalLightService != null)
                    {
                        signalLightService.Dispose();
                        Log.Information("[APP] Đã giải phóng SignalLightService (Relay đèn tín hiệu).");
                    }

                    var googleSheetsWorker = ServiceProvider.GetService<GoogleSheetsSyncWorker>();
                    if (googleSheetsWorker != null)
                    {
                        googleSheetsWorker.Dispose();
                        Log.Information("[APP] Đã dừng luồng đồng bộ Google Sheets.");
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[APP] Lỗi khi cố gắng đóng cổng COM lúc tắt phần mềm.");
            }

            // Giải phóng DI Container
            if (ServiceProvider is IDisposable disposableProvider)
            {
                disposableProvider.Dispose();
            }

            Log.Information("=== HỆ THỐNG TRẠM CÂN ĐÃ TẮT HOÀN TOÀN ===");
            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}