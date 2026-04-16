using System;
using System.Threading.Tasks;

namespace AutoWeighbridgeSystem.Common
{
    /// <summary>
    /// Extension methods tiện ích cho <see cref="Task"/>.
    /// Cung cấp cách chạy task theo kiểu fire-and-forget có bảo vệ exception,
    /// thay thế cho pattern <c>_ = SomeTask()</c> vốn nuốt lỗi im lặng.
    /// </summary>
    public static class TaskExtensions
    {
        /// <summary>
        /// Chạy một <see cref="Task"/> theo kiểu fire-and-forget nhưng vẫn xử lý exception
        /// thông qua callback <paramref name="onError"/>, thay vì để chúng bị mất hoàn toàn.
        /// <para>
        /// <b>Dùng khi nào:</b> Cần khởi động task ngay mà không cần <c>await</c>,
        /// nhưng vẫn muốn biết nếu có lỗi xảy ra (ví dụ: load dữ liệu lúc khởi động).
        /// </para>
        /// <b>Ví dụ sử dụng:</b>
        /// <code>
        /// LoadDataAsync().FireAndForgetSafe(ex => Log.Error(ex, "Lỗi tải dữ liệu"));
        /// </code>
        /// </summary>
        /// <param name="task">Task cần chạy nền.</param>
        /// <param name="onError">Callback xử lý khi Task throw exception (tùy chọn).</param>
        public static async void FireAndForgetSafe(this Task task, Action<Exception> onError = null)
        {
            try
            {
                await task;
            }
            catch (Exception ex)
            {
                onError?.Invoke(ex);
            }
        }
    }
}
