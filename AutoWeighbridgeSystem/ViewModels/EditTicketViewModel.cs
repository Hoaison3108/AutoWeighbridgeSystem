using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.EntityFrameworkCore;
using AutoWeighbridgeSystem.Data;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class EditTicketViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly DashboardDataService _dashboardDataService;
        private readonly IUserNotificationService _notificationService;
        private WeighingTicket _originalTicket;

        [ObservableProperty] private string _licensePlate;
        [ObservableProperty] private string _customerName;
        [ObservableProperty] private string _productName;
        
        [ObservableProperty] private decimal _grossWeight;
        [ObservableProperty] private decimal _tareWeight;
        [ObservableProperty] private decimal _netWeight;

        [ObservableProperty] private DateTime _timeIn;
        [ObservableProperty] private DateTime _timeOut;
        [ObservableProperty] private string _reason = "";

        [ObservableProperty] private ObservableCollection<string> _availableVehicles = new();
        [ObservableProperty] private ObservableCollection<string> _availableCustomers = new();
        [ObservableProperty] private ObservableCollection<string> _availableProducts = new();

        public Action RequestClose { get; set; }
        public Action SuccessCallback { get; set; }
        public bool IsSavedSuccessfully { get; private set; } = false;

        public EditTicketViewModel(
            IDbContextFactory<AppDbContext> contextFactory,
            DashboardDataService dashboardDataService,
            IUserNotificationService notificationService)
        {
            _contextFactory = contextFactory;
            _dashboardDataService = dashboardDataService;
            _notificationService = notificationService;
        }

        public async Task InitializeAsync(WeighingTicket ticket)
        {
            _originalTicket = ticket;
            
            LicensePlate = ticket.LicensePlate;
            CustomerName = ticket.CustomerName;
            ProductName = ticket.ProductName;
            GrossWeight = ticket.GrossWeight;
            TareWeight = ticket.TareWeight;
            NetWeight = ticket.NetWeight;
            TimeIn = ticket.TimeIn;
            TimeOut = ticket.TimeOut ?? ticket.TimeIn;

            var data = await _dashboardDataService.LoadInitialDataAsync();
            Application.Current.Dispatcher.Invoke(() =>
            {
                AvailableVehicles = new(data.Vehicles);
                AvailableCustomers = new(data.Customers);
                AvailableProducts = new(data.Products);
            });
        }

        partial void OnGrossWeightChanged(decimal value) => CalculateNet();
        partial void OnTareWeightChanged(decimal value) => CalculateNet();

        private void CalculateNet()
        {
            NetWeight = GrossWeight - TareWeight;
        }

        [RelayCommand]
        private void Cancel()
        {
            RequestClose?.Invoke();
        }

        [RelayCommand]
        private async Task SaveAsync()
        {
            if (string.IsNullOrWhiteSpace(LicensePlate))
            {
                _notificationService.ShowWarning("Vui lòng nhập Biển số xe", "LỖI NHẬP LIỆU");
                return;
            }

            if (string.IsNullOrWhiteSpace(Reason))
            {
                _notificationService.ShowWarning("Vui lòng nhập lý do chỉnh sửa để phục vụ đối soát.", "BẮT BUỘC");
                return;
            }

            if (GrossWeight <= 0 || TareWeight < 0 || GrossWeight < TareWeight)
            {
                _notificationService.ShowWarning("Số liệu cân không hợp lệ (Gross > 0, Gross >= Tare).", "LỖI DỮ LIỆU");
                return;
            }

            try
            {
                using var db = _contextFactory.CreateDbContext();
                var ticketInDb = await db.WeighingTickets.FirstOrDefaultAsync(t => t.TicketID == _originalTicket.TicketID);

                if (ticketInDb == null)
                {
                    _notificationService.ShowError("Không tìm thấy phiếu trong CSDL.", "LỖI");
                    return;
                }

                // Tạo Audit Log bằng cách so sánh thay đổi
                string changes = "";
                if (ticketInDb.LicensePlate != LicensePlate) changes += $"Plate: {ticketInDb.LicensePlate}->{LicensePlate}, ";
                if (ticketInDb.CustomerName != CustomerName) changes += $"Cust: {ticketInDb.CustomerName}->{CustomerName}, ";
                if (ticketInDb.ProductName != ProductName) changes += $"Prod: {ticketInDb.ProductName}->{ProductName}, ";
                if (ticketInDb.GrossWeight != GrossWeight) changes += $"G: {ticketInDb.GrossWeight:N0}->{GrossWeight:N0}, ";
                if (ticketInDb.TareWeight != TareWeight) changes += $"T: {ticketInDb.TareWeight:N0}->{TareWeight:N0}, ";
                if (ticketInDb.TimeIn != TimeIn) changes += $"In: {ticketInDb.TimeIn:HH:mm}->{TimeIn:HH:mm}, ";
                if (ticketInDb.TimeOut != TimeOut) changes += $"Out: {ticketInDb.TimeOut:HH:mm}->{TimeOut:HH:mm}, ";

                if (string.IsNullOrEmpty(changes))
                {
                    _notificationService.ShowInfo("Không có thông tin nào thay đổi.", "THÔNG BÁO");
                    CloseAction?.Invoke();
                    return;
                }

                string editLog = $"[Sửa {DateTime.Now:HH:mm dd/MM}] {changes} Lý do: {Reason}";
                
                // Cập nhật dữ liệu
                ticketInDb.LicensePlate = LicensePlate;
                ticketInDb.CustomerName = CustomerName;
                ticketInDb.ProductName = ProductName;
                ticketInDb.GrossWeight = GrossWeight;
                ticketInDb.TareWeight = TareWeight;
                ticketInDb.NetWeight = NetWeight;
                ticketInDb.TimeIn = TimeIn;
                ticketInDb.TimeOut = TimeOut;
                
                ticketInDb.Note = string.IsNullOrEmpty(ticketInDb.Note) ? editLog : ticketInDb.Note + " | " + editLog;

                // Cập nhật VehicleId nếu biển số thay đổi
                var vehicle = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.LicensePlate == LicensePlate);
                ticketInDb.VehicleId = vehicle?.VehicleId;

                await db.SaveChangesAsync();

                _notificationService.ShowInfo("Đã cập nhật phiếu thành công.", "THÀNH CÔNG");
                IsSavedSuccessfully = true;
                SuccessCallback?.Invoke();
                RequestClose?.Invoke();
            }
            catch (Exception ex)
            {
                string msg = ex.Message;
                if (ex.InnerException != null) msg += "\nChi tiết: " + ex.InnerException.Message;
                _notificationService.ShowError($"Lỗi hệ thống: {msg}", "LỖI");
            }
        }
    }
}
