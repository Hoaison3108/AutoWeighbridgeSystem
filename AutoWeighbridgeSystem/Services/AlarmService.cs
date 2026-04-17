using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Threading.Tasks;

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

        public AlarmService(RelayService relayService, IConfiguration configuration)
        {
            _relayService = relayService;
            _configuration = configuration;
        }

        /// <summary>
        /// Kích hoạt còi báo hiệu không đồng bộ trong thời gian cấu hình sẵn.
        /// Nếu còi đang kêu, lệnh gọi mới sẽ bị bỏ qua để tránh xung đột phần cứng.
        /// </summary>
        public async Task TriggerAlarmAsync()
        {
            if (_isRinging) return;
            _isRinging = true;

            try
            {
                string port = _configuration["RelaySettings:ComPort"];
                if (string.IsNullOrEmpty(port)) return;

                int duration = int.Parse(_configuration["RelaySettings:AlarmDurationMs"] ?? "1500");

                // Gọi xuống RelayService của phần cứng
                await Task.Run(() => _relayService.TriggerAlarmAsync(port, duration));
                Log.Information("[ALARM] Đã kích hoạt còi báo thành công.");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ALARM] Lỗi kích hoạt còi báo");
            }
            finally
            {
                _isRinging = false;
            }
        }
    }
}