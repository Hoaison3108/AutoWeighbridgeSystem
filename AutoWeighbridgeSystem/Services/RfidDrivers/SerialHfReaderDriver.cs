using System;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Threading;
using Serilog;

namespace AutoWeighbridgeSystem.Services.RfidDrivers
{
    /// <summary>
    /// Driver cho đầu đọc RFID HF/LF kết nối qua Serial Port.
    /// <para>
    /// Đây là <b>code hiện tại của RfidMultiService được đóng gói lại</b>
    /// — bộ lọc <c>char.IsDigit</c>, debounce timer 80ms, auto-close khi lỗi.
    /// Không có thay đổi logic nào so với trước.
    /// </para>
    /// </summary>
    public sealed class SerialHfReaderDriver : IRfidReaderDriver
    {
        // =========================================================================
        // CONFIG
        // =========================================================================
        private const int DebounceMs       = 80;   // Debounce: đợi 80ms sau byte cuối rồi mới xử lý
        private const int MaxBufferChars   = 200;  // Giới hạn buffer chống nhiễu
        private const int BufferKeepTail   = 32;   // Giữ lại phần đuôi khi overflow

        // =========================================================================
        // STATE
        // =========================================================================
        private SerialPort? _port;
        private readonly StringBuilder _buffer = new StringBuilder();
        private Timer?    _debounceTimer;
        private volatile bool _disposed;
        private volatile bool _isReading = true;

        private SerialDataReceivedEventHandler?  _dataHandler;
        private SerialErrorReceivedEventHandler? _errorHandler;

        // =========================================================================
        // IRfidReaderDriver
        // =========================================================================

        /// <inheritdoc/>
        public string DriverName => "SerialHF";

        /// <inheritdoc/>
        public event Action<string>? CardDetected;

        /// <inheritdoc/>
        public event Action? Disconnected;

        /// <inheritdoc/>
        public void Open(string comPort, int baudRate)
        {
            SafeClose();

            _port = new SerialPort(comPort, baudRate, Parity.None, 8, StopBits.One)
            {
                Encoding = Encoding.GetEncoding("ISO-8859-1")
            };

            _dataHandler  = (_, _) => HandleDataReceived();
            _errorHandler = (_, e) =>
            {
                Log.Warning("[RFID-HF] ErrorReceived tại {Port}: {Err}", comPort, e.EventType);
                TriggerDisconnect();
            };

            _port.DataReceived  += _dataHandler;
            _port.ErrorReceived += _errorHandler;
            _port.Open();

            // Debounce timer — bắt đầu ở trạng thái tắt (Timeout.Infinite)
            _debounceTimer = new Timer(_ => ProcessBuffer(), null, Timeout.Infinite, Timeout.Infinite);

            Log.Information("[RFID-HF] Đã mở cổng {Port} @ {Baud}", comPort, baudRate);
        }

        /// <inheritdoc/>
        public void Close()
        {
            SafeClose();
        }

        /// <inheritdoc/>
        public void ResumeReading() => _isReading = true;

        /// <inheritdoc/>
        public void PauseReading() => _isReading = false;

        // =========================================================================
        // XỬ LÝ DỮ LIỆU — giống hệt logic cũ trong RfidMultiService
        // =========================================================================

        private void HandleDataReceived()
        {
            if (_port == null || !_port.IsOpen) return;

            try
            {
                string raw = _port.ReadExisting();
                if (string.IsNullOrEmpty(raw)) return;

                lock (_buffer)
                {
                    _buffer.Append(raw);

                    // Chống overflow — giữ đuôi có thể là đầu frame tiếp theo
                    if (_buffer.Length > MaxBufferChars)
                    {
                        string tail = _buffer.ToString(_buffer.Length - BufferKeepTail, BufferKeepTail);
                        _buffer.Clear();
                        _buffer.Append(tail);
                    }
                }

                // Reset debounce: 80ms sau byte cuối mới xử lý
                _debounceTimer?.Change(DebounceMs, Timeout.Infinite);
            }
            catch (Exception ex)
            {
                Log.Warning("[RFID-HF] Lỗi đọc dữ liệu: {Msg}", ex.Message);
                TriggerDisconnect();
            }
        }

        private void ProcessBuffer()
        {
            if (!_isReading)
            {
                lock (_buffer) { _buffer.Clear(); }
                return;
            }

            string data;
            lock (_buffer)
            {
                data = _buffer.ToString();
                _buffer.Clear();
            }

            if (string.IsNullOrEmpty(data)) return;

            // Bộ lọc HF/LF: chỉ lấy chữ số 0-9 (mã thẻ HF/LF luôn là số thuần)
            string cleanId = new string(data.Where(char.IsDigit).ToArray());

            if (!string.IsNullOrEmpty(cleanId))
            {
                Log.Debug("[RFID-HF] Mã thẻ HF: {Id}", cleanId);
                CardDetected?.Invoke(cleanId);
            }
        }

        private void TriggerDisconnect()
        {
            SafeClose();
            Disconnected?.Invoke();
        }

        private void SafeClose()
        {
            try
            {
                _debounceTimer?.Dispose();
                _debounceTimer = null;

                if (_port != null)
                {
                    if (_dataHandler  != null) _port.DataReceived  -= _dataHandler;
                    if (_errorHandler != null) _port.ErrorReceived -= _errorHandler;
                    if (_port.IsOpen)
                    {
                        _port.DiscardInBuffer();
                        _port.Close();
                    }
                    _port.Dispose();
                    _port = null;
                }
            }
            catch { /* Port có thể đã ở trạng thái lỗi */ }
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
