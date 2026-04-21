using System;
using System.IO.Ports;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ điều phối giao tiếp trực tiếp với mạch LCRelay USB (CH340).
    /// Tối ưu hóa: Duy trì kết nối persistent, cơ chế chống nhiễu DTR/RTS và tự động phục hồi.
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
        /// Mở hoặc duy trì kết nối tới cổng COM.
        /// </summary>
        public async Task<bool> OpenAsync(string comPort, int baudRate, Parity parity, int dataBits, StopBits stopBits)
        {
            bool lockAcquired = false;
            try
            {
                await _lock.WaitAsync();
                lockAcquired = true;
                return EnsureOpenInternal(comPort, baudRate, parity, dataBits, stopBits);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RELAY] Lỗi khi mở cổng {ComPort}", comPort);
                return false;
            }
            finally
            {
                if (lockAcquired) _lock.Release();
            }
        }

        /// <summary>
        /// Kích hoạt chuông báo hiệu dạng xung (Pulse).
        /// </summary>
        public async Task TriggerBellAsync(string comPort, int baudRate, Parity parity, int dataBits, StopBits stopBits, int durationMs)
        {
            bool lockAcquired = false;
            try
            {
                await _lock.WaitAsync();
                lockAcquired = true;

                if (!EnsureOpenInternal(comPort, baudRate, parity, dataBits, stopBits))
                    throw new InvalidOperationException($"Không thể mở cổng {comPort}");

                // 1. Xóa buffer rác và gửi lệnh Bật
                _serialPort!.DiscardInBuffer();
                _serialPort!.DiscardOutBuffer();
                _serialPort!.Write(_relayOnCommand, 0, _relayOnCommand.Length);
                Log.Debug("[RELAY] LCRelay -> Gửi lệnh Bật qua {ComPort}", comPort);
                
                // 2. Chờ đủ thời gian (Giữ khóa để đảm bảo tính tuần tự)
                await Task.Delay(durationMs);

                // 3. Gửi lệnh Tắt
                try
                {
                    if (_serialPort != null && _serialPort.IsOpen)
                    {
                        _serialPort.Write(_relayOffCommand, 0, _relayOffCommand.Length);
                        Log.Debug("[RELAY] LCRelay -> Gửi lệnh Tắt qua {ComPort}", comPort);
                    }
                    else
                    {
                        Log.Warning("[RELAY] Cổng bị đóng trước khi gửi lệnh Tắt. Đang thử phục hồi...");
                        if (EnsureOpenInternal(comPort, baudRate, parity, dataBits, stopBits))
                        {
                            _serialPort!.Write(_relayOffCommand, 0, _relayOffCommand.Length);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Log.Warning(ex, "[RELAY] Lệnh Tắt thất bại. Thử lại lần cuối...");
                    await Task.Delay(200); 
                    if (EnsureOpenInternal(comPort, baudRate, parity, dataBits, stopBits))
                    {
                        _serialPort!.Write(_relayOffCommand, 0, _relayOffCommand.Length);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RELAY] Lỗi thực thi xung LCRelay qua {ComPort}", comPort);
                CloseInternal();
                throw;
            }
            finally
            {
                if (lockAcquired) _lock.Release();
            }
        }

        private bool EnsureOpenInternal(string comPort, int baudRate, Parity parity, int dataBits, StopBits stopBits)
        {
            try
            {
                if (_serialPort != null && (_lastPort != comPort || _lastBaud != baudRate))
                {
                    CloseInternal();
                }

                if (_serialPort == null)
                {
                    _serialPort = new SerialPort(comPort, baudRate, parity, dataBits, stopBits)
                    {
                        ReadTimeout = 500,
                        WriteTimeout = 500,
                        DtrEnable = true, // Tăng độ ổn định cho chip CH340
                        RtsEnable = true
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
                    Log.Information("[RELAY] Đã mở/duy trì cổng {ComPort} thành công.", comPort);
                }

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RELAY] Lỗi EnsureOpenInternal tại {ComPort}", comPort);
                CloseInternal();
                return false;
            }
        }

        public void Close()
        {
            _lock.Wait();
            try { CloseInternal(); }
            finally { _lock.Release(); }
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
