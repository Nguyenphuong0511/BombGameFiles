using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Data.SqlClient;

namespace BombGameServer
{


    class DatabaseHelper
    {
        private string connectionString = "Server=DESKTOP-210BBP1;Database=BombGame;Trusted_Connection=True;";

        public bool TestConnection()
        {
            try
            {
                using (SqlConnection conn = new SqlConnection(connectionString))
                {
                    conn.Open();
                    Console.WriteLine("✅ Ket noi SQL thanh cong!");
                    return true;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Loi ket noi SQL: " + ex.Message);
                return false;
            }
        }

        public int AddPlayer(string username, string passwordHash)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "INSERT INTO NguoiChoi (TenDangNhap, MatKhau) OUTPUT INSERTED.MaNguoiChoi VALUES (@u, @p)";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@p", passwordHash);
                return (int)cmd.ExecuteScalar(); // ✅ trả về ID vừa tạo
            }
        }


        public bool CheckLogin(string username, string passwordHash)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT COUNT(*) FROM NguoiChoi WHERE TenDangNhap = @u AND MatKhau = @p";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@u", username);
                cmd.Parameters.AddWithValue("@p", passwordHash);
                int count = (int)cmd.ExecuteScalar();
                return count > 0;
            }
        }

        public void UpdateScore(int playerId, int score)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "UPDATE NguoiChoi SET Diem = ISNULL(Diem, 0) + @s WHERE MaNguoiChoi = @id";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@s", score);
                cmd.Parameters.AddWithValue("@id", playerId);
                cmd.ExecuteNonQuery();
            }
        }


        public void AddMatch(int playerId, string result)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "INSERT INTO TranDau (MaNguoiChoi, KetQua) VALUES (@id, @r)";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", playerId);
                cmd.Parameters.AddWithValue("@r", result);
                cmd.ExecuteNonQuery();
            }
        }
        public int GetPlayerId(string username)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "SELECT MaNguoiChoi FROM NguoiChoi WHERE TenDangNhap = @u";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@u", username);

                object result = cmd.ExecuteScalar();
                // Nếu không tìm thấy, trả về 0 thay vì để sập chương trình
                return (result != null) ? (int)result : 0;
            }
        }
        // Thêm vào class DatabaseHelper
        public void LogActivity(int playerId, string action, string position, int roomId)
        {
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = @"INSERT INTO NhatKyHoatDong (MaNguoiChoi, HanhDong, ToaDo, MaPhong) 
                         VALUES (@id, @action, @pos, @roomId)";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@id", playerId);
                cmd.Parameters.AddWithValue("@action", action);
                cmd.Parameters.AddWithValue("@pos", position);
                cmd.Parameters.AddWithValue("@roomId", roomId);
                cmd.ExecuteNonQuery();
            }
        }

        public void UpdatePlayerStatus(int playerId, string currentPos)
        {
            // Cập nhật tọa độ liên tục vào bảng người chơi nếu cần giám sát realtime
            using (SqlConnection conn = new SqlConnection(connectionString))
            {
                conn.Open();
                string query = "UPDATE NguoiChoi SET ViTriHienTai = @pos WHERE MaNguoiChoi = @id";
                SqlCommand cmd = new SqlCommand(query, conn);
                cmd.Parameters.AddWithValue("@pos", currentPos);
                cmd.Parameters.AddWithValue("@id", playerId);
                cmd.ExecuteNonQuery();
            }
        }
    }

}
