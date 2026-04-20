using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;
using AutoWeighbridgeSystem.Common;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ nghiệp vụ cốt lõi của quy trình cân xe.
    /// Xử lý toàn bộ logic cân: tạo phiếu mới (cân vào), cập nhật phiếu (cân ra),
    /// cân 1 lần (One-Pass), tự hủy phiếu treo sau 24h, và hủy phiếu thủ công.
    /// </summary>
    public class WeighingBusinessService
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

        /// <summary>
        /// Lock tĩnh bảo vệ việc sinh TicketID — đảm bảo chỉ 1 luồng
        /// thực hiện đọc Max + tăng số tại một thời điểm, tránh trùng mã.
        /// </summary>
        private static readonly SemaphoreSlim _ticketIdLock = new SemaphoreSlim(1, 1);

        public WeighingBusinessService(IDbContextFactory<AppDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        /// <summary>
        /// Xử lý một sự kiện cân: tạo phiếu mới hoặc hoàn tất phiếu đang treo.
        /// <list type="bullet">
        ///   <item><b>Cân 1 lần (One-Pass)</b>: dùng khối lượng bì đã đăng ký, hoàn tất ngay.</item>
        ///   <item><b>Cân 2 lần — Lần 1</b>: tạo phiếu mới với GrossWeight, chờ lần cân thứ 2.</item>
        ///   <item><b>Cân 2 lần — Lần 2</b>: tìm phiếu đang treo, tính Net = Gross - Tare, đóng phiếu.</item>
        ///   <item><b>Auto-void</b>: nếu phiếu treo quá 24h, tự động hủy trước khi tạo phiếu mới.</item>
        /// </list>
        /// </summary>
        /// <param name="licensePlate">Biển số xe.</param>
        /// <param name="vehicleId">ID xe trong database (0 nếu chưa xác định).</param>
        /// <param name="customerName">Tên khách hàng (snapshot lưu vào phiếu).</param>
        /// <param name="productName">Tên hàng hóa (snapshot lưu vào phiếu).</param>
        /// <param name="finalWeight">Trọng lượng đã chốt từ đầu cân (kg).</param>
        /// <param name="isOnePassMode">Chế độ cân 1 lần hay 2 lần.</param>
        public async Task<WeighingProcessResult> ProcessWeighingAsync(
      string licensePlate, int vehicleId, string customerName, string productName, decimal finalWeight, bool isOnePassMode) // Thêm tham số isOnePassMode
        {
            var result = new WeighingProcessResult();
            licensePlate = licensePlate.FormatLicensePlate();

            if (string.IsNullOrWhiteSpace(licensePlate))
            {
                result.IsSuccess = false;
                result.Message = "Biển số xe không hợp lệ.";
                return result;
            }

            try
            {
                using var db = _dbContextFactory.CreateDbContext();

                // Tìm phiếu đang treo
                var openTicket = await db.WeighingTickets
                    .FirstOrDefaultAsync(t => t.LicensePlate == licensePlate && t.TimeOut == null && !t.IsVoid);

                // Logic Auto-Void (Hủy tự động 24h) vẫn giữ nguyên
                if (openTicket != null)
                {
                    if ((DateTime.Now - openTicket.TimeIn).TotalHours > 24)
                    {
                        openTicket.IsVoid = true;
                        openTicket.VoidReason = "Tự động hủy do quá 24h";
                        db.WeighingTickets.Update(openTicket);
                        openTicket = null;
                    }
                }

                // ================================================================
                // NHÁNH 1: XỬ LÝ CÂN 1 LẦN (ONE-PASS WEIGHING)
                // ================================================================
                if (isOnePassMode)
                {
                    if (openTicket != null)
                    {
                        result.IsSuccess = false;
                        result.Message = "Xe này đang có phiếu cân 2 lần chưa hoàn tất. Vui lòng hủy phiếu cũ hoặc tắt chế độ Cân 1 lần.";
                        return result;
                    }

                    // Lấy thông tin xe để bốc khối lượng bì
                    // TỐI ƯU: Gọi DB 1 lần duy nhất, tắt Tracking để chạy nhanh nhất có thể
                    var vehicle = await db.Vehicles
                        .AsNoTracking()
                        .FirstOrDefaultAsync(v => (v.VehicleId == vehicleId || v.LicensePlate == licensePlate) && !v.IsDeleted);

                    if (vehicle == null)
                    {
                        result.IsSuccess = false;
                        result.Message = $"Không tìm thấy thông tin xe {licensePlate} trong hệ thống!";
                        return result;
                    }
                    decimal tareWeight = vehicle?.TareWeight ?? 0;

                    if (tareWeight <= 0)
                    {
                        result.IsSuccess = false;
                        result.Message = $"Xe {licensePlate} chưa được đăng ký Khối lượng bì mặc định. Không thể cân 1 lần!";
                        return result;
                    }

                    // Tạo phiếu và hoàn tất ngay lập tức
                    var onePassTicket = new WeighingTicket
                    {
                        TicketID = await GenerateTicketIdAsync(db),
                        VehicleId = vehicleId == 0 ? null : vehicleId, // null = xe chưa đăng ký
                        LicensePlate = licensePlate,
                        CustomerName = customerName,
                        ProductName = productName,
                        TimeIn = DateTime.Now,
                        TimeOut = DateTime.Now, // Đóng phiếu ngay
                        GrossWeight = finalWeight, // Cân thực tế là Gross
                        TareWeight = tareWeight,   // Bì lấy từ Database
                        NetWeight = finalWeight - tareWeight // Hàng thực tế
                    };

                    // Chống số âm nếu bì khai báo lớn hơn số cân thực tế
                    if (onePassTicket.NetWeight < 0) onePassTicket.NetWeight = 0;

                    db.WeighingTickets.Add(onePassTicket);
                    await db.SaveChangesAsync();

                    result.IsSuccess = true;
                    result.IsFirstWeighing = false; // Coi như đã hoàn thành
                    result.Ticket = onePassTicket;
                    result.Message = $"CÂN 1 LẦN THÀNH CÔNG!\nNet: {onePassTicket.NetWeight:N0} KG (Thân: {tareWeight:N0} KG)";

                    return result; // Kết thúc sớm luồng 1 lần
                }

                // ================================================================
                // NHÁNH 2: XỬ LÝ CÂN 2 LẦN BÌNH THƯỜNG (Giữ nguyên logic cũ của bạn)
                // ================================================================
                if (openTicket == null)
                {
                    // Cân Lần 1...
                    var newTicket = new WeighingTicket
                    {
                        TicketID = await GenerateTicketIdAsync(db),
                        VehicleId = vehicleId == 0 ? null : vehicleId, // null = xe chưa đăng ký
                        LicensePlate = licensePlate,
                        CustomerName = customerName,
                        ProductName = productName,
                        TimeIn = DateTime.Now,
                        GrossWeight = finalWeight,
                        TareWeight = 0,
                        NetWeight = 0
                    };
                    db.WeighingTickets.Add(newTicket);
                    await db.SaveChangesAsync();

                    result.IsSuccess = true;
                    result.IsFirstWeighing = true;
                    result.Message = $"CÂN VÀO THÀNH CÔNG: {licensePlate}";
                }
                else
                {
                    // Cân Lần 2...
                    openTicket.TimeOut = DateTime.Now;
                    decimal firstWeight = openTicket.GrossWeight;

                    if (finalWeight > firstWeight)
                    {
                        openTicket.GrossWeight = finalWeight;
                        openTicket.TareWeight = firstWeight;
                    }
                    else
                    {
                        openTicket.GrossWeight = firstWeight;
                        openTicket.TareWeight = finalWeight;
                    }

                    openTicket.NetWeight = openTicket.GrossWeight - openTicket.TareWeight;
                    db.WeighingTickets.Update(openTicket);
                    await db.SaveChangesAsync();

                    result.IsSuccess = true;
                    result.IsFirstWeighing = false;
                    result.Message = $"XUẤT HÀNG THÀNH CÔNG: {openTicket.NetWeight:N0} KG";
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WEIGHING] Lỗi cơ sở dữ liệu khi xử lý phiếu");
                result.IsSuccess = false;
                string msg = ex.Message;
                if (ex.InnerException != null) msg += "\nChi tiết: " + ex.InnerException.Message;
                result.Message = "Lỗi hệ thống: " + msg;
            }

            return result;
        }

        // =========================================================================
        // HÀM TẠO PHIẾU CÂN SỰ CỐ (MANUAL INPUT)
        // =========================================================================
        public async Task<(bool IsSuccess, string Message)> CreateManualTicketAsync(string licensePlate, string customerName, string productName, decimal grossWeight, decimal tareWeight, DateTime timeIn, DateTime timeOut, string reason)
        {
            var result = (IsSuccess: false, Message: "");
            try
            {
                using var db = _dbContextFactory.CreateDbContext();

                string ticketId = await GenerateTicketIdAsync(db);
                decimal netWeight = grossWeight - tareWeight;
                
                var vehicleInfo = await db.Vehicles.AsNoTracking().FirstOrDefaultAsync(v => v.LicensePlate == licensePlate);

                var newTicket = new WeighingTicket
                {
                    TicketID = ticketId,
                    VehicleId = vehicleInfo?.VehicleId,
                    LicensePlate = licensePlate,
                    CustomerName = customerName,
                    ProductName = productName,
                    GrossWeight = grossWeight,
                    TareWeight = tareWeight,
                    NetWeight = netWeight,
                    TimeIn = timeIn,
                    TimeOut = timeOut,
                    IsVoid = false,
                    Note = $"[NHẬP THỦ CÔNG - SỰ CỐ]: {reason}"
                };

                db.WeighingTickets.Add(newTicket);
                await db.SaveChangesAsync();

                result.IsSuccess = true;
                result.Message = $"ĐÃ LƯU: {ticketId} ({netWeight:N0} kg)";
                Log.Information("[WEIGHING-MANUAL] Tạo phiếu sự cố {TicketId} xe {Plate}. Net: {Net}", ticketId, licensePlate, netWeight);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WEIGHING-MANUAL] Lỗi cơ sở dữ liệu khi nhập thủ công");
                result.IsSuccess = false;
                string msg = ex.Message;
                if (ex.InnerException != null) msg += "\nChi tiết: " + ex.InnerException.Message;
                result.Message = "Lỗi hệ thống: " + msg;
            }
            return result;
        }

        // =========================================================================
        // HÀM HỦY PHIẾU CÂN
        // =========================================================================

        /// <summary>
        /// Hủy một phiếu cân theo ID. Hàm được tách từ DashboardViewModel để đặt
        /// đúng chỗ trong Business Layer.
        /// </summary>
        public async Task<(bool IsSuccess, string Message)> VoidTicketAsync(string ticketId, string reason = "Hủy thủ công")
        {
            if (string.IsNullOrWhiteSpace(ticketId))
                return (false, "Mã phiếu không hợp lệ.");

            try
            {
                using var db = _dbContextFactory.CreateDbContext();
                var ticket = await db.WeighingTickets
                    .IgnoreQueryFilters()
                    .FirstOrDefaultAsync(t => t.TicketID == ticketId);

                if (ticket == null)
                    return (false, $"Không tìm thấy phiếu {ticketId}.");

                if (ticket.IsVoid)
                    return (false, $"Phiếu {ticketId} đã bị hủy trước đó.");

                ticket.IsVoid = true;
                ticket.VoidReason = reason;
                db.WeighingTickets.Update(ticket);
                await db.SaveChangesAsync();

                Log.Information("[WEIGHING] Đã hủy phiếu {TicketId}, lý do: {Reason}", ticketId, reason);
                return (true, ticketId);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[WEIGHING] Lỗi khi hủy phiếu {TicketId}", ticketId);
                string msg = ex.Message;
                if (ex.InnerException != null) msg += "\nChi tiết: " + ex.InnerException.Message;
                return (false, "Lỗi hệ thống: " + msg);
            }
        }

        // =========================================================================
        // HÀM BỔ TRỢ (Sinh mã tự động)
        // =========================================================================
        /// <summary>
        /// Sinh mã phiếu cân theo định dạng <c>yyMMddxxx</c> (vd: 260416001).
        /// Tìm mã lớn nhất trong ngày hiện tại và tăng lên 1.
        /// </summary>
        private async Task<string> GenerateTicketIdAsync(AppDbContext db)
        {
            // SemaphoreSlim(1,1): chỉ 1 luồng thực hiện đoạn đọc-tăng-trả về tại một thời điểm.
            // Ngăn trường hợp 2 xe cân cùng lúc đọc được cùng MaxTicketId → sinh mã trùng.
            await _ticketIdLock.WaitAsync();
            try
            {
                string prefix = DateTime.Now.ToString("yyMMdd");

                var maxTicketId = await db.WeighingTickets
                    .IgnoreQueryFilters()
                    .Where(t => t.TicketID.StartsWith(prefix))
                    .Select(t => (string?)t.TicketID)
                    .MaxAsync();

                int nextNum = 1;
                if (!string.IsNullOrEmpty(maxTicketId) && maxTicketId.Length > prefix.Length)
                {
                    string suffix = maxTicketId.Substring(prefix.Length).Replace("-", "");
                    if (int.TryParse(suffix, out int num))
                        nextNum = num + 1;
                }
                return $"{prefix}{nextNum:D3}";
            }
            finally
            {
                _ticketIdLock.Release();
            }
        }
    }
}