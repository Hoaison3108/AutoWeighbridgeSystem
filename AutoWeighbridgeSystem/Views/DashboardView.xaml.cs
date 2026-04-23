using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using LibVLCSharp.Shared;
using MediaPlayer = LibVLCSharp.Shared.MediaPlayer;

namespace AutoWeighbridgeSystem.Views
{
    public partial class DashboardView : UserControl
    {
        public DashboardView()
        {
            InitializeComponent();

            // Đảm bảo khi DataContext thay đổi (do DI bơm vào), ta cũng gán VideoPlayer
            this.DataContextChanged += DashboardView_DataContextChanged;
        }

        private void DashboardView_DataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            SetupCameraBinding();
        }

        private void UserControl_Loaded(object sender, RoutedEventArgs e)
        {
            SetupCameraBinding();
        }

        private LibVLC _libVLC;
        private MediaPlayer _mediaPlayer;
        private bool _isReconnecting = false;

        /// <summary>
        /// Gắn kết Camera giữa View và ViewModel và ra lệnh mở luồng.
        /// </summary>
        private void SetupCameraBinding()
        {
            if (this.DataContext is DashboardViewModel vm)
            {
                if (vm.CameraUri != null)
                {
                    InitializeVLCAndPlay(vm.CameraUri);
                }
            }
        }

        private void InitializeVLCAndPlay(Uri cameraUri)
        {
                // Cấu hình VLC: Bộ đệm 800ms để cân bằng giữa Low Latency và sự ổn định (trước đây là 200ms - quá thấp gây mất kết nối)
                // Ép RTSP xài TCP (chống rác hình/giật lag do mất gói), tắt âm thanh
                _libVLC = new LibVLC("--network-caching=800", "--rtsp-tcp", "--no-audio", "--drop-late-frames", "--live-caching=800");
                _mediaPlayer = new MediaPlayer(_libVLC);
                
                this.CameraPlayer.MediaPlayer = _mediaPlayer;

                // Bind events
                _mediaPlayer.Playing += MediaPlayer_Playing;
                _mediaPlayer.EncounteredError += MediaPlayer_EncounteredError;
                _mediaPlayer.EndReached += MediaPlayer_EncounteredError; // Khi luồng kết thúc đột ngột cũng kích hoạt reconnect
            }

            // Mỗi lần reconnect hoặc bắt đầu đều tạo Media mới để giải phóng buffer lỗi cũ
            var media = new Media(_libVLC, cameraUri.AbsoluteUri, FromType.FromLocation);
            media.AddOption(":rtsp-frame-buffer-size=500000"); // Tăng buffer size cho frame hình
            _mediaPlayer.Play(media);
        }

        // =========================================================================
        // AUTOCOMPLETE TEXT CHANGED HANDLERS
        // =========================================================================

        /// <summary>
        /// Cập nhật VehicleFilterText trong ViewModel khi người dùng gõ vào ComboBox Biển số.
        /// Chỉ phản ứng khi ở Manual Mode (ComboBox đang enabled).
        /// </summary>
        private void VehicleComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is DashboardViewModel vm && vm.IsManualMode)
            {
                vm.VehicleAutocomplete.FilterText = VehicleComboBox.Text;
                // Chỉ mở khi người dùng đang thao tác trực tiếp
                if (!string.IsNullOrEmpty(VehicleComboBox.Text) && VehicleComboBox.IsKeyboardFocusWithin)
                    VehicleComboBox.IsDropDownOpen = true;
            }
        }

        /// <summary>
        /// Cập nhật CustomerFilterText trong ViewModel khi người dùng gõ vào ComboBox Khách hàng.
        /// </summary>
        private void CustomerComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is DashboardViewModel vm && vm.IsManualMode)
            {
                vm.CustomerAutocomplete.FilterText = CustomerComboBox.Text;
                if (!string.IsNullOrEmpty(CustomerComboBox.Text) && CustomerComboBox.IsKeyboardFocusWithin)
                    CustomerComboBox.IsDropDownOpen = true;
            }
        }

        /// <summary>
        /// Cập nhật ProductFilterText trong ViewModel khi người dùng gõ vào ComboBox Hàng hóa.
        /// </summary>
        private void ProductComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is DashboardViewModel vm && vm.IsManualMode)
            {
                vm.ProductAutocomplete.FilterText = ProductComboBox.Text;
                if (!string.IsNullOrEmpty(ProductComboBox.Text) && ProductComboBox.IsKeyboardFocusWithin)
                    ProductComboBox.IsDropDownOpen = true;
            }
        }

        /// <summary>KHI CAMERA ĐÃ LÊN HÌNH</summary>
        private void MediaPlayer_Playing(object sender, EventArgs e)
        {
            _isReconnecting = false;
            // LibVLC chạy callback ở background thread. Invoke sang UI thread để báo trạng thái an toàn:
            Dispatcher.InvokeAsync(() =>
            {
                if (this.DataContext is DashboardViewModel vm)
                {
                    vm.CameraStatus = "Camera Online (LibVLCSharp)";
                    vm.NotifyCameraStatus(HardwareConnectionStatus.Online);
                }
            });
        }

        /// <summary>KHI CAMERA GẶP SỰ CỐ, MẤT KẾT NỐI / DỮ LIỆU CHẬM</summary>
        private void MediaPlayer_EncounteredError(object sender, EventArgs e)
        {
            if (_isReconnecting) return;
            _isReconnecting = true;
            
            Dispatcher.InvokeAsync(() =>
            {
                if (this.DataContext is DashboardViewModel vm)
                {
                    vm.CameraStatus = "⚠️ LỖI KẾT NỐI CAMERA";
                    vm.NotifyCameraStatus(HardwareConnectionStatus.Offline);

                    // Auto-retry: đợi 5 giây rồi thử mở lại như RTSP stream
                    _ = Task.Run(async () =>
                    {
                        await Task.Delay(5000);
                        await Dispatcher.InvokeAsync(() =>
                        {
                            if (vm.CameraUri != null)
                            {
                                vm.CameraStatus = "🔄 Đang kết nối lại camera...";
                                vm.NotifyCameraStatus(HardwareConnectionStatus.Reconnecting);
                                InitializeVLCAndPlay(vm.CameraUri);
                            }
                        });
                    });
                }
            });
        }
    }
}