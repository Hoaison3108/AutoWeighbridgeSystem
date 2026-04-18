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

                using (var scope = ServiceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    dbContext.Database.Migrate();
                }

                // Ép hệ thống khởi tạo RFID Service và Scale Service
                ServiceProvider.GetRequiredService<ScaleService>();
                ServiceProvider.GetRequiredService<RfidMultiService>();

                var loginWindow = ServiceProvider.GetRequiredService<LoginWindow>();
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
                options.UseSqlServer(Configuration.GetConnectionString("DefaultConnection"));
            });

            // --- Nhóm 2: Hardware & Session Services (Singleton) ---
            services.AddSingleton<RelayService>();
            services.AddSingleton<AppSession>();
            services.AddSingleton<RfidBusinessService>();
            services.AddSingleton<AlarmService>();
            services.AddSingleton<WeighingBusinessService>();
            services.AddSingleton<DashboardWorkflowService>();
            services.AddSingleton<DashboardSaveService>();
            services.AddSingleton<DashboardDataService>();
            services.AddSingleton<HardwareWatchdogService>();
            services.AddSingleton<DashboardEventCoordinator>();
            services.AddSingleton<IUserNotificationService, UserNotificationService>();
            services.AddSingleton<SystemClockService>();

            services.AddSingleton<ScaleService>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var scaleService = new ScaleService();

                try
                {
                    var section = config.GetSection("ScaleSettings");
                    string port = section["ComPort"];

                    if (!string.IsNullOrEmpty(port))
                    {
                        int baud = int.TryParse(section["BaudRate"], out int b) ? b : 2400;
                        int dataBits = int.TryParse(section["DataBits"], out int d) ? d : 7;
                        Enum.TryParse(section["Parity"] ?? "Even", out Parity parity);
                        Enum.TryParse(section["StopBits"] ?? "One", out StopBits stopBits);

                        string protocolName = section["Protocol"] ?? "VishayVT220";

                        var protocolFactory = provider.GetRequiredService<IScaleProtocolFactory>();
                        IScaleProtocol selectedProtocol = protocolFactory.Create(protocolName);

                        scaleService.Initialize(port, baud, dataBits, parity, stopBits, selectedProtocol);
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

                if (!string.IsNullOrEmpty(deskPort))
                    rfidService.AddReader(ReaderRoles.Desk,     deskPort, deskBaud);
                if (!string.IsNullOrEmpty(inPort))
                    rfidService.AddReader(ReaderRoles.ScaleIn,  inPort,   inBaud);
                if (!string.IsNullOrEmpty(outPort))
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
            services.AddTransient<LoginViewModel>();

            // --- Nhóm 4: Views (Giao diện) ---
            services.AddSingleton<MainWindow>();
            services.AddTransient<LoginWindow>();
            services.AddTransient<DashboardView>();
            services.AddTransient<VehicleRegistrationView>();

            services.AddSingleton<CustomerViewModel>();
            services.AddTransient<CustomerView>();
            services.AddSingleton<ProductViewModel>();
            services.AddTransient<ProductView>();
            services.AddSingleton<SettingsViewModel>();
            services.AddTransient<SettingsView>();
            services.AddTransient<WeighingHistoryViewModel>();
            services.AddTransient<WeighingHistoryView>();
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
                        // Giả định ScaleService của bạn có hàm Close() hoặc Disconnect()
                        // Nếu tên hàm khác, bạn đổi lại tên hàm cho đúng nhé
                        scaleService.Close();
                        Log.Information("[APP] Đã ngắt kết nối an toàn với Đầu cân.");
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