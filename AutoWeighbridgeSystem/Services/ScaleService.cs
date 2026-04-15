using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO.Ports;
using System.Linq;
using System.Text;
using AutoWeighbridgeSystem.Services.Protocols;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    public class ScaleService : IDisposable
    {
        private SerialPort _serialPort;
        private IScaleProtocol _protocol;

        private readonly Queue<decimal> _weightBuffer = new Queue<decimal>();
        private readonly StringBuilder _incomingDataBuffer = new StringBuilder();

        private const int BufferSize = 10;

        // === CÁC BIẾN CHO THROTTLING THÔNG MINH ===
        private readonly Stopwatch _uiThrottleStopwatch = Stopwatch.StartNew();
        private const int MinUiUpdateIntervalMs = 40; // ~25 FPS cho UI siêu mượt
        private bool _lastBroadcastedStableState = false; // Nhớ trạng thái để không bỏ lỡ nhịp chốt

        public decimal CurrentWeight { get; private set; }
        public bool IsScaleStable { get; private set; }

        public event Action<decimal, bool> WeightChanged;

        public void Initialize(string portName, int baudRate, int dataBits, Parity parity, StopBits stopBits, IScaleProtocol protocol)
        {
            try
            {
                _protocol = protocol;

                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Close();
                    _serialPort.Dispose();
                }

                _serialPort = new SerialPort(portName, baudRate, parity, dataBits, stopBits)
                {
                    Encoding = Encoding.ASCII,
                    ReadTimeout = 500,
                    ReceivedBytesThreshold = 1
                };

                _serialPort.DataReceived += SerialPort_DataReceived;
                _serialPort.Open();

                Log.Information("[SCALE] Đã kết nối đầu cân chuẩn {Protocol} tại {Port}", protocol.ProtocolName, portName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SCALE] Lỗi khởi tạo cổng {Port}", portName);
            }
        }

        private void SerialPort_DataReceived(object sender, SerialDataReceivedEventArgs e)
        {
            if (!_serialPort.IsOpen) return;

            try
            {
                // Đọc TẤT CẢ những gì đang kẹt trong cổng COM
                string rawData = _serialPort.ReadExisting();
                _incomingDataBuffer.Append(rawData);

                string currentBuffer = _incomingDataBuffer.ToString();

                // 1. Tìm vị trí của cờ P+ hoặc @+ TỪ CUỐI LÊN (Lấy tín hiệu mới nhất)
                int pIndex = currentBuffer.LastIndexOf("P+");
                int aIndex = currentBuffer.LastIndexOf("@+");
                int targetIndex = Math.Max(pIndex, aIndex);

                // 2. Nếu tìm thấy và đủ độ dài (VD: P+029780 cần 8 ký tự)
                if (targetIndex != -1 && targetIndex + 8 <= currentBuffer.Length)
                {
                    string weightStr = currentBuffer.Substring(targetIndex + 2, 6);

                    if (decimal.TryParse(weightStr, out decimal weight))
                    {
                        bool isHardwareStable = (targetIndex == pIndex);

                        // Đẩy số mới nhất đi xử lý
                        ProcessWeightStability(weight, isHardwareStable);

                        // QUAN TRỌNG NHẤT: Xóa sạch toàn bộ buffer để triệt tiêu độ trễ!
                        _incomingDataBuffer.Clear();
                    }
                }
                else if (_incomingDataBuffer.Length > 200)
                {
                    // Chống tràn bộ nhớ nếu nhiễu tín hiệu
                    _incomingDataBuffer.Clear();
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[SCALE] Nhiễu tín hiệu DataReceived: {Msg}", ex.Message);
            }
        }

        private void ProcessWeightStability(decimal weight, bool isHardwareStable)
        {
            CurrentWeight = weight;

            // === 1. THUẬT TOÁN LAI (HYBRID STABILITY) ===
            if (weight > 50)
            {
                // Nếu phần cứng báo ổn định (có chữ P+) -> Chốt ngay lập tức, độ trễ 0s
                if (isHardwareStable)
                {
                    IsScaleStable = true;
                    // Reset lại buffer mềm để chuẩn bị cho lần cân tiếp theo
                    if (_weightBuffer.Count > 0) _weightBuffer.Clear();
                }
                else
                {
                    // Nếu phần cứng báo dao động (@+), đẩy vào Buffer để phần mềm tự tính
                    _weightBuffer.Enqueue(weight);
                    if (_weightBuffer.Count > BufferSize) _weightBuffer.Dequeue();

                    // Tăng độ lệch lên 10kg theo yêu cầu chống rung
                    decimal delta = _weightBuffer.Count > 0 ? _weightBuffer.Max() - _weightBuffer.Min() : 0;
                    IsScaleStable = (_weightBuffer.Count >= BufferSize) && (delta <= 10);
                }
            }
            else
            {
                IsScaleStable = false;
                if (_weightBuffer.Count > 0) _weightBuffer.Clear();
            }

            // === 2. THROTTLING THÔNG MINH (BẢO VỆ GIAO DIỆN) ===
            bool stateChanged = (IsScaleStable != _lastBroadcastedStableState);

            // Bắn Event NẾU: Trạng thái vừa thay đổi (Ưu tiên tuyệt đối) HOẶC đã đủ thời gian 40ms
            if (stateChanged || _uiThrottleStopwatch.ElapsedMilliseconds >= MinUiUpdateIntervalMs)
            {
                WeightChanged?.Invoke(CurrentWeight, IsScaleStable);

                // Cập nhật lại bộ nhớ đệm cho lần sau
                _lastBroadcastedStableState = IsScaleStable;
                _uiThrottleStopwatch.Restart();
            }
        }

        public void Close()
        {
            if (_serialPort != null && _serialPort.IsOpen)
            {
                _serialPort.DataReceived -= SerialPort_DataReceived;
                _serialPort.Close();
                Log.Information("[SCALE] Đã đóng cổng cân.");
            }
        }

        public void Dispose()
        {
            Close();
            _serialPort?.Dispose();
        }
    }
}