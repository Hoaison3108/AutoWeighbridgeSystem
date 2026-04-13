using ClosedXML.Excel;
using Microsoft.Win32;
using AutoWeighbridgeSystem.Models;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;

namespace AutoWeighbridgeSystem.Services
{
    public class ExcelExportService : IExportService
    {
        public async Task ExportTicketsToExcelAsync(IEnumerable<WeighingTicket> tickets, string reportTitle)
        {
            var saveFileDialog = new SaveFileDialog
            {
                Filter = "Excel Workbook (*.xlsx)|*.xlsx",
                FileName = $"BaoCao_ChiTiet_{DateTime.Now:yyyyMMdd_HHmm}.xlsx",
                Title = "Lưu báo cáo sản lượng trạm cân"
            };

            if (saveFileDialog.ShowDialog() != true) return;

            try
            {
                await Task.Run(() =>
                {
                    using (var workbook = new XLWorkbook())
                    {
                        var worksheet = workbook.Worksheets.Add("DuLieuCan");

                        // --- 1. PHẦN HEADER & BRANDING (Mở rộng Range đến cột J) ---
                        worksheet.Cell("A1").Value = "CÔNG TY KHOÁNG SẢN RẠNG ĐÔNG";
                        worksheet.Cell("A1").Style.Font.SetBold().Font.SetFontSize(14);
                        worksheet.Range("A1:J1").Merge(); // Mở rộng đến J

                        var titleCell = worksheet.Cell("A2");
                        titleCell.Value = reportTitle.ToUpper();
                        titleCell.Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.FromHtml("#2196F3"));
                        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Range("A2:J2").Merge(); // Mở rộng đến J

                        worksheet.Cell("A3").Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm}";
                        worksheet.Range("A3:J3").Merge().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                        // --- 2. ĐỊNH NGHĨA TIÊU ĐỀ CỘT (Bổ sung Thân xe) ---
                        string[] headers = {
                            "STT", "Số Phiếu", "Biển Số", "Khách Hàng", "Sản Phẩm",
                            "Giờ Vào", "Giờ Ra", "Tổng (Kg)", "Thân xe(kg)", "Hàng (Kg)"
                        };

                        for (int i = 0; i < headers.Length; i++)
                        {
                            var cell = worksheet.Cell(4, i + 1);
                            cell.Value = headers[i];
                            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2196F3");
                            cell.Style.Font.FontColor = XLColor.Red; // màu tiêu đề
                            cell.Style.Font.Bold = true;
                            cell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                            cell.Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                        }

                        // --- 3. ĐỔ DỮ LIỆU ---
                        int startRow = 5;
                        int currentRow = startRow;
                        int stt = 1;

                        foreach (var t in tickets)
                        {
                            worksheet.Cell(currentRow, 1).Value = stt++;
                            worksheet.Cell(currentRow, 2).Value = t.TicketID;
                            worksheet.Cell(currentRow, 3).Value = t.LicensePlate;
                            worksheet.Cell(currentRow, 4).Value = t.CustomerName;
                            worksheet.Cell(currentRow, 5).Value = t.ProductName;
                            worksheet.Cell(currentRow, 6).Value = t.TimeIn;
                            worksheet.Cell(currentRow, 7).Value = t.TimeOut;
                            worksheet.Cell(currentRow, 8).Value = t.GrossWeight;
                            worksheet.Cell(currentRow, 9).Value = t.TareWeight; // Cột mới bổ sung
                            worksheet.Cell(currentRow, 10).Value = t.NetWeight;

                            // Nghiệp vụ: Phiếu hủy
                            if (t.IsVoid)
                            {
                                var range = worksheet.Range(currentRow, 1, currentRow, 10);
                                range.Style.Font.Strikethrough = true;
                                range.Style.Font.FontColor = XLColor.Red;
                            }

                            // Định dạng số và ngày tháng cho 10 cột
                            worksheet.Cell(currentRow, 6).Style.DateFormat.Format = "dd/MM/yy HH:mm";
                            worksheet.Cell(currentRow, 7).Style.DateFormat.Format = "dd/MM/yy HH:mm";
                            worksheet.Cell(currentRow, 8).Style.NumberFormat.Format = "#,##0";
                            worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "#,##0";
                            worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "#,##0";

                            worksheet.Range(currentRow, 1, currentRow, 10).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            currentRow++;
                        }

                        // --- 4. DÒNG TỔNG CỘNG (Mở rộng cho 3 cột khối lượng) ---
                        int lastDataRow = currentRow - 1;
                        worksheet.Cell(currentRow, 7).Value = "TỔNG CỘNG:";
                        worksheet.Cell(currentRow, 7).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, 7).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        // Công thức SUM cho cột H (Tổng), I (Thân), J (Hàng)
                        worksheet.Cell(currentRow, 8).FormulaA1 = $"SUM(H{startRow}:H{lastDataRow})";
                        worksheet.Cell(currentRow, 9).FormulaA1 = $"SUM(I{startRow}:I{lastDataRow})";
                        worksheet.Cell(currentRow, 10).FormulaA1 = $"SUM(J{startRow}:J{lastDataRow})";

                        var summaryRange = worksheet.Range(currentRow, 8, currentRow, 10);
                        summaryRange.Style.Font.Bold = true;
                        summaryRange.Style.NumberFormat.Format = "#,##0";
                        summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E3F2FD");
                        worksheet.Range(currentRow, 1, currentRow, 10).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

                        worksheet.Columns().AdjustToContents();
                        workbook.SaveAs(saveFileDialog.FileName);
                    }
                });

                if (MessageBox.Show("Xuất báo cáo thành công! Bạn có muốn mở file ngay không?",
                                   "Thành công", MessageBoxButton.YesNo, MessageBoxImage.Information) == MessageBoxResult.Yes)
                {
                    Process.Start(new ProcessStartInfo(saveFileDialog.FileName) { UseShellExecute = true });
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Lỗi khi xuất file Excel: {ex.Message}", "Lỗi", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
    }
}