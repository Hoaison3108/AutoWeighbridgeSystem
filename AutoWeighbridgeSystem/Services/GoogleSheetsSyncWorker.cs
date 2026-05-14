using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Background Worker lập lịch đồng bộ dữ liệu lên Google Sheets.
    /// Chạy mỗi 30 phút bắt đầu từ 7:30.
    /// </summary>
    public class GoogleSheetsSyncWorker : IDisposable
    {
        private readonly GoogleSheetsExportService _exportService;
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;
        private readonly SystemClockService _systemClock;
        private readonly IUserNotificationService _notificationService;
        private readonly Microsoft.Extensions.Configuration.IConfiguration _configuration;
        private CancellationTokenSource? _cts;
        private Task? _backgroundTask;

        public GoogleSheetsSyncWorker(
            GoogleSheetsExportService exportService,
            IDbContextFactory<AppDbContext> dbContextFactory,
            SystemClockService systemClock,
            IUserNotificationService notificationService,
            Microsoft.Extensions.Configuration.IConfiguration configuration)
        {
            _exportService = exportService;
            _dbContextFactory = dbContextFactory;
            _systemClock = systemClock;
            _notificationService = notificationService;
            _configuration = configuration;
        }

        public void Start()
        {
            _cts = new CancellationTokenSource();
            _backgroundTask = Task.Run(() => WorkerLoopAsync(_cts.Token), _cts.Token);
            Log.Information("[GOOGLE_SHEETS_WORKER] Đã khởi động luồng lập lịch đồng bộ ngầm.");
        }

        private async Task WorkerLoopAsync(CancellationToken token)
        {
            // Trễ khởi động một chút để hệ thống chính ổn định trước
            await Task.Delay(5000, token);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    var now = _systemClock.CurrentTime;

                    // Đọc cấu hình bật/tắt (mặc định là true)
                    string? enableStr = _configuration["GoogleSheets:EnableAutoSync"];
                    bool enableAutoSync = true;
                    if (!string.IsNullOrEmpty(enableStr))
                    {
                        bool.TryParse(enableStr, out enableAutoSync);
                    }

                    // Chỉ đồng bộ từ 7:00 sáng trở đi, không giới hạn giờ kết thúc và khi tính năng bật
                    if (enableAutoSync && now.TimeOfDay >= new TimeSpan(7, 0, 0))
                    {
                        // Kiểm tra phút thứ 00 hoặc 30
                        if (now.Minute == 0 || now.Minute == 30)
                        {
                            Log.Information("[GOOGLE_SHEETS_WORKER] Bắt đầu đồng bộ tự động lúc {Time}", now.ToString("HH:mm"));
                            await PerformSyncAsync(now.Date);

                            // Đợi qua phút hiện tại để không kích hoạt nhiều lần trong cùng 1 phút
                            await Task.Delay(TimeSpan.FromMinutes(1), token);
                        }
                    }

                    // Chờ 10 giây rồi kiểm tra lại (để giảm tải CPU)
                    await Task.Delay(10000, token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[GOOGLE_SHEETS_WORKER] Lỗi không xác định trong vòng lặp.");
                    await Task.Delay(30000, token); // Đợi 30s rồi thử lại
                }
            }
        }

        /// <summary>
        /// Thực thi lệnh lấy dữ liệu ngày cụ thể và đẩy lên Google Sheets
        /// </summary>
        public async Task PerformSyncAsync(DateTime targetDate)
        {
            try
            {
                using var db = await _dbContextFactory.CreateDbContextAsync();

                // Lấy toàn bộ phiếu cân hợp lệ (đã hoàn thành và không bị hủy) trong ngày mục tiêu
                var tickets = await db.WeighingTickets
                    .AsNoTracking()
                    .Where(t => t.TimeIn.Date == targetDate.Date && t.TimeOut != null && !t.IsVoid)
                    .OrderBy(t => t.TimeIn)
                    .ToListAsync();

                // Kiểm tra kết quả trả về — thất bại thì thông báo rõ ràng lên UI
                bool syncSuccess = await _exportService.SyncDailyTicketsAsync(tickets);
                if (!syncSuccess)
                {
                    Log.Warning("[GOOGLE_SHEETS_WORKER] Đồng bộ tự động thất bại lúc {Time}.", targetDate.ToString("HH:mm dd/MM/yyyy"));
                    _notificationService.ShowError(
                        $"Đồng bộ tự động Google Sheets lúc {DateTime.Now:HH:mm} thất bại!\n" +
                        "• Kiểm tra file credentials.json\n" +
                        "• Kiểm tra kết nối Internet\n" +
                        "Chi tiết xem trong file log.",
                        "ĐỒNG BỘ TỰ ĐỘNG THẤT BẠI");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GOOGLE_SHEETS_WORKER] Lỗi khi trích xuất dữ liệu Database để đồng bộ.");
                _notificationService.ShowError(
                    $"Lỗi đồng bộ tự động Google Sheets: {ex.Message}",
                    "LỖI HỆ THỐNG");
            }
        }

        public void Stop()
        {
            if (_cts != null && !_cts.IsCancellationRequested)
            {
                _cts.Cancel();
            }
        }

        public void Dispose()
        {
            Stop();
            _cts?.Dispose();
        }
    }
}
