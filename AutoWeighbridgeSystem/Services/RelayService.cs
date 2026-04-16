using System;
using System.IO.Ports;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ cấp thấp để giao tiếp trực tiếp với mạch Relay USB/Serial (LC Relay / CH340).
    /// Gửi lệnh nhị phân để bật/tắt mạch relay, qua đó điều khiển còi báo hiệu.
    /// <para>
    /// Mã lệnh hex chuẩn cho mạch LC Relay thông dụng:<br/>
    /// Bật: <c>0xA0, 0x01, 0x01, 0xA2</c><br/>
    /// Tắt: <c>0xA0, 0x01, 0x00, 0xA1</c>
    /// </para>
    /// </summary>
    public class RelayService
    {
        // Các mã lệnh Hex chuẩn cho module Relay USB/Serial thông dụng (LC Relay)
        // Lưu ý: Các mã này có thể thay đổi tùy thuộc vào nhà sản xuất phần cứng của bạn
        private readonly byte[] _relayOnCommand  = { 0xA0, 0x01, 0x01, 0xA2 };
        private readonly byte[] _relayOffCommand = { 0xA0, 0x01, 0x00, 0xA1 };

        /// <summary>
        /// Kích hoạt còi báo hiệu trong một khoảng thời gian, sau đó tự tắt.
        /// Toàn bộ thao tác được thực hiện trên luồng nền để không block UI.
        /// Exception bị bắt thầm lặng để tránh crash khi rút cáp USB mạch relay.
        /// </summary>
        /// <param name="comPort">Tên cổng COM của mạch relay (vd: "COM6").</param>
        /// <param name="durationMs">Thời gian hú còi tính bằng millisecond (vd: 1500 = 1.5 giây).</param>
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
