using System;
using System.Collections.Generic;
using System.IO.Ports;
using System.Text; // Thêm thư viện này
using System.Threading;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    public class RfidMultiService : IDisposable
    {
        private readonly List<SerialPort> _activePorts = new List<SerialPort>();
        public event Action<string, string> CardRead;

        private const char STX = (char)0x02;
        private const char ETX = (char)0x03;

        public void AddReader(string roleName, string comPort, int baudRate)
        {
            // THAY ĐỔI 1: Log cảnh báo nếu thông số trống
            if (string.IsNullOrWhiteSpace(comPort))
            {
                Log.Warning("[RFID] Không thể thêm đầu đọc {Role} vì ComPort bị trống! Kiểm tra appsettings.json", roleName);
                return;
            }

            try
            {
                // THAY ĐỔI 2: Ép mã hóa Encoding.Default để đọc được các ký tự điều khiển STX/ETX
                SerialPort port = new SerialPort(comPort, baudRate, Parity.None, 8, StopBits.One);
                port.Encoding = Encoding.GetEncoding("ISO-8859-1"); // Đảm bảo đọc chuẩn 8-bit

                port.DataReceived += (sender, e) =>
                {
                    try
                    {
                        SerialPort sp = (SerialPort)sender;
                        Thread.Sleep(100); // Tăng lên 100ms cho chắc chắn nhận đủ gói tin

                        string rawData = sp.ReadExisting();
                        if (string.IsNullOrEmpty(rawData)) return;

                        Log.Information("[RFID] {Role} nhận dữ liệu thô: {Raw}", roleName, rawData);

                        // THAY ĐỔI 3: Dùng Regex hoặc xử lý chuỗi an toàn hơn
                        // Chỉ lấy các chữ số 0-9
                        string cleanId = "";
                        foreach (char c in rawData)
                        {
                            if (char.IsDigit(c)) cleanId += c;
                        }

                        if (!string.IsNullOrEmpty(cleanId))
                        {
                            Log.Debug("[RFID] {Role} trích xuất mã số: {Data}", roleName, cleanId);

                            // Bắn sự kiện
                            CardRead?.Invoke(roleName, cleanId);
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "[RFID] Lỗi xử lý dữ liệu từ {Role}", roleName);
                    }
                };

                port.Open();
                _activePorts.Add(port);
                Log.Information("[RFID] ĐÃ MỞ CỔNG {Port} cho đầu đọc {Role}", comPort, roleName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RFID] THẤT BẠI khi mở cổng {Port} cho {Role}", comPort, roleName);
            }
        }

        public void CloseAll()
        {
            foreach (var port in _activePorts)
            {
                try
                {
                    if (port.IsOpen)
                    {
                        port.DiscardInBuffer();
                        port.Close();
                    }
                    port.Dispose();
                }
                catch (Exception ex) { Log.Warning("[RFID] Lỗi đóng cổng {Port}: {Msg}", port.PortName, ex.Message); }
            }
            _activePorts.Clear();
        }

        public void Dispose() => CloseAll();
    }
}