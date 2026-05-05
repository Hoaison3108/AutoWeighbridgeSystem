using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Models
{
    /// <summary>
    /// Định nghĩa các vai trò (vị trí) của đầu đọc thẻ RFID trong hệ thống
    /// </summary>
    public static class ReaderRoles
    {
        public const string ScaleIn = "ScaleIn";
        public const string ScaleOut = "ScaleOut";
        public const string Desk = "Desk";
        public const string ManualInput = "ManualInput"; // Dùng cho nhập tay
    }
}
