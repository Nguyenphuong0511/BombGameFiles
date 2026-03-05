using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Linq;

namespace BombGameServer
{
    class GameServer
    {
        private TcpListener listener;
        private List<Player> players = new List<Player>();
        private int port;
        private int maxPlayers;
        public DatabaseHelper dbHelper = new DatabaseHelper();

        public GameServer(int port, int maxPlayers = 10)
        {
            this.port = port;
            this.maxPlayers = maxPlayers;
            listener = new TcpListener(IPAddress.Any, port);

            // Khởi chạy luồng giám sát màn hình Console Dashboard
            _ = MonitorStatusLoop();
        }

        public async Task StartAsync()
        {
            listener.Start();
            Console.WriteLine($"[SYSTEM]: Server started on port {port}");

            while (true)
            {
                try
                {
                    TcpClient client = await listener.AcceptTcpClientAsync();
                    Player newPlayer = new Player(client);

                    lock (players)
                    {
                        if (players.Count < maxPlayers)
                        {
                            players.Add(newPlayer);
                            _ = HandleClientAsync(newPlayer);
                        }
                        else
                        {
                            Console.WriteLine("[SYSTEM]: Room Full! Connection refused.");
                            client.Close();
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"[ERROR]: Accept error: {ex.Message}");
                }
            }
        }

        private async Task HandleClientAsync(Player player)
        {
            try
            {
                NetworkStream stream = player.Connection.GetStream();
                byte[] buffer = new byte[4096];

                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break; // Client đóng kết nối

                    string rawMessage = Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    // Giải quyết vấn đề gói tin JSON bị dính nhau: {"Action":"MOVE"}{"Action":"MOVE"}
                    // Chuyển thành {"Action":"MOVE"}|{"Action":"MOVE"} để tách ra
                    string formattedMsg = rawMessage.Replace("}{", "}|{");
                    string[] messages = formattedMsg.Split('|');

                    foreach (var msg in messages)
                    {
                        if (string.IsNullOrWhiteSpace(msg)) continue;

                        try
                        {
                            var packet = JsonConvert.DeserializeObject<dynamic>(msg);
                            string action = packet.Action;

                            switch (action)
                            {
                                case "LOGIN":
                                    player.Name = packet.Username;
                                    // Tìm ID trong SQL hoặc tạo mới
                                    int id = dbHelper.GetPlayerId(player.Name);
                                    if (id == 0) // Nếu chưa tồn tại trong bảng NguoiChoi
                                    {
                                        id = dbHelper.AddPlayer(player.Name, "default_pw");
                                    }
                                    player.PlayerId = id;

                                    // Ghi log vào bảng NhatKyHoatDong
                                    dbHelper.LogActivity(player.PlayerId, "LOGIN", player.Position, player.CurrentRoomId);

                                    // Phản hồi cho client biết ID của họ
                                    SendToSinglePlayer(player, "LOGIN_SUCCESS", new { id = player.PlayerId });
                                    break;

                                case "MOVE":
                                    player.Position = packet.Data;
                                    player.CurrentRoomId = (int)packet.RoomId;

                                    // Log hành động MOVE vào SQL
                                    dbHelper.LogActivity(player.PlayerId, "MOVE", player.Position, player.CurrentRoomId);
                                    break;

                                case "PASS_BOMB":
                                    string toWho = packet.To;
                                    player.HasBomb = false;

                                    Player target;
                                    lock (players) { target = players.FirstOrDefault(p => p.Name == toWho); }

                                    if (target != null)
                                    {
                                        target.HasBomb = true;
                                        dbHelper.LogActivity(player.PlayerId, $"PASS_BOMB_TO_{toWho}", player.Position, player.CurrentRoomId);
                                        BroadcastMessage("PASS_BOMB", new { from = player.Name, to = toWho });
                                    }
                                    break;
                            }
                        }
                        catch (JsonException ex)
                        {
                            Console.WriteLine($"[JSON ERROR]: {ex.Message} in message: {msg}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                // Xử lý khi người chơi thoát đột ngột (tắt ứng dụng ngang xương)
            }
            finally
            {
                if (player.PlayerId > 0)
                {
                    dbHelper.LogActivity(player.PlayerId, "DISCONNECT", player.Position, player.CurrentRoomId);
                }

                lock (players) { players.Remove(player); }
                player.Connection.Close();
            }
        }

        private async Task MonitorStatusLoop()
        {
            while (true)
            {
                DisplayServerStatus();
                await Task.Delay(1500); // Cập nhật màn hình mỗi 1.5 giây
            }
        }

        public void DisplayServerStatus()
        {
            Console.Clear();
            Console.WriteLine("===========================================================================");
            Console.WriteLine($"   BOMBGAME SERVER MONITOR - {DateTime.Now:HH:mm:ss} | Online: {players.Count}");
            Console.WriteLine("===========================================================================");
            Console.WriteLine("{0,-8} {1,-15} {2,-10} {3,-20} {4,-10}", "SQL_ID", "Name", "Room", "Position", "Bomb");
            Console.WriteLine("---------------------------------------------------------------------------");

            List<Player> snapshot;
            lock (players) { snapshot = new List<Player>(players); }

            foreach (var p in snapshot)
            {
                string holdingBomb = p.HasBomb ? "🔥 YES" : "NO";
                string roomId = p.CurrentRoomId == 0 ? "Lobby" : p.CurrentRoomId.ToString();

                Console.WriteLine("{0,-8} {1,-15} {2,-10} {3,-20} {4,-10}",
                    p.PlayerId,
                    p.Name.Length > 12 ? p.Name.Substring(0, 10) + ".." : p.Name,
                    roomId,
                    p.Position ?? "0,0,0",
                    holdingBomb);
            }
            Console.WriteLine("===========================================================================");
            Console.WriteLine(" [LOG]: SQL Logs are being updated in 'NhatKyHoatDong' table.");
        }

        public void BroadcastMessage(string type, object payload)
        {
            string json = JsonConvert.SerializeObject(new { Action = type, Data = payload });
            byte[] data = Encoding.UTF8.GetBytes(json);

            lock (players)
            {
                foreach (var p in players)
                {
                    try
                    {
                        if (p.Connection.Connected)
                            p.Connection.GetStream().Write(data, 0, data.Length);
                    }
                    catch { /* Bỏ qua người chơi bị lỗi kết nối */ }
                }
            }
        }

        private void SendToSinglePlayer(Player p, string type, object payload)
        {
            try
            {
                string json = JsonConvert.SerializeObject(new { Action = type, Data = payload });
                byte[] data = Encoding.UTF8.GetBytes(json);
                p.Connection.GetStream().Write(data, 0, data.Length);
            }
            catch { }
        }
    }
}