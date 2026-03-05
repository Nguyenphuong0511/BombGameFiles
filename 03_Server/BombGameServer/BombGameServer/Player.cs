using System;
using System.Net.Sockets;

namespace BombGameServer
{
    // Lưu thông tin người chơi
    public class Player
    {
        public TcpClient Connection { get; set; }
        public int MaNguoiChoi { get; set; }
        public string TenDangNhap { get; set; } = "Unknown";
        public int MaPhong { get; set; } = 0;
        public string ToaDo { get; set; } = "0,0,0";
        public bool CoBom { get; set; } = false;
        public bool DaSanSang { get; set; } = false; // Mới
        public bool LaChuPhong { get; set; } = false; // Mới
        public bool DangTrongTran { get; set; } = false; // Mới
        public string NhanVat { get; set; } = "Chưa chọn"; // Bổ sung dòng này
        public DateTime? ThoiGianDangNhap { get; set; } = null; // New: store last login timestamp (nullable)

        public Player(TcpClient client) { Connection = client; }
    }
}
