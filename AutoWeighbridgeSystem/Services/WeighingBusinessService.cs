using AutoWeighbridgeSystem.Data;
using AutoWeighbridgeSystem.Models;
using Microsoft.EntityFrameworkCore;
using Serilog;
using System;
using System.Linq;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    public class WeighingBusinessService
    {
        private readonly IDbContextFactory<AppDbContext> _dbContextFactory;

        public WeighingBusinessService(IDbContextFactory<AppDbContext> dbContextFactory)
        {
            _dbContextFactory = dbContextFactory;
        }

        public async Task<WeighingProcessResult> ProcessWeighingAsync(
      string licensePlate, int vehicleId, string customerName, string productName, decimal finalWeight, bool isOnePassMode) // Thêm tham số isOnePassMode
        {
            var result = new WeighingProcessResult();

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
                    var vehicle = await db.Vehicles.FindAsync(vehicleId);
                    // NẾU KHÔNG THẤY THEO ID, THỬ TÌM THEO BIỂN SỐ (Xử lý cho gõ tay)
                    if (vehicle == null)
                    {
                        vehicle = await db.Vehicles.FirstOrDefaultAsync(v => v.LicensePlate == licensePlate && !v.IsDeleted);
                    }

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
                        VehicleId = vehicleId,
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
                        VehicleId = vehicleId,
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
                Log.Error(ex, "[WEIGHING] Lỗi cơ sở dữ liệu");
                result.IsSuccess = false;
                result.Message = "Lỗi hệ thống: " + ex.Message;
            }

            return result;
        }

        // =========================================================================
        // HÀM BỔ TRỢ (Sinh mã tự động)
        // =========================================================================
        private async Task<string> GenerateTicketIdAsync(AppDbContext db)
        {
            string prefix = DateTime.Now.ToString("yyMMdd");

            // Tìm phiếu cuối cùng trong ngày (Bỏ qua bộ lọc IsVoid để đếm số thứ tự chính xác)
            var lastTicket = await db.WeighingTickets
                .IgnoreQueryFilters()
                .Where(t => t.TicketID.StartsWith(prefix))
                .OrderByDescending(t => t.TicketID)
                .FirstOrDefaultAsync();

            int nextNum = 1;
            if (lastTicket != null && lastTicket.TicketID.Contains("-"))
            {
                if (int.TryParse(lastTicket.TicketID.Split('-').Last(), out int num))
                {
                    nextNum = num + 1;
                }
            }
            return $"{prefix}-{nextNum:D3}";
        }
    }
}