using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ watchdog và timeout cho phần cứng cân.
    /// Đảm nhiệm hai nhiệm vụ độc lập chạy trên luồng nền:
    /// <list type="number">
    ///   <item>
    ///     <b>Hardware Watchdog</b>: giám sát liên tục tín hiệu đầu cân.
    ///     Nếu không nhận được dữ liệu trong khoảng thời gian cấu hình,
    ///     gọi callback <c>onSignalLost</c> để thông báo lên UI.
    ///   </item>
    ///   <item>
    ///     <b>Pending Timeout</b>: đếm ngược cho xe đang trong hàng chờ.
    ///     Nếu xe không lên cân trong thời gian quy định, tự động xóa dữ liệu chờ.
    ///   </item>
    /// </list>
    /// </summary>
    public sealed class HardwareWatchdogService : IDisposable
    {
        private DateTime _lastScaleDataReceivedTime = DateTime.Now;
        private CancellationTokenSource _pendingTimeoutCts;
        private CancellationTokenSource _watchdogCts;
        private readonly object _lock = new object();

        /// <summary>
        /// Cập nhật mốc thời gian nhận tín hiệu từ đầu cân.
        /// Phải được gọi mỗi khi <c>ScaleService.WeightChanged</c> kích hoạt,
        /// để watchdog biết rằng đầu cân vẫn đang hoạt động bình thường.
        /// </summary>
        public void NotifyScaleDataReceived()
        {
            _lastScaleDataReceivedTime = DateTime.Now;
        }

        /// <summary>
        /// Khởi động vòng lặp watchdog chạy nền, giám sát tín hiệu đầu cân.
        /// Mỗi <paramref name="hardwareWatchdogSeconds"/> giây kiểm tra một lần;
        /// nếu không có tín hiệu mới → gọi <paramref name="onSignalLost"/>.
        /// </summary>
        /// <param name="hardwareWatchdogSeconds">Ngưỡng thời gian (giây) không có tín hiệu coi là mất kết nối.</param>
        /// <param name="onSignalLost">Callback được gọi khi phát hiện mất tín hiệu đầu cân.</param>
        public void StartHardwareWatchdog(int hardwareWatchdogSeconds, Action onSignalLost)
        {
            lock (_lock)
            {
                _watchdogCts?.Cancel();
                _watchdogCts = new CancellationTokenSource();
            }

            var token = _watchdogCts.Token;
            Task.Run(async () =>
            {
                try
                {
                    while (!token.IsCancellationRequested)
                    {
                        // Kiểm tra định kỳ mỗi 10 giây để đảm bảo phản ứng nhanh ngay khi đạt ngưỡng timeout
                        await Task.Delay(TimeSpan.FromSeconds(10), token);
                        
                        if ((DateTime.Now - _lastScaleDataReceivedTime).TotalSeconds > hardwareWatchdogSeconds)
                        {
                            onSignalLost?.Invoke();
                        }
                    }
                }
                catch (TaskCanceledException) { }
                catch (OperationCanceledException) { }
            }, token);
        }

        /// <summary>
        /// Bắt đầu đếm ngược timeout cho xe đang trong hàng chờ.
        /// Nếu xe không lên cân trong <paramref name="queueTimeoutSeconds"/> giây,
        /// tự động gọi <paramref name="onPendingTimeout"/> để xóa hàng chờ.
        /// Gọi lại hàm này sẽ reset bộ đếm.
        /// </summary>
        /// <param name="queueTimeoutSeconds">Thời gian tối đa chờ xe lên cân (giây).</param>
        /// <param name="onPendingTimeout">Callback được gọi khi hàng chờ hết thời gian.</param>
        public void StartPendingTimeout(int queueTimeoutSeconds, Action onPendingTimeout)
        {
            lock (_lock)
            {
                _pendingTimeoutCts?.Cancel();
                _pendingTimeoutCts = new CancellationTokenSource();
            }

            var token = _pendingTimeoutCts.Token;
            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(queueTimeoutSeconds), token);
                    onPendingTimeout?.Invoke();
                }
                catch (OperationCanceledException) { }
            }, token);
        }

        /// <summary>
        /// Hủy bộ đếm ngược timeout hiện tại (khi form được reset hoặc xe đã được cân xong).
        /// </summary>
        public void CancelPendingTimeout()
        {
            lock (_lock)
            {
                _pendingTimeoutCts?.Cancel();
            }
        }

        /// <summary>Dừng cả watchdog và pending timeout. Gọi trong <c>Dispose</c> của ViewModel.</summary>
        public void StopAll()
        {
            lock (_lock)
            {
                _watchdogCts?.Cancel();
                _pendingTimeoutCts?.Cancel();
            }
        }

        /// <inheritdoc/>
        public void Dispose()
        {
            StopAll();
        }
    }
}
