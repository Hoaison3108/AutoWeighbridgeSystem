using AutoWeighbridgeSystem.ViewModels;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using Unosquare.FFME;
using Unosquare.FFME.Common;

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

        /// <summary>
        /// Gắn kết Camera giữa View và ViewModel và ra lệnh mở luồng.
        /// </summary>
        private void SetupCameraBinding()
        {
            if (this.DataContext is DashboardViewModel vm)
            {
                // 1. Gán control thực tế vào ViewModel để Watchdog hoạt động
                vm.VideoPlayer = this.CameraPlayer;

                // 2. Ép mở camera ngay nếu chưa mở
                if (vm.CameraUri != null && !this.CameraPlayer.IsOpen && !this.CameraPlayer.IsOpening)
                {
                    // Chạy Open trong task để không block UI
                    _ = this.CameraPlayer.Open(vm.CameraUri);
                }
            }
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
                vm.VehicleFilterText = VehicleComboBox.Text;
                // Mở dropdown để hiển thị gợi ý ngay khi gõ
                if (!string.IsNullOrEmpty(VehicleComboBox.Text))
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
                vm.CustomerFilterText = CustomerComboBox.Text;
                if (!string.IsNullOrEmpty(CustomerComboBox.Text))
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
                vm.ProductFilterText = ProductComboBox.Text;
                if (!string.IsNullOrEmpty(ProductComboBox.Text))
                    ProductComboBox.IsDropDownOpen = true;
            }
        }

        /// <summary>
        /// KHI CAMERA ĐÃ LÊN HÌNH (Fix lỗi kẹt chữ "Đang khởi tạo FFME")
        /// </summary>
        private void CameraPlayer_MediaOpened(object sender, EventArgs e)
        {
            if (this.DataContext is DashboardViewModel vm)
            {
                // Cập nhật đúng chuỗi để StringToVisibilityConverter ẩn lớp Overlay
                vm.CameraStatus = "Camera Online (FFME 4.4.350)";
            }
        }

        /// <summary>
        /// Cấu hình FFME truớc khi mở luồng camera:
        /// - Buộc dùng Software Decoding (không dùng GPU) bằng cách set VideoHardwareDevice = null.
        /// - Tắt âm thanh: camera trạm cân không cần audio.
        /// - Tối ưu buffer và transport cho RTSP UDP (giảm độ trễ khung hình).
        /// </summary>
        private void CameraPlayer_MediaOpening(object sender, MediaOpeningEventArgs e)
        {
            // =============================================================
            // 1. FORCE SOFTWARE DECODING — không dùng GPU
            // =============================================================
            // API 4.4.350: set null = dùng software decoder (libavcodec thuần tuý)
            // set thành một HardwareDeviceInfo cụ thể = bật hardware acceleration.
            // Lý do chọn null:
            //   - Máy công nghiệp thường dùng GPU tích hợp (Intel/AMD) với driver cũ.
            //   - D3D11VA/DXVA2 không ổn định với RTSP dài hạn chạy 24/7.
            //   - Software decoding tốn thêm 5-10% CPU nhưng không bao giờ crash.
            e.Options.VideoHardwareDevice = null;

            // =============================================================
            // 2. TắT ÂM THANH — camera cân không cần audio
            // =============================================================
            e.Options.IsAudioDisabled = true;

            // =============================================================
            // 3. TỐI Ư U BUFFER — giảm độ trễ hiển thị khung hình
            // =============================================================
            // MinimumPlaybackBufferPercent = 0: không chờ pre-buffer, phát ngay khi có frame.
            e.Options.MinimumPlaybackBufferPercent = 0;

            // =============================================================
            // 4. TỐI Ư U RTSP TRANSPORT — dùng DecoderParams (API 4.4.350)
            // =============================================================
            // Ép dùng UDP để giảm overhead trên mạng LAN.
            // Nếu camera ở mạng không ổn định hoặc qua NAT, đổi thành "tcp".
            e.Options.DecoderParams["rtsp_transport"] = "udp";

            // Timeout kết nối RTSP: 5 giây (tránh đờ vô hạn khi camera off)
            e.Options.DecoderParams["stimeout"] = "5000000"; // đơn vị: micro-giây

            // =============================================================
            // 5. TắT TIME SYNC — phù hợp stream trực tiếp (live stream)
            // =============================================================
            // IsTimeSyncDisabled = true: không đồng bộ timestamp với đồng hồ hệ thống,
            // tránh hiện tượng giật khung hình khi camera có drift clock nhỏ.
            e.Options.IsTimeSyncDisabled = true;
        }

        private void CameraPlayer_MediaFailed(object sender, MediaFailedEventArgs e)
        {
            if (this.DataContext is not DashboardViewModel vm) return;

            vm.CameraStatus = "⚠️ LỖI KẾT NỐI CAMERA";

            // Auto-retry: đợi 5 giây rồi thử mở lại như RTSP stream
            _ = Task.Run(async () =>
            {
                await Task.Delay(5000);
                await Dispatcher.InvokeAsync(async () =>
                {
                    try
                    {
                        if (vm.CameraUri != null)
                        {
                            vm.CameraStatus = "🔄 Đang kết nối lại camera...";
                            await this.CameraPlayer.Open(vm.CameraUri);
                        }
                    }
                    catch
                    {
                        vm.CameraStatus = "❌ Camera không khả dụng";
                    }
                });
            });
        }
    }
}