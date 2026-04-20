using AutoWeighbridgeSystem.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Threading.Tasks;
using System.Windows;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class ManualTicketViewModel : ObservableObject
    {
        private readonly WeighingBusinessService _weighingBusiness;
        private readonly DashboardDataService _dashboardDataService;
        private readonly IUserNotificationService _notificationService;

        [ObservableProperty] private string _licensePlate;
        [ObservableProperty] private string _customerName;
        [ObservableProperty] private string _productName;

        [ObservableProperty] private decimal _grossWeight;
        [ObservableProperty] private decimal _tareWeight;
        [ObservableProperty] private decimal _netWeight;

        [ObservableProperty] private DateTime _timeIn = DateTime.Now.AddMinutes(-30);
        [ObservableProperty] private DateTime _timeOut = DateTime.Now;
        [ObservableProperty] private string _reason = "Nhập thủ công";

        // Danh sách gợi ý từ DB
        [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<string> _availableVehicles = new();
        [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<string> _availableCustomers = new();
        [ObservableProperty] private System.Collections.ObjectModel.ObservableCollection<string> _availableProducts = new();

        // Cờ báo hiệu đã lưu xong để Window tự đóng
        public Action CloseAction { get; set; }
        public bool IsSavedSuccessfully { get; private set; } = false;

        public ManualTicketViewModel(
            WeighingBusinessService weighingBusiness,
            DashboardDataService dashboardDataService,
            IUserNotificationService notificationService)
        {
            _weighingBusiness = weighingBusiness;
            _dashboardDataService = dashboardDataService;
            _notificationService = notificationService;

            _ = InitializeDataAsync();
        }

        private async Task InitializeDataAsync()
        {
            var data = await _dashboardDataService.LoadInitialDataAsync();
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Nạp danh sách trước
                AvailableVehicles = new(data.Vehicles);
                AvailableCustomers = new(data.Customers);
                AvailableProducts = new(data.Products);

                // Gán giá trị mặc định sau khi đã có danh sách gợi ý
                if (string.IsNullOrEmpty(ProductName))
                {
                    ProductName = data.DefaultProductName;
                }
            });
        }

        partial void OnLicensePlateChanged(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return;

            // Tự động tìm kiếm xe trong DB để lấy Bì và Khách hàng
            Task.Run(async () =>
            {
                var vehicle = await _dashboardDataService.GetVehicleByPlateAsync(value);
                if (vehicle != null)
                {
                    Application.Current.Dispatcher.Invoke(() =>
                    {
                        TareWeight = vehicle.TareWeight;
                        if (vehicle.Customer != null)
                            CustomerName = vehicle.Customer.CustomerName;
                    });
                }
            });
        }

        partial void OnGrossWeightChanged(decimal value) => CalculateNet();
        partial void OnTareWeightChanged(decimal value) => CalculateNet();

        private void CalculateNet()
        {
            NetWeight = GrossWeight - TareWeight;
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(LicensePlate))
            {
                _notificationService.ShowWarning("Vui lòng nhập Biển số xe", "LỖI NHẬP LIỆU");
                return;
            }

            if (GrossWeight <= 0)
            {
                _notificationService.ShowWarning("Tổng trọng lượng (Gross) phải lớn hơn 0.", "LỖI NHẬP LIỆU");
                return;
            }

            if (TareWeight < 0 || TareWeight >= GrossWeight)
            {
                _notificationService.ShowWarning("Lỗi: Nhập sai trọng lượng Tổng hoặc Bì. (Bì phải nhỏ hơn Tổng trọng)", "LỖI NHẬP LIỆU");
                return;
            }

            if (TimeOut < TimeIn)
            {
                _notificationService.ShowWarning("Thời gian xe ra không được phép trước thời gian vào.", "LỖI THỜI GIAN");
                return;
            }

            if (string.IsNullOrWhiteSpace(Reason))
            {
                _notificationService.ShowWarning("Vui lòng nhập rõ lý do sự cố để giải trình sau này.", "LỖI DỮ LIỆU");
                return;
            }

            if (!_notificationService.Confirm($"Bạn có chắc chắn muốn TẠO THỦ CÔNG phiếu cân cho xe {LicensePlate} không?\nKhối lượng hàng sẽ được ghi nhận là: {NetWeight:N0} kg", "XÁC NHẬN"))
            {
                return;
            }

            var result = await _weighingBusiness.CreateManualTicketAsync(
                LicensePlate,
                CustomerName,
                ProductName,
                GrossWeight,
                TareWeight,
                TimeIn,
                TimeOut,
                Reason
            );

            if (result.IsSuccess)
            {
                _notificationService.ShowInfo(result.Message, "THÀNH CÔNG");
                IsSavedSuccessfully = true;
                CloseAction?.Invoke();
            }
            else
            {
                _notificationService.ShowError(result.Message, "THẤT BẠI");
            }
        }
    }
}
