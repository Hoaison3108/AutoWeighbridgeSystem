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

namespace AutoWeighbridgeSystem.ViewModels
{
    public partial class WeighingHistoryViewModel : ObservableObject
    {
        private readonly IDbContextFactory<AppDbContext> _contextFactory;
        private readonly IExportService _exportService;
        private readonly IUserNotificationService _notificationService;

        public WeighingHistoryViewModel(
            IDbContextFactory<AppDbContext> contextFactory,
            IExportService exportService,
            IUserNotificationService notificationService)
        {
            _contextFactory = contextFactory;
            _exportService  = exportService;
            _notificationService = notificationService;

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

        // =========================================================================
        // CHỈNH SỬA PHIẾU (Edit Panel)
        // =========================================================================

        /// <summary>Khi người dùng chọn một dòng trong DataGrid → tự động điền form chỉnh sửa.</summary>
        partial void OnSelectedTicketChanged(WeighingTicket value)
        {
            if (value == null || value.IsVoid)
            {
                IsEditPanelVisible = false;
                return;
            }

            // Nạp giá trị hiện tại của phiếu vào các ô edit
            EditGrossWeight = value.GrossWeight;
            EditTareWeight  = value.TareWeight;
            EditNote        = value.Note ?? "";
            IsEditPanelVisible = true;
        }

        /// <summary>GrossWeight đang được chỉnh sửa (không sửa trực tiếp trên model).</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EditNetWeight))]
        private decimal _editGrossWeight;

        /// <summary>TareWeight đang được chỉnh sửa.</summary>
        [ObservableProperty]
        [NotifyPropertyChangedFor(nameof(EditNetWeight))]
        private decimal _editTareWeight;

        /// <summary>Net = Gross - Tare — tự tính lại khi một trong hai thay đổi.</summary>
        public decimal EditNetWeight => EditGrossWeight - EditTareWeight;

        /// <summary>Ghi chú lý do chỉnh sửa (bắt buộc).</summary>
        [ObservableProperty] private string _editNote = "";

        /// <summary>Trạng thái hiển thị của panel chỉnh sửa.</summary>
        [ObservableProperty] private bool _isEditPanelVisible = false;

        /// <summary>Trạng thái đang lưu — dùng để disable nút CẬP NHẬT khi đang xử lý.</summary>
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
                IsEditPanelVisible = false;
                SelectedTicket     = null;
            }
            catch (Exception ex)
            {
                _notificationService.ShowError(UiText.Messages.DataQueryError(ex.Message), UiText.Titles.SystemError);
            }
        }

        // =========================================================================
        // COMMANDS — CHỈNH SỬA KHỐI LƯỢNG
        // =========================================================================

