using System;
using System.Net.Sockets;

namespace BombGameServer
{
    public class Player
    {
        // Kết nối mạng
        public TcpClient Connection { get; set; }

        // Thông tin định danh từ DB
        public int MaNguoiChoi { get; set; }

        /// <summary>Luôn khớp cột NguoiChoi.TenDangNhap — dùng cho tra cứu SQL (LayMaNguoiChoi).</summary>
        public string TenDangNhap { get; set; } = "Unknown";

        /// <summary>Nickname hiển thị — khớp cột Nickname; không dùng làm khóa tra cứu đăng nhập.</summary>
        public string TenHienThi { get; set; }

        public string NhanVat { get; set; } = "Default";

        /// <summary>Tên hiển thị ưu tiên nickname, không làm lẫn với TenDangNhap.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(TenHienThi) ? TenDangNhap : TenHienThi;

        // Trạng thái trong phòng/trận đấu
        public int MaPhong { get; set; } = 0;
        public bool LaChuPhong { get; set; } = false;
        public bool DaSanSang { get; set; } = false;
        public bool DangTrongTran { get; set; } = false;

        // Logic Game
        public string ToaDo { get; set; } = "0,0,0";

        public bool CoBom { get; set; } = false;
        public DateTime? ThoiGianDangNhap { get; set; }

        // In-memory counter used to compute rankings (kept in RAM and persisted by DB calls)
        public int TimesReceivedBomb { get; set; } = 0;

        public Player(TcpClient client)
        {
            Connection = client;
            ThoiGianDangNhap = DateTime.Now;
        }
    }
}