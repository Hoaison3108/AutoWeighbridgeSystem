using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    public class AlarmService
    {
        private readonly RelayService _relayService;
        private readonly IConfiguration _configuration;
        private bool _isRinging = false; // Chống spam click hú còi liên tục

        public AlarmService(RelayService relayService, IConfiguration configuration)
        {
            _relayService = relayService;
            _configuration = configuration;
        }

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