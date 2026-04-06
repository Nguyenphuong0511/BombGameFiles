using System;
using System.Data;
using System.Data.SqlClient;

namespace BombGameServer
{
    public class DatabaseHelper
    {
        private readonly string connectionString =
    @"Server=ADMIN-PC;Database=BombGame;Trusted_Connection=True;TrustServerCertificate=True;";

        public void TestConnection()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    Console.WriteLine("[SQL]: Kết nối thành công!");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: " + ex.Message);
            }
        }

        public int DangKyNguoiChoi(string username, string password)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_DangKy", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        // Đảm bảo tên tham số @ khớp chính xác với trong SQL
                        cmd.Parameters.AddWithValue("@TenDangNhap", username.Trim());
                        cmd.Parameters.AddWithValue("@MatKhau", password.Trim());

                        object result = cmd.ExecuteScalar();
                        if (result != null)
                        {
                            return Convert.ToInt32(result);
                        }
                        return 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ [SQL ERROR]: " + ex.Message);
                return -2; // Lỗi kết nối hoặc không tìm thấy SP
            }
        }

        // Đăng nhập người chơi (TenDangNhap + MatKhau trong bảng NguoiChoi)
        public int DangNhapNguoiChoi(string user, string pass)
        {
            try
            {
                user = (user ?? "").Trim();
                pass = (pass ?? "").Trim();
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = "SELECT MaNguoiChoi FROM NguoiChoi WHERE TenDangNhap = @user AND MatKhau = @pass";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@user", user);
                        cmd.Parameters.AddWithValue("@pass", pass);

                        var result = cmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                            return Convert.ToInt32(result);
                        return -1;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR] DangNhapNguoiChoi: " + ex.Message);
                return -1;
            }
        }

        // Quên mật khẩu (reset)
        public void QuenMatKhau(string username, string newPassword)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_QuenMatKhau", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@TenDangNhap", username.Trim());
                        cmd.Parameters.AddWithValue("@MatKhauMoi", newPassword.Trim());
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: " + ex.Message);
            }
        }
        public string LayMatKhau(string tenDangNhap)
        {
            // Giả sử bạn có bảng NgườiChoi với cột TenDangNhap và MatKhau
            using (var conn = new SqlConnection(connectionString))
            {
                conn.Open();
                using (var cmd = new SqlCommand("SELECT MatKhau FROM NguoiChoi WHERE TenDangNhap = @ten", conn))
                {
                    cmd.Parameters.AddWithValue("@ten", tenDangNhap.Trim());
                    var result = cmd.ExecuteScalar();
                    return result != null ? result.ToString() : null;
                }
            }
        }
        public bool CapNhatNickname(int maID, string nickname)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    // Cập nhật cột Nickname dựa trên MaNguoiChoi
                    string sql = "UPDATE NguoiChoi SET Nickname = @name WHERE MaNguoiChoi = @id";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@name", nickname);
                        cmd.Parameters.AddWithValue("@id", maID);
                        return cmd.ExecuteNonQuery() > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: Lỗi cập nhật nickname: " + ex.Message);
                return false;
            }
        }
        public int LuuPhongVaoSQL(string tenPhong, int maChuPhong, int soNguoiToiDa, string roomCode)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = @"INSERT INTO PhongChoi (TenPhong, MaChuPhong, TrangThai, SoNguoiHienTai, SoNguoiToiThieu, SoNguoiToiDa, MaCode)
                       OUTPUT INSERTED.MaPhong
                       VALUES (@ten, @chu, N'Đang chờ', 1, 4, @max, @code)";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@ten", tenPhong);
                    cmd.Parameters.AddWithValue("@chu", maChuPhong);
                    cmd.Parameters.AddWithValue("@max", soNguoiToiDa);
                    cmd.Parameters.AddWithValue("@code", roomCode);

                    var result = cmd.ExecuteScalar();
                    return result != null ? Convert.ToInt32(result) : 0;
                }
            }
        }
        public void CapNhatNguoiChoi(int maNguoiChoi, int maPhong, string nickname, bool laChuPhong)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string sql = @"UPDATE NguoiChoi 
                       SET MaPhong = @phong, Nickname = @nick, LaChuPhong = @host 
                       WHERE MaNguoiChoi = @id";
                using (SqlCommand cmd = new SqlCommand(sql, conn))
                {
                    cmd.Parameters.AddWithValue("@phong", maPhong);
                    cmd.Parameters.AddWithValue("@nick", nickname);
                    cmd.Parameters.AddWithValue("@host", laChuPhong ? 1 : 0);
                    cmd.Parameters.AddWithValue("@id", maNguoiChoi);
                    cmd.ExecuteNonQuery();
                }
            }
        }

        // Tham gia phòng chơi
        public int ThamGiaPhongChoi(int maPhong, int maNguoiChoi)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_ThamGiaPhong", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaPhong", maPhong);
                        cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);

                        var result = cmd.ExecuteScalar();
                        return Convert.ToInt32(result);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: " + ex.Message);
                return 0;
            }
        }

        // Chọn nhân vật
        public void ChonNhanVat(int maNguoiChoi, int nhanVat)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_ChonNhanVat", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaNguoi", maNguoiChoi);
                        cmd.Parameters.AddWithValue("@NhanVat", nhanVat);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: " + ex.Message);
            }
        }



        // Lưu nhật ký hoạt động (SP; fallback INSERT nếu SP lỗi — bảng NhatKyHoatDong)
        public void LuuNhatKyHoatDong(int maNguoiChoi, string hanhDong, int? maPhong, string toaDo)
        {
            if (maNguoiChoi <= 0)
            {
                Console.WriteLine("[SQL WARN]: LuuNhatKyHoatDong bỏ qua — MaNguoiChoi không hợp lệ.");
                return;
            }
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_LuuNhatKyHoatDong", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);
                        cmd.Parameters.AddWithValue("@HanhDong", hanhDong ?? "");
                        cmd.Parameters.AddWithValue("@MaPhong", maPhong.HasValue ? (object)maPhong.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToaDo", string.IsNullOrEmpty(toaDo) ? (object)DBNull.Value : toaDo);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR] LuuNhatKyHoatDong (SP): " + ex.Message);
                LuuNhatKyHoatDongDirect(maNguoiChoi, hanhDong, maPhong, toaDo);
            }
        }

        /// <summary>Dự phòng khi SP lỗi — cần cột: MaNguoiChoi, HanhDong, MaPhong, ToaDo, ThoiGian.</summary>
        private void LuuNhatKyHoatDongDirect(int maNguoiChoi, string hanhDong, int? maPhong, string toaDo)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"INSERT INTO NhatKyHoatDong (MaNguoiChoi, HanhDong, MaPhong, ToaDo, ThoiGian)
