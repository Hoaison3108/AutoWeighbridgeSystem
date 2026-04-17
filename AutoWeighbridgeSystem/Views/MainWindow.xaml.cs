using System;
using System.Windows;
using AutoWeighbridgeSystem.ViewModels;
using Microsoft.EntityFrameworkCore;
using AutoWeighbridgeSystem.Data;

namespace AutoWeighbridgeSystem.Views
{
    public partial class MainWindow : Window
    {
        private readonly MainViewModel _viewModel;

        public MainWindow(MainViewModel viewModel)
        {
            InitializeComponent();

            // 2. Gán ViewModel điều phối
            _viewModel = viewModel;
            this.DataContext = _viewModel;
        }

        // XÓA BỎ: Các sự kiện CameraPlayer_MediaOpening và gán _viewModel.VideoPlayer
        // Vì MainWindow không còn giữ control CameraPlayer nữa.
    }
}