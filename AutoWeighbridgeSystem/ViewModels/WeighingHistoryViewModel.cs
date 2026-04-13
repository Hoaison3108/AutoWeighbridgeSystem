using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using AutoWeighbridgeSystem.Services; // Thêm namespace service
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.EntityFrameworkCore;
using System;
using System.Collections.ObjectModel;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class WeighingHistoryViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IExportService _exportService; // Khai báo Service

        public WeighingHistoryViewModel(
            IDbContextFactory<AppDbContext> contextFactory,
            IExportService exportService) // Inject thông qua Constructor
        {
            _contextFactory = contextFactory;
            _exportService = exportService;

            _fromDate = DateTime.Today;
            _toDate = DateTime.Today.AddDays(1).AddSeconds(-1);

            _ = LoadHistoryAsync();
        }

        [ObservableProperty] private ObservableCollection<WeighingTicket> _tickets;
        [ObservableProperty] private WeighingTicket _selectedTicket;
        [ObservableProperty] private DateTime _fromDate;
        [ObservableProperty] private DateTime _toDate;
        [ObservableProperty] private string _searchText;

        // Statistics
        [ObservableProperty] private int _totalVehicles;
        [ObservableProperty] private decimal _totalGross;
        [ObservableProperty] private decimal _totalTare;
        [ObservableProperty] private decimal _totalNet;

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
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi truy xuất dữ liệu: {ex.Message}", "Lỗi Hệ Thống", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        private void UpdateStatistics(List<WeighingTicket> data)
        {
            var validTickets = data.Where(t => !t.IsVoid && t.TimeOut.HasValue).ToList();
            TotalVehicles = validTickets.Count;
            TotalGross = validTickets.Sum(t => t.GrossWeight);
            TotalTare = validTickets.Sum(t => t.TareWeight);
            TotalNet = validTickets.Sum(t => t.NetWeight);
        }

        [RelayCommand]
        private async Task VoidTicketAsync(WeighingTicket ticket)
        {
            if (ticket == null || ticket.IsVoid) return;

            var confirm = MessageBox.Show($"Xác nhận HỦY phiếu: {ticket.TicketID}?", "Cảnh báo", MessageBoxButton.YesNo, MessageBoxImage.Warning);
            if (confirm == MessageBoxResult.Yes)
            {
                using var context = _contextFactory.CreateDbContext();
                var ticketInDb = await context.WeighingTickets.IgnoreQueryFilters()
                                              .FirstOrDefaultAsync(t => t.TicketID == ticket.TicketID);

                if (ticketInDb != null)
                {
                    ticketInDb.IsVoid = true;
                    ticketInDb.VoidReason = "Nhân viên yêu cầu hủy";
                    ticketInDb.Note = $"Hủy lúc: {DateTime.Now:HH:mm dd/MM/yyyy}";
                    await context.SaveChangesAsync();
                    await LoadHistoryAsync();
                }
            }
        }

        [RelayCommand]
        private void PrintTicket(WeighingTicket ticket)
        {
            if (ticket == null) return;
            MessageBox.Show($"Đang in lại phiếu {ticket.TicketID}...", "Máy in");
        }

        [RelayCommand]
        private async Task ExportExcelAsync()
        {
            if (Tickets == null || !Tickets.Any())
            {
                MessageBox.Show("Không có dữ liệu để xuất!", "Thông báo");
                return;
            }

            // GỌI SERVICE MODULE ĐÃ TÁCH
            await _exportService.ExportTicketsToExcelAsync(Tickets, "BÁO CÁO CHI TIẾT SẢN LƯỢNG TRẠM CÂN");
        }
    }
}