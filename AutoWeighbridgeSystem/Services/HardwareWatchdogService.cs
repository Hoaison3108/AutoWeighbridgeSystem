using System;
using System.Threading;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    public sealed class HardwareWatchdogService : IDisposable
    {
        private DateTime _lastScaleDataReceivedTime = DateTime.Now;
        private CancellationTokenSource _pendingTimeoutCts;
        private CancellationTokenSource _watchdogCts;

        public void NotifyScaleDataReceived()
        {
            _lastScaleDataReceivedTime = DateTime.Now;
        }

        public void StartHardwareWatchdog(int hardwareWatchdogSeconds, Action onSignalLost)
        {
            _watchdogCts?.Cancel();
            _watchdogCts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                while (!_watchdogCts.Token.IsCancellationRequested)
                {
                    await Task.Delay(TimeSpan.FromSeconds(hardwareWatchdogSeconds), _watchdogCts.Token);
                    if ((DateTime.Now - _lastScaleDataReceivedTime).TotalSeconds > hardwareWatchdogSeconds)
                        onSignalLost?.Invoke();
                }
            }, _watchdogCts.Token);
        }

        public void StartPendingTimeout(int queueTimeoutSeconds, Action onPendingTimeout)
        {
            _pendingTimeoutCts?.Cancel();
            _pendingTimeoutCts = new CancellationTokenSource();

            Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(queueTimeoutSeconds), _pendingTimeoutCts.Token);
                    onPendingTimeout?.Invoke();
                }
                catch
                {
                }
            }, _pendingTimeoutCts.Token);
        }

        public void CancelPendingTimeout()
        {
            _pendingTimeoutCts?.Cancel();
        }

        public void StopAll()
        {
            _watchdogCts?.Cancel();
            _pendingTimeoutCts?.Cancel();
        }

        public void Dispose()
        {
            StopAll();
        }
    }
}
