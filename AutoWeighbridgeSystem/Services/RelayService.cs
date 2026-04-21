using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ cấp thấp để giao tiếp trực tiếp với mạch Relay USB/Serial (LC Relay / CH340).
    /// Phiên bản nâng cấp: Duy trì cổng COM mở liên tục (Persistent) để tránh lỗi khởi tạo Driver.
    /// </summary>
    public class RelayService : IDisposable
    {
        private SerialPort? _serialPort;
        private readonly SemaphoreSlim _lock = new SemaphoreSlim(1, 1);
        
        private string? _lastPort;
        private int _lastBaud;
        private Parity _lastParity;
        private int _lastDataBits;
        private StopBits _lastStopBits;

        private readonly byte[] _relayOnCommand  = { 0xA0, 0x01, 0x01, 0xA2 };
        private readonly byte[] _relayOffCommand = { 0xA0, 0x01, 0x00, 0xA1 };

        /// <summary>
        /// Mở cổng COM và giữ kết nối. Nếu cổng đã mở với thông số cũ, sẽ tự động đóng và mở lại.
        /// </summary>
        public async Task<bool> OpenAsync(string comPort, int baudRate, Parity parity, int dataBits, StopBits stopBits)
        {
            await _lock.WaitAsync();
            try
            {
                // Nếu thông số thay đổi hoặc cổng chưa khởi tạo -> Khởi tạo mới
                if (_serialPort == null || _lastPort != comPort || _lastBaud != baudRate)
                {
                    CloseInternal();
                    _serialPort = new SerialPort(comPort, baudRate, parity, dataBits, stopBits)
                    {
                        ReadTimeout = 500,
                        WriteTimeout = 500
                    };
                    
                    _lastPort = comPort;
                    _lastBaud = baudRate;
                    _lastParity = parity;
                    _lastDataBits = dataBits;
                    _lastStopBits = stopBits;
                }

                if (!_serialPort.IsOpen)
                {
                    _serialPort.Open();
                    Log.Information("[RELAY] Đã mở kết nối duy trì tới cổng {ComPort}", comPort);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RELAY] Không thể mở kết nối tới {ComPort}", comPort);
                CloseInternal(); // Giải phóng nếu lỗi
                return false;
            }
            finally
            {
                _lock.Release();
            }
        }

        /// <summary>
        /// Kích hoạt còi báo hiệu. Sử dụng kết nối hiện có.
        /// </summary>
        public async Task TriggerAlarmAsync(string comPort, int baudRate, Parity parity, int dataBits, StopBits stopBits, int durationMs)
        {
            await _lock.WaitAsync();
            try
            {
                // 1. Phải đảm bảo cổng đã mở
                if (_serialPort == null || !_serialPort.IsOpen)
                {
                    _lock.Release(); // Nhường lock cho OpenAsync
                    bool opened = await OpenAsync(comPort, baudRate, parity, dataBits, stopBits);
                    await _lock.WaitAsync();

                    if (!opened) throw new InvalidOperationException($"Không thể mở cổng {comPort}");
                }

                // 2. Gửi lệnh Bật
                _serialPort.Write(_relayOnCommand, 0, _relayOnCommand.Length);
                
                // 3. Chờ (Không giữ lock để tránh block các tác vụ khác nếu có)
                _lock.Release();
                await Task.Delay(durationMs);
                await _lock.WaitAsync();

                // 4. Gửi lệnh Tắt
                if (_serialPort != null && _serialPort.IsOpen)
                {
                    _serialPort.Write(_relayOffCommand, 0, _relayOffCommand.Length);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RELAY] Lỗi khi thực thi lệnh còi qua cổng {ComPort}", comPort);
                CloseInternal(); // Đóng cổng nếu gặp lỗi để lần sau force-reopen
                throw;
            }
            finally
            {
                if (_lock.CurrentCount == 0) _lock.Release();
            }
        }

        public void Close()
        {
            _lock.Wait();
            CloseInternal();
            _lock.Release();
        }

        private void CloseInternal()
        {
            try
            {
                if (_serialPort != null)
                {
                    if (_serialPort.IsOpen) _serialPort.Close();
                    _serialPort.Dispose();
                }
            }
            catch { /* Ignore */ }
            finally
            {
                _serialPort = null;
            }
        }

        public void Dispose()
        {
            CloseInternal();
            _lock.Dispose();
        }
    }
}
