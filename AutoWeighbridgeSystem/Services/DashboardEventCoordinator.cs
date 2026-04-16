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
            Func<bool>   getIsAutoMode,
            Func<bool>   getIsWeightLocked,
            Func<bool>   getIsProcessingSave,
            Func<string> getSelectedProductName)
        {
            _getIsAutoMode = getIsAutoMode;
            _getIsWeightLocked = getIsWeightLocked;
            _getIsProcessingSave = getIsProcessingSave;
            _getSelectedProductName = getSelectedProductName;

            _scaleService.WeightChanged += OnScaleWeightChanged;
            _rfidService.CardRead += OnRfidCardRead;

            // Subscribe trạng thái kết nối phần cứng — chuyển tiếp thông báo lên UI
            _scaleService.Disconnected        += OnScaleDisconnected;
            _scaleService.Reconnected         += OnScaleReconnected;
            _scaleService.ReconnectAttempting += OnScaleReconnectAttempting;
            _rfidService.ReaderDisconnected   += OnRfidReaderDisconnected;
            _rfidService.ReaderReconnected    += OnRfidReaderReconnected;

            StartHardwareWatchdog();

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
                () => CameraMessageRequested?.Invoke("⚠️ MẤT TÍN HIỆU ĐẦU CÂN!", false));
        }

        // =========================================================================
        // HARDWARE CONNECTION STATUS HANDLERS
        // =========================================================================

        /// <summary>Phản ứng khi đầu cân mất kết nối: reset form và hiển thị cảnh báo.</summary>
        private void OnScaleDisconnected()
        {
            _dashboardWorkflow.ClearPendingData();
            Application.Current?.Dispatcher.InvokeAsync(() =>
            {
                FormResetRequested?.Invoke("⚠️ ĐẦU CÂN MẤT KẼT NỐI! Đang thử kết nối lại...");
            });
        }

        /// <summary>Phản ứng khi đầu cân kết nối lại thành công.</summary>
        private void OnScaleReconnected()
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
                CameraMessageRequested?.Invoke("✅ ĐẦU CÂN ĐÃ KẼT NỐI LạI!", true));
        }

        /// <summary>Hiển thị thông tin lần thử kết nối lại đang diễn ra.</summary>
        private void OnScaleReconnectAttempting(int attempt)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
                CameraMessageRequested?.Invoke($"🔄 ĐẦU CÂN: Đang thử kết nối lại lần {attempt}...", false));
        }

        /// <summary>Phản ứng khi một đầu đọ RFID mất kết nối.</summary>
        private void OnRfidReaderDisconnected(string roleName)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
                CameraMessageRequested?.Invoke($"⚠️ RFID {roleName} MẤT KẼT NỐI! Đang thử kết nối lại...", false));
        }

        /// <summary>Phản ứng khi một đầu đọ RFID kết nối lại thành công.</summary>
        private void OnRfidReaderReconnected(string roleName)
        {
            Application.Current?.Dispatcher.InvokeAsync(() =>
                CameraMessageRequested?.Invoke($"✅ RFID {roleName} ĐÃ KẼT NỐI LạI!", true));
        }

        // =========================================================================
        // CLEANUP
        // =========================================================================
        public void Dispose()
        {
            _scaleService.WeightChanged        -= OnScaleWeightChanged;
            _scaleService.Disconnected         -= OnScaleDisconnected;
            _scaleService.Reconnected          -= OnScaleReconnected;
            _scaleService.ReconnectAttempting  -= OnScaleReconnectAttempting;
            _rfidService.CardRead              -= OnRfidCardRead;
            _rfidService.ReaderDisconnected    -= OnRfidReaderDisconnected;
            _rfidService.ReaderReconnected     -= OnRfidReaderReconnected;
            _hardwareWatchdog.StopAll();
            Log.Information("[COORDINATOR] Coordinator đã dừng và giải phóng resources.");
        }
    }
}
