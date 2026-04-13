using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Services
{
    /// <summary>
    /// Lớp quản lý Phiên làm việc (Lưu thông tin nhân viên đang đăng nhập)
    /// </summary>
    public class AppSession
    {
        // Khởi tạo mặc định là 1 (Admin) để bạn test. 
        // Sau này khi có màn hình Login, bạn sẽ gán lại ID thực tế vào đây.
        public int CurrentUserId { get; set; } = 1;

        public string CurrentUserName { get; set; } = "Administrator";

        public string Role { get; set; } = "Admin";

        public DateTime LoginTime { get; set; } = DateTime.Now;

        // Hàm này sẽ được gọi ở Màn hình Đăng nhập (Login Window)
        public void SetUser(int userId, string userName, string role)
        {
            CurrentUserId = userId;
            CurrentUserName = userName;
            Role = role;
            LoginTime = DateTime.Now;
        }

        public void Logout()
        {
            CurrentUserId = 0;
            CurrentUserName = string.Empty;
            Role = string.Empty;
        }
    }
}
