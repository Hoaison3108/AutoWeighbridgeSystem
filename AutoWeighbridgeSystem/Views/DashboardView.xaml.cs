using AutoWeighbridgeSystem.ViewModels;
using AutoWeighbridgeSystem.Services;
using System;
using System.Windows;
using System.Windows.Controls;

namespace AutoWeighbridgeSystem.Views
{
    /// <summary>
    /// DashboardView hiện là Persistent View (Singleton).
    /// Hỗ trợ cơ chế hiển thị trạng thái Camera và Tự động kết nối lại.
    /// </summary>
    public partial class DashboardView : UserControl
    {
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
                // Bước 1: Gán MediaPlayer vào VideoView TRƯỚC — LibVLC cần host window sẵn sàng.
                // Nếu StartStream() được gọi trước bước này, LibVLC sẽ tạo native window riêng.
                this.CameraPlayer.MediaPlayer = _viewModel.CameraMediaPlayer;

                // Bước 2: SAU KHI đã có host, mới bắt đầu stream
                _viewModel.StartCameraStream();

                // Lắng nghe PropertyChanged để Auto-focus (logic thuần UI — hợp lệ ở code-behind)
                _viewModel.PropertyChanged += ViewModel_PropertyChanged;
            }
            catch (Exception ex)
            {
                Serilog.Log.Error(ex, "[DASHBOARD] Lỗi gán MediaPlayer cho CameraPlayer");
            }
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