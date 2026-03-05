using Newtonsoft.Json;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace BombGameClient
{
    class Program
    {
        static TcpClient client;
        static NetworkStream stream;

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                client = new TcpClient("127.0.0.1", 8888);
                stream = client.GetStream();
                Console.WriteLine("✅ Đã kết nối tới Server!");

                // ĐĂNG NHẬP
                Console.Write("Nhập tên nhân vật: ");
                string username = Console.ReadLine();
                Console.Title = "Người chơi: " + username; // Giúp phân biệt các cửa sổ test

                var loginPacket = new { Action = "LOGIN", Username = username };
                SendMessage(loginPacket);

                // Chạy luồng nhận tin nhắn từ Server
                _ = Task.Run(() => ReceiveMessages());

                // Menu giữ Client luôn chạy để test nhiều người
                while (true)
                {
                    Console.WriteLine("\n--- MENU TEST ---");
                    Console.WriteLine("1. Gửi tọa độ (MOVE)");
                    Console.WriteLine("2. Truyền bom (PASS_BOMB)");
                    Console.WriteLine("3. Thoát");
                    Console.Write("Chọn: ");
                    string choice = Console.ReadLine();

                    if (choice == "1")
                    {
                        Random r = new Random();
                        string pos = $"{r.Next(0, 100)},0,{r.Next(0, 100)}";
                        SendMessage(new { Action = "MOVE", Data = pos, RoomId = 1 });
                        Console.WriteLine($"[GỬI] Đã cập nhật tọa độ: {pos}");
                    }
                    else if (choice == "2")
                    {
                        Console.Write("Nhập tên người nhận bom: ");
                        string target = Console.ReadLine();
                        SendMessage(new { Action = "PASS_BOMB", To = target });
                    }
                    else if (choice == "3") break;
                }
            }
            catch (Exception ex) { Console.WriteLine("❌ Lỗi: " + ex.Message); Console.ReadLine(); }
        }

        static void SendMessage(object packet)
        {
            string json = JsonConvert.SerializeObject(packet);
            byte[] data = Encoding.UTF8.GetBytes(json);
            stream.Write(data, 0, data.Length);
        }

        static void ReceiveMessages()
        {
            byte[] buffer = new byte[2048];
            while (true)
            {
                try
                {
                    int bytesRead = stream.Read(buffer, 0, buffer.Length);
                    if (bytesRead > 0)
                    {
                        string msg = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                        Console.WriteLine("\n[SERVER]: " + msg);
                    }
                }
                catch { break; }
            }
        }
    }
}