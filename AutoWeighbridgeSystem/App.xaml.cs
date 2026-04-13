using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using AutoWeighbridgeSystem.Services.Protocols; // Thêm namespace Protocols
using AutoWeighbridgeSystem.ViewModels;
using AutoWeighbridgeSystem.Views;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using System;
using System.IO;
using System.IO.Ports; // Thêm thư viện COM Port
using System.Windows;

namespace AutoWeighbridgeSystem
{
    public partial class App : Application
    {
        // Quản lý toàn bộ các Service (DI Container)
        public static IServiceProvider ServiceProvider { get; private set; }

        // Quản lý cấu hình (đọc từ appsettings.json)
        public IConfiguration Configuration { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            base.OnStartup(e);

            // 1. Cấu hình hệ thống Ghi Log chuyên nghiệp
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
                // 2. Khởi tạo Configuration: Đọc appsettings.json
                Configuration = new ConfigurationBuilder()
                    .SetBasePath(Directory.GetCurrentDirectory())
                    .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
                    .Build();

                // 3. Khởi tạo Dependency Injection (DI)
                var serviceCollection = new ServiceCollection();
                ConfigureServices(serviceCollection);
                ServiceProvider = serviceCollection.BuildServiceProvider();

                // 4. Tự động Migration & Seeding: Đảm bảo DB luôn sẵn sàng khi mở App
                using (var scope = ServiceProvider.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<AppDbContext>();
                    dbContext.Database.Migrate();
                }

                // Ép hệ thống khởi tạo RFID Service và Scale Service ngay lập tức
                ServiceProvider.GetRequiredService<ScaleService>();
                ServiceProvider.GetRequiredService<RfidMultiService>();

                // 5. Khởi chạy màn hình đăng nhập
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

            // CẬP NHẬT: Cấu hình ScaleService linh hoạt (Strategy Pattern)
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
                        // Đọc thông số kết nối (mặc định cho Vishay là 2400-7-Even-1)
                        int baud = int.TryParse(section["BaudRate"], out int b) ? b : 2400;
                        int dataBits = int.TryParse(section["DataBits"], out int d) ? d : 7;
                        Enum.TryParse(section["Parity"] ?? "Even", out Parity parity);
                        Enum.TryParse(section["StopBits"] ?? "One", out StopBits stopBits);

                        // Đọc tên chuẩn từ file cấu hình (mặc định Vishay)
                        string protocolName = section["Protocol"] ?? "VishayVT220";

                        IScaleProtocol selectedProtocol = protocolName switch
                        {
                            "VishayVT220" => new VishayVT220protocol(),
                            _ => new VishayVT220protocol()
                        };

                        // Khởi tạo Service với chuẩn và cấu hình đọc được
                        scaleService.Initialize(port, baud, dataBits, parity, stopBits, selectedProtocol);
                    }
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[APP] Lỗi cấu hình đầu cân");
                }

                return scaleService;
            });

            // Cấu hình RfidMultiService tự động mở cổng COM từ appsettings.json
            services.AddSingleton<RfidMultiService>(provider =>
            {
                var config = provider.GetRequiredService<IConfiguration>();
                var rfidService = new RfidMultiService();

                string inPort = config["RfidSettings:ScaleIn:ComPort"];
                string outPort = config["RfidSettings:ScaleOut:ComPort"];
                string deskPort = config["RfidSettings:Desk:ComPort"];

                if (!int.TryParse(config["RfidSettings:Desk:BaudRate"], out int baudRate))
                {
                    baudRate = 9600;
                }

                if (!string.IsNullOrEmpty(deskPort))
                    rfidService.AddReader(ReaderRoles.Desk, deskPort, baudRate);

                if (!string.IsNullOrEmpty(inPort))
                    rfidService.AddReader(ReaderRoles.ScaleIn, inPort, baudRate);

                if (!string.IsNullOrEmpty(outPort))
                    rfidService.AddReader(ReaderRoles.ScaleOut, outPort, baudRate);

                return rfidService;
            });

            // --- Nhóm 3: ViewModels (Logic nghiệp vụ) ---
            services.AddSingleton<MainViewModel>();
            services.AddSingleton<IExportService, ExcelExportService>();
            services.AddSingleton<DashboardViewModel>();
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
            Log.Information("=== HỆ THỐNG TRẠM CÂN ĐÓNG KẾT NỐI ===");

            if (ServiceProvider is IDisposable disposableProvider)
            {
                disposableProvider.Dispose();
            }

            Log.CloseAndFlush();
            base.OnExit(e);
        }
    }
}