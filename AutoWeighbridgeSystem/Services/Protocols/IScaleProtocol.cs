using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services.Protocols
{
    public interface IScaleProtocol
    {
        /// <summary>Tên định danh của chuẩn (vd: VishayVT220, Toledo, Mettler).</summary>
        string ProtocolName { get; }

        /// <summary>
        /// Phân tích dữ liệu thô từ Serial Port.
        /// Giữ lại để tương thích ngược — dùng khi cần parse một cụm dữ liệu đơn lẻ.
        /// </summary>
        (decimal Weight, bool IsHardwareStable)? ParseWeight(string rawData);

        /// <summary>
        /// Tìm và trích xuất một frame hợp lệ từ buffer tich lũy.
        /// Trả về dữ liệu cân và phần dư chưa xử lý, hoặc <c>null</c> nếu chưa có frame đầy đủ.
        /// Đưa việc quản lý buffer và cắt frame ra khỏi <see cref="ScaleService"/> —
        /// mỗi protocol tự quyết định cách tìm và cắt.
        /// </summary>
        /// <param name="buffer">Chuỗi dữ liệu tich lũy từ Serial Port (chưa được xử lý).</param>
        /// <returns>
        /// Tuple gồm:
        /// <list type="bullet">
        /// <item><c>Weight</c> — trọng lượng trích xuất được (kg).</item>
        /// <item><c>IsHardwareStable</c> — phần cứng báo ổn định hay không.</item>
        /// <item><c>Remainder</c> — phần dư chưa xử lý, giữ lại cho lần append tiếp theo.</item>
        /// </list>
        /// Trả về <c>null</c> nếu buffer chưa chứa đủ một frame hoàn chỉnh.
        /// </returns>
        (decimal Weight, bool IsHardwareStable, string Remainder)? TryExtractFrame(string buffer);
    }
}
