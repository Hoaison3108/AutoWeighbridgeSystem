using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.IO.Ports;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Models;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ điều khiển còi báo hiệu (alarm) thông qua <see cref="RelayService"/>.
    /// Bao gồm cơ chế chống spam: nếu còi đang kêu thì bỏ qua mọi yêu cầu kích hoạt mới.
    /// Thời gian hú còi được đọc từ cấu hình <c>RelaySettings:AlarmDurationMs</c>.
    /// </summary>
    public class AlarmService
    {
        private readonly RelayService _relayService;
        private readonly IConfiguration _configuration;
        private volatile bool _isRinging = false; // Chống spam — volatile đảm bảo thread-safe khi nhiều luồng kiểm tra cùng lúc

        /// <summary>Sự kiện báo trạng thái kết nối phần cứng chuông.</summary>
        public event Action<HardwareConnectionStatus>? HardwareStatusChanged;

        public AlarmService(RelayService relayService, IConfiguration configuration)
        {
            _relayService = relayService;
            _configuration = configuration;
        }

        /// <summary>
        /// Kiểm tra và khởi tạo kết nối tới cổng COM lúc khởi động để giữ kết nối duy trì.
        /// </summary>
        public void Initialize()
        {
            try
            {
                string portName = _configuration["RelaySettings:ComPort"];
                if (string.IsNullOrEmpty(portName))
                {
                    HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Offline);
                    return;
                }

                int baudRate = int.TryParse(_configuration["RelaySettings:BaudRate"], out int br) ? br : 9600;
                int dataBits = int.TryParse(_configuration["RelaySettings:DataBits"], out int db) ? db : 8;
                Enum.TryParse(_configuration["RelaySettings:Parity"], true, out Parity parity);
                Enum.TryParse(_configuration["RelaySettings:StopBits"], true, out StopBits stopBits);

                // Thử mở kết nối duy trì (Persistent)
                bool success = _relayService.OpenAsync(portName, baudRate, parity, dataBits, stopBits).GetAwaiter().GetResult();
                
                HardwareStatusChanged?.Invoke(success ? HardwareConnectionStatus.Online : HardwareConnectionStatus.Offline);
            }
            catch
            {
                HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Offline);
            }
        }

        /// <summary>
        /// Kích hoạt còi báo hiệu không đồng bộ trong thời gian cấu hình sẵn.
        /// Nếu còi đang kêu, lệnh gọi mới sẽ bị bỏ qua để tránh xung đột phần cứng.
        /// </summary>
        public async Task TriggerAlarmAsync()
        {
            if (_isRinging)
            {
                Log.Warning("[ALARM] Bỏ qua yêu cầu kích chuông do chuông đang kêu (Anti-spam).");
                return;
            }

            _isRinging = true;

            try
            {
                string portName = _configuration["RelaySettings:ComPort"];
                if (string.IsNullOrEmpty(portName)) return;

                // Đọc toàn bộ thông số từ cấu hình (với giá trị mặc định an toàn)
                int baudRate = int.TryParse(_configuration["RelaySettings:BaudRate"], out int br) ? br : 9600;
                int dataBits = int.TryParse(_configuration["RelaySettings:DataBits"], out int db) ? db : 8;
                int duration = int.TryParse(_configuration["RelaySettings:AlarmDurationMs"], out int d) ? d : 1500;

                Enum.TryParse(_configuration["RelaySettings:Parity"], true, out Parity parity);
                Enum.TryParse(_configuration["RelaySettings:StopBits"], true, out StopBits stopBits);

                // Gọi xuống RelayService của phần cứng
                await _relayService.TriggerAlarmAsync(portName, baudRate, parity, dataBits, stopBits, duration);
                
                Log.Information("[ALARM] Đã kích hoạt còi báo thành công.");
                HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Online);
            }
            catch (Exception ex)
            {
                // Lúc này lỗi từ RelayService đã ném lên đây
                Log.Error(ex, "[ALARM] Lỗi kích hoạt còi báo - Chuyển đèn sang ĐỎ");
                HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Offline);
            }
            finally
            {
                _isRinging = false;
            }
        }
    }
}