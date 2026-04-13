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

            // 1. Khởi tạo FFmpeg tại đây là đúng (vì nó nạp thư viện cho toàn bộ App)
            Unosquare.FFME.Library.FFmpegDirectory = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "ffmpeg");
            Unosquare.FFME.Library.LoadFFmpeg();

            // 2. Gán ViewModel điều phối
            _viewModel = viewModel;
            this.DataContext = _viewModel;
        }

        // XÓA BỎ: Các sự kiện CameraPlayer_MediaOpening và gán _viewModel.VideoPlayer
        // Vì MainWindow không còn giữ control CameraPlayer nữa.
    }
}