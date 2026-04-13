using AutoWeighbridgeSystem.ViewModels;
using System;
using System.Windows;
using System.Windows.Controls;
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
        /// Cấu hình tối ưu cho luồng Camera IP
        /// </summary>
        private void CameraPlayer_MediaOpening(object sender, MediaOpeningEventArgs e)
        {
            // Tắt âm thanh và giảm buffer để hình ảnh mượt nhất, ít trễ nhất
            e.Options.IsAudioDisabled = true;
            e.Options.MinimumPlaybackBufferPercent = 0;
        }

        private void CameraPlayer_MediaFailed(object sender, MediaFailedEventArgs e)
        {
            if (this.DataContext is DashboardViewModel vm)
            {
                vm.CameraStatus = "LỖI KẾT NỐI CAMERA: " + e.ErrorException.Message;
            }
        }
    }
}