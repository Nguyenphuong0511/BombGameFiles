    using System;
using System.Data;
using System.Data.SqlClient;

namespace BombGameServer
{
    class DatabaseHelper
    {
        private string connectionString = "Server=ADMIN-PC;Database=BombGame;Trusted_Connection=True;";

        public bool TestConnection()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    Console.WriteLine("✅ Kết nối SQL thành công!");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi kết nối SQL: " + ex.Message);
                return false;
            }
        }

        // Đăng ký người chơi: kiểm tra tồn tại -> insert -> trả về ID mới
        // Return codes:
        //  >0 : new user id (success)
        //  -1 : missing/invalid input
        //  -2 : username exists
        //  -3 : nickname (NhanVat) exists
        public int DangKyNguoiChoi(string username, string password, string character)
        {
            try
            {
                // Basic validation
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password) || string.IsNullOrWhiteSpace(character))
                    return -1;

                username = username.Trim();
                character = character.Trim();

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // 1. Kiểm tra tên đăng nhập đã tồn tại chưa
                    using (SqlCommand checkCmd = new SqlCommand("SELECT COUNT(1) FROM NguoiChoi WHERE TenDangNhap = @TenDangNhap", conn))
                    {
                        checkCmd.Parameters.AddWithValue("@TenDangNhap", username);
                        int exists = Convert.ToInt32(checkCmd.ExecuteScalar());
                        if (exists > 0)
                            return -2; // trùng tên
                    }

                    // 1b. Kiểm tra biệt danh (NhanVat) đã bị dùng chưa
                    using (SqlCommand checkChar = new SqlCommand("SELECT COUNT(1) FROM NguoiChoi WHERE NhanVat = @NhanVat", conn))
                    {
                        checkChar.Parameters.AddWithValue("@NhanVat", character);
                        int charExists = Convert.ToInt32(checkChar.ExecuteScalar());
                        if (charExists > 0)
                            return -3; // nickname exists
                    }

                    // 2. Chèn bản ghi mới (lưu đầy đủ thông tin nhập)
                    string sql = @"INSERT INTO NguoiChoi (TenDangNhap, MatKhau, NhanVat, Diem, MaPhong)
                                   VALUES (@TenDangNhap, @MatKhau, @NhanVat, 0, 0);
                                   SELECT CAST(SCOPE_IDENTITY() AS INT);";

                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@TenDangNhap", username);
                        cmd.Parameters.AddWithValue("@MatKhau", password);
                        cmd.Parameters.AddWithValue("@NhanVat", (object)character ?? DBNull.Value);

                        object result = cmd.ExecuteScalar();
                        int newId = result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;

                        if (newId > 0)
                        {
                            // Lưu nhật ký ngay sau khi đăng ký thành công
                            LuuNhatKyHoatDong(newId, "ĐĂNG KÝ");
                            Console.WriteLine($"[DB]: Đã đăng ký người chơi '{username}' (ID: {newId}, NV: {character})");
                        }

                        return newId;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi khi đăng ký: " + ex.Message);
                return 0;
            }
        }

        // Đăng nhập - trả về MaNguoiChoi (0 nếu thất bại)
        public int DangNhapNguoiChoi(string username, string password)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                    return 0;

                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 MaNguoiChoi FROM NguoiChoi WHERE TenDangNhap = @TenDangNhap AND MatKhau = @MatKhau", conn))
                    {
                        cmd.Parameters.AddWithValue("@TenDangNhap", username);
                        cmd.Parameters.AddWithValue("@MatKhau", password);
                        object result = cmd.ExecuteScalar();
                        if (result == null || result == DBNull.Value) return 0;
                        return Convert.ToInt32(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi đăng nhập: " + ex.Message);
                return 0;
            }
        }

        // Cập nhật thời gian đăng nhập vào cột Thoigiandangnhap nếu có, ngược lại ghi vào nhật ký
        public void CapNhatThoiGianDangNhap(int playerId, DateTime loginTime)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();

                    // Thử cập nhật cột Thoigiandangnhap
                    using (SqlCommand cmd = new SqlCommand("UPDATE NguoiChoi SET Thoigiandangnhap = @LoginTime WHERE MaNguoiChoi = @Id", conn))
                    {
                        cmd.Parameters.AddWithValue("@LoginTime", loginTime);
                        cmd.Parameters.AddWithValue("@Id", playerId);
                        int rows = cmd.ExecuteNonQuery();
                        if (rows > 0)
                        {
                            Console.WriteLine($"[DB]: Cập nhật Thoigiandangnhap cho Player {playerId} = {loginTime:yyyy-MM-dd HH:mm:ss}");
                            return;
                        }
                    }

                    // Nếu không có cột hoặc update không ảnh hưởng -> fallback ghi vào nhật ký
                    LuuNhatKyHoatDong(playerId, $"ĐĂNG NHẬP @ {loginTime:yyyy-MM-dd HH:mm:ss}", null, null);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi khi cập nhật thời gian đăng nhập: " + ex.Message);
                try { LuuNhatKyHoatDong(playerId, $"ĐĂNG NHẬP @ {loginTime:yyyy-MM-dd HH:mm:ss}", null, null); } catch { }
            }
        }

        // New: read stored last-login from DB (nullable)
        public DateTime? LayThoiGianDangNhap(int playerId)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 Thoigiandangnhap FROM NguoiChoi WHERE MaNguoiChoi = @Id", conn))
                    {
                        cmd.Parameters.AddWithValue("@Id", playerId);
                        object result = cmd.ExecuteScalar();
                        if (result == null || result == DBNull.Value) return null;
                        return Convert.ToDateTime(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi LayThoiGianDangNhap: " + ex.Message);
                return null;
            }
        }

        // Tạo phòng và trả về mã phòng (MaPhong) mới tạo từ SQL
        public int TaoPhongChoi(string roomName, int chuPhongId)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    // Nếu có stored procedure, dùng; nếu không có, dùng INSERT giả định.
                    // Cố gắng dùng sp_TaoPhongChoi nếu tồn tại
                    try
                    {
                        using (SqlCommand cmd = new SqlCommand("sp_TaoPhongChoi", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@TenPhong", roomName);
                            object result = cmd.ExecuteScalar();
                            int maPhongMoi = result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
                            if (maPhongMoi > 0)
                            {
                                Console.WriteLine($"✅ [DB]: Đã tạo phòng '{roomName}' với mã ID: {maPhongMoi}");
                                LuuNhatKyHoatDong(chuPhongId, "TẠO PHÒNG", maPhongMoi, "0,0,0");
                            }
                            return maPhongMoi;
                        }
                    }
                    catch
                    {
                        // Fallback: tạo phòng đơn giản
                        string sql = @"INSERT INTO PhongCho (TenPhong, NgayTao) VALUES (@TenPhong, GETDATE());
                                       SELECT CAST(SCOPE_IDENTITY() AS INT);";
                        using (SqlCommand cmd = new SqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@TenPhong", roomName);
                            object result = cmd.ExecuteScalar();
                            int maPhongMoi = result != null && result != DBNull.Value ? Convert.ToInt32(result) : 0;
                            if (maPhongMoi > 0)
                            {
                                Console.WriteLine($"✅ [DB]: Đã tạo phòng '{roomName}' (fallback) với mã ID: {maPhongMoi}");
                                LuuNhatKyHoatDong(chuPhongId, "TẠO PHÒNG", maPhongMoi, "0,0,0");
                            }
                            return maPhongMoi;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ [LỖI DB]: Không thể tạo phòng: " + ex.Message);
                return 0;
            }
        }

        // Tham gia phòng
        public int ThamGiaPhongChoi(int maPhong, int maNguoiChoi)
        {
            try
            {
                // Gọi stored procedure nếu có, fallback cập nhật NguoiChoi.MaPhong
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    try
                    {
                        using (SqlCommand cmd = new SqlCommand("sp_ThamGiaPhongChoi", conn))
                        {
                            cmd.CommandType = CommandType.StoredProcedure;
                            cmd.Parameters.AddWithValue("@MaPhong", maPhong);
                            cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);
                            object result = cmd.ExecuteScalar();
                            return Convert.ToInt32(result);
                        }
                    }
                    catch
                    {
                        string sql = "UPDATE NguoiChoi SET MaPhong = @MaPhong WHERE MaNguoiChoi = @MaNguoiChoi";
                        using (SqlCommand cmd = new SqlCommand(sql, conn))
                        {
                            cmd.Parameters.AddWithValue("@MaPhong", maPhong);
                            cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);
                            int rows = cmd.ExecuteNonQuery();
                            return rows > 0 ? 1 : 0;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi khi tham gia phòng: " + ex.Message);
                return 0;
            }
        }

        // Ghi nhật ký hoạt động
        public void LuuNhatKyHoatDong(int playerId, string action, int? roomId = null, string pos = null)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_LuuNhatKyHoatDong", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaNguoiChoi", playerId);
                        cmd.Parameters.AddWithValue("@HanhDong", action);
                        cmd.Parameters.AddWithValue("@MaPhong", (object)roomId ?? DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToaDo", (object)pos ?? "0,0,0");
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi lưu nhật ký: " + ex.Message);
            }
        }

        // Lưu thông tin truyền bom
        public void LuuLichSuTruyenBom(int fromId, int toId, int maTran, int khoangCach)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_LuuLichSuTruyenBom", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaNguoiChoiFrom", fromId);
                        cmd.Parameters.AddWithValue("@MaNguoiChoiTo", toId);
                        cmd.Parameters.AddWithValue("@MaTranDau", maTran);
                        cmd.Parameters.AddWithValue("@KhoangCachTruyenBom", khoangCach);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine("Loi luu lich su: " + ex.Message); }
        }

        // Lưu kết quả trận đấu
        public void LuuKetQuaTranDau(int maNguoiChoi, string ketQua, int maPhong)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = "INSERT INTO KetQua (MaNguoiChoi, KetQua, MaPhong, ThoiGian) VALUES (@userId, @result, @roomId, GETDATE())";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@userId", maNguoiChoi);
                    cmd.Parameters.AddWithValue("@result", ketQua);
                    cmd.Parameters.AddWithValue("@roomId", maPhong);
                    cmd.ExecuteNonQuery();
                    Console.WriteLine($"[DB]: Đã lưu kết quả {ketQua} cho Player {maNguoiChoi}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("Lỗi lưu kết quả: " + ex.Message);
            }
        }

        // Lấy điểm hiện tại của người chơi từ bảng NguoiChoi (cột Diem)
        public int LayDiemNguoiChoi(int playerId)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 Diem FROM NguoiChoi WHERE MaNguoiChoi = @MaNguoiChoi", conn))
                {
                    cmd.Parameters.AddWithValue("@MaNguoiChoi", playerId);
                    object result = cmd.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        return 0;
                    return Convert.ToInt32(result);
                }
            }
        }

        // Lấy mã phòng hiện tại của người chơi từ bảng NguoiChoi (cột MaPhong)
        public int LayPhongNguoiChoi(int playerId)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 MaPhong FROM NguoiChoi WHERE MaNguoiChoi = @MaNguoiChoi", conn))
                {
                    cmd.Parameters.AddWithValue("@MaNguoiChoi", playerId);
                    object result = cmd.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        return 0;
                    return Convert.ToInt32(result);
                }
            }
        }

        // Lấy mã người chơi theo tên đăng nhập (phục vụ đăng nhập)
        public int LayMaNguoiChoi(string username)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 MaNguoiChoi FROM NguoiChoi WHERE TenDangNhap = @TenDangNhap", conn))
                {
                    cmd.Parameters.AddWithValue("@TenDangNhap", username);
                    object result = cmd.ExecuteScalar();
                    if (result == null || result == DBNull.Value)
                        return 0;
                    return Convert.ToInt32(result);
                }
            }
        }

        // New: reset password by user id (returns true if updated)
        public bool DatLaiMatKhau(int maNguoiChoi, string newPassword)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_DatLaiMatKhau", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);
                        cmd.Parameters.AddWithValue("@MatKhau", newPassword);
                        object result = cmd.ExecuteScalar();
                        return result != null && Convert.ToInt32(result) == 1;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi khi đặt lại mật khẩu: " + ex.Message);
                return false;
            }
        }

        public int TaoTranDau(int maPhong, int maNguoiChoi, string ketQua)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_TaoTranDau", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaPhong", maPhong);
                        cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);
                        cmd.Parameters.AddWithValue("@KetQua", ketQua);

                        object result = cmd.ExecuteScalar();
                        return Convert.ToInt32(result); // trả về mã trận đấu mới
                    }
                }
            }
            catch
            {
                return 0; // lỗi thì trả về 0
            }
        }
        public int LayTranDauTheoPhong(int maPhong)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("SELECT TOP 1 MaTranDau FROM TranDau WHERE MaPhong=@MaPhong ORDER BY NgayChoi DESC", conn))
                    {
                        cmd.Parameters.AddWithValue("@MaPhong", maPhong);
                        object result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToInt32(result) : 0;
                    }
                }
            }
            catch
            {
                return 0;
            }
        }
        public void XoaPhongVaKetQua(int maPhong)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    // Xóa phòng và các bản ghi tạm thời của trận đấu đó
                    string sql = "DELETE FROM PhongCho WHERE MaPhong = @id; DELETE FROM TranDau WHERE MaPhong = @id";
                    SqlCommand cmd = new SqlCommand(sql, conn);
                    cmd.Parameters.AddWithValue("@id", maPhong);
                    cmd.ExecuteNonQuery();
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi khi xóa phòng: " + ex.Message);
            }
        }
    }
}
