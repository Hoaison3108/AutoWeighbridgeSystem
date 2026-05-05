using System;
using Serilog;

namespace AutoWeighbridgeSystem.Services.RfidDrivers
{
    /// <summary>
    /// Factory tạo <see cref="IRfidReaderDriver"/> theo loại cấu hình.
    /// Tương tự <see cref="Protocols.ScaleProtocolFactory"/> cho đầu cân.
    /// <para>
    /// Loại driver được chỉ định qua <c>appsettings.json → RfidSettings:DriverType</c>.
    /// </para>
    /// </summary>
    public sealed class RfidDriverFactory
    {
        /// <summary>
        /// Tạo một instance driver mới theo tên loại.
        /// </summary>
        /// <param name="driverType">
        /// Tên loại driver. Các giá trị hợp lệ:
        /// <list type="bullet">
        ///   <item><c>"SerialHf"</c> — Đầu đọc HF/LF qua COM (mặc định, code cũ).</item>
        ///   <item><c>"HopelandSdk"</c> — Đầu đọc UHF Hopeland S130L qua COM + SDK.</item>
        /// </list>
        /// </param>
        /// <returns>Instance driver tương ứng.</returns>
        /// <exception cref="ArgumentException">Ném nếu <paramref name="driverType"/> không được nhận diện.</exception>
        public IRfidReaderDriver Create(string driverType)
        {
            return driverType?.Trim().ToLowerInvariant() switch
            {
                "serialhf"    => new SerialHfReaderDriver(),
                "hopelandsdk"
                or "hopeland" => new HopelandSdkDriver(),
                null or ""    => CreateDefault(),
                _             => throw new ArgumentException(
                                     $"[RFID] DriverType '{driverType}' không được hỗ trợ. " +
                                     $"Dùng: 'SerialHf' hoặc 'HopelandSdk'.")
            };
        }

        private static IRfidReaderDriver CreateDefault()
        {
            Log.Warning("[RFID] DriverType trống hoặc null — sử dụng SerialHf làm mặc định.");
            return new SerialHfReaderDriver();
        }
    }
}
