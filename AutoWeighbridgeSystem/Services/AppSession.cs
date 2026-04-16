using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Lưu trữ thông tin phiên làm việc (session) của người dùng đang đăng nhập.
    /// Được đăng ký là Singleton để có thể truy cập từ bất kỳ ViewModel nào trong ứng dụng.
    /// </summary>
    public class AppSession
    {
        /// <summary>ID của người dùng đang đăng nhập.</summary>
        public int CurrentUserId { get; set; } = 1;

        /// <summary>Tên hiển thị của người dùng đang đăng nhập.</summary>
        public string CurrentUserName { get; set; } = "Administrator";

        /// <summary>Vai trò của người dùng (Admin, Operator, ...).</summary>
        public string Role { get; set; } = "Admin";

        /// <summary>Thời điểm đăng nhập vào hệ thống.</summary>
        public DateTime LoginTime { get; set; } = DateTime.Now;

        /// <summary>
        /// Cập nhật thông tin phiên sau khi đăng nhập thành công.
        /// Được gọi từ <c>LoginViewModel</c> sau khi xác thực người dùng.
        /// </summary>
        /// <param name="userId">ID người dùng trong database.</param>
        /// <param name="userName">Tên hiển thị.</param>
        /// <param name="role">Vai trò (Admin / Operator).</param>
        public void SetUser(int userId, string userName, string role)
        {
            CurrentUserId   = userId;
            CurrentUserName = userName;
            Role            = role;
            LoginTime       = DateTime.Now;
        }

        /// <summary>
        /// Xóa thông tin phiên, đặt lại về trạng thái chưa đăng nhập.
        /// Được gọi khi người dùng bấm Đăng xuất.
        /// </summary>
        public void Logout()
        {
            CurrentUserId   = 0;
            CurrentUserName = string.Empty;
            Role            = string.Empty;
        }
    }
}