VALUES (@MaNguoiChoi, @HanhDong, @MaPhong, @ToaDo, SYSUTCDATETIME())";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);
                        cmd.Parameters.AddWithValue("@HanhDong", hanhDong ?? "");
                        cmd.Parameters.AddWithValue("@MaPhong", maPhong.HasValue ? (object)maPhong.Value : DBNull.Value);
                        cmd.Parameters.AddWithValue("@ToaDo", string.IsNullOrEmpty(toaDo) ? (object)DBNull.Value : toaDo);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex2)
            {
                Console.WriteLine("[SQL ERROR] LuuNhatKyHoatDongDirect: " + ex2.Message);
            }
        }

        // Lưu truyền bom
        public void LuuTruyenBom(int maPhong, int nguoiTruyen, int nguoiNhan, double khoangCach)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_LuuTruyenBom", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaPhong", maPhong);
                        cmd.Parameters.AddWithValue("@NguoiTruyen", nguoiTruyen);
                        cmd.Parameters.AddWithValue("@NguoiNhan", nguoiNhan);
                        cmd.Parameters.AddWithValue("@KhoangCach", khoangCach);
                        cmd.Parameters.AddWithValue("@ThoiGianTruyen", DateTime.Now);
                        cmd.ExecuteNonQuery();
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: " + ex.Message);
            }
        }
        public void XoaPhongVaKetQua(int maPhong)
        {
            // TODO: Viết logic xóa phòng và kết quả nếu cần
        }
        public int LayMaNguoiChoi(string tenDangNhap)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("SELECT MaNguoiChoi FROM NguoiChoi WHERE TenDangNhap = @Ten", conn))
                    {
                        cmd.Parameters.AddWithValue("@Ten", tenDangNhap.Trim());
                        var result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToInt32(result) : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: " + ex.Message);
                return 0;
            }
        }
        public DateTime? LayThoiGianDangNhap(int maNguoiChoi)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (var cmd = new SqlCommand("SELECT ThoiGian FROM NhatKyHoatDong WHERE MaNguoiChoi = @MaNguoiChoi AND HanhDong = N'ĐĂNG NHẬP' ORDER BY ThoiGian DESC", conn))
                    {
                        cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);
                        var result = cmd.ExecuteScalar();
                        if (result != null && result != DBNull.Value)
                            return Convert.ToDateTime(result);
                        return null;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: " + ex.Message);
                return null;
            }
        }

        // Thông tin người chơi
        public class NguoiChoiInfo
        {
            public string Nickname { get; set; }
            public int? NhanVat { get; set; }
            public int MaPhong { get; set; }
            public bool LaChuPhong { get; set; }
            public bool DaSanSang { get; set; }
        }

        public NguoiChoiInfo LayThongTinNguoiChoi(int maNguoiChoi)
        {
            try
            {
                using (var conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = "SELECT Nickname, NhanVat, MaPhong, LaChuPhong, DaSanSang FROM NguoiChoi WHERE MaNguoiChoi = @id";
                    using (var cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@id", maNguoiChoi);
                        using (var rdr = cmd.ExecuteReader())
                        {
                            if (rdr.Read())
                            {
                                return new NguoiChoiInfo
                                {
                                    Nickname = rdr["Nickname"] != DBNull.Value ? rdr["Nickname"].ToString() : null,
                                    NhanVat = rdr["NhanVat"] != DBNull.Value ? (int?)Convert.ToInt32(rdr["NhanVat"]) : null,
                                    MaPhong = rdr["MaPhong"] != DBNull.Value ? Convert.ToInt32(rdr["MaPhong"]) : 0,
                                    LaChuPhong = rdr["LaChuPhong"] != DBNull.Value ? Convert.ToInt32(rdr["LaChuPhong"]) == 1 : false,
                                    DaSanSang = rdr["DaSanSang"] != DBNull.Value ? Convert.ToInt32(rdr["DaSanSang"]) == 1 : false
                                };
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: LayThongTinNguoiChoi: " + ex.Message);
            }
            return null;
        }
        public bool CapNhatTrangThaiReady(int maNguoiChoi, bool daSanSang)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_CapNhatTrangThaiReady", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);
                        cmd.Parameters.AddWithValue("@DaSanSang", daSanSang);

                        int rows = cmd.ExecuteNonQuery();
                        return rows > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DB ERROR]: " + ex.Message);
                return false;
            }
        }
        public bool CapNhatTrangThaiPhong(int maPhong, string trangThai)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    using (SqlCommand cmd = new SqlCommand("sp_CapNhatTrangThaiPhong", conn))
                    {
                        cmd.CommandType = CommandType.StoredProcedure;
                        cmd.Parameters.AddWithValue("@MaPhong", maPhong);
                        cmd.Parameters.AddWithValue("@TrangThai", trangThai);

                        int rows = cmd.ExecuteNonQuery();
                        return rows > 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[DB ERROR]: " + ex.Message);
                return false;
            }
        }
        public bool LuuKetQuaTran(int maTran, int maPhong, int maNguoiChoi, string nickname, string trangThai, int soLanNhanBom, int thuHang)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string query = @"INSERT INTO KetQuaTran (MaTran, MaPhong, MaNguoiChoi, Nickname, TrangThai, SoLanNhanBom, ThuHang) 
                             VALUES (@MaTran, @MaPhong, @MaNguoiChoi, @Nickname, @TrangThai, @SoLanNhanBom, @ThuHang)";
                    using (SqlCommand cmd = new SqlCommand(query, conn))
                    {
                        cmd.Parameters.AddWithValue("@MaTran", maTran);
                        cmd.Parameters.AddWithValue("@MaPhong", maPhong);
                        cmd.Parameters.AddWithValue("@MaNguoiChoi", maNguoiChoi);
                        cmd.Parameters.AddWithValue("@Nickname", nickname);
                        cmd.Parameters.AddWithValue("@TrangThai", trangThai);
                        cmd.Parameters.AddWithValue("@SoLanNhanBom", soLanNhanBom);
                        cmd.Parameters.AddWithValue("@ThuHang", thuHang);

                        cmd.ExecuteNonQuery();
                    }
                }
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: " + ex.Message);
                return false;
            }
        }

        // Create a match record and return inserted MaTran (best-effort)
        public int TaoTran(int maPhong)
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    string sql = @"INSERT INTO Tran (MaPhong, ThoiGianBatDau)
                                   OUTPUT INSERTED.MaTran
                                   VALUES (@MaPhong, @ThoiGianBatDau)";
                    using (SqlCommand cmd = new SqlCommand(sql, conn))
                    {
                        cmd.Parameters.AddWithValue("@MaPhong", maPhong);
                        cmd.Parameters.AddWithValue("@ThoiGianBatDau", DateTime.Now);
                        var result = cmd.ExecuteScalar();
                        return result != null ? Convert.ToInt32(result) : 0;
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("[SQL ERROR]: TaoTran: " + ex.Message);
                return 0;
            }
        }

    }
}
