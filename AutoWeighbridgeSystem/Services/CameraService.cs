using System;
using System.Threading;
using System.Threading.Tasks;
using LibVLCSharp.Shared;
using Serilog;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ quản lý Camera tập trung (Singleton).
    /// Hỗ trợ cơ chế tự động kết nối lại (Self-Healing) khi gặp sự cố mạng.
    /// </summary>
    public sealed class CameraService : IDisposable
    {
        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private string _currentUri;
        private bool _isDisposed = false;
        private CancellationTokenSource _reconnectCts;

        public MediaPlayer MediaPlayer => _mediaPlayer;
        public event EventHandler Reconnecting;
        public event EventHandler Reconnected;

        public CameraService()
        {
            Core.Initialize();
            
            // Cấu hình tối ưu cho luồng RTSP công nghiệp
            _libVLC = new LibVLC(
                "--network-caching=1000", 
                "--rtsp-tcp", 
                "--no-audio", 
                "--drop-late-frames", 
                "--live-caching=1000",
                "--no-video-title-show",
                "--embedded-video"
            );
            
            _mediaPlayer = new MediaPlayer(_libVLC);
            _mediaPlayer.EncounteredError += OnMediaPlayerError;
        }

        public void StartStream(string rtspUrl)
        {
            if (string.IsNullOrEmpty(rtspUrl)) return;
            if (_currentUri == rtspUrl && _mediaPlayer.IsPlaying) return;

            try
            {
                _currentUri = rtspUrl;
                PlayInternal();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[CAMERA] Lỗi khởi động luồng video: {Url}", rtspUrl);
            }
        }

        private void PlayInternal()
        {
            if (string.IsNullOrEmpty(_currentUri)) return;

            var media = new Media(_libVLC, _currentUri, FromType.FromLocation);
            media.AddOption(":rtsp-frame-buffer-size=500000");
            media.AddOption(":no-video-title-show");
            
            _mediaPlayer.Play(media);
            Log.Information("[CAMERA] Bắt đầu phát luồng: {Url}", _currentUri);
        }

        private void OnMediaPlayerError(object? sender, EventArgs e)
        {
            Log.Warning("[CAMERA] Phát hiện lỗi kết nối. Đang chuẩn bị kết nối lại...");
            TriggerReconnection();
        }

        private void TriggerReconnection()
        {
            if (_isDisposed) return;

            // Hủy tác vụ kết nối lại cũ nếu đang chạy
            _reconnectCts?.Cancel();
            _reconnectCts = new CancellationTokenSource();

            var token = _reconnectCts.Token;

            Task.Run(async () =>
            {
                try
                {
                    Reconnecting?.Invoke(this, EventArgs.Empty);
                    
                    // Thử lại sau 5 giây để tránh quá tải camera
                    await Task.Delay(5000, token);
                    
                    if (token.IsCancellationRequested) return;

                    Log.Information("[CAMERA] Đang thử kết nối lại...");
                    PlayInternal();
                    
                    Reconnected?.Invoke(this, EventArgs.Empty);
                }
                catch (TaskCanceledException) { }
                catch (Exception ex)
                {
                    Log.Error(ex, "[CAMERA] Lỗi trong quá trình kết nối lại");
                }
            }, token);
        }

        public void StopStream()
        {
            _reconnectCts?.Cancel();
            if (_mediaPlayer.IsPlaying) _mediaPlayer.Stop();
        }

        public void Dispose()
        {
            _isDisposed = true;
            _reconnectCts?.Cancel();
            _reconnectCts?.Dispose();

            try
            {
                if (_mediaPlayer != null)
                {
                    _mediaPlayer.EncounteredError -= OnMediaPlayerError;
                    if (_mediaPlayer.IsPlaying) _mediaPlayer.Stop();
                    _mediaPlayer.Dispose();
                }
                _libVLC?.Dispose();
                Log.Information("[CAMERA] Đã giải phóng tài nguyên Camera.");
            }
            catch (Exception ex)
            {
                Log.Warning("[CAMERA] Lỗi khi Dispose Camera: {Msg}", ex.Message);
            }
        }
    }
}
