using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.IO.Ports;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Models;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ điều khiển đèn tín hiệu (Signal Light) thông qua mạch Relay riêng biệt.
    /// Kích hoạt khi đầu đọc RFID tại trạm cân (ScaleIn/ScaleOut) nhận được tín hiệu thẻ.
    ///
    /// THIẾT KẾ QUAN TRỌNG: Service này sở hữu một <see cref="RelayService"/> riêng (private),
    /// KHÔNG dùng chung RelayService với <see cref="AlarmService"/>. Điều này đảm bảo:
    ///   - Lock của đèn và lock của chuông hoàn toàn độc lập.
    ///   - Đèn và chuông có thể kích hoạt đồng thời mà không block lẫn nhau.
    /// </summary>
    public class SignalLightService : IDisposable
    {
        // RelayService riêng biệt — KHÔNG inject từ DI, KHÔNG chia sẻ với AlarmService
        private readonly RelayService _ownedRelayService = new RelayService();
        private readonly IConfiguration _configuration;
        private volatile bool _isLighting = false; // Chống kích trùng
        private bool _disposed = false;

        /// <summary>Sự kiện báo trạng thái kết nối phần cứng đèn tín hiệu.</summary>
        public event Action<HardwareConnectionStatus>? HardwareStatusChanged;

        /// <summary>
        /// Constructor chỉ nhận IConfiguration — không cần RelayService từ DI.
        /// RelayService nội bộ được tạo riêng để tránh tranh chấp lock với AlarmService.
        /// </summary>
        public SignalLightService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        /// <summary>
        /// Kiểm tra và khởi tạo kết nối tới cổng COM lúc khởi động.
        /// Chạy bất đồng bộ để tránh làm treo UI Thread.
        /// </summary>
        public void Initialize()
        {
            Task.Run(async () =>
            {
                try
                {
                    var settings = GetSettings();
                    if (string.IsNullOrEmpty(settings.Port) || settings.Port == "None")
                    {
                        HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Disabled);
                        return;
                    }

                    bool success = await _ownedRelayService.OpenAsync(
                        settings.Port,
                        settings.Baud,
                        settings.Parity,
                        settings.DataBits,
                        settings.StopBits);

                    HardwareStatusChanged?.Invoke(success ? HardwareConnectionStatus.Online : HardwareConnectionStatus.Offline);
                    Log.Information("[SIGNAL_LIGHT] Khởi tạo kết nối Relay đèn tại {Port}: {Status}",
                        settings.Port, success ? "Online" : "Offline");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[SIGNAL_LIGHT] Lỗi khi khởi tạo kết nối Relay đèn tín hiệu");
                    HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Offline);
                }
            });
        }

        /// <summary>
        /// Kích hoạt đèn tín hiệu khi RFID tại trạm cân đọc được thẻ.
        /// Đèn sẽ bật trong <c>LightDurationMs</c> mili-giây rồi tự tắt.
        /// Fire-and-forget safe — không block luồng gọi.
        /// </summary>
        public async Task TriggerLightAsync()
        {
            if (_isLighting) return;
            _isLighting = true;

            try
            {
                var settings = GetSettings();
                if (string.IsNullOrEmpty(settings.Port) || settings.Port == "None")
                {
                    Log.Debug("[SIGNAL_LIGHT] Bỏ qua kích đèn vì cổng Relay là 'None'.");
                    return;
                }

                // Dùng RelayService riêng — lock hoàn toàn độc lập với AlarmService
                await _ownedRelayService.TriggerBellAsync(
                    settings.Port,
                    settings.Baud,
                    settings.Parity,
                    settings.DataBits,
                    settings.StopBits,
                    settings.LightDurationMs);

                Log.Information("[SIGNAL_LIGHT] Đã kích đèn tín hiệu thành công qua cổng {Port}", settings.Port);
                HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Online);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SIGNAL_LIGHT] Lỗi kích đèn tín hiệu");
                HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Offline);
            }
            finally
            {
                _isLighting = false;
            }
        }

        /// <summary>Giải phóng RelayService nội bộ khi ứng dụng tắt.</summary>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            try
            {
                _ownedRelayService.Close();
                _ownedRelayService.Dispose();
                Log.Information("[SIGNAL_LIGHT] Đã giải phóng RelayService đèn tín hiệu.");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "[SIGNAL_LIGHT] Lỗi khi giải phóng RelayService đèn tín hiệu.");
            }
        }

        private (string Port, int Baud, Parity Parity, int DataBits, StopBits StopBits, int LightDurationMs) GetSettings()
        {
            string port  = _configuration["SignalLightSettings:ComPort"] ?? "None";
            int baud     = int.TryParse(_configuration["SignalLightSettings:BaudRate"],        out int br)  ? br  : 9600;
            int data     = int.TryParse(_configuration["SignalLightSettings:DataBits"],        out int db)  ? db  : 8;
            int duration = int.TryParse(_configuration["SignalLightSettings:LightDurationMs"], out int ld)  ? ld  : 2000;

            Enum.TryParse(_configuration["SignalLightSettings:Parity"],   true, out Parity   parity);
            Enum.TryParse(_configuration["SignalLightSettings:StopBits"], true, out StopBits stopBits);

            return (port, baud, parity, data, stopBits, duration);
        }
    }
}
