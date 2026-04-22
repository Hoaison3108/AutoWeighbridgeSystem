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
    /// Phiên bản tối ưu cho LCRelay, giữ kết nối duy trì và chống nhiễu.
    /// </summary>
    public class AlarmService
    {
        private readonly RelayService _relayService;
        private readonly IConfiguration _configuration;
        private volatile bool _isRinging = false; // Chống spam chuông

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
                var settings = GetRelaySettings();
                if (string.IsNullOrEmpty(settings.Port) || settings.Port == "None")
                {
                    HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Disabled);
                    return;
                }

                // Thử mở kết nối duy trì (Persistent)
                bool success = _relayService.OpenAsync(
                    settings.Port, 
                    settings.Baud, 
                    settings.Parity, 
                    settings.DataBits, 
                    settings.StopBits).GetAwaiter().GetResult();
                
                HardwareStatusChanged?.Invoke(success ? HardwareConnectionStatus.Online : HardwareConnectionStatus.Offline);
            }
            catch
            {
                HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Offline);
            }
        }

        /// <summary>
        /// Kích hoạt chuông báo hiệu khi cân thành công.
        /// </summary>
        public async Task TriggerAlarmAsync()
        {
            if (_isRinging) return;
            _isRinging = true;

            try
            {
                var settings = GetRelaySettings();
                if (string.IsNullOrEmpty(settings.Port) || settings.Port == "None")
                {
                    Log.Debug("[ALARM] Bỏ qua kích chuông vì cổng Relay là 'None'.");
                    return;
                }
                
                await _relayService.TriggerBellAsync(
                    settings.Port, 
                    settings.Baud, 
                    settings.Parity, 
                    settings.DataBits, 
                    settings.StopBits, 
                    settings.BellDur);
                
                Log.Information("[ALARM] Đã kích chuông thành công qua cổng {Port}", settings.Port);
                HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Online);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[ALARM] Lỗi kích chuông");
                HardwareStatusChanged?.Invoke(HardwareConnectionStatus.Offline);
            }
            finally
            {
                _isRinging = false;
            }
        }

        private (string Port, int Baud, Parity Parity, int DataBits, StopBits StopBits, int BellDur) GetRelaySettings()
        {
            string port = _configuration["RelaySettings:ComPort"];
            int baud = int.TryParse(_configuration["RelaySettings:BaudRate"], out int br) ? br : 9600;
            int data = int.TryParse(_configuration["RelaySettings:DataBits"], out int db) ? db : 8;
            int bDur = int.TryParse(_configuration["RelaySettings:AlarmDurationMs"], out int ad) ? ad : 1500;

            Enum.TryParse(_configuration["RelaySettings:Parity"], true, out Parity parity);
            Enum.TryParse(_configuration["RelaySettings:StopBits"], true, out StopBits stopBits);

            return (port, baud, parity, data, stopBits, bDur);
        }
    }
}