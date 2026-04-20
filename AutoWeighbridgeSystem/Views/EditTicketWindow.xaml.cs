using System.Windows;

namespace AutoWeighbridgeSystem.Views
{
    public partial class EditTicketWindow : Window
    {
        public EditTicketWindow()
        {
            InitializeComponent();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
