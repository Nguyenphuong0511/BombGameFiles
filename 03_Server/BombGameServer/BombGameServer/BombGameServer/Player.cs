using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace BombGameServer
{
    // Lưu thông tin người chơi
    class Player
    {
        public int PlayerId { get; set; } // ID trong SQL
        public TcpClient Connection { get; private set; }
        public bool HasBomb { get; set; }
        public string Name { get; set; }
        public string PasswordHash { get; set; }

        public string Position { get; set; } = "0,0,0";
        public int CurrentRoomId { get; set; } = 0;
        public Player(TcpClient client)
        {
            Connection = client;
            Name = "Unknown"; // sẽ cập nhật từ client
            HasBomb = false;
        }
    }
}
