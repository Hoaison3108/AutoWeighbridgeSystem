using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AutoWeighbridgeSystem.Models;
using Microsoft.Extensions.Configuration;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Điều phối các sự kiện phần cứng (Cân + RFID) và chuyển đổi chúng thành
    /// các hành động cấp cao để ViewModel phản ứng. ViewModel không cần biết
    /// cách đầu cân hay RFID hoạt động — chỉ cần phản ứng với các event từ đây.
    /// </summary>
    public sealed class DashboardEventCoordinator : IDisposable
    {
        // =========================================================================
        // DEPENDENCIES
        // =========================================================================
        private readonly ScaleService _scaleService;
        private readonly RfidMultiService _rfidService;
        private readonly DashboardWorkflowService _dashboardWorkflow;
        private readonly HardwareWatchdogService _hardwareWatchdog;

        // =========================================================================
        // CẤU HÌNH (đọc từ appsettings — tách ra khỏi ViewModel)
        // =========================================================================
        private int _queueTimeoutSeconds = 45;
        private int _hardwareWatchdogSeconds = 60;

        // =========================================================================
        // STATE DELEGATES — ViewModel cung cấp giá trị hiện tại qua Func<>
        // Dùng Func thay vì tham chiếu trực tiếp để tránh coupling và thread-safe
        // =========================================================================
        private Func<bool> _getIsAutoMode = () => true;
        private Func<bool> _getIsWeightLocked = () => false;
        private Func<bool> _getIsProcessingSave = () => false;
        private Func<string> _getSelectedProductName = () => "Hàng hóa";

        /// <summary>Cờ: đã nhận được dữ liệu từ đầu cân lần đầu (port open ≠ thiết bị kết nối thật).</summary>
        private volatile bool _scaleDataReceived = false;

        // =========================================================================
        // EVENTS — ViewModel lắng nghe và phản ứng
        // =========================================================================

        /// <summary>Kích hoạt khi thuật toán quyết định cần lưu phiếu cân tự động.</summary>
        public event Func<decimal, PendingVehicleData, Task>? AutoSaveRequested;

        /// <summary>Kích hoạt khi cần xóa form và reset về trạng thái ban đầu.</summary>
        public event Action<string>? FormResetRequested;

        /// <summary>Kích hoạt khi cần hiển thị thông báo lên màn hình Camera.</summary>
        public event Action<string, bool>? CameraMessageRequested;  // (message, autoHide)

        /// <summary>Kích hoạt khi đầu đọc RFID bắt được thẻ hợp lệ (không phải Desk).</summary>
        public event Action<string, string>? RfidCaptured;  // (cardId, locationLabel)

        /// <summary>Kích hoạt khi cần bắt đầu đếm ngược timeout cho xe đang chờ.</summary>
        public event Action? PendingTimeoutStartRequested;

        /// <summary>
        /// Kích hoạt khi trạng thái kết nối của một thiết bị phần cứng thay đổi.
        /// Tham số: <c>(string device, HardwareConnectionStatus status)</c>.<br/>
        /// Device names: <c>"Scale"</c> | <c>"ScaleIn"</c> | <c>"ScaleOut"</c> | <c>"Desk"</c> | <c>"Camera"</c>.
        /// </summary>
        public event Action<string, HardwareConnectionStatus>? HardwareStatusChanged;

        // =========================================================================
        // CONSTRUCTOR
        // =========================================================================
        public DashboardEventCoordinator(
            ScaleService scaleService,
            RfidMultiService rfidService,
            DashboardWorkflowService dashboardWorkflow,
            HardwareWatchdogService hardwareWatchdog,
            IConfiguration configuration)
        {
            _scaleService = scaleService;
            _rfidService = rfidService;
            _dashboardWorkflow = dashboardWorkflow;
            _hardwareWatchdog = hardwareWatchdog;

            LoadConfiguration(configuration);
        }

        private void LoadConfiguration(IConfiguration configuration)
        {
            if (int.TryParse(configuration["ScaleSettings:QueueTimeoutSeconds"], out int qt))
                _queueTimeoutSeconds = qt;
            if (int.TryParse(configuration["ScaleSettings:HardwareWatchdogSeconds"], out int wd))
                _hardwareWatchdogSeconds = wd;
        }

        // =========================================================================
        // PUBLIC API
        // =========================================================================

        /// <summary>
        /// Khởi động Coordinator: subscribe hardware events và watchdog.
        /// ViewModel gọi hàm này trong constructor, truyền vào các Func để
        /// Coordinator có thể đọc trạng thái hiện tại của ViewModel khi cần.
        /// </summary>
        public void Start(
            Func<bool> getIsAutoMode,
            Func<bool> getIsWeightLocked,
            Func<bool> getIsProcessingSave,
            Func<string> getSelectedProductName)
        {
            _getIsAutoMode = getIsAutoMode;
            _getIsWeightLocked = getIsWeightLocked;
            _getIsProcessingSave = getIsProcessingSave;
            _getSelectedProductName = getSelectedProductName;

            _scaleService.WeightChanged          += OnScaleWeightChanged;
            _rfidService.CardRead                 += OnRfidCardRead;

            // Subscribe trạng thái kết nối phần cứng — chuyển tiếp thông báo lên UI
            _scaleService.Disconnected            += OnScaleDisconnected;
            _scaleService.Reconnected             += OnScaleReconnected;
            _scaleService.ReconnectAttempting     += OnScaleReconnectAttempting;
            _rfidService.ReaderDisconnected       += OnRfidReaderDisconnected;
            _rfidService.ReaderReconnected        += OnRfidReaderReconnected;

            StartHardwareWatchdog();

            // Replay trạng thái ban đầu: hardware có thể đã mở port TRƯỚC khi
            // Coordinator kịp subscribe vào events → kiểm tra trực tiếp, không qua event chain
            // (không dùng NotifyInitialStatus để tránh trigger toast "đã kết nối lại")
            RefreshInitialHardwareStatus();

            Log.Information("[COORDINATOR] Coordinator đã khởi động và lắng nghe phần cứng.");
        }

        /// <summary>Yêu cầu bắt đầu đếm ngược pending timeout (cho xe đang chờ lên cân).</summary>
        public void RequestPendingTimeout()
        {
            _hardwareWatchdog.StartPendingTimeout(
                _queueTimeoutSeconds,
                () => _dashboardWorkflow.ClearPendingData());
        }

        /// <summary>Hủy bỏ pending timeout hiện tại (form vừa được reset).</summary>
        public void CancelPendingTimeout()
        {
            _hardwareWatchdog.CancelPendingTimeout();
        }

        /// <summary>Xóa dữ liệu xe đang chờ trong workflow.</summary>
        public void ClearPendingData()
        {
            _dashboardWorkflow.ClearPendingData();
        }

        /// <summary>Ngưỡng trọng lượng tối thiểu để xử lý (đọc từ DashboardWorkflowService).</summary>
        public decimal MinWeightThreshold => _dashboardWorkflow.MinWeightThreshold;

        // =========================================================================
        // HARDWARE EVENT HANDLERS (tách ra khỏi ViewModel)
        // =========================================================================

        private void OnScaleWeightChanged(decimal weight, bool isStable)
        {
            // Thông báo watchdog vẫn nhận được tín hiệu
            _hardwareWatchdog.NotifyScaleDataReceived();

            // Chỉ set Online khi dữ liệu thực tế chạy vào — port open không đảm bảo thiết bị kết nối
            if (!_scaleDataReceived)
            {
                _scaleDataReceived = true;
                Application.Current?.Dispatcher.BeginInvoke(() =>
                    HardwareStatusChanged?.Invoke("Scale", HardwareConnectionStatus.Online));
            }

            var decision = _dashboardWorkflow.EvaluateScaleEvent(
                weight,
                isStable,
                _getIsAutoMode(),
                _getIsProcessingSave(),
                _getIsWeightLocked());

            // Không có hành động cần thiết — bỏ qua
            if (!decision.ShouldClearPendingAndReset && !decision.ShouldSave) return;

            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    if (decision.ShouldClearPendingAndReset)
                    {
                        FormResetRequested?.Invoke(decision.CameraMessage);
                        return;
                    }

                    // Kiểm tra lại lần nữa trên UI thread trước khi save
                    if (_getIsProcessingSave() || _getIsWeightLocked()) return;

                    await (AutoSaveRequested?.Invoke(decision.WeightToSave, decision.PendingVehicle)
                           ?? Task.CompletedTask);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[COORDINATOR] Lỗi trong Dispatcher callback của Scale event.");
                }
            });
        }

        private void OnRfidCardRead(string readerRole, string cardId)
        {
            // Ghi nhận reader này đã thực sự đọc được thẻ — mới set Online (không chỉ port open)
            Application.Current?.Dispatcher.BeginInvoke(() =>
                HardwareStatusChanged?.Invoke(readerRole, HardwareConnectionStatus.Online));

            Application.Current?.Dispatcher.InvokeAsync(async () =>
            {
                try
                {
                    // Bộ lọc chống đọc trùng (cooldown)
                    if (_dashboardWorkflow.ShouldIgnoreRfidRead(readerRole)) return;

                    // Đầu đọc tại bàn (Desk) không tham gia vào luồng cân tự động
                    if (readerRole == ReaderRoles.Desk) return;

                    // Thông báo cho ViewModel biết mã thẻ vừa đọc được
                    RfidCaptured?.Invoke(cardId, $"Mã thẻ RFID (Nhận từ {readerRole}):");

                    var decision = await _dashboardWorkflow.EvaluateRfidEventAsync(
                        cardId,
                        _getSelectedProductName(),
                        _getIsAutoMode(),
                        isScaleStable: _scaleService.IsScaleStable,
                        currentWeight: _scaleService.CurrentWeight);

                    if (decision.ShouldShowMessage)
                        CameraMessageRequested?.Invoke(decision.CameraMessage, decision.MessageAutoHide);

                    if (decision.ShouldStartPendingTimeout)
                        PendingTimeoutStartRequested?.Invoke();

                    if (decision.ShouldSave)
                        await (AutoSaveRequested?.Invoke(decision.WeightToSave, decision.PendingVehicle)
                               ?? Task.CompletedTask);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "[COORDINATOR] Lỗi trong Dispatcher callback của RFID event.");
                }
            });
        }

        private void StartHardwareWatchdog()
        {
            _hardwareWatchdog.StartHardwareWatchdog(
                _hardwareWatchdogSeconds,
                () =>
                {
                    // Mất tín hiệu dữ liệu (Im lặng): 
                    // Nếu port vẫn mở thì chỉ chuyển sang màu Vàng (Standby), không báo Đỏ
                    _scaleDataReceived = false;
                    Application.Current?.Dispatcher.BeginInvoke(() =>
                    {
                        var status = _scaleService.IsConnected 
                            ? HardwareConnectionStatus.Connecting 
                            : HardwareConnectionStatus.Offline;
                            
                        HardwareStatusChanged?.Invoke("Scale", status);
                        
                        // Không hiện thông báo to trên Camera khi chỉ là trạng thái nghỉ (im lặng)
                        // Chỉ báo đỏ nếu port thực sự bị ngắt (xử lý trong OnScaleDisconnected)
                    });
                });
        }

        // =========================================================================
        // HARDWARE CONNECTION STATUS HANDLERS
        // =========================================================================

        /// <summary>
        /// Đọc trạng thái thực của hardware ngay sau khi Subscribe events (giải quyết race condition
        /// khởi động). Chỉ set dot màu — không trigger toast hay camera message.
        /// </summary>
        private void RefreshInitialHardwareStatus()
        {
            // Scale: Nếu port mở được thì 'Connecting' (vàng) — chưa có dữ liệu nên chưa thể xác nhận Online
            // Port mở không đảm bảo cáp được cắm vào thiết bị thực tế
            var scaleStatus = _scaleService.IsConnected
                ? HardwareConnectionStatus.Connecting   // port mở, chờ dữ liệu đầu tiên
                : HardwareConnectionStatus.Offline;     // port không mở được
            HardwareStatusChanged?.Invoke("Scale", scaleStatus);

            // RFID: 'Connecting' (vàng) cho các reader có port đang mở
            // → sẽ chuyển 'Online' (xanh) chỉ khi đọc được thẻ (OnRfidCardRead)
            var connectedRoles = _rfidService.ConnectedReaderRoles;
            foreach (var role in new[] { ReaderRoles.ScaleIn, ReaderRoles.ScaleOut, ReaderRoles.Desk })
            {
                var status = connectedRoles.Contains(role)
                    ? HardwareConnectionStatus.Connecting  // port mở, chờ card
                    : HardwareConnectionStatus.Offline;    // port không mở
                HardwareStatusChanged?.Invoke(role, status);
            }
        }


        /// <summary>Phản ứng khi đầu cân mất kết nối: reset form và hiển thị cảnh báo.</summary>
        private void OnScaleDisconnected()
        {
            _scaleDataReceived = false; // reset để lần kết nối lại phải nhận data mới được set Online
            _dashboardWorkflow.ClearPendingData();
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                HardwareStatusChanged?.Invoke("Scale", HardwareConnectionStatus.Offline);
                FormResetRequested?.Invoke("⚠️ ĐẦU CÂN MẤT KẾT NỐI! Đang thử kết nối lại...");
            });
        }

        /// <summary>Phản ứng khi đầu cân kết nối lại thành công.</summary>
        private void OnScaleReconnected()
        {
            // Kết nối lại thành công — reset cờ, đợi dữ liệu thực tế mới set Online
            // (OnScaleWeightChanged sẽ set Online khi frame đầu tiên đến)
            _scaleDataReceived = false;
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                HardwareStatusChanged?.Invoke("Scale", HardwareConnectionStatus.Connecting);
                CameraMessageRequested?.Invoke("✅ ĐẦU CÂN ĐÃ KẾT NỐI LẠI!", true);
            });
        }

        /// <summary>Hiển thị thông tin lần thử kết nối lại đang diễn ra.</summary>
        private void OnScaleReconnectAttempting(int attempt)
        {
            // Reset cờ khi đang thử lại
            _scaleDataReceived = false;
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                HardwareStatusChanged?.Invoke("Scale", HardwareConnectionStatus.Reconnecting);
                CameraMessageRequested?.Invoke($"🔄 ĐẦU CÂN: Đang thử kết nối lại lần {attempt}...", false);
            });
        }

        /// <summary>Phản ứng khi một đầu đọc RFID mất kết nối.</summary>
        private void OnRfidReaderDisconnected(string roleName)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                HardwareStatusChanged?.Invoke(roleName, HardwareConnectionStatus.Offline);
                CameraMessageRequested?.Invoke($"⚠️ RFID {roleName} MẤT KẾT NỐI! Đang thử kết nối lại...", false);
            });
        }

        /// <summary>Phản ứng khi một đầu đọc RFID kết nối lại thành công.</summary>
        private void OnRfidReaderReconnected(string roleName)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                HardwareStatusChanged?.Invoke(roleName, HardwareConnectionStatus.Online);
                CameraMessageRequested?.Invoke($"✅ RFID {roleName} ĐÃ KẾT NỐI LẠI!", true);
            });
        }

        // =========================================================================
        // CLEANUP
        // =========================================================================
        public void Dispose()
        {
            _scaleService.WeightChanged         -= OnScaleWeightChanged;
            _scaleService.Disconnected           -= OnScaleDisconnected;
            _scaleService.Reconnected            -= OnScaleReconnected;
            _scaleService.ReconnectAttempting    -= OnScaleReconnectAttempting;
            _rfidService.CardRead                -= OnRfidCardRead;
            _rfidService.ReaderDisconnected      -= OnRfidReaderDisconnected;
            _rfidService.ReaderReconnected       -= OnRfidReaderReconnected;
            _hardwareWatchdog.StopAll();
            Log.Information("[COORDINATOR] Coordinator đã dừng và giải phóng resources.");
        }
    }
}