        /// <summary>
        /// Cập nhật GrossWeight, TareWeight, NetWeight của phiếu được chọn vào database.
        /// Ghi lại log chỉnh sửa kèm lý do vào cột Note.
        /// </summary>
        [RelayCommand]
        private async Task UpdateWeightAsync()
        {
            if (SelectedTicket == null || SelectedTicket.IsVoid) return;

            // Validate dữ liệu đầu vào
            if (EditGrossWeight <= 0 || EditTareWeight < 0)
            {
                _notificationService.ShowWarning("Trọng lượng không hợp lệ (GrossWeight phải > 0).", UiText.Titles.Warning);
                return;
            }
            if (EditGrossWeight < EditTareWeight)
            {
                _notificationService.ShowWarning("Tổng (Gross) không thể nhỏ hơn Thân Xe (Tare).", UiText.Titles.Warning);
                return;
            }
            if (string.IsNullOrWhiteSpace(EditNote))
            {
                _notificationService.ShowWarning("Vui lòng nhập lý do chỉnh sửa trước khi lưu.", UiText.Titles.Warning);
                return;
            }

            // Xác nhận trước khi ghi đè
            string confirmMsg = $"Cập nhật phiếu [{SelectedTicket.TicketID}]?\n\n" +
                                $"   Tổng (Gross): {SelectedTicket.GrossWeight:N0} → {EditGrossWeight:N0} kg\n" +
                                $"   Thân xe (Tare): {SelectedTicket.TareWeight:N0} → {EditTareWeight:N0} kg\n" +
                                $"   Hàng thực (Net): {SelectedTicket.NetWeight:N0} → {EditNetWeight:N0} kg\n\n" +
                                $"Lý do: {EditNote}";

            if (!_notificationService.Confirm(confirmMsg, "XÁC NHẬN CẬP NHẬT", MessageBoxButton.YesNo, MessageBoxImage.Question))
                return;

            IsSaving = true;
            try
            {
                using var context = _contextFactory.CreateDbContext();
                var ticketInDb = await context.WeighingTickets
                                              .IgnoreQueryFilters()
                                              .FirstOrDefaultAsync(t => t.TicketID == SelectedTicket.TicketID);

                if (ticketInDb == null)
                {
                    _notificationService.ShowError("Không tìm thấy phiếu trong database.", UiText.Titles.Error);
                    return;
                }

                // Ghi lại log chỉnh sửa cũ trước khi ghi đè
                string editLog = $"[Sửa {DateTime.Now:HH:mm dd/MM/yyyy}] " +
                                 $"Gross: {ticketInDb.GrossWeight:N0}→{EditGrossWeight:N0}, " +
                                 $"Tare: {ticketInDb.TareWeight:N0}→{EditTareWeight:N0}. " +
                                 $"Lý do: {EditNote}";

                ticketInDb.GrossWeight = EditGrossWeight;
                ticketInDb.TareWeight  = EditTareWeight;
                ticketInDb.NetWeight   = EditNetWeight;
                ticketInDb.Note        = string.IsNullOrEmpty(ticketInDb.Note)
                                            ? editLog
                                            : ticketInDb.Note + " | " + editLog;

                await context.SaveChangesAsync();

                _notificationService.ShowInfo(
                    $"✅ Đã cập nhật phiếu [{SelectedTicket.TicketID}] thành công.\nNet mới: {EditNetWeight:N0} kg",
                    "CẬP NHẬT THÀNH CÔNG");

                // Reload để cập nhật lại DataGrid
                await LoadHistoryAsync();
            }
            catch (Exception ex)
            {
                _notificationService.ShowError($"Lỗi khi cập nhật: {ex.Message}", UiText.Titles.Error);
            }
            finally
            {
                IsSaving = false;
            }
        }

        /// <summary>Hủy chỉnh sửa, đóng panel edit và bỏ chọn dòng.</summary>
        [RelayCommand]
        private void CancelEdit()
        {
            IsEditPanelVisible = false;
            SelectedTicket     = null;
        }

        /// <summary>
        /// Chọn một phiếu từ nút Sửa trong DataGrid để mở panel chỉnh sửa.
        /// Không cho sửa phiếu đã bị hủy.
        /// </summary>
        [RelayCommand]
        private void SelectForEdit(WeighingTicket ticket)
        {
            if (ticket == null || ticket.IsVoid)
            {
                _notificationService.ShowWarning("Không thể chỉnh sửa phiếu đã bị hủy.", UiText.Titles.Warning);
                return;
            }
            SelectedTicket = ticket; // OnSelectedTicketChanged sẽ tự mở panel
        }

        // =========================================================================
        // COMMANDS — HỦY PHIẾU / IN / XUẤT EXCEL
        // =========================================================================

        [RelayCommand]
        private async Task VoidTicketAsync(WeighingTicket ticket)
        {
            if (ticket == null || ticket.IsVoid) return;

            if (_notificationService.Confirm(
                UiText.Messages.VoidTicketConfirm(ticket.TicketID),
                UiText.Titles.Warning,
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning))
            {
                using var context = _contextFactory.CreateDbContext();
                var ticketInDb = await context.WeighingTickets.IgnoreQueryFilters()
                                              .FirstOrDefaultAsync(t => t.TicketID == ticket.TicketID);

                if (ticketInDb != null)
                {
                    ticketInDb.IsVoid     = true;
                    ticketInDb.VoidReason = "Nhân viên yêu cầu hủy";
                    ticketInDb.Note       = $"Hủy lúc: {DateTime.Now:HH:mm dd/MM/yyyy}";
                    await context.SaveChangesAsync();
                    await LoadHistoryAsync();
                }
            }
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
            await _exportService.ExportTicketsToExcelAsync(Tickets, "BÁO CÁO CHI TIẾT SẢN LƯỢNG TRẠM CÂN");
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