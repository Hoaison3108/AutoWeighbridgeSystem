using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.ViewModels;
using AutoWeighbridgeSystem.Services;
using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using LibVLCSharp.Shared;
using Microsoft.Extensions.DependencyInjection;

namespace AutoWeighbridgeSystem.Views
{
    /// <summary>
    /// DashboardView hiện là Persistent View (Singleton).
    /// Hỗ trợ cơ chế hiển thị trạng thái Camera và Tự động kết nối lại.
    /// </summary>
    public partial class DashboardView : UserControl
    {
        private CameraService _cameraService;
        private DashboardViewModel _viewModel;
        private bool _isInitialized = false;

        public DashboardView()
        {
            InitializeComponent();
            this.Loaded += DashboardView_Loaded;
        }

        private void DashboardView_Loaded(object sender, RoutedEventArgs e)
        {
            if (!_isInitialized)
            {
                InitializeCameraOnce();
                _isInitialized = true;
            }
        }

        private void InitializeCameraOnce()
        {
            _viewModel = this.DataContext as DashboardViewModel;
            if (_viewModel == null) return;

            try
            {
                _cameraService = App.ServiceProvider.GetRequiredService<CameraService>();

                // Liên kết MediaPlayer vào UI
                this.CameraPlayer.MediaPlayer = _cameraService.MediaPlayer;

                // Đăng ký sự kiện từ MediaPlayer
                _cameraService.MediaPlayer.Playing += OnMediaPlayerPlaying;
                _cameraService.MediaPlayer.EncounteredError += OnMediaPlayerError;

                // Đăng ký sự kiện Tự phục hồi từ Service
                _cameraService.Reconnecting += OnCameraReconnecting;
                _cameraService.Reconnected += OnCameraReconnected;

                // Bắt đầu luồng video ban đầu
                if (_viewModel.CameraUri != null)
                {
                    _cameraService.StartStream(_viewModel.CameraUri.AbsoluteUri);
                }

                // Cập nhật trạng thái ban đầu
                UpdateUiStatus();
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[DASHBOARD] Lỗi khởi tạo Camera Persistent");
            }
        }

        private void OnMediaPlayerPlaying(object sender, EventArgs e)
        {
            UpdateUiStatus();
        }

        private void OnMediaPlayerError(object sender, EventArgs e)
        {
            UpdateUiStatus();
        }

        private void OnCameraReconnecting(object sender, EventArgs e)
        {
            Dispatcher.InvokeAsync(() => {
                if (_viewModel != null)
                {
                    _viewModel.CameraStatus = "🔄 ĐANG KẾT NỐI LẠI CAMERA...";
                    _viewModel.NotifyCameraStatus(HardwareConnectionStatus.Connecting);
                }
            });
        }

        private void OnCameraReconnected(object sender, EventArgs e)
        {
            // Trạng thái sẽ tự cập nhật khi MediaPlayer phát sự kiện Playing
        }

        private void UpdateUiStatus()
        {
            Dispatcher.InvokeAsync(() => {
                if (_viewModel == null || _cameraService?.MediaPlayer == null) return;

                if (_cameraService.MediaPlayer.IsPlaying)
                {
                    _viewModel.CameraStatus = "Camera Online (Persistent)";
                    _viewModel.NotifyCameraStatus(HardwareConnectionStatus.Online);
                }
                else
                {
                    _viewModel.CameraStatus = "⚠️ MẤT KẾT NỐI CAMERA";
                    _viewModel.NotifyCameraStatus(HardwareConnectionStatus.Offline);
                }
            });
        }

        // =========================================================================
        // AUTOCOMPLETE HANDLERS
        // =========================================================================
        private void VehicleComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_viewModel != null && _viewModel.IsManualMode)
            {
                _viewModel.VehicleAutocomplete.FilterText = VehicleComboBox.Text;
                if (!string.IsNullOrEmpty(VehicleComboBox.Text) && VehicleComboBox.IsKeyboardFocusWithin)
                    VehicleComboBox.IsDropDownOpen = true;
            }
        }

        private void CustomerComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_viewModel != null && _viewModel.IsManualMode)
            {
                _viewModel.CustomerAutocomplete.FilterText = CustomerComboBox.Text;
                if (!string.IsNullOrEmpty(CustomerComboBox.Text) && CustomerComboBox.IsKeyboardFocusWithin)
                    CustomerComboBox.IsDropDownOpen = true;
            }
        }

        private void ProductComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_viewModel != null && _viewModel.IsManualMode)
            {
                _viewModel.ProductAutocomplete.FilterText = ProductComboBox.Text;
                if (!string.IsNullOrEmpty(ProductComboBox.Text) && ProductComboBox.IsKeyboardFocusWithin)
                    ProductComboBox.IsDropDownOpen = true;
            }
        }
    }
}