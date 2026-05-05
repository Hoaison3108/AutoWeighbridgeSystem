using System;
using System.Linq;
using System.Threading;
using RFIDReaderAPI;
using RFIDReaderAPI.Interface;
using RFIDReaderAPI.Models;
using Serilog;

namespace AutoWeighbridgeSystem.Services.RfidDrivers
{
    /// <summary>
    /// Driver cho đầu đọc UHF RFID Hopeland kết nối qua Serial Port (COM).
    /// <para>
    /// SDK Hopeland sử dụng pattern callback: sau khi <see cref="RFIDReader.CreateSerialConn"/>
    /// thành công, SDK tự động gọi <see cref="OutPutTags"/> trên background thread
    /// mỗi khi phát hiện thẻ EPC mới trong vùng đọc.
    /// </para>
    /// <para>
    /// <b>EPC deduplication:</b> SDK gọi callback liên tục với tần suất cao khi
    /// thẻ vẫn còn trong vùng đọc. Driver áp dụng cooldown <see cref="EpcCooldownMs"/>
    /// để chỉ phát <see cref="CardDetected"/> một lần mỗi <c>3000ms</c> cho cùng EPC.
    /// </para>
    /// </summary>
    public sealed class HopelandSdkDriver : IRfidReaderDriver, IAsynchronousMessage
    {
        // =========================================================================
        // CONFIG
        // =========================================================================

        /// <summary>Thời gian (ms) bỏ qua EPC trùng lặp — ngăn phát event liên tục.</summary>
        private const int EpcCooldownMs = 3000;

        // =========================================================================
        // STATE
        // =========================================================================

        /// <summary>
        /// Connection ID trả về từ SDK sau khi kết nối thành công.
        /// Có dạng "COM4:115200".
        /// </summary>
        private string? _connId;

        private volatile bool _disposed;
        private volatile bool _isReading;

        /// <summary>Lưu thời điểm lần cuối phát event cho từng EPC.</summary>
        private readonly System.Collections.Concurrent.ConcurrentDictionary<string, DateTime> _epcCooldown = new();

        // =========================================================================
        // IRfidReaderDriver
        // =========================================================================

        /// <inheritdoc/>
        public string DriverName => "HopelandSDK";

        /// <inheritdoc/>
        public event Action<string>? CardDetected;

        /// <inheritdoc/>
        public event Action? Disconnected;

        /// <inheritdoc/>
        /// <remarks>
        /// <paramref name="comPort"/> phải là tên COM đơn giản (ví dụ: "COM4").
        /// Driver tự ghép thành format "COM4:baudRate" theo yêu cầu của SDK.
        /// </remarks>
        public void Open(string comPort, int baudRate)
        {
            // SDK nhận connParam dạng "COM4:115200"
            string connParam = $"{comPort}:{baudRate}";

            // this (HopelandSdkDriver) triển khai IAsynchronousMessage — SDK sẽ gọi OutPutTags trên instance này
            bool isConnected = RFIDReader.CreateSerialConn(connParam, this);

            if (!isConnected)
                throw new InvalidOperationException($"[RFID-Hopeland] Không thể kết nối tới {connParam}. Kiểm tra COM port và thiết bị.");

            if (!RFIDReader.CheckConnect(connParam))
            {
                RFIDReader.CloseConn(connParam);
                throw new InvalidOperationException($"[RFID-Hopeland] Kết nối {connParam} không ổn định sau khi mở.");
            }

            _connId = connParam;

            Log.Information("[RFID-Hopeland] Đã kết nối thành công tại {ConnId}. (Trạng thái: Tạm dừng / Đợi lệnh quét)", _connId);
        }

        /// <inheritdoc/>
        public void Close()
        {
            SafeClose();
        }

        /// <inheritdoc/>
        public void ResumeReading()
        {
            if (_isReading || string.IsNullOrEmpty(_connId)) return;

            int result = RFIDReader._Tag6C.GetEPC(_connId, eAntennaNo._1, eReadType.Inventory);
            if (result != 0)
            {
                Log.Warning("[RFID-Hopeland] GetEPC trả về mã lỗi {Code} — Không thể bật đầu đọc.", result);
                return;
            }

            _isReading = true;
            Log.Information("[RFID-Hopeland] BẬT SÓNG UHF (ResumeReading) tại {ConnId}", _connId);
        }

        /// <inheritdoc/>
        public void PauseReading()
        {
            if (!_isReading || string.IsNullOrEmpty(_connId)) return;

            try
            {
                RFIDReader._RFIDConfig.Stop(_connId);
                _isReading = false;
                Log.Information("[RFID-Hopeland] TẮT SÓNG UHF (PauseReading) tại {ConnId}", _connId);
            }
            catch (Exception ex)
            {
                Log.Warning("[RFID-Hopeland] Lỗi khi PauseReading: {Msg}", ex.Message);
            }
        }

        // =========================================================================
        // IAsynchronousMessage — SDK callbacks
        // =========================================================================

        /// <summary>
        /// SDK gọi method này trên background thread khi phát hiện thẻ EPC.
        /// </summary>
        public void OutPutTags(Tag_Model tag_Model)
        {
            if (!_isReading) return;

            // Bỏ qua nếu tag lỗi hoặc EPC rỗng
            if (tag_Model == null || tag_Model.Result != 0x00) return;
            if (string.IsNullOrWhiteSpace(tag_Model.EPC)) return;

            string epc = tag_Model.EPC.Replace(" ", "").ToUpperInvariant();
            if (string.IsNullOrEmpty(epc)) return;

            // Deduplication: bỏ qua nếu EPC này vừa được xử lý trong EpcCooldownMs
            DateTime now = DateTime.Now;
            if (_epcCooldown.TryGetValue(epc, out DateTime lastTime))
            {
                if ((now - lastTime).TotalMilliseconds < EpcCooldownMs)
                    return;
            }
            _epcCooldown[epc] = now;

            Log.Debug("[RFID-Hopeland] EPC phát hiện: {EPC} | RSSI: {RSSI}", epc, tag_Model.RSSI);
            CardDetected?.Invoke(epc);
        }

        /// <summary>SDK gọi khi kết thúc một cycle đọc (dùng cho chế độ single-read).</summary>
        public void OutPutTagsOver() { /* Không cần xử lý ở chế độ continuous inventory */ }

        /// <summary>SDK gọi khi có sự kiện GPI (không dùng trong hệ thống này).</summary>
        public void GPIControlMsg(GPI_Model gpi_model) { }

        /// <summary>SDK gọi để ghi debug message (forward sang Serilog Debug).</summary>
        public void WriteDebugMsg(string msg)
            => Log.Debug("[RFID-Hopeland-SDK] {Msg}", msg);

        /// <summary>SDK gọi để ghi log (forward sang Serilog Information).</summary>
        public void WriteLog(string msg)
            => Log.Information("[RFID-Hopeland-SDK] {Msg}", msg);

        /// <summary>SDK gọi khi port đang kết nối — phát Disconnected nếu mất kết nối giữa chừng.</summary>
        public void PortConnecting(string connId)
            => Log.Debug("[RFID-Hopeland] PortConnecting: {ConnId}", connId);

        /// <summary>SDK gọi khi port đóng — kích hoạt reconnect.</summary>
        public void PortClosing(string connId)
        {
            Log.Warning("[RFID-Hopeland] PortClosing: {ConnId}", connId);
            _connId = null;
            Disconnected?.Invoke();
        }

        /// <summary>SDK gọi cho các sự kiện upload chung (không dùng trong hệ thống này).</summary>
        public void EventUpload(RFIDReaderAPI.Models.CallBackEnum callBackType, object param) { }

        // =========================================================================
        // CLEANUP
        // =========================================================================

        private void SafeClose()
        {
            try
            {
                if (!string.IsNullOrEmpty(_connId))
                {
                    // Dừng đọc trước khi đóng kết nối
                    try { RFIDReader._RFIDConfig.Stop(_connId); } catch { }
                    RFIDReader.CloseConn(_connId);
                    Log.Information("[RFID-Hopeland] Đã đóng kết nối {ConnId}", _connId);
                    _connId = null;
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RFID-Hopeland] Lỗi khi đóng kết nối: {Msg}", ex.Message);
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            SafeClose();
        }
    }
}
