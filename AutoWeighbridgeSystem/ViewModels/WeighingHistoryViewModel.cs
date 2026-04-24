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

            // Initialize Manual Ticket VM for the second tab
            ManualTicketVM = App.ServiceProvider.GetRequiredService<ManualTicketViewModel>();
            ManualTicketVM.SuccessCallback = () => _ = LoadHistoryAsync();
            ManualTicketVM.RequestClose = () => SelectedTabIndex = 0;

            EditTicketVM = App.ServiceProvider.GetRequiredService<EditTicketViewModel>();
            EditTicketVM.SuccessCallback = () => _ = LoadHistoryAsync();
            EditTicketVM.RequestClose = () => SelectedTabIndex = 0;

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
        
        // Pagination
        private int _currentPage = 0;
        private const int PageSize = 50;
        [ObservableProperty] private bool _hasMoreData = true;
        [ObservableProperty] private bool _isLoading = false;

        // =========================================================================
        // THỐNG KÊ
        // =========================================================================
        [ObservableProperty] private int     _totalVehicles;
        [ObservableProperty] private decimal _totalGross;
        [ObservableProperty] private decimal _totalTare;
        [ObservableProperty] private decimal _totalNet;

        // Tab Control management
        [ObservableProperty] private int _selectedTabIndex = 0;
        public ManualTicketViewModel ManualTicketVM { get; }
        public EditTicketViewModel EditTicketVM { get; }

        [ObservableProperty] private bool _isSaving = false;

        // =========================================================================
        // COMMANDS — TRA CỨU
        // =========================================================================

        [RelayCommand]
        public async Task LoadHistoryAsync()
        {
            if (IsLoading) return;
            
            try
            {
                IsLoading = true;
                _currentPage = 0;
                HasMoreData = true;

                using var context = _contextFactory.CreateDbContext();
                
                // 1. QUERY CƠ SỞ (Base Query) dùng chung logic lọc
                var baseQuery = GetFilteredQuery(context);

                // 2. TÍNH TOÁN THỐNG KÊ TẠI DATABASE (Tối ưu nhất cho Pentium G5400)
                // Thay vì tải hàng vạn dòng về RAM để Sum, ta chỉ lấy đúng 4 con số tổng.
                var statsQuery = baseQuery.Where(t => !t.IsVoid && t.TimeOut.HasValue);
                
                TotalVehicles = await statsQuery.CountAsync();
                if (TotalVehicles > 0)
                {
                    TotalGross = await statsQuery.SumAsync(t => t.GrossWeight);
                    TotalTare  = await statsQuery.SumAsync(t => t.TareWeight);
                    TotalNet   = await statsQuery.SumAsync(t => t.NetWeight);
                }
                else
                {
                    TotalGross = TotalTare = TotalNet = 0;
                }

                // 3. TẢI DỮ LIỆU PHÂN TRANG (Pagination)
                var result = await baseQuery.AsNoTracking()
                                           .OrderByDescending(t => t.TimeIn)
                                           .Take(PageSize)
                                           .ToListAsync();

                Tickets = new ObservableCollection<WeighingTicket>(result);
                HasMoreData = result.Count == PageSize;
                SelectedTicket = null;
            }
            catch (Exception ex)
            {
                _notificationService.ShowError(UiText.Messages.DataQueryError(ex.Message), UiText.Titles.SystemError);
            }
            finally
            {
                IsLoading = false;
            }
        }

        [RelayCommand]
        public async Task LoadMoreAsync()
        {
            if (IsLoading || !HasMoreData) return;

            try
            {
                IsLoading = true;
                _currentPage++;

                using var context = _contextFactory.CreateDbContext();
                var query = GetFilteredQuery(context);

                var nextBatch = await query.AsNoTracking()
                                           .OrderByDescending(t => t.TimeIn)
                                           .Skip(_currentPage * PageSize)
                                           .Take(PageSize)
                                           .ToListAsync();

                foreach (var ticket in nextBatch)
                {
                    Tickets.Add(ticket);
                }

                HasMoreData = nextBatch.Count == PageSize;
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("Lỗi khi tải thêm dữ liệu: " + ex.Message, UiText.Titles.SystemError);
            }
            finally
            {
                IsLoading = false;
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

            await EditTicketVM.InitializeAsync(ticket);
            SelectedTabIndex = 2; // Chuyển sang Tab "Chỉnh sửa phiếu"
        }

        // =========================================================================
        // COMMANDS — HỦY PHIẾU / IN / XUẤT EXCEL / SỰ CỐ
        // =========================================================================

        [RelayCommand]
        private void OpenManualTicketForm()
        {
            SelectedTabIndex = 1; // Chuyển sang Tab "Tạo phiếu sự cố"
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
            try
            {
                IsLoading = true;

                using var context = _contextFactory.CreateDbContext();
                // Lấy query chung và lọc bỏ phiếu hủy
                var query = GetFilteredQuery(context).Where(t => !t.IsVoid);

                // Lấy toàn bộ dữ liệu không phân trang
                var allTickets = await query.AsNoTracking().OrderBy(t => t.TimeIn).ToListAsync();

                if (allTickets == null || !allTickets.Any())
                {
                    _notificationService.ShowInfo(UiText.Messages.NoDataToExport, UiText.Titles.Info);
                    return;
                }

                // Sắp xếp lại danh sách (thường báo cáo Excel cần từ cũ đến mới)
                var validTickets = allTickets.OrderBy(t => t.TimeIn).ToList();
                
                await _exportService.ExportTicketsToExcelAsync(validTickets, "BÁO CÁO CHI TIẾT SẢN LƯỢNG TRẠM CÂN");
            }
            catch (Exception ex)
            {
                _notificationService.ShowError("Lỗi hệ thống khi xuất Excel: " + ex.Message, UiText.Titles.SystemError);
            }
            finally
            {
                IsLoading = false;
            }
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

        /// <summary>
        /// Tạo Query cơ sở áp dụng các bộ lọc Ngày tháng và Từ khóa tìm kiếm.
        /// Giúp tập trung logic Filter vào một nơi duy nhất.
        /// </summary>
        private IQueryable<WeighingTicket> GetFilteredQuery(AppDbContext context)
        {
            var searchStart = FromDate.Date;
            var searchEnd = ToDate.Date.AddDays(1).AddTicks(-1);

            var query = context.WeighingTickets
                               .IgnoreQueryFilters()
                               .Where(t => t.TimeIn >= searchStart && t.TimeIn <= searchEnd);

            if (!string.IsNullOrWhiteSpace(SearchText))
            {
                string search = SearchText.ToLower();
                query = query.Where(t => t.LicensePlate.ToLower().Contains(search) ||
                                         t.CustomerName.ToLower().Contains(search));
            }

            return query;
        }
    }
}