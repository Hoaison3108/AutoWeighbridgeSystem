using AutoWeighbridgeSystem.ViewModels;
using System.Windows;

namespace AutoWeighbridgeSystem.Views
{
    public partial class ManualTicketWindow : Window
    {
        public ManualTicketWindow()
        {
            InitializeComponent();
            Loaded += (s, e) =>
            {
                if (DataContext is ManualTicketViewModel vm)
                {
                    vm.CloseAction = new System.Action(this.Close);
                }
            };
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            this.Close();
        }
    }
}
