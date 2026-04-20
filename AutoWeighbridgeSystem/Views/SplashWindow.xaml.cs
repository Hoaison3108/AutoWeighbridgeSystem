using AutoWeighbridgeSystem.ViewModels;
using System.Windows;

namespace AutoWeighbridgeSystem.Views
{
    public partial class SplashWindow : Window
    {
        public SplashWindow()
        {
            InitializeComponent();
            Loaded += SplashWindow_Loaded;
        }

        private async void SplashWindow_Loaded(object sender, RoutedEventArgs e)
        {
            if (DataContext is SplashViewModel vm)
            {
                vm.CloseAction = () => this.Close();
                await vm.StartInitAsync();
            }
        }
    }
}
