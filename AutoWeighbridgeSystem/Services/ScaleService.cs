using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using AutoWeighbridgeSystem.Services.Protocols;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    public class ScaleService : IDisposable
    {
        private SerialPort _serialPort;
        private IScaleProtocol _protocol;

        // Dùng Queue để tối ưu hiệu năng O(1) thay vì List.RemoveAt(0) là O(n)
        private readonly Queue<decimal> _weightBuffer = new Queue<decimal>();

        // Buffer trung gian để ghép các mảnh chuỗi bị cắt đoạn từ cổng COM
        private readonly StringBuilder _incomingDataBuffer = new StringBuilder();

        private const int BufferSize = 20; // Khoảng 2 giây nếu đầu cân gửi 10Hz
        public decimal CurrentWeight { get; private set; }
        public bool IsScaleStable { get; private set; }

        public event Action<decimal, bool> WeightChanged;

        /// <summary>
        /// Khởi tạo kết nối cân với chuẩn Protocol linh hoạt
        /// </summary>
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
                    ReceivedBytesThreshold = 1 // Kích hoạt sự kiện ngay khi có 1 byte
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
                // 1. Đọc tất cả dữ liệu đang có và cộng dồn vào StringBuilder
                string rawData = _serialPort.ReadExisting();
                _incomingDataBuffer.Append(rawData);

                // 2. Chuyển cho Protocol mổ xẻ chuỗi
                // Protocol sẽ tìm trong đống "xà bần" dữ liệu xem có con số nào hợp lệ không
                decimal? parsedWeight = _protocol.ParseWeight(_incomingDataBuffer.ToString());

                if (parsedWeight.HasValue)
                {
                    ProcessWeightStability(parsedWeight.Value);

                    // Xóa buffer sau khi đã tìm thấy số cân hợp lệ để tránh tích tụ dữ liệu cũ
                    _incomingDataBuffer.Clear();
                }

                // Chống tràn bộ nhớ nếu nhận rác liên tục mà không parse được số
                if (_incomingDataBuffer.Length > 200) _incomingDataBuffer.Clear();
            }
            catch (Exception ex)
            {
                Log.Debug("[SCALE] Nhiễu tín hiệu: {Msg}", ex.Message);
            }
        }

        private void ProcessWeightStability(decimal weight)
        {
            CurrentWeight = weight;

            // Thuật toán Rolling Window bằng Queue
            _weightBuffer.Enqueue(weight);
            if (_weightBuffer.Count > BufferSize) _weightBuffer.Dequeue();

            // Logic xét ổn định: Dao động trong buffer không quá 5kg (có thể cấu hình lại)
            if (weight > 50)
            {
                decimal delta = _weightBuffer.Max() - _weightBuffer.Min();
                IsScaleStable = (_weightBuffer.Count >= BufferSize) && (delta <= 5);
            }
            else
            {
                IsScaleStable = false;
                if (_weightBuffer.Count > 0) _weightBuffer.Clear();
            }

            // Bắn sự kiện cập nhật UI
            WeightChanged?.Invoke(CurrentWeight, IsScaleStable);
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