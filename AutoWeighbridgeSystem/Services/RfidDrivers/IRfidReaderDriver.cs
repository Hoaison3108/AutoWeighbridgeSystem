using System;

namespace AutoWeighbridgeSystem.Services.RfidDrivers
{
    /// <summary>
    /// Trừu tượng hóa cách một đầu đọc RFID nhận và xử lý dữ liệu thô.
    /// Tương tự <see cref="Protocols.IScaleProtocol"/> cho đầu cân —
    /// <see cref="RfidMultiService"/> không cần biết đang làm việc với
    /// thẻ HF, UHF serial hay Hopeland SDK.
    /// </summary>
    public interface IRfidReaderDriver : IDisposable
    {
        /// <summary>Tên driver hiển thị trong log.</summary>
        string DriverName { get; }

        /// <summary>
        /// Mở kết nối với thiết bị đầu đọc.
        /// </summary>
        /// <param name="comPort">Cổng COM (ví dụ: "COM4"). Với Hopeland SDK format là "COM4:115200".</param>
        /// <param name="baudRate">Tốc độ truyền (bps).</param>
        /// <exception cref="Exception">Ném exception nếu mở cổng thất bại.</exception>
        void Open(string comPort, int baudRate);

        /// <summary>Đóng kết nối và giải phóng tài nguyên một cách an toàn.</summary>
        void Close();

        /// <summary>Bật chế độ quét thẻ (phát sóng với UHF).</summary>
        void ResumeReading();

        /// <summary>Tắt chế độ quét thẻ (ngừng phát sóng, ngủ chờ).</summary>
        void PauseReading();

        /// <summary>
        /// Phát ra khi đầu đọc nhận được thẻ hợp lệ.
        /// Tham số: <c>cleanCardId</c> đã được chuẩn hóa (hex hoặc số).
        /// </summary>
        event Action<string>? CardDetected;

        /// <summary>Phát ra khi mất kết nối với đầu đọc.</summary>
        event Action? Disconnected;
    }
}
