using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.IO.Ports;

namespace AutoWeighbridgeSystem.Services
{
    public class RelayService
    {
        // Các mã lệnh Hex chuẩn cho module Relay USB/Serial thông dụng (LC Relay)
        // Lưu ý: Các mã này có thể thay đổi tùy thuộc vào nhà sản xuất phần cứng của bạn
        private readonly byte[] _relayOnCommand = { 0xA0, 0x01, 0x01, 0xA2 };
        private readonly byte[] _relayOffCommand = { 0xA0, 0x01, 0x00, 0xA1 };

        /// <summary>
        /// Kích hoạt còi báo hiệu trong một khoảng thời gian
        /// </summary>
        /// <param name="comPort">Tên cổng COM (vd: "COM4")</param>
        /// <param name="durationMs">Thời gian hú còi (mặc định 1500ms = 1.5s)</param>
        public async Task TriggerAlarmAsync(string comPort, int durationMs)
        {
            try
            {
                // Sử dụng Task.Run để đẩy toàn bộ tác vụ phần cứng xuống luồng nền
                // Đảm bảo không làm đóng băng (treo) giao diện ứng dụng WPF
                await Task.Run(async () =>
                {
                    using (SerialPort port = new SerialPort(comPort, 9600, Parity.None, 8, StopBits.One))
                    {
                        port.Open();

                        // 1. Đóng mạch Relay -> Bật còi báo hiệu
                        port.Write(_relayOnCommand, 0, _relayOnCommand.Length);

                        // 2. Duy trì trạng thái còi kêu trong khoảng thời gian cấu hình
                        await Task.Delay(durationMs);

                        // 3. Mở mạch Relay -> Tắt còi báo hiệu
                        port.Write(_relayOffCommand, 0, _relayOffCommand.Length);

                        port.Close();
                    }
                });
            }
            catch (Exception ex)
            {
                // Bắt lỗi thầm lặng để tránh crash toàn bộ phần mềm 
                // trong trường hợp ai đó lỡ tay rút cáp USB của mạch CH340
                Console.WriteLine($"[Lỗi Relay]: Không thể điều khiển mạch CH340 qua cổng {comPort} - {ex.Message}");
            }
        }
    }
}
