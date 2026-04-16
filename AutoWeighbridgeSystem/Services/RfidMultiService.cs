using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ quản lý nhiều đầu đọc thẻ RFID cùng lúc qua các cổng COM riêng biệt.
    /// Mỗi đầu đọc được gán một vai trò (<see cref="Models.ReaderRoles"/>):
    /// ScaleIn (xe vào), ScaleOut (xe ra), Desk (bàn điều hành).
    /// <para>
    /// <b>Auto-Reconnect</b>: Mỗi đầu đọc có watchdog độc lập. Khi phát hiện mất kết nối
    /// (lỗi Serial hoặc port tự đóng), service tự động thử kết nối lại với backoff
    /// 5s → 5s → 10s → 30s → 60s cho đến khi thành công.
    /// </para>
    /// </summary>
    public class RfidMultiService : IDisposable
    {
        // =========================================================================
        // INNER CLASS — lưu trữ thông tin từng đầu đọc
        // =========================================================================

        /// <summary>Lưu trạng thái và thông số kết nối của một đầu đọc RFID.</summary>
        private sealed class RfidReaderEntry
        {
            public string   RoleName      { get; }
            public string   ComPort       { get; }
            public int      BaudRate      { get; }
            public SerialPort?             Port           { get; set; }
            public volatile bool           IsReconnecting;
            public CancellationTokenSource ReconnectCts   = new CancellationTokenSource();

            /// <summary>Lưu tham chiếu handler để unsubscribe đúng cách khi cần.</summary>
            public SerialDataReceivedEventHandler?  DataHandler;
            public SerialErrorReceivedEventHandler? ErrorHandler;

            public RfidReaderEntry(string roleName, string comPort, int baudRate)
            {
                RoleName = roleName;
                ComPort  = comPort;
                BaudRate = baudRate;
            }
        }

        // =========================================================================
        // STATE
        // =========================================================================
        private readonly List<RfidReaderEntry> _readers = new List<RfidReaderEntry>();

        /// <summary>Các mốc thời gian chờ (giây) giữa các lần thử kết nối lại.</summary>
        private static readonly int[] ReconnectDelaysSeconds = { 5, 5, 10, 30, 60 };

        // =========================================================================
        // EVENTS
        // =========================================================================

        /// <summary>
        /// Sự kiện phát ra khi có đầu đọc nhận được mã thẻ RFID hợp lệ.
        /// Tham số: <c>(string readerRole, string cleanCardId)</c>.
        /// </summary>
        public event Action<string, string>? CardRead;

        /// <summary>
        /// Phát ra khi một đầu đọc RFID mất kết nối.
        /// Tham số: <c>string roleName</c> (ScaleIn / ScaleOut / Desk).
        /// </summary>
        public event Action<string>? ReaderDisconnected;

        /// <summary>
        /// Phát ra khi một đầu đọc RFID kết nối lại thành công.
        /// Tham số: <c>string roleName</c>.
        /// </summary>
        public event Action<string>? ReaderReconnected;

        // =========================================================================
        // PUBLIC API
        // =========================================================================

        /// <summary>
        /// Thêm và khởi động một đầu đọc RFID mới vào hệ thống.
        /// Tự động đăng ký watchdog per-reader để hỗ trợ auto-reconnect.
        /// </summary>
        /// <param name="roleName">Vai trò của đầu đọc (ScaleIn, ScaleOut, Desk).</param>
        /// <param name="comPort">Cổng COM kết nối (vd: "COM4").</param>
        /// <param name="baudRate">Tốc độ truyền (thường là 9600).</param>
        public void AddReader(string roleName, string comPort, int baudRate)
        {
            if (string.IsNullOrWhiteSpace(comPort))
            {
                Log.Warning("[RFID] Không thể thêm đầu đọc {Role} vì ComPort bị trống!", roleName);
                return;
            }

            var entry = new RfidReaderEntry(roleName, comPort, baudRate);
            _readers.Add(entry);

            OpenReaderPort(entry);
        }

        /// <summary>
        /// Đóng và giải phóng tất cả các cổng RFID đang hoạt động.
        /// Hủy mọi vòng lặp reconnect đang chạy.
        /// </summary>
        public void CloseAll()
        {
            foreach (var entry in _readers)
            {
                entry.ReconnectCts.Cancel();
                SafeCloseReaderPort(entry);
            }
            _readers.Clear();
        }

        // =========================================================================
        // KHỞI TẠO PORT CHO TỪNG ĐẦU ĐỌC
        // =========================================================================

        /// <summary>
        /// Mở cổng COM cho một đầu đọc cụ thể, đăng ký các event handler.
        /// Được gọi từ <see cref="AddReader"/> và từ vòng lặp reconnect.
        /// </summary>
        private void OpenReaderPort(RfidReaderEntry entry)
        {
            try
            {
                SafeCloseReaderPort(entry);

                var port = new SerialPort(entry.ComPort, entry.BaudRate, Parity.None, 8, StopBits.One)
                {
                    Encoding = Encoding.GetEncoding("ISO-8859-1")
                };

                // Tạo named handlers để có thể unsubscribe đúng cách
                entry.DataHandler  = (s, e) => HandleDataReceived(entry, (SerialPort)s);
                entry.ErrorHandler = (s, e) =>
                {
                    Log.Warning("[RFID] ErrorReceived từ {Role}: {Error}", entry.RoleName, e.EventType);
                    HandleReaderDisconnect(entry);
                };

                port.DataReceived  += entry.DataHandler;
                port.ErrorReceived += entry.ErrorHandler;
                port.Open();

                entry.Port = port;
                Log.Information("[RFID] ĐÃ MỞ CỔNG {Port} cho đầu đọc {Role}", entry.ComPort, entry.RoleName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RFID] THẤT BẠI khi mở cổng {Port} cho {Role}", entry.ComPort, entry.RoleName);
            }
        }

        // =========================================================================
        // XỬ LÝ DỮ LIỆU
        // =========================================================================

        private void HandleDataReceived(RfidReaderEntry entry, SerialPort sp)
        {
            try
            {
                Thread.Sleep(100); // Chờ nhận đủ gói tin

                string rawData = sp.ReadExisting();
                if (string.IsNullOrEmpty(rawData)) return;

                Log.Information("[RFID] {Role} nhận dữ liệu thô: {Raw}", entry.RoleName, rawData);

                // Lọc lấy chỉ các chữ số 0-9
                string cleanId = "";
                foreach (char c in rawData)
                    if (char.IsDigit(c)) cleanId += c;

                if (!string.IsNullOrEmpty(cleanId))
                {
                    Log.Debug("[RFID] {Role} trích xuất mã số: {Data}", entry.RoleName, cleanId);
                    CardRead?.Invoke(entry.RoleName, cleanId);
                }
            }
            catch (Exception ex)
            {
                Log.Warning("[RFID] Lỗi đọc dữ liệu từ {Role} — có thể mất kết nối: {Msg}", entry.RoleName, ex.Message);
                HandleReaderDisconnect(entry);
            }
        }

        // =========================================================================
        // AUTO-RECONNECT
        // =========================================================================

        /// <summary>
        /// Xử lý mất kết nối của một đầu đọc: đóng port, phát event, bắt đầu reconnect loop.
        /// </summary>
        private void HandleReaderDisconnect(RfidReaderEntry entry)
        {
            if (entry.IsReconnecting) return;
            entry.IsReconnecting = true;

            Log.Warning("[RFID] Phát hiện mất kết nối đầu đọc {Role} tại {Port}. Bắt đầu thử kết nối lại...",
                entry.RoleName, entry.ComPort);

            SafeCloseReaderPort(entry);
            ReaderDisconnected?.Invoke(entry.RoleName);
            StartReaderReconnectLoop(entry);
        }

        /// <summary>
        /// Đóng port của một đầu đọc một cách an toàn, unsubscribe events.
        /// </summary>
        private void SafeCloseReaderPort(RfidReaderEntry entry)
        {
            try
            {
                if (entry.Port != null)
                {
                    if (entry.DataHandler  != null) entry.Port.DataReceived  -= entry.DataHandler;
                    if (entry.ErrorHandler != null) entry.Port.ErrorReceived -= entry.ErrorHandler;
                    if (entry.Port.IsOpen)
                    {
                        entry.Port.DiscardInBuffer();
                        entry.Port.Close();
                    }
                    entry.Port.Dispose();
                    entry.Port = null;
                }
            }
            catch { /* Port có thể đã ở trạng thái lỗi, bỏ qua */ }
        }

        /// <summary>
        /// Vòng lặp thử kết nối lại cho một đầu đọc cụ thể với backoff tăng dần.
        /// </summary>
        private void StartReaderReconnectLoop(RfidReaderEntry entry)
        {
            // Hủy vòng lặp cũ nếu đang chạy
            entry.ReconnectCts.Cancel();
            entry.ReconnectCts = new CancellationTokenSource();
            var token = entry.ReconnectCts.Token;

            Task.Run(async () =>
            {
                int attempt = 0;

                while (!token.IsCancellationRequested)
                {
                    int delaySec = ReconnectDelaysSeconds[Math.Min(attempt, ReconnectDelaysSeconds.Length - 1)];
                    Log.Information("[RFID] [{Role}] Thử kết nối lại lần {Attempt} sau {Delay}s...",
                        entry.RoleName, attempt + 1, delaySec);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySec), token);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // CloseAll() đã được gọi
                    }

                    if (token.IsCancellationRequested) break;

                    try
                    {
                        // Thử mở lại port
                        var testPort = new SerialPort(entry.ComPort, entry.BaudRate, Parity.None, 8, StopBits.One)
                        {
                            Encoding = Encoding.GetEncoding("ISO-8859-1")
                        };

                        entry.DataHandler  = (s, e) => HandleDataReceived(entry, (SerialPort)s);
                        entry.ErrorHandler = (s, e) =>
                        {
                            Log.Warning("[RFID] ErrorReceived từ {Role}: {Error}", entry.RoleName, e.EventType);
                            HandleReaderDisconnect(entry);
                        };

                        testPort.DataReceived  += entry.DataHandler;
                        testPort.ErrorReceived += entry.ErrorHandler;
                        testPort.Open();

                        // Thành công
                        entry.Port          = testPort;
                        entry.IsReconnecting = false;

                        Log.Information("[RFID] ✅ [{Role}] KẾT NỐI LẠI THÀNH CÔNG tại {Port} (sau {Attempt} lần thử).",
                            entry.RoleName, entry.ComPort, attempt + 1);
                        ReaderReconnected?.Invoke(entry.RoleName);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("[RFID] [{Role}] Lần thử {Attempt} thất bại: {Msg}",
                            entry.RoleName, attempt + 1, ex.Message);
                    }

                    attempt++;
                }

                entry.IsReconnecting = false;
            }, token);
        }

        // =========================================================================
        // CLEANUP
        // =========================================================================

        /// <inheritdoc/>
        public void Dispose() => CloseAll();
    }
}