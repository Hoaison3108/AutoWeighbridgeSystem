using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Services.Protocols;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ giao tiếp với đầu cân điện tử qua cổng COM (Serial Port).
    /// Chịu trách nhiệm: mở kết nối, nhận dữ liệu thô, phân tích giao thức,
    /// tính toán trạng thái ổn định và phát sự kiện <see cref="WeightChanged"/>.
    /// <para>
    /// <b>Auto-Reconnect</b>: Khi mất kết nối (rút cáp, mất điện thiết bị...),
    /// service tự động thử kết nối lại theo chu kỳ tăng dần:
    /// 5s → 5s → 10s → 30s → 60s (lặp lại ở 60s cho đến khi thành công).
    /// </para>
    /// </summary>
    public class ScaleService : IDisposable
    {
        // =========================================================================
        // PHẦN CỨNG
        // =========================================================================
        private SerialPort? _serialPort;
        private IScaleProtocol? _protocol;

        private readonly Queue<decimal> _weightBuffer = new Queue<decimal>();
        private readonly StringBuilder _incomingDataBuffer = new StringBuilder();
        private const int BufferSize = 3; // Số mẫu ổn định cần tích lũy (Giảm từ 5 xuống 3 để chốt cân nhanh hơn)

        /// <summary>Ngưỡng kích thước (ký tự) để kích hoạt xóa buffer khi nhiễu tín hiệu.</summary>
        /// <remarks>
        /// BaudRate 2400 → tối đa ~240 byte/giây. Frame VT220 = 8 ký tự, FT111D = 17 ký tự.
        /// 200 ký tự ≈ 1-2 giây dữ liệu rác liên tục — phát hiện nhiễu nhanh mà không cắt nậm frame hợp lệ.
        /// </remarks>
        private const int MaxIncomingBufferSize = 200;

        /// <summary>Số ký tự cuối giữ lại sau khi overflow — đủ chứa 1 frame đang dở (VT220=8, FT111D=17).</summary>
        private const int BufferOverflowKeepTail = 32;

        private decimal _minWeightThreshold = 50; // Ngưỡng tối thiểu để xử lý ổn định
        private decimal _stabilityDelta = 50;     // Ngưỡng dao động tối đa để coi là ổn định

        // =========================================================================
        // THROTTLING UI
        // =========================================================================
        private readonly Stopwatch _uiThrottleStopwatch = Stopwatch.StartNew();
        private const int MinUiUpdateIntervalMs = 33; // ~30 FPS — đủ mượt, giảm 40% so với 50 FPS
        private bool _lastBroadcastedStableState = false;

        // =========================================================================
        // THÔNG SỐ KẾT NỐI (lưu lại để dùng khi reconnect)
        // =========================================================================
        private string? _portName;
        private int _baudRate;
        private int _dataBits;
        private Parity _parity;
        private StopBits _stopBits;

        // =========================================================================
        // TRẠNG THÁI RECONNECT
        // =========================================================================
        private volatile bool _isConnected = false;
        private volatile bool _isReconnecting = false;
        private CancellationTokenSource _reconnectCts = new CancellationTokenSource();

        /// <summary>Các mốc thời gian chờ (giây) giữa các lần thử kết nối lại.</summary>
        private static readonly int[] ReconnectDelaysSeconds = { 5, 5, 10, 30, 60 };

        // =========================================================================
        // PUBLIC PROPERTIES
        // =========================================================================

        private readonly object _weightLock = new object();
        private decimal _currentWeight;

        /// <summary>Trọng lượng hiện tại đọc được từ đầu cân (kg).</summary>
        public decimal CurrentWeight
        {
            get
            {
                lock (_weightLock)
                {
                    return _currentWeight;
                }
            }
        }

        /// <summary>
        /// <c>true</c> nếu đầu cân đang báo trạng thái ổn định (P+) hoặc
        /// bộ đệm phần mềm xác nhận dao động trong ngưỡng cho phép (&lt;= 50kg).
        /// </summary>
        public bool IsScaleStable { get; private set; }

        /// <summary><c>true</c> khi đầu cân đang kết nối bình thường.</summary>
        public bool IsConnected => _isConnected;

        /// <summary><c>true</c> nếu đầu cân bị vô hiệu hóa (cấu hình là 'None').</summary>
        public bool IsDisabled => _portName == "None";

        /// <summary>Tên cổng COM đang sử dụng.</summary>
        public string? PortName => _portName;

        // =========================================================================
        // EVENTS
        // =========================================================================

        /// <summary>
        /// Sự kiện phát ra khi có dữ liệu cân mới hợp lệ.
        /// Tham số: <c>(decimal weight, bool isStable)</c>. Tần suất tối đa ~25 lần/giây.
        /// </summary>
        public event Action<decimal, bool>? WeightChanged;

        /// <summary>Phát ra khi kết nối lần đầu với đầu cân thành công (sau Initialize hoặc Reinitialize).</summary>
        public event Action? Connected;

        /// <summary>Phát ra ngay khi phát hiện mất kết nối với đầu cân.</summary>
        public event Action? Disconnected;

        /// <summary>Phát ra khi kết nối lại với đầu cân thành công.</summary>
        public event Action? Reconnected;

        /// <summary>
        /// Phát ra trước mỗi lần thử kết nối lại.
        /// Tham số: số lần thử (1-based).
        /// </summary>
        public event Action<int>? ReconnectAttempting;

        // =========================================================================
        // KHỞI TẠO
        // =========================================================================

        /// <summary>
        /// Gọi ngay sau khi Coordinator đã subscribe events — replay trạng thái hiện tại
        /// để không bị miss nếu port đã mở trước khi subscriber kịp attach.
        /// </summary>
        public void NotifyInitialStatus()
        {
            if (_isConnected) Connected?.Invoke();
        }


        /// <summary>
        /// Khởi tạo và mở kết nối với đầu cân qua cổng COM.
        /// Lưu lại thông số kết nối để dùng khi auto-reconnect.
        /// </summary>
        public void Initialize(string portName, int baudRate, int dataBits,
                               Parity parity, StopBits stopBits, IScaleProtocol protocol,
                               decimal minWeightThreshold = 50, decimal stabilityDelta = 50)
        {
            // Lưu lại thông số để dùng khi reconnect
            _portName = portName;
            _baudRate = baudRate;
            _dataBits = dataBits;
            _parity = parity;
            _stopBits = stopBits;
            _protocol = protocol;
            _minWeightThreshold = minWeightThreshold;
            _stabilityDelta = stabilityDelta;

            if (string.IsNullOrEmpty(portName) || portName == "None")
            {
                _isConnected = false;
                Log.Information("[SCALE] Thiết bị đầu cân được cấu hình là 'None'. Bỏ qua kết nối.");
                return;
            }

            // TỐI ƯU: Chạy việc mở cổng trong background task để không block UI/Startup
            Task.Run(() => OpenPort());
        }

        /// <summary>
        /// Mở cổng COM. Được gọi từ <see cref="Initialize"/> và từ vòng lặp reconnect.
        /// </summary>
        private void OpenPort()
        {
            if (string.IsNullOrEmpty(_portName) || _portName == "None") return;
            try
            {
                // Giải phóng port cũ nếu còn tồn tại
                SafeClosePort();

                _serialPort = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
                {
                    Encoding = Encoding.ASCII,
                    ReadTimeout = 500,
                    ReceivedBytesThreshold = 1
                };

                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.ErrorReceived += SerialPort_ErrorReceived;
                _serialPort.Open();

                _isConnected = true;
                Log.Information("[SCALE] Đã kết nối đầu cân chuẩn {Protocol} tại {Port}", _protocol.ProtocolName, _portName);
                // Status event KHÔNG fire ở đây — phát từ NotifyInitialStatus() hoặc Reconnected event
            }
            catch (Exception ex)
            {
                _isConnected = false;
                Log.Error(ex, "[SCALE] Lỗi khởi tạo cổng {Port}", _portName);
            }
        }

        // =========================================================================
        // XỬ LÝ DỮ LIỆU
        // =========================================================================

        /// <summary>
        /// Xử lý dữ liệu thô nhận được từ Serial Port.
        /// Tích lũy vào buffer, sau đó ủy quyền toàn bộ việc parse frame cho
        /// <see cref="IScaleProtocol.TryExtractFrame"/> — ScaleService không biết gì
        /// về format giao thức cụ thể (P+, @+, Modbus, hay bất kỳ chuẩn nào).
        /// Nếu xảy ra lỗi đọc, kích hoạt quy trình <see cref="HandleDisconnect"/>.
        /// </summary>
        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (_serialPort == null || !_serialPort.IsOpen) return;

            try
            {
                string rawData;
                lock (_incomingDataBuffer)
                {
                    rawData = _serialPort.ReadExisting();
                    _incomingDataBuffer.Append(rawData);
                }

                string currentBuffer;
                lock (_incomingDataBuffer)
                {
                    currentBuffer = _incomingDataBuffer.ToString();
                }

                // Ủy quyền hoàn toàn cho protocol: tìm frame, parse giá trị, tính remainder
                var frameResult = _protocol.TryExtractFrame(currentBuffer);

                if (frameResult.HasValue)
                {
                    ProcessWeightStability(frameResult.Value.Weight, frameResult.Value.IsHardwareStable);

                    // Cập nhật buffer: giữ lại phần dư chưa xử lý
                    lock (_incomingDataBuffer)
                    {
                        _incomingDataBuffer.Clear();
                        if (frameResult.Value.Remainder.Length > 0)
                            _incomingDataBuffer.Append(frameResult.Value.Remainder);
                    }
                }
                else if (currentBuffer.Length > MaxIncomingBufferSize)
                {
                    // Buffer tràn do nhiễu tín hiệu kéo dài (không parse được frame nào)
                    // Giữ lại 32 ký tự cuối — có thể là đầu của frame hợp lệ tiếp theo
                    string tail = currentBuffer.Substring(currentBuffer.Length - BufferOverflowKeepTail);
                    lock (_incomingDataBuffer)
                    {
                        _incomingDataBuffer.Clear();
                        _incomingDataBuffer.Append(tail);
                    }
                    Log.Warning("[SCALE] Buffer tràn ({Size} ký tự) tại {Port} — xóa rác, giữ {Keep} ký tự cuối.",
                        currentBuffer.Length, _portName, BufferOverflowKeepTail);
                }
            }
            catch (Exception ex)
            {
                // IOException / InvalidOperationException thường xảy ra khi rút cáp
                Log.Warning("[SCALE] Lỗi đọc dữ liệu — có thể mất kết nối: {Msg}", ex.Message);
                HandleDisconnect();
            }
        }


        /// <summary>
        /// Xử lý lỗi phần cứng từ Serial Port (frame error, buffer overflow...).
        /// Kích hoạt quy trình <see cref="HandleDisconnect"/>.
        /// </summary>
        private void SerialPort_ErrorReceived(object sender, SerialErrorReceivedEventArgs e)
        {
            Log.Warning("[SCALE] SerialPort ErrorReceived: {Error} — kích hoạt reconnect.", e.EventType);
            HandleDisconnect();
        }

        // =========================================================================
        // THUẬT TOÁN TÍNH ĐỘ ỔN ĐỊNH
        // =========================================================================

        /// <summary>
        /// Thuật toán ổn định theo yêu cầu gốc:
        /// 1. Chỉ tin tưởng và bắt đầu đếm khi đầu cân gửi cờ ỔN ĐỊNH (P+ hoặc i00).
        /// 2. Dù đầu cân báo ỔN ĐỊNH, vẫn phải thu thập đủ 10 mẫu và dao động &lt;= 50kg thì mới chốt (chống chốt non).
        /// 3. Bất cứ khi nào đầu cân báo DAO ĐỘNG (@+ hoặc i80), lập tức hủy bộ đếm và báo chưa ổn định.
        /// Áp dụng throttling 40ms trước khi phát <see cref="WeightChanged"/>.
        /// </summary>
        private void ProcessWeightStability(decimal weight, bool isHardwareStable)
        {
            lock (_weightLock)
            {
                _currentWeight = weight;
            }

            if (weight > _minWeightThreshold)
            {
                if (!isHardwareStable)
                {
                    // Khi cân báo dao động (@+) -> KHÔNG đếm, báo chưa ổn định, xóa bộ đệm
                    IsScaleStable = false;
                    lock (_weightBuffer)
                    {
                        if (_weightBuffer.Count > 0) _weightBuffer.Clear();
                    }
                }
                else
                {
                    // Khi cân báo ổn định (P+) -> Mới bắt đầu gom 10 frame để chốt
                    lock (_weightBuffer)
                    {
                        _weightBuffer.Enqueue(weight);
                        if (_weightBuffer.Count > BufferSize) _weightBuffer.Dequeue();

                        if (_weightBuffer.Count < BufferSize)
                        {
                            IsScaleStable = false; // Chưa đủ 10 mẫu -> chưa chốt
                        }
                        else
                        {
                            decimal delta = _weightBuffer.Max() - _weightBuffer.Min();
                            IsScaleStable = (delta <= _stabilityDelta);
                        }
                    }
                }
            }
            else
            {
                IsScaleStable = false;
                lock (_weightBuffer)
                {
                    if (_weightBuffer.Count > 0) _weightBuffer.Clear();
                }
            }

            bool stateChanged = (IsScaleStable != _lastBroadcastedStableState);
            if (stateChanged || _uiThrottleStopwatch.ElapsedMilliseconds >= MinUiUpdateIntervalMs)
            {
                WeightChanged?.Invoke(CurrentWeight, IsScaleStable);
                _lastBroadcastedStableState = IsScaleStable;
                _uiThrottleStopwatch.Restart();
            }
        }

        // =========================================================================
        // AUTO-RECONNECT
        // =========================================================================

        /// <summary>
        /// Chủ động cưỡng bức đóng port và thực hiện quy trình kết nối lại.
        /// Dùng cho Watchdog khi phát hiện port vẫn Open nhưng không có dữ liệu đổ về.
        /// </summary>
        public void ForceReconnect()
        {
            if (_isConnected || _isReconnecting)
            {
                Log.Warning("[SCALE] Watchdog yêu cầu Force Reconnect tại {Port}...", _portName);
                HandleDisconnect();
            }
        }

        /// <summary>
        /// Xử lý sự kiện mất kết nối: đóng port an toàn, phát event <see cref="Disconnected"/>,
        /// và bắt đầu vòng lặp thử kết nối lại.
        /// </summary>
        private void HandleDisconnect()
        {
            // Chỉ xử lý một lần nếu đang reconnect
            if (_isReconnecting) return;

            _isConnected = false;
            _isReconnecting = true;

            Log.Warning("[SCALE] Phát hiện mất kết nối đầu cân tại {Port}. Bắt đầu thử kết nối lại...", _portName);

            SafeClosePort();
            Disconnected?.Invoke();
            StartReconnectLoop();
        }

        /// <summary>
        /// Đóng port hiện tại một cách an toàn, unsubscribe events, bỏ qua mọi exception.
        /// </summary>
        private void SafeClosePort()
        {
            lock (_weightLock) // Tái sử dụng lock để bảo vệ _serialPort
            {
                try
                {
                    if (_serialPort != null)
                    {
                        _serialPort.DataReceived -= SerialPort_DataReceived;
                        _serialPort.ErrorReceived -= SerialPort_ErrorReceived;
                        if (_serialPort.IsOpen) _serialPort.Close();
                        _serialPort.Dispose();
                        _serialPort = null!;
                    }
                }
                catch { /* Bỏ qua — port có thể đã ở trạng thái lỗi */ }
            }
        }

        /// <summary>
        /// Vòng lặp thử kết nối lại với backoff tăng dần:
        /// 5s → 5s → 10s → 30s → 60s (lặp lại ở 60s).
        /// Dừng lại khi kết nối thành công hoặc khi <see cref="Dispose"/> được gọi.
        /// </summary>
        private void StartReconnectLoop()
        {
            // Hủy vòng lặp cũ nếu có
            _reconnectCts.Cancel();
            _reconnectCts = new CancellationTokenSource();
            var token = _reconnectCts.Token;

            Task.Run(async () =>
            {
                int attempt = 0;

                while (!token.IsCancellationRequested)
                {
                    if (string.IsNullOrEmpty(_portName) || _portName == "None") break;
                    int delaySec = ReconnectDelaysSeconds[Math.Min(attempt, ReconnectDelaysSeconds.Length - 1)];
                    Log.Information("[SCALE] Thử kết nối lại lần {Attempt} sau {Delay}s...", attempt + 1, delaySec);
                    ReconnectAttempting?.Invoke(attempt + 1);

                    try
                    {
                        await Task.Delay(TimeSpan.FromSeconds(delaySec), token);
                    }
                    catch (OperationCanceledException)
                    {
                        break; // Dispose() đã được gọi
                    }

                    if (token.IsCancellationRequested) break;

                    try
                    {
                        // Thử mở lại cổng
                        var testPort = new SerialPort(_portName, _baudRate, _parity, _dataBits, _stopBits)
                        {
                            Encoding = Encoding.ASCII,
                            ReadTimeout = 500,
                            ReceivedBytesThreshold = 1
                        };

                        try
                        {
                            testPort.Open();
                            testPort.DataReceived += SerialPort_DataReceived;
                            testPort.ErrorReceived += SerialPort_ErrorReceived;

                            // Thành công
                            _serialPort = testPort;
                            _isConnected = true;
                            _isReconnecting = false;

                            lock (_weightBuffer) _weightBuffer.Clear();
                            lock (_incomingDataBuffer) _incomingDataBuffer.Clear();

                            Log.Information("[SCALE] ✅ KẾT NỐI LẠI THÀNH CÔNG tại {Port} (sau {Attempt} lần thử).", _portName, attempt + 1);
                            Reconnected?.Invoke();
                            return;
                        }
                        catch
                        {
                            // Nếu Open() lỗi, phải dispose ngay đối tượng testPort này
                            testPort.Dispose();
                            throw; // Re-throw để catch bên ngoài log lỗi
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("[SCALE] Lần thử {Attempt} thất bại: {Msg}", attempt + 1, ex.Message);
                    }

                    attempt++;
                }

                _isReconnecting = false;
            }, token);
        }

        // =========================================================================
        // CLEANUP
        // =========================================================================

        /// <summary>Đóng kết nối Serial Port với đầu cân một cách an toàn.</summary>
        public void Close()
        {
            _reconnectCts.Cancel();
            SafeClosePort();
            Log.Information("[SCALE] Đã đóng cổng cân.");
        }

        /// <summary>
        /// Khởi động lại kết nối với đầu cân bằng thông số mới.
        /// <para>
        /// Vì <see cref="ScaleService"/> là Singleton, toàn bộ subscriber hiện có
        /// (<c>WeightChanged</c>, <c>Disconnected</c>...) vẫn còn nguyên sau khi reinit.
        /// Không cần thay đổi gì ở ViewModel hay Coordinator.
        /// </para>
        /// </summary>
        public void Reinitialize(string portName, int baudRate, int dataBits,
                                 Parity parity, StopBits stopBits, IScaleProtocol protocol,
                                 decimal minWeightThreshold = 50, decimal stabilityDelta = 50)
        {
            Log.Information("[SCALE] Đang khởi động lại với cổng {Port} ({Baud} bps)...", portName, baudRate);
            Close();   // Hủy reconnect loop cũ + đóng port hiện tại
            Initialize(portName, baudRate, dataBits, parity, stopBits, protocol, minWeightThreshold, stabilityDelta);
            Log.Information("[SCALE] Khởi động lại hoàn tất.");
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            Close();
            _reconnectCts.Dispose();
        }
    }
}