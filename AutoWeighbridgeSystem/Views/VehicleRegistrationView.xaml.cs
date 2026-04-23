using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Shapes;

namespace AutoWeighbridgeSystem.Views
{
    /// <summary>
    /// Interaction logic for VehicleRegistrationView.xaml
    /// </summary>
    public partial class VehicleRegistrationView : UserControl
    {
        public VehicleRegistrationView()
        {
            InitializeComponent();
        }

        private void VehicleComboBox_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is ViewModels.VehicleRegistrationViewModel vm)
            {
                vm.VehicleAutocomplete.FilterText = VehicleComboBox.Text;
                
                // CHỈ mở dropdown nếu người dùng đang chủ động gõ (có focus)
                // Tránh việc tự động bật lên khi quẹt thẻ RFID hoặc cập nhật từ code
                if (!string.IsNullOrEmpty(VehicleComboBox.Text) && VehicleComboBox.IsKeyboardFocusWithin)
                {
                    VehicleComboBox.IsDropDownOpen = true;
                }
            }
        }
    }
}
