using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Common;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using Microsoft.Extensions.DependencyInjection;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class WeighingHistoryViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IExportService _exportService;
        private readonly IUserNotificationService _notificationService;
        private readonly WeighingBusinessService _weighingBusiness;

        public WeighingHistoryViewModel(
            IDbContextFactory<AppDbContext> contextFactory,
            IExportService exportService,
            IUserNotificationService notificationService,
            WeighingBusinessService weighingBusiness)
        {
            _contextFactory      = contextFactory;
            _exportService       = exportService;
            _notificationService = notificationService;
            _weighingBusiness    = weighingBusiness;

            _fromDate = DateTime.Today;
            _toDate   = DateTime.Today.AddDays(1).AddSeconds(-1);

            _ = LoadHistoryAsync();
        }

        // =========================================================================
        // DANH SÁCH & TÌM KIẾM
        // =========================================================================
        [ObservableProperty] private ObservableCollection<WeighingTicket> _tickets;
        [ObservableProperty] private WeighingTicket _selectedTicket;
        [ObservableProperty] private DateTime _fromDate;
        [ObservableProperty] private DateTime _toDate;
        [ObservableProperty] private string _searchText;

        // =========================================================================
        // THỐNG KÊ
        // =========================================================================
        [ObservableProperty] private int     _totalVehicles;
        [ObservableProperty] private decimal _totalGross;
        [ObservableProperty] private decimal _totalTare;
        [ObservableProperty] private decimal _totalNet;

        // (Đã xóa Panel chỉnh sửa cũ để chuyển sang cửa sổ Popup)
        [ObservableProperty] private bool _isSaving = false;

        // =========================================================================
        // COMMANDS — TRA CỨU
        // =========================================================================

        [RelayCommand]
        public async Task LoadHistoryAsync()
        {
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var query = context.WeighingTickets
                                   .IgnoreQueryFilters()
                                   .Where(t => t.TimeIn >= FromDate && t.TimeIn <= ToDate);

                if (!string.IsNullOrWhiteSpace(SearchText))
                {
                    string search = SearchText.ToLower();
                    query = query.Where(t => t.LicensePlate.ToLower().Contains(search) ||
                                             t.CustomerName.ToLower().Contains(search));
                }

                var result = await query.OrderByDescending(t => t.TimeIn).ToListAsync();
                Tickets = new ObservableCollection<WeighingTicket>(result);
                UpdateStatistics(result);

                // Reset panel chỉnh sửa sau khi reload
                SelectedTicket     = null;
            }
            catch (Exception ex)
            {
                _notificationService.ShowError(UiText.Messages.DataQueryError(ex.Message), UiText.Titles.SystemError);
            }
        }

        /// <summary>
        /// Mở cửa sổ Popup để chỉnh sửa toàn bộ thông tin phiếu cân.
        /// </summary>
        [RelayCommand]
        private async Task SelectForEdit(WeighingTicket ticket)
        {
            if (ticket == null || ticket.IsVoid)
            {
                _notificationService.ShowWarning("Không thể chỉnh sửa phiếu đã bị hủy.", UiText.Titles.Warning);
                return;
            }

            var vm = App.ServiceProvider.GetRequiredService<EditTicketViewModel>();
            await vm.InitializeAsync(ticket);

            var window = new Views.EditTicketWindow 
            { 
                DataContext = vm, 
                Owner = Application.Current.MainWindow 
            };
            
            vm.CloseAction = () => window.Close();
            window.ShowDialog();

            if (vm.IsSavedSuccessfully)
            {
                await LoadHistoryAsync();
            }
        }

        // =========================================================================
        // COMMANDS — HỦY PHIẾU / IN / XUẤT EXCEL / SỰ CỐ
        // =========================================================================

        [RelayCommand]
        private void OpenManualTicketForm()
        {
            var vm = App.ServiceProvider.GetRequiredService<ManualTicketViewModel>();
            var window = new Views.ManualTicketWindow { DataContext = vm, Owner = Application.Current.MainWindow };
            window.ShowDialog();

            if (vm.IsSavedSuccessfully)
            {
                _ = LoadHistoryAsync();
            }
        }

        [RelayCommand]
        private async Task VoidTicketAsync(WeighingTicket ticket)
        {
            if (ticket == null || ticket.IsVoid) return;

            if (!_notificationService.Confirm(
                UiText.Messages.VoidTicketConfirm(ticket.TicketID),
                UiText.Titles.Warning,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning))
                return;

            // Gọi Business Service thay vì viết lại logic trực tiếp trong ViewModel
            var (isSuccess, message) = await _weighingBusiness.VoidTicketAsync(
                ticket.TicketID,
                reason: "Nhân viên yêu cầu hủy");

            if (isSuccess)
                await LoadHistoryAsync();
            else
                _notificationService.ShowWarning(message, UiText.Titles.Warning);
        }

        [RelayCommand]
        private void PrintTicket(WeighingTicket ticket)
        {
            if (ticket == null) return;
            _notificationService.ShowInfo(UiText.Messages.PrintTicketInfo(ticket.TicketID), UiText.Titles.Printer);
        }

        [RelayCommand]
        private async Task ExportExcelAsync()
        {
            if (Tickets == null || !Tickets.Any())
            {
                _notificationService.ShowInfo(UiText.Messages.NoDataToExport, UiText.Titles.Info);
                return;
            }

            // Chỉ xuất các phiếu hợp lệ (không bị hủy) để hàm SUM trong Excel tính toán chính xác
            var validTickets = Tickets.Where(t => !t.IsVoid).ToList();
            
            if (!validTickets.Any())
            {
                _notificationService.ShowInfo("Không có dữ liệu hợp lệ (tất cả các phiếu trong danh sách đều đã bị hủy) để xuất báo cáo.", UiText.Titles.Info);
                return;
            }

            await _exportService.ExportTicketsToExcelAsync(validTickets, "BÁO CÁO CHI TIẾT SẢN LƯỢNG TRẠM CÂN");
        }

        // =========================================================================
        // HELPERS
        // =========================================================================

        private void UpdateStatistics(List<WeighingTicket> data)
        {
            var validTickets = data.Where(t => !t.IsVoid && t.TimeOut.HasValue).ToList();
            TotalVehicles = validTickets.Count;
            TotalGross    = validTickets.Sum(t => t.GrossWeight);
            TotalTare     = validTickets.Sum(t => t.TareWeight);
            TotalNet      = validTickets.Sum(t => t.NetWeight);
        }
    }
}