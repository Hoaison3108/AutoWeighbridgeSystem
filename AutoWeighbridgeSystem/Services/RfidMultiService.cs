using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Services.RfidDrivers;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ quản lý nhiều đầu đọc thẻ RFID cùng lúc.
    /// Mỗi đầu đọc được gán một vai trò (<see cref="Models.ReaderRoles"/>):
    /// ScaleIn (xe vào), ScaleOut (xe ra), Desk (bàn điều hành).
    /// <para>
    /// <b>Driver-based:</b> Loại phần cứng được trừu tượng hóa qua <see cref="IRfidReaderDriver"/>.
    /// Thay đổi loại đầu đọc chỉ cần đổi <c>DriverType</c> trong <c>appsettings.json</c>,
    /// không cần sửa code.
    /// </para>
    /// <para>
    /// <b>Auto-Reconnect:</b> Mỗi đầu đọc có watchdog độc lập. Khi mất kết nối,
    /// service tự thử lại với backoff 5s → 5s → 10s → 30s → 60s.
    /// </para>
    /// </summary>
    public class RfidMultiService : IDisposable
    {
        // =========================================================================
        // INNER CLASS — trạng thái mỗi đầu đọc
        // =========================================================================

        private sealed class RfidReaderEntry
        {
            public string   RoleName  { get; }
            public string   ComPort   { get; }
            public int      BaudRate  { get; }

            public IRfidReaderDriver? Driver { get; set; }
            public volatile bool IsReconnecting;
            public CancellationTokenSource ReconnectCts = new();

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

        private readonly List<RfidReaderEntry>  _readers       = new();
        private readonly RfidDriverFactory      _driverFactory = new();
        private string _driverType = "SerialHf";

        /// <summary>Trạng thái hiện tại: Các đầu đọc tự động (ScaleIn/ScaleOut) có đang được kích hoạt quét hay không.</summary>
        public bool IsAutoReadersActive { get; private set; } = false;

        private static readonly int[] ReconnectDelaysSeconds = { 5, 5, 10, 30, 60 };

        // =========================================================================
        // PUBLIC API
        // =========================================================================

        /// <summary>Số đầu đọc đang được quản lý (bao gồm đang reconnect).</summary>
        public int ActiveReaderCount => _readers.Count;

        /// <summary>Vai trò của các đầu đọc đang kết nối thành công.</summary>
        public IReadOnlyList<string> ConnectedReaderRoles
        {
            get
            {
                lock (_readers)
                {
                    return _readers
                        .Where(r => r.Driver != null && !r.IsReconnecting)
                        .Select(r => r.RoleName)
                        .ToList();
                }
            }
        }

        // =========================================================================
        // EVENTS
        // =========================================================================

        /// <summary>
        /// Phát ra khi có đầu đọc nhận được mã thẻ hợp lệ.
        /// Tham số: <c>(readerRole, cleanCardId)</c>.
        /// </summary>
        public event Action<string, string>? CardRead;
        
        /// <summary>Phát ra khi một đầu đọc mất kết nối. Tham số: roleName.</summary>
        public event Action<string>? ReaderDisconnected;

        /// <summary>Phát ra khi một đầu đọc kết nối lại thành công. Tham số: roleName.</summary>
        public event Action<string>? ReaderReconnected;

        /// <summary>Phát ra khi trạng thái quét UHF tự động thay đổi (Ngủ/Thức).</summary>
        public event Action<bool>? AutoReadersStateChanged;

        // =========================================================================
        // KHỞI TẠO
        // =========================================================================

        /// <summary>
        /// Gọi sau khi Coordinator đã subscribe events để replay trạng thái kết nối hiện tại.
        /// </summary>
        public void NotifyInitialStatus()
        {
            lock (_readers)
            {
                foreach (var entry in _readers.Where(r => r.Driver != null && !r.IsReconnecting))
                    ReaderReconnected?.Invoke(entry.RoleName);
            }
        }

        /// <summary>
        /// Thêm và khởi động một đầu đọc mới với driver hiện tại.
        /// </summary>
        public void AddReader(string roleName, string comPort, int baudRate)
        {
            if (string.IsNullOrWhiteSpace(comPort) || comPort == "None")
            {
                Log.Information("[RFID] Đầu đọc {Role} = 'None'. Bỏ qua.", roleName);
                return;
            }

            var entry = new RfidReaderEntry(roleName, comPort, baudRate);
            lock (_readers) { _readers.Add(entry); }

            // Mở driver trong background — không block UI/Startup
            Task.Run(() => InitializeDriver(entry));
        }

        /// <summary>Đóng và giải phóng tất cả đầu đọc đang hoạt động.</summary>
        public void CloseAll()
        {
            List<RfidReaderEntry> snapshot;
            lock (_readers) { snapshot = new List<RfidReaderEntry>(_readers); }

            foreach (var entry in snapshot)
            {
                entry.ReconnectCts.Cancel();
                SafeCloseDriver(entry);
            }

            lock (_readers) { _readers.Clear(); }
        }

        /// <summary>
        /// Khởi động lại tất cả đầu đọc với thông số và driver type mới.
        /// <para>
        /// Vì <see cref="RfidMultiService"/> là Singleton, subscriber của <see cref="CardRead"/>
        /// vẫn còn nguyên — không cần thay đổi ở Coordinator hay ViewModel.
        /// </para>
        /// </summary>
        public void ReinitializeReaders(
            string? inPort,   int inBaud,
            string? outPort,  int outBaud,
            string? deskPort, int deskBaud,
            string  driverType = "SerialHf")
        {
            Log.Information("[RFID] Reinitialize với driver '{Type}'...", driverType);
            _driverType = driverType;
            CloseAll();

            if (!string.IsNullOrEmpty(deskPort) && deskPort != "None") AddReader(Models.ReaderRoles.Desk,    deskPort, deskBaud);
            if (!string.IsNullOrEmpty(inPort)   && inPort   != "None") AddReader(Models.ReaderRoles.ScaleIn, inPort,   inBaud);
            if (!string.IsNullOrEmpty(outPort)  && outPort  != "None") AddReader(Models.ReaderRoles.ScaleOut,outPort,  outBaud);

            Log.Information("[RFID] Hoàn tất — {Count} đầu đọc ({Type}).", _readers.Count, driverType);
        }

        // =========================================================================
        // DRIVER LIFECYCLE
        // =========================================================================

        private void InitializeDriver(RfidReaderEntry entry)
        {
            try
            {
                SafeCloseDriver(entry);

                var driver = _driverFactory.Create(_driverType);

                // Subscribe events trước khi Open để không miss event
                driver.CardDetected  += cardId => CardRead?.Invoke(entry.RoleName, cardId);
                driver.Disconnected  += ()      => HandleDriverDisconnect(entry);

                driver.Open(entry.ComPort, entry.BaudRate);
                entry.Driver = driver;

                // Cả 3 làn (In/Out/Desk) đều quét theo trạng thái chung (phụ thuộc vào cân có tải hay không)
                if (IsAutoReadersActive)
                    driver.ResumeReading();
                else
                    driver.PauseReading();

                Log.Information("[RFID] ✅ [{Role}] Driver '{Type}' tại {Port} đã sẵn sàng.",
                    entry.RoleName, _driverType, entry.ComPort);
                ReaderReconnected?.Invoke(entry.RoleName);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[RFID] ❌ [{Role}] Không thể khởi tạo driver tại {Port}.",
                    entry.RoleName, entry.ComPort);
            }
        }

        private void SafeCloseDriver(RfidReaderEntry entry)
        {
            try
            {
                entry.Driver?.Close();
                entry.Driver?.Dispose();
                entry.Driver = null;
            }
            catch { }
        }

        /// <summary>
        /// Bật/Tắt chế độ quét của các đầu đọc ngoài trạm cân (ScaleIn, ScaleOut).
        /// <para>
        /// <b>KHÔNG điều khiển Desk reader</b> — Desk có vòng đời độc lập do
        /// <see cref="SetDeskReaderActive"/> quản lý, giúp Desk luôn sẵn sàng
        /// nhận thẻ ngay cả khi cân không có xe.
        /// </para>
        /// </summary>
        public void SetAutoReadersActive(bool isActive)
        {
            if (IsAutoReadersActive == isActive) return;
            IsAutoReadersActive = isActive;

            AutoReadersStateChanged?.Invoke(isActive);

            lock (_readers)
            {
                // Chỉ điều khiển ScaleIn và ScaleOut — Desk được quản lý riêng
                foreach (var entry in _readers.Where(r =>
                    r.Driver != null && r.RoleName != Models.ReaderRoles.Desk))
                {
                    if (isActive)
                        entry.Driver!.ResumeReading();
                    else
                        entry.Driver!.PauseReading();
                }
            }
        }

        /// <summary>
        /// Bật/Tắt riêng đầu đọc Desk (bàn điều hành / vị trí tài xế quẹt thẻ).
        /// <para>
        /// Desk luôn phát sóng độc lập với bàn cân để phục vụ 2 nghiệp vụ:<br/>
        /// - <b>Manual Mode:</b> Gán thẻ / cập nhật thông tin xe khi không có xe trên cân.<br/>
        /// - <b>Auto Mode:</b> Tài xế tự quẹt thẻ trên cân để cập nhật bì tự động.
        /// </para>
        /// </summary>
        /// <param name="isActive">True = bật sóng, False = ngừng phát sóng.</param>
        public void SetDeskReaderActive(bool isActive)
        {
            lock (_readers)
            {
                var desk = _readers.FirstOrDefault(r =>
                    r.RoleName == Models.ReaderRoles.Desk && r.Driver != null);

                if (desk == null)
                {
                    Log.Debug("[RFID] SetDeskReaderActive({Active}): Không tìm thấy Desk reader.", isActive);
                    return;
                }

                if (isActive)
                {
                    desk.Driver!.ResumeReading();
                    Log.Information("[RFID] Desk reader ĐÃ BẬT SÓNG (độc lập).");
                }
                else
                {
                    desk.Driver!.PauseReading();
                    Log.Information("[RFID] Desk reader ĐÃ TẮT SÓNG.");
                }
            }
        }

        // =========================================================================
        // AUTO-RECONNECT
        // =========================================================================

        private void HandleDriverDisconnect(RfidReaderEntry entry)
        {
            if (entry.IsReconnecting) return;
            entry.IsReconnecting = true;

            Log.Warning("[RFID] Mất kết nối [{Role}] tại {Port}. Bắt đầu reconnect...",
                entry.RoleName, entry.ComPort);

            SafeCloseDriver(entry);
            ReaderDisconnected?.Invoke(entry.RoleName);
            StartReconnectLoop(entry);
        }

        private void StartReconnectLoop(RfidReaderEntry entry)
        {
            entry.ReconnectCts.Cancel();
            entry.ReconnectCts = new CancellationTokenSource();
            var token = entry.ReconnectCts.Token;

            Task.Run(async () =>
            {
                int attempt = 0;

                while (!token.IsCancellationRequested)
                {
                    if (string.IsNullOrEmpty(entry.ComPort) || entry.ComPort == "None") break;

                    int delaySec = ReconnectDelaysSeconds[Math.Min(attempt, ReconnectDelaysSeconds.Length - 1)];
                    Log.Information("[RFID] [{Role}] Thử lại lần {N} sau {D}s...", entry.RoleName, attempt + 1, delaySec);

                    try { await Task.Delay(TimeSpan.FromSeconds(delaySec), token); }
                    catch (OperationCanceledException) { break; }

                    if (token.IsCancellationRequested) break;

                    try
                    {
                        var driver = _driverFactory.Create(_driverType);
                        driver.CardDetected += cardId => CardRead?.Invoke(entry.RoleName, cardId);
                        driver.Disconnected += ()     => HandleDriverDisconnect(entry);

                        driver.Open(entry.ComPort, entry.BaudRate);
                        entry.Driver         = driver;
                        entry.IsReconnecting = false;

                        if (IsAutoReadersActive)
                            driver.ResumeReading();
                        else
                            driver.PauseReading();

                        Log.Information("[RFID] ✅ [{Role}] Reconnect thành công tại {Port} (lần {N}).",
                            entry.RoleName, entry.ComPort, attempt + 1);
                        ReaderReconnected?.Invoke(entry.RoleName);
                        return;
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("[RFID] [{Role}] Lần {N} thất bại: {Msg}", entry.RoleName, attempt + 1, ex.Message);
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