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

                // Lắng nghe sự kiện để Auto-focus
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;

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

        private void ViewModel_PropertyChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            // Tự động focus vào ô Biển số xe khi gạt công tắc sang MANUAL
            if (e.PropertyName == nameof(DashboardViewModel.IsAutoMode))
            {
                if (_viewModel != null && !_viewModel.IsAutoMode)
                {
                    Dispatcher.InvokeAsync(() => {
                        VehicleTextBox?.Focus();
                    }, System.Windows.Threading.DispatcherPriority.Input);
                }
            }
            // Tự động focus vào ô Biển số xe sau khi lưu thành công hoặc bấm nút Làm mới (ResetForm)
            else if (e.PropertyName == nameof(DashboardViewModel.LicensePlate))
            {
                if (_viewModel != null && _viewModel.IsManualMode && string.IsNullOrEmpty(_viewModel.LicensePlate))
                {
                    Dispatcher.InvokeAsync(() => {
                        VehicleTextBox?.Focus();
                    }, System.Windows.Threading.DispatcherPriority.Input);
                }
            }
        }

        // =========================================================================
        // AUTOCOMPLETE HANDLERS
        // =========================================================================
        private void VehicleComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_viewModel != null && _viewModel.IsManualMode)
            {
                _viewModel.VehicleAutocomplete.FilterText = VehicleComboBox.Text;
                
                // Tránh lỗi UX: Nếu người dùng click chọn 1 item từ DropDown (tạo ra Exact Match)
                // thì không ép mở lại DropDown nữa để DropDown có thể tự đóng tự nhiên.
                bool isExactMatch = VehicleComboBox.SelectedItem != null && 
                                    VehicleComboBox.SelectedItem.ToString().Equals(VehicleComboBox.Text, StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(VehicleComboBox.Text) && 
                    (VehicleComboBox.IsKeyboardFocusWithin || (VehicleTextBox != null && VehicleTextBox.IsKeyboardFocusWithin)) && 
                    !isExactMatch)
                {
                    VehicleComboBox.IsDropDownOpen = true;
                }
            }
        }

        private void CustomerComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_viewModel != null && _viewModel.IsManualMode)
            {
                _viewModel.CustomerAutocomplete.FilterText = CustomerComboBox.Text;
                
                bool isExactMatch = CustomerComboBox.SelectedItem != null && 
                                    CustomerComboBox.SelectedItem.ToString().Equals(CustomerComboBox.Text, StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(CustomerComboBox.Text) && 
                    (CustomerComboBox.IsKeyboardFocusWithin || (CustomerTextBox != null && CustomerTextBox.IsKeyboardFocusWithin)) && 
                    !isExactMatch)
                {
                    CustomerComboBox.IsDropDownOpen = true;
                }
            }
        }

        private void ProductComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (_viewModel != null && _viewModel.IsManualMode)
            {
                _viewModel.ProductAutocomplete.FilterText = ProductComboBox.Text;

                bool isExactMatch = ProductComboBox.SelectedItem != null && 
                                    ProductComboBox.SelectedItem.ToString().Equals(ProductComboBox.Text, StringComparison.OrdinalIgnoreCase);

                if (!string.IsNullOrEmpty(ProductComboBox.Text) && 
                    (ProductComboBox.IsKeyboardFocusWithin || (ProductTextBox != null && ProductTextBox.IsKeyboardFocusWithin)) && 
                    !isExactMatch)
                {
                    ProductComboBox.IsDropDownOpen = true;
                }
            }
        }

        private void VehicleInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Tab)
            {
                if (VehicleComboBox.IsDropDownOpen && VehicleComboBox.HasItems)
                {
                    var view = _viewModel?.VehicleAutocomplete?.View;
                    if (view != null && !view.IsEmpty)
                    {
                        string firstItem = null;
                        foreach (var item in view) { firstItem = item as string; break; }
                        if (firstItem != null && _viewModel != null)
                        {
                            _viewModel.LicensePlate = firstItem;
                        }
                    }
                }
                VehicleComboBox.IsDropDownOpen = false;
                
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    CustomerTextBox?.Focus();
                }
            }
        }

        private void CustomerInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Tab)
            {
                if (CustomerComboBox.IsDropDownOpen && CustomerComboBox.HasItems)
                {
                    var view = _viewModel?.CustomerAutocomplete?.View;
                    if (view != null && !view.IsEmpty)
                    {
                        string firstItem = null;
                        foreach (var item in view) { firstItem = item as string; break; }
                        if (firstItem != null && _viewModel != null)
                        {
                            _viewModel.CustomerName = firstItem;
                        }
                    }
                }
                CustomerComboBox.IsDropDownOpen = false;
                
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    ProductTextBox?.Focus();
                }
            }
        }

        private void ProductInput_PreviewKeyDown(object sender, System.Windows.Input.KeyEventArgs e)
        {
            if (e.Key == System.Windows.Input.Key.Enter || e.Key == System.Windows.Input.Key.Tab)
            {
                if (ProductComboBox.IsDropDownOpen && ProductComboBox.HasItems)
                {
                    var view = _viewModel?.ProductAutocomplete?.View;
                    if (view != null && !view.IsEmpty)
                    {
                        string firstItem = null;
                        foreach (var item in view) { firstItem = item as string; break; }
                        if (firstItem != null && _viewModel != null)
                        {
                            _viewModel.ProductName = firstItem;
                        }
                    }
                }
                ProductComboBox.IsDropDownOpen = false;
                
                if (e.Key == System.Windows.Input.Key.Enter)
                {
                    e.Handled = true;
                    if (_viewModel != null && _viewModel.CanManualSave)
                    {
                        if (_viewModel.ManualSaveCommand.CanExecute(null))
                            _viewModel.ManualSaveCommand.Execute(null);
                    }
                }
            }
        }
    }
}