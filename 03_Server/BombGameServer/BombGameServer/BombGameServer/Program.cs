using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;          // Thư viện này để hiểu IPAddress
using System.Net.Sockets;  // Thư viện này để hiểu TcpListener

namespace BombGameServer
{
    internal class Program
    {
        static async Task Main(string[] args)
        {
            DatabaseHelper db = new DatabaseHelper();
            db.TestConnection(); // kiểm tra kết nối SQL

            GameServer server = new GameServer(8888, 10);
            await server.StartAsync();
        }
    }
}
