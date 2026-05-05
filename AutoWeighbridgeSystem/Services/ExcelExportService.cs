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
                FileName = $"BaoCao_ChiTiet_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx",
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

                        // --- 1. PHẦN HEADER & BRANDING (Mở rộng Range đến cột K) ---
                        worksheet.Cell("A1").Value = "CÔNG TY KHOÁNG SẢN RẠNG ĐÔNG";
                        worksheet.Cell("A1").Style.Font.SetBold().Font.SetFontSize(14);
                        worksheet.Range("A1:K1").Merge();

                        var titleCell = worksheet.Cell("A2");
                        titleCell.Value = reportTitle.ToUpper();
                        titleCell.Style.Font.SetBold().Font.SetFontSize(16).Font.SetFontColor(XLColor.FromHtml("#2196F3"));
                        titleCell.Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Center;
                        worksheet.Range("A2:K2").Merge();

                        worksheet.Cell("A3").Value = $"Ngày xuất: {DateTime.Now:dd/MM/yyyy HH:mm:ss}";
                        worksheet.Range("A3:K3").Merge().Style.Alignment.SetHorizontal(XLAlignmentHorizontalValues.Center);

                        // --- 2. ĐỊNH NGHĨA TIÊU ĐỀ CỘT (Bổ sung cột Ngày) ---
                        string[] headers = {
                            "STT", "Số Phiếu", "Biển Số", "Khách Hàng", "Sản Phẩm",
                            "Ngày", "Giờ Vào", "Giờ Ra", "Tổng (Kg)", "Thân xe(kg)", "Hàng (Kg)"
                        };

                        for (int i = 0; i < headers.Length; i++)
                        {
                            var cell = worksheet.Cell(4, i + 1);
                            cell.Value = headers[i];
                            cell.Style.Fill.BackgroundColor = XLColor.FromHtml("#2196F3");
                            cell.Style.Font.FontColor = XLColor.Red; // màu tiêu đề quay lại màu đỏ theo yêu cầu
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
                            worksheet.Cell(currentRow, 6).Value = t.TimeIn;       // Cột Ngày mới
                            worksheet.Cell(currentRow, 7).Value = t.TimeIn;       // Giờ Vào
                            worksheet.Cell(currentRow, 8).Value = t.TimeOut;      // Giờ Ra
                            worksheet.Cell(currentRow, 9).Value = t.GrossWeight;  // Tổng
                            worksheet.Cell(currentRow, 10).Value = t.TareWeight;  // Thân xe
                            worksheet.Cell(currentRow, 11).Value = t.NetWeight;   // Hàng

                            // Nghiệp vụ: Phiếu hủy
                            if (t.IsVoid)
                            {
                                var range = worksheet.Range(currentRow, 1, currentRow, 11);
                                range.Style.Font.Strikethrough = true;
                                range.Style.Font.FontColor = XLColor.Red;
                            }

                            // Định dạng số và ngày tháng
                            worksheet.Cell(currentRow, 6).Style.DateFormat.Format = "dd/MM/yyyy";
                            worksheet.Cell(currentRow, 7).Style.DateFormat.Format = "HH:mm:ss";
                            worksheet.Cell(currentRow, 8).Style.DateFormat.Format = "HH:mm:ss";
                            worksheet.Cell(currentRow, 9).Style.NumberFormat.Format = "#,##0";
                            worksheet.Cell(currentRow, 10).Style.NumberFormat.Format = "#,##0";
                            worksheet.Cell(currentRow, 11).Style.NumberFormat.Format = "#,##0";

                            worksheet.Range(currentRow, 1, currentRow, 11).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;
                            currentRow++;
                        }

                        // --- 4. DÒNG TỔNG CỘNG (Mở rộng cho 3 cột khối lượng: I, J, K) ---
                        int lastDataRow = currentRow - 1;
                        worksheet.Cell(currentRow, 8).Value = "TỔNG CỘNG:";
                        worksheet.Cell(currentRow, 8).Style.Font.Bold = true;
                        worksheet.Cell(currentRow, 8).Style.Alignment.Horizontal = XLAlignmentHorizontalValues.Right;

                        // Công thức SUM cho cột I (Tổng), J (Thân), K (Hàng)
                        worksheet.Cell(currentRow, 9).FormulaA1 = $"SUM(I{startRow}:I{lastDataRow})";
                        worksheet.Cell(currentRow, 10).FormulaA1 = $"SUM(J{startRow}:J{lastDataRow})";
                        worksheet.Cell(currentRow, 11).FormulaA1 = $"SUM(K{startRow}:K{lastDataRow})";

                        var summaryRange = worksheet.Range(currentRow, 9, currentRow, 11);
                        summaryRange.Style.Font.Bold = true;
                        summaryRange.Style.NumberFormat.Format = "#,##0";
                        summaryRange.Style.Fill.BackgroundColor = XLColor.FromHtml("#E3F2FD");
                        worksheet.Range(currentRow, 1, currentRow, 11).Style.Border.OutsideBorder = XLBorderStyleValues.Thin;

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