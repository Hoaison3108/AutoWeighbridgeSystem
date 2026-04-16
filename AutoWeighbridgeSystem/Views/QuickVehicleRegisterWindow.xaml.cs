using AutoWeighbridgeSystem.ViewModels;
using System.Windows;
using System.Windows.Input;

namespace AutoWeighbridgeSystem.Views
{
    public partial class QuickVehicleRegisterWindow : Window
    {
        public QuickVehicleRegisterWindow()
        {
            InitializeComponent();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ButtonState == MouseButtonState.Pressed)
            {
                DragMove();
            }
        }
        
        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is QuickVehicleRegisterViewModel vm)
            {
                vm.CloseAction = new System.Action(this.Close);
            }
        }
    }
}
