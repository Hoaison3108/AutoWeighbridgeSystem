-- KỊCH BẢN THÊM DỮ LIỆU MẪU (SEED DATA)
-- CHÚ Ý: Hãy đổi [YourDatabaseName] thành tên Database thực tế của bạn
USE [AutoWeighbridgeDB];
GO

-- ==========================================
-- 0. XÓA DỮ LIỆU CŨ (NẾU CÓ) ĐỂ TRÁNH LỖI TRÙNG LẶP
-- Phải xóa theo thứ tự: Bảng con (có khóa ngoại) xóa trước, bảng cha xóa sau
-- ==========================================
DELETE FROM [WeighingTickets];
DELETE FROM [Vehicles];
DELETE FROM [Customers];
DELETE FROM [Products];

-- Reset lại các cột ID tự động tăng (IDENTITY) về 0 để số thứ tự đẹp lại từ đầu
DBCC CHECKIDENT ('[Vehicles]', RESEED, 0);
DBCC CHECKIDENT ('[WeighingTickets]', RESEED, 0);
GO

-- ==========================================
-- 1. THÊM KHÁCH HÀNG (Bảng Customers)
-- Note: CustomerId là chuỗi (tối đa 8 ký tự), bạn phải tự định nghĩa (Không tự tăng)
-- ==========================================
INSERT INTO [Customers] ([CustomerId], [CustomerName], [IsDeleted])
VALUES 
('KH000001', N'Công ty TNHH Vật Liệu Xây Dựng Số 1', 0),
('KH000002', N'Tập đoàn Rạng Đông', 0),
('KH000003', N'Công ty Vận tải ABC', 0),
('KH000004', N'Khách lẻ vãng lai', 0);
GO

-- ==========================================
-- 2. THÊM SẢN PHẨM (Bảng Products)
-- Note: ProductId là chuỗi (tối đa 8 ký tự), bạn phải tự định nghĩa (Không tự tăng)
-- ==========================================
INSERT INTO [Products] ([ProductId], [ProductName], [IsDeleted])
VALUES 
('SP000001', N'Đá xanh biên hòa (1x2)', 0),
('SP000002', N'Cát xây tô', 0),
('SP000003', N'Đá mi bụi', 0),
('SP000004', N'Xỉ than', 0);
GO

-- ==========================================
-- 3. THÊM XE (Bảng Vehicles)
-- Note: VehicleId là khóa chính tự động tăng (IDENTITY) => Không cần chèn.
-- Yêu cầu: CustomerId mượn từ bảng KHÁCH HÀNG bên trên phải tồn tại
-- ==========================================
INSERT INTO [Vehicles] ([LicensePlate], [RfidCardId], [TareWeight], [CustomerId], [IsDeleted])
VALUES 
-- Xe 1: Có gắn mã thẻ RFID
('86C-123.45', 'A1B2C3D4_CARD1', 12050.00, 'KH000001', 0),
-- Xe 2: Xe chưa gắn thẻ (NULL)
('51D-987.65', NULL, 15000.00, 'KH000002', 0),
-- Xe 3: Có gắn mã thẻ RFID
('29C-444.44', 'E5F6G7H8_CARD2', 13200.00, 'KH000003', 0);
GO

-- =========================================================================
-- PHẦN THÊM: CÁC LỆNH SQL CƠ BẢN THƯỜNG DÙNG ĐỂ THAO TÁC / KIỂM TRA DỮ LIỆU
-- (Kéo thả chuột bôi đen đoạn cần chạy rồi bấm F5 / Execute)
-- =========================================================================

-- 1. LỆNH TRUY VẤN CƠ BẢN (SELECT)
-- Lấy toàn bộ danh sách Khách hàng
-- SELECT * FROM [Customers];

-- Lấy danh sách Xe và chỉ hiển thị Biển số, Trọng lượng bì
-- SELECT [LicensePlate], [TareWeight] FROM [Vehicles];

-- 2. TRUY VẤN MỞ RỘNG (WHERE, ORDER BY)
-- Tìm khách hàng có tên chứa chữ 'Rạng Đông'
-- SELECT * FROM [Customers] WHERE [CustomerName] LIKE N'%Rạng Đông%';

-- Lấy danh sách phiếu cân sắp xếp mới nhất lên đầu
-- SELECT * FROM [WeighingTickets] ORDER BY [TimeIn] DESC;

-- 3. TRUY VẤN CÓ KẾT NỐI BẢNG (JOIN)
-- Xem danh sách Xe hiển thị kèm theo Tên Khách Hàng thay vì chỉ hiển thị mã CustomerId
-- SELECT v.VehicleId, v.LicensePlate, v.TareWeight, c.CustomerName 
-- FROM [Vehicles] v
-- INNER JOIN [Customers] c ON v.CustomerId = c.CustomerId;

-- 4. LỆNH CẬP NHẬT DỮ LIỆU (UPDATE)
-- (CẢNH BÁO: Luôn phải dùng mệnh đề WHERE nếu không sẽ cập nhật toàn bộ bảng)
-- Đổi tên MÃ SẢN PHẨM 'SP000001' thành 'Đá loại 1'
-- UPDATE [Products] 
-- SET [ProductName] = N'Đá loại 1' 
-- WHERE [ProductId] = 'SP000001';

-- Cập nhật trọng lượng bì cho xe '86C-123.45' lên 12500 kg
-- UPDATE [Vehicles] 
-- SET [TareWeight] = 12500.00 
-- WHERE [LicensePlate] = '86C-123.45';

-- 5. LỆNH XÓA DỮ LIỆU - XÓA CỨNG (DELETE)
-- (CẢNH BÁO: Mất dữ liệu vĩnh viễn, luôn nhớ kèm WHERE)
-- Xóa khách hàng bị tạo nhầm 'KH000004'
-- DELETE FROM [Customers] WHERE [CustomerId] = 'KH000004';

-- DELETE FROM [Vehicles] 
-- WHERE [VehicleId] IN ('1','6','7','8','11')

-- DELETE FROM [WeighingTickets] 
-- WHERE [VehicleId] IN ('1','8','11','7'); xóa nhiều cùng lúc

-- 6. LỆNH XÓA DỮ LIỆU - XÓA MỀM (SOFT DELETE)
-- Thay vì xóa mất tích, chúng ta chỉ đánh dấu trạng thái IsDeleted = 1 (Ẩn đi trên phần mềm)
-- UPDATE [Vehicles] SET [IsDeleted] = 1 WHERE [LicensePlate] = '51D-987.65';
