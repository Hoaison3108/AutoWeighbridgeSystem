using System.Windows.Controls;

namespace AutoWeighbridgeSystem.Views
{
    public partial class EditTicketView : UserControl
    {
        public EditTicketView()
        {
            InitializeComponent();
        }

        private void VehicleCombo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is ViewModels.EditTicketViewModel vm)
            {
                vm.VehicleAutocomplete.FilterText = VehicleCombo.Text;
                if (!string.IsNullOrEmpty(VehicleCombo.Text)) VehicleCombo.IsDropDownOpen = true;
            }
        }

        private void CustomerCombo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is ViewModels.EditTicketViewModel vm)
            {
                vm.CustomerAutocomplete.FilterText = CustomerCombo.Text;
                if (!string.IsNullOrEmpty(CustomerCombo.Text)) CustomerCombo.IsDropDownOpen = true;
            }
        }

        private void ProductCombo_TextChanged(object sender, TextChangedEventArgs e)
        {
            if (DataContext is ViewModels.EditTicketViewModel vm)
            {
                vm.ProductAutocomplete.FilterText = ProductCombo.Text;
                if (!string.IsNullOrEmpty(ProductCombo.Text)) ProductCombo.IsDropDownOpen = true;
            }
        }
    }
}
