using AutoWeighbridgeSystem.Models;
using Google.Apis.Auth.OAuth2;
using Google.Apis.Services;
using Google.Apis.Sheets.v4;
using Google.Apis.Sheets.v4.Data;
using Microsoft.Extensions.Configuration;
using Serilog;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Dịch vụ kết nối API Google Sheets.
    /// Có nhiệm vụ Ghi đè (Clear & Write) toàn bộ danh sách phiếu cân trong ngày lên "Sheet A".
    /// </summary>
    public class GoogleSheetsExportService
    {
        private readonly string[] Scopes = { SheetsService.Scope.Spreadsheets };
        private readonly IConfiguration _configuration;
        private SheetsService? _sheetsService;

        public GoogleSheetsExportService(IConfiguration configuration)
        {
            _configuration = configuration;
        }

        private bool InitializeService()
        {
            if (_sheetsService != null) return true;

            try
            {
                string credPath = _configuration["GoogleSheets:CredentialsFilePath"] ?? "credentials.json";
                if (!File.Exists(credPath))
                {
                    Log.Warning("[GOOGLE_SHEETS] Không tìm thấy file xác thực: {Path}", credPath);
                    return false;
                }

                GoogleCredential credential;
                using (var stream = new FileStream(credPath, FileMode.Open, FileAccess.Read))
                {
                    credential = GoogleCredential.FromStream(stream).CreateScoped(Scopes);
                }

                _sheetsService = new SheetsService(new BaseClientService.Initializer()
                {
                    HttpClientInitializer = credential,
                    ApplicationName = "AutoWeighbridgeSystem"
                });

                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GOOGLE_SHEETS] Lỗi khởi tạo Google Sheets Service.");
                return false;
            }
        }

        /// <summary>
        /// Đồng bộ danh sách phiếu lên Google Sheets.
        /// Chức năng: Xóa trắng toàn bộ dữ liệu cũ (chừa Header) và ghi đè toàn bộ danh sách mới.
        /// </summary>
        public async Task<bool> SyncDailyTicketsAsync(List<WeighingTicket> tickets)
        {
            if (!InitializeService()) return false;

            string spreadsheetId = _configuration["GoogleSheets:SpreadsheetId"];
            string sheetName = _configuration["GoogleSheets:SheetName"];

            if (string.IsNullOrEmpty(spreadsheetId) || string.IsNullOrEmpty(sheetName))
            {
                Log.Warning("[GOOGLE_SHEETS] Chưa cấu hình SpreadsheetId hoặc SheetName.");
                return false;
            }

            try
            {
                // Bước 1: Clear dữ liệu cũ từ dòng 2 trở xuống (Giữ Header dòng 1)
                // Range: "SheetName!A2:Z"
                string clearRange = $"{sheetName}!A2:Z";
                var clearRequest = _sheetsService!.Spreadsheets.Values.Clear(new ClearValuesRequest(), spreadsheetId, clearRange);
                await clearRequest.ExecuteAsync();

                if (tickets == null || tickets.Count == 0)
                {
                    Log.Information("[GOOGLE_SHEETS] Đã xóa trắng Sheet {SheetName} (Hôm nay không có phiếu cân nào).", sheetName);
                    return true;
                }

                // Bước 2: Chuẩn bị dữ liệu ghi đè
                var valueRange = new ValueRange();
                var oblist = new List<IList<object>>();

                int stt = 1;
                foreach (var ticket in tickets)
                {
                    var rowData = new List<object>
                    {
                        stt++,                                          // 1. STT
                        ticket.TicketID,                                // 2. Số Phiếu
                        ticket.LicensePlate,                            // 3. Biển Số
                        ticket.CustomerName ?? "",                      // 4. Khách Hàng
                        ticket.ProductName ?? "",                       // 5. Sản Phẩm
                        ticket.TimeIn.ToString("dd/MM/yyyy"),           // 6. Ngày
                        ticket.TimeIn.ToString("HH:mm:ss"),             // 7. Giờ Vào
                        ticket.TimeOut?.ToString("HH:mm:ss") ?? "",     // 8. Giờ Ra
                        ticket.GrossWeight,                             // 9. Tổng (Kg)
                        ticket.TareWeight,                              // 10. Thân xe (Kg)
                        ticket.NetWeight                                // 11. Hàng (Kg)
                    };
                    oblist.Add(rowData);
                }

                valueRange.Values = oblist;

                // Bước 3: Ghi dữ liệu mới vào từ dòng 2
                string writeRange = $"{sheetName}!A2";
                var appendRequest = _sheetsService.Spreadsheets.Values.Update(valueRange, spreadsheetId, writeRange);
                appendRequest.ValueInputOption = SpreadsheetsResource.ValuesResource.UpdateRequest.ValueInputOptionEnum.USERENTERED;
                
                var appendResponse = await appendRequest.ExecuteAsync();

                Log.Information("[GOOGLE_SHEETS] Đã đồng bộ thành công {Count} phiếu lên Sheet {SheetName}.", tickets.Count, sheetName);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[GOOGLE_SHEETS] Lỗi khi đồng bộ lên Google Sheets.");
                return false;
            }
        }
    }
}
