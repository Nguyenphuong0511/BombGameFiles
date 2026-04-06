using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;

namespace BombGameServer
{
    class GameServer
    {
        private TcpListener listener;
        private List<Player> players = new List<Player>();
        private int port;
        public DatabaseHelper dbHelper = new DatabaseHelper();

        // Quản lý phòng trong bộ nhớ
        private readonly Dictionary<int, RoomInfo> rooms = new Dictionary<int, RoomInfo>();

        // Manage one BombTimer per active room
        private readonly Dictionary<int, BombTimer> roomTimers = new Dictionary<int, BombTimer>();

        // TESTING FLAG: when true, allow room start with 1 player and ignore Ready checks.
        // Set to false for production (require 4 players + allReady).
        private const bool TESTING_ALLOW_SINGLE_PLAYER_START = true;

        private class RoomInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int TargetPlayers { get; set; }
            public int HostPlayerId { get; set; }
            public string Code { get; set; }
        }

        public GameServer(int port, int maxPlayers)
        {
            this.port = port;
            listener = new TcpListener(IPAddress.Any, port);
        }

        public async Task StartAsync()
        {
            listener.Start();
            Console.WriteLine($"[SERVER]: Dang chay tren cong {port}...");
            _ = Task.Run(() => ConsoleStatusLoop());

            while (true)
            {
                TcpClient client = await listener.AcceptTcpClientAsync();
                Player newPlayer = new Player(client);
                lock (players) { players.Add(newPlayer); }
                _ = Task.Run(() => HandleClient(newPlayer));
                Console.WriteLine("[CONNECT]: Mot nguoi choi moi da ket noi.");
            }
        }

        private async Task HandleClient(Player player)
        {
            NetworkStream stream = player.Connection.GetStream();
            byte[] buffer = new byte[4096];
            string recvBuffer = "";

            try
            {
                while (true)
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    recvBuffer += Encoding.UTF8.GetString(buffer, 0, bytesRead);

                    while (recvBuffer.Contains("\n"))
                    {
                        int index = recvBuffer.IndexOf("\n");
                        string packet = recvBuffer.Substring(0, index).Trim();
                        recvBuffer = recvBuffer.Substring(index + 1);

                        if (!string.IsNullOrEmpty(packet))
                        {
                            Console.WriteLine($"[RECEIVE from {player.DisplayName}]: {packet}");
                            HandleClientMessage(player, packet);
                        }
                    }
                }
            }
            catch (Exception ex) { Console.WriteLine($"[DISCONNECT]: {player.DisplayName} loi: {ex.Message}"); }
            finally
            {
                lock (players) { players.Remove(player); }
                player.Connection.Close();

                if (player.CoBom && player.MaPhong != 0)
                {
                    lock (roomTimers)
                    {
                        if (roomTimers.ContainsKey(player.MaPhong))
                        {
                            var other = players.FirstOrDefault(p => p.MaPhong == player.MaPhong && p != player && p.DangTrongTran);
                            if (other != null)
                            {
                                roomTimers[player.MaPhong].Reset(other);
                                // increment in-memory counter
                                other.TimesReceivedBomb++;
                                dbHelper.LuuTruyenBom(player.MaPhong, player.MaNguoiChoi, other.MaNguoiChoi, 0);
                            }
                            else
                            {
                                roomTimers[player.MaPhong].StopAllTimers();
                                roomTimers.Remove(player.MaPhong);
                            }
                        }
                    }
                }
            }
        }

        private void HandleClientMessage(Player player, string message)
        {
            try
            {
                JObject data = JObject.Parse(message);
                string action = (string)data["Action"];

                switch (action)
                {
                    case "LOGIN":
                        ProcessLogin(player, data);
                        break;

                    case "REGISTER":
                        ProcessRegister(player, data);
                        break;

                    case "RESET_PASS":
                        ProcessResetPass(player, data);
                        break;

                    case "SET_NICKNAME":
                    case "UPDATE_NICKNAME": // accept both client names
                        HandleSetNickname(player, data);
                        break;

                    case "CREATE_ROOM":
                        ProcessCreateRoom(player, data);
                        break;

                    case "JOIN_ROOM":
                        ProcessJoinRoom(player, data);
                        break;

                    case "SELECT_CHAR":
                        ProcessSelectChar(player, data);
                        break;

                    case "READY":
                        ProcessReady(player, data);
                        break;

                    case "START_GAME":
                        ProcessStartGame(player, data);
                        break;

                    case "MOVE":
                        ProcessMove(player, data);
                        break;

                    case "BOMB_TRANSFER":
                        int toId = data["ToId"] != null ? data["ToId"].Value<int>() : 0;
                        int roomId = player.MaPhong;
                        if (roomId == 0)
                        {
                            SendMessage(player, new { Action = "BOMB_TRANSFER_FAIL", message = "Ban khong o trong phong!" });
                            break;
                        }

                        Player target = null;
                        lock (players)
                        {
                            target = players.FirstOrDefault(p => p.MaNguoiChoi == toId && p.MaPhong == roomId && p.DangTrongTran);
                        }

                        if (target == null)
                        {
                            SendMessage(player, new { Action = "BOMB_TRANSFER_FAIL", message = "Nguoi choi dich khong ton tai trong phong!" });
                            break;
                        }

                        lock (roomTimers)
                        {
                            if (roomTimers.TryGetValue(roomId, out var bt))
                            {
                                bt.Reset(target);

                                // increment in-memory counter for ranking
                                target.TimesReceivedBomb++;

                                // persist transfer to DB (distance not calculated here)
                                dbHelper.LuuTruyenBom(roomId, player.MaNguoiChoi, target.MaNguoiChoi, 0);
                                // clear sender flag locally
                                player.CoBom = false;
                                SendMessage(player, new { Action = "BOMB_TRANSFER_OK", ToId = toId });
                            }
                            else
                            {
                                SendMessage(player, new { Action = "BOMB_TRANSFER_FAIL", message = "Tran chua bat dau hoac timer khong ton tai!" });
                            }
                        }
                        break;

                    case "BOMB_EXPLODE":
                        // allow client to notify explosion (defensive) - server authoritative logic remains in BombTimer
                        int explodedId = data["MaID"] != null ? data["MaID"].Value<int>() : player.MaNguoiChoi;
                        var explodedPlayer = players.FirstOrDefault(p => p.MaNguoiChoi == explodedId);
                        if (explodedPlayer != null)
                        {
                            BroadcastToRoom(explodedPlayer.MaPhong, "PLAYER_OUT", new { player = explodedPlayer.DisplayName, playerId = explodedPlayer.MaNguoiChoi });
                            dbHelper.LuuNhatKyHoatDong(explodedPlayer.MaNguoiChoi, "THUA DO NO BOM (client)", explodedPlayer.MaPhong, explodedPlayer.ToaDo);
                        }
                        break;

                    default:
                        Console.WriteLine($"[WARNING]: Hanh dong khong xac dinh: {action}");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ERROR]: Loi xu ly tin nhan tu {player.MaNguoiChoi}: {ex.Message}");
            }
        }

        private void ProcessLogin(Player player, JObject data)
        {
            string username = (string)data["Username"];
            string password = (string)data["Password"];

            int userId = dbHelper.DangNhapNguoiChoi(username, password);

            if (userId > 0)
            {
                player.MaNguoiChoi = userId;
                var info = dbHelper.LayThongTinNguoiChoi(userId);

                // TenDangNhap luôn là tên đăng nhập (cột SQL) — để LayMaNguoiChoi / EnsurePlayerId hoạt động.
                player.TenDangNhap = username;
                if (info != null && !string.IsNullOrWhiteSpace(info.Nickname))
                    player.TenHienThi = info.Nickname.Trim();
                else
                    player.TenHienThi = null;

                player.NhanVat = info?.NhanVat?.ToString() ?? null;
                player.MaPhong = info?.MaPhong ?? 0;
                player.LaChuPhong = info?.LaChuPhong ?? false;
                player.DaSanSang = info?.DaSanSang ?? false;
                player.DangTrongTran = false;

                // Khớp chuỗi với LayThoiGianDangNhap (HanhDong = N'ĐĂNG NHẬP')
                dbHelper.LuuNhatKyHoatDong(userId, "ĐĂNG NHẬP", player.MaPhong != 0 ? (int?)player.MaPhong : null, null);

                var payload = new
                {
                    Action = "LOGIN_SUCCESS",
                    MaID = userId,
                    Username = username,
                    Nickname = player.DisplayName,
                    CharacterIndex = (info?.NhanVat),
                    MaPhong = player.MaPhong,
                    IsHost = player.LaChuPhong,
                    IsReady = player.DaSanSang
                };
                SendMessage(player, payload);

                Console.WriteLine($"[LOGIN]: {username} (Ma={userId}) da vao game. HienThi='{player.DisplayName}', NhanVat={player.NhanVat}, MaPhong={player.MaPhong}");

                if (player.MaPhong != 0 && rooms.ContainsKey(player.MaPhong))
                {
                    BroadcastRoomUpdate(player.MaPhong);
                }
            }
            else
            {
                SendMessage(player, new { Action = "LOGIN_FAIL", message = "Sai tai khoan hoac mat khau!" });
            }
        }

        private void ProcessRegister(Player player, JObject data)
        {
            string user = (string)data["Username"];
            string pass = (string)data["Password"];

            int result = dbHelper.DangKyNguoiChoi(user, pass);

            if (result > 0)
            {
                int ma = dbHelper.LayMaNguoiChoi(user);
                player.MaNguoiChoi = ma;
                player.TenDangNhap = user;
                player.TenHienThi = user;

                // Log registration without setting MaPhong (null)
                dbHelper.LuuNhatKyHoatDong(ma, "ĐĂNG KÝ", null, null);
                SendMessage(player, new { Action = "REGISTER_SUCCESS", MaID = ma, Username = user });
            }
            else if (result == -1)
            {
                SendMessage(player, new { Action = "REGISTER_FAIL", message = "Ten dang nhap da ton tai!" });
            }
            else
            {
                SendMessage(player, new { Action = "REGISTER_FAIL", message = "Loi ket noi SQL!" });
            }
        }


        private void ProcessResetPass(Player player, JObject data)
        {
            string user = (string)data["Username"];
            // Sử dụng phương thức đúng từ DatabaseHelper để lấy mật khẩu
            string password = dbHelper.LayMatKhau(user);
            int uidForgot = dbHelper.LayMaNguoiChoi(user);
            if (uidForgot > 0)
                dbHelper.LuuNhatKyHoatDong(uidForgot, "QUÊN MẬT KHẨU", null, null);

            if (!string.IsNullOrEmpty(password))
                SendMessage(player, new { Action = "RESET_SUCCESS", message = "Mật khẩu của bạn là: " + password });
            else
                SendMessage(player, new { Action = "RESET_FAIL", message = "Không tìm thấy người chơi này!" });
        }
        private void ProcessCreateRoom(Player player, JObject data)
        {
            try
            {
                // Try to resolve player's DB id
                EnsurePlayerId(player, data);

                // If client provided a username but not MaID, try lookup using that username
                if (player.MaNguoiChoi == 0)
                {
                    string providedUsername = data["Username"]?.ToString();
                    if (!string.IsNullOrWhiteSpace(providedUsername))
                    {
                        int id = dbHelper.LayMaNguoiChoi(providedUsername);
                        if (id > 0) player.MaNguoiChoi = id;
                    }
                }

                if (player.MaNguoiChoi == 0)
                {
                    SendMessage(player, new { Action = "CREATE_ROOM_FAIL", message = "Bạn chưa đăng nhập!" });
                    return;
                }

                string roomName = (string)data["RoomName"] ?? "Phòng của " + player.DisplayName;
                int maxPlayers = (int?)data["MaxPlayers"] ?? 4;

                string nickname = (string)data["Nickname"];
                if (!string.IsNullOrWhiteSpace(nickname))
                {
                    if (player.MaNguoiChoi > 0)
                    {
                        bool saved = dbHelper.CapNhatNickname(player.MaNguoiChoi, nickname);
                        if (saved)
                        {
                            player.TenHienThi = nickname.Trim();
                            dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "ĐỔI NICKNAME", null, nickname.Trim());
                        }
                    }
                    else
                    {
                        player.TenHienThi = nickname.Trim();
                    }
                }

                string roomCode = GenerateRoomCode();
                int maPhong = dbHelper.LuuPhongVaoSQL(roomName, player.MaNguoiChoi, maxPlayers, roomCode);

                if (maPhong <= 0)
                {
                    SendMessage(player, new { Action = "CREATE_ROOM_FAIL", message = "Lỗi lưu phòng vào Database!" });
                    return;
                }

                RoomInfo newRoom = new RoomInfo
                {
                    Id = maPhong,
                    Name = roomName,
                    TargetPlayers = maxPlayers,
                    HostPlayerId = player.MaNguoiChoi,
                    Code = roomCode
                };

                lock (rooms)
                {
                    rooms[maPhong] = newRoom;
                }

                player.MaPhong = maPhong;
                player.DaSanSang = true;
                player.LaChuPhong = true;

                dbHelper.CapNhatNguoiChoi(player.MaNguoiChoi, maPhong, player.DisplayName, true);
                dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "TẠO PHÒNG", maPhong, null);

                var response = new
                {
                    Action = "CREATE_ROOM_SUCCESS",
                    RoomId = maPhong,
                    RoomCode = roomCode,
                    RoomName = roomName,
                    MaxPlayers = maxPlayers,
                    Message = "Tạo phòng thành công!"
                };

                SendMessage(player, response);
                BroadcastRoomUpdate(maPhong);
                Console.WriteLine($"[SERVER]: Người chơi {player.DisplayName} đã tạo phòng {roomName} [{roomCode}] (MaPhong={maPhong})");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR]: Lỗi khi tạo phòng: " + ex.Message);
                SendMessage(player, new { Action = "CREATE_ROOM_FAIL", message = "Lỗi hệ thống khi tạo phòng!" });
            }

        }


        private void ProcessJoinRoom(Player player, JObject data)
        {
            // Ensure player's DB id if possible
            EnsurePlayerId(player, data);

            // also try username lookup if provided
            if (player.MaNguoiChoi == 0)
            {
                string providedUsername = data["Username"]?.ToString();
                if (!string.IsNullOrWhiteSpace(providedUsername))
                {
                    int id = dbHelper.LayMaNguoiChoi(providedUsername);
                    if (id > 0) player.MaNguoiChoi = id;
                }
            }

            if (player.MaNguoiChoi == 0)
            {
                SendMessage(player, new { Action = "JOIN_ROOM_FAIL", message = "Bạn chưa đăng nhập!" });
                return;
            }

            string code = data["RoomCode"]?.ToString();
            var room = rooms.Values.FirstOrDefault(r => r.Code == code);

            if (room != null)
            {
                string nickname = (string)data["Nickname"];
                if (!string.IsNullOrWhiteSpace(nickname))
                {
                    if (player.MaNguoiChoi > 0)
                    {
                        dbHelper.CapNhatNickname(player.MaNguoiChoi, nickname);
                        player.TenHienThi = nickname.Trim();
                        dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "ĐỔI NICKNAME", room.Id, nickname.Trim());
                    }
                    else
                    {
                        player.TenHienThi = nickname.Trim();
                    }
                }

                player.MaPhong = room.Id;
                player.LaChuPhong = false;
                player.DaSanSang = false;

                int result = dbHelper.ThamGiaPhongChoi(room.Id, player.MaNguoiChoi);
                dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "THAM GIA PHÒNG", room.Id, null);
                dbHelper.CapNhatNguoiChoi(player.MaNguoiChoi, room.Id, player.DisplayName, false);

                if (result > 0)
                {
                    SendMessage(player, new { Action = "JOIN_ROOM_SUCCESS", roomName = room.Name, RoomCode = room.Code });
                    BroadcastRoomUpdate(room.Id);
                }
                else
                {
                    SendMessage(player, new { Action = "JOIN_ROOM_FAIL", message = "Lỗi lưu Database khi tham gia phòng!" });
                }
            }
            else
            {
                SendMessage(player, new { Action = "JOIN_ROOM_FAIL", message = "Mã phòng không tồn tại!" });
            }
        }
        private void EnsurePlayerId(Player player, JObject data)
        {
            int maID = (int?)data["MaID"] ?? 0;
            if (maID > 0 && player.MaNguoiChoi == 0)
            {
                player.MaNguoiChoi = maID;
            }

            // If still no id, try to resolve by Username field in JSON
            if (player.MaNguoiChoi == 0)
            {
                string providedUsername = data["Username"]?.ToString();
                if (!string.IsNullOrWhiteSpace(providedUsername))
                {
                    int id = dbHelper.LayMaNguoiChoi(providedUsername);
                    if (id > 0) player.MaNguoiChoi = id;
                }
            }

            // Finally, try to resolve by the current in-memory TenDangNhap (connection-local name)
            if (player.MaNguoiChoi == 0 && !string.IsNullOrWhiteSpace(player.TenDangNhap) && player.TenDangNhap != "Unknown")
            {
                int id = dbHelper.LayMaNguoiChoi(player.TenDangNhap);
                if (id > 0) player.MaNguoiChoi = id;
            }
        }

        private void HandleSetNickname(Player player, JObject data)
        {
            string newNickname = (string)data["Nickname"];
            int maID = (int?)data["MaID"] ?? player.MaNguoiChoi;

            if (string.IsNullOrWhiteSpace(newNickname))
            {
                SendMessage(player, new { Action = "SET_NICKNAME_FAIL", message = "Nickname không được để trống!" });
                return;
            }

            // If client sent MaID, sync into player
            if (maID > 0 && player.MaNguoiChoi == 0)
            {
                player.MaNguoiChoi = maID;
            }

            // Try to resolve id from Username or existing TenDangNhap if still unknown
            if (player.MaNguoiChoi == 0)
            {
                string providedUsername = data["Username"]?.ToString();
                if (!string.IsNullOrWhiteSpace(providedUsername))
                {
                    int id = dbHelper.LayMaNguoiChoi(providedUsername);
                    if (id > 0) player.MaNguoiChoi = id;
                }
            }

            // Chỉ cập nhật hiển thị — không ghi đè TenDangNhap (tên đăng nhập SQL)
            string nickTrim = newNickname.Trim();
            player.TenHienThi = nickTrim;

            if (player.MaNguoiChoi > 0)
            {
                bool isSaved = dbHelper.CapNhatNickname(player.MaNguoiChoi, nickTrim);
                if (isSaved)
                {
                    dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "ĐỔI NICKNAME", player.MaPhong != 0 ? (int?)player.MaPhong : null, nickTrim);
                    SendMessage(player, new
                    {
                        Action = "SET_NICKNAME_SUCCESS",
                        Nickname = nickTrim,
                        Persisted = true
                    });

                    Console.WriteLine($"[SQL]: Người chơi ID {player.MaNguoiChoi} đã đổi nickname thành: {nickTrim}");
                    return;
                }
                else
                {
                    SendMessage(player, new { Action = "SET_NICKNAME_FAIL", message = "Lỗi Database khi lưu Nickname!" });
                    return;
                }
            }
            else
            {
                // No DB id: notify client that name changed locally but not persisted
                SendMessage(player, new
                {
                    Action = "SET_NICKNAME_SUCCESS",
                    Nickname = nickTrim,
                    Persisted = false,
                    Message = "Nickname đã đổi cục bộ. Đăng nhập để lưu vào Database."
                });

                Console.WriteLine($"[LOCAL]: Người chơi chưa có MaNguoiChoi. Đổi nickname cục bộ thành: {nickTrim}");
            }
        }

        private void ProcessSelectChar(Player player, JObject data)
        {
            if (player.MaNguoiChoi == 0)
            {
                SendMessage(player, new { Action = "CHAR_SELECT_FAIL", Message = "Bạn chưa đăng nhập!" });
                return;
            }

            try
            {
                int characterIndex = (int)data["CharacterIndex"];

                player.NhanVat = characterIndex.ToString();

                dbHelper.ChonNhanVat(player.MaNguoiChoi, characterIndex);
                dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "CHỌN NHÂN VẬT", player.MaPhong == 0 ? null : (int?)player.MaPhong, null);

                SendMessage(player, new { Action = "CHAR_SELECT_SUCCESS", Character = characterIndex });

                if (player.MaPhong != 0)
                    BroadcastRoomUpdate(player.MaPhong);

                Console.WriteLine($"[SQL SUCCESS]: Người chơi {player.DisplayName} đã chọn nhân vật số {characterIndex}.");
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR]: " + ex.Message);
                SendMessage(player, new { Action = "CHAR_SELECT_FAIL", Message = "Lỗi lưu nhân vật vào Database!" });
            }
        }

        // Khi một người vào phòng thành công, gửi danh sách toàn bộ người chơi trong phòng đó cho tất cả mọi người
        private void BroadcastRoomUpdate(int roomId)
        {
            var playersInRoom = players.Where(p => p.MaPhong == roomId).Select(p => new {
                MaID = p.MaNguoiChoi,
                Nickname = p.DisplayName,
                CharacterIndex = string.IsNullOrEmpty(p.NhanVat) ? -1 : int.Parse(p.NhanVat),
                IsReady = p.DaSanSang,
                IsHost = (rooms.ContainsKey(roomId) && rooms[roomId].HostPlayerId == p.MaNguoiChoi),
                TimesReceivedBomb = p.TimesReceivedBomb
            }).ToList();

            foreach (var p in players.Where(p => p.MaPhong == roomId))
            {
                SendMessage(p, new
                {
                    Action = "ROOM_UPDATE",
                    PlayerList = playersInRoom,
                    CurrentCount = playersInRoom.Count,
                    // include both names so client won't miss expected field names
                    MaxPlayers = rooms[roomId].TargetPlayers,
                    MaxCount = rooms[roomId].TargetPlayers,
                    RoomCode = rooms[roomId].Code,
                    RoomName = rooms[roomId].Name
                });
            }
        }
        private void ProcessReady(Player player, JObject data)
        {
            if (player.MaNguoiChoi == 0)
            {
                SendMessage(player, new { Action = "READY_FAIL", Message = "Bạn chưa đăng nhập!" });
                return;
            }

            player.DaSanSang = (bool)data["IsReady"];
            dbHelper.CapNhatTrangThaiReady(player.MaNguoiChoi, player.DaSanSang);
            dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, player.DaSanSang ? "SẴN SÀNG" : "HỦY SẴN SÀNG", player.MaPhong == 0 ? null : (int?)player.MaPhong, null);
            // Optionally: persist ready flag to DB (if you have a column)
            // dbHelper.CapNhatNguoiChoi(player.MaNguoiChoi, player.MaPhong, player.TenDangNhap, player.LaChuPhong);

            BroadcastRoomUpdate(player.MaPhong);

            Console.WriteLine($"[READY]: Người chơi {player.DisplayName} {(player.DaSanSang ? "sẵn sàng" : "hủy sẵn sàng")}.");
        }

        private void ProcessStartGame(Player player, JObject data)
        {
            var room = rooms.Values.FirstOrDefault(r => r.HostPlayerId == player.MaNguoiChoi);
            if (room == null) return;

            var playersInRoom = players.Where(p => p.MaPhong == room.Id).ToList();

            // check ready
            bool allReady = playersInRoom.All(p => p.DaSanSang);

            int requiredPlayers = TESTING_ALLOW_SINGLE_PLAYER_START ? 1 : 4;
            bool readyCondition = TESTING_ALLOW_SINGLE_PLAYER_START ? true : allReady;

            if (playersInRoom.Count >= requiredPlayers && readyCondition)
            {
                // 1. Cập nhật trạng thái phòng trong SQL
                dbHelper.CapNhatTrangThaiPhong(room.Id, "DANGCHOI");

                // 2. Ghi nhật ký hoạt động
                dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "BẮT ĐẦU TRẬN", room.Id, null);

                // 3. Broadcast cho tất cả thành viên trong phòng
                BroadcastRoomUpdate(room.Id); // ensure clients have latest player list
                BroadcastToRoom(room.Id, "GAME_START", new { Message = "Bắt đầu trận đấu!" });

                // 4. Create BombTimer for this room if not exists and pick random initial holder
                lock (roomTimers)
                {
                    if (!roomTimers.ContainsKey(room.Id))
                    {
                        var rnd = new Random();
                        var initialHolder = playersInRoom[rnd.Next(playersInRoom.Count)];
                        initialHolder.CoBom = true;
                        var bt = new BombTimer(initialHolder, this);
                        roomTimers[room.Id] = bt;
                    }
                }
            }
            else
            {
                string msg;
                if (TESTING_ALLOW_SINGLE_PLAYER_START)
                    msg = $"Cần tối thiểu {requiredPlayers} người để bắt đầu (chế độ test).";
                else
                    msg = "Cần tối thiểu 4 người và tất cả phải Sẵn sàng!";

                SendMessage(player, new { Action = "START_FAIL", Message = msg });
            }
        }


        private void ProcessMove(Player player, JObject data)
        {
            // Đồng bộ vị trí: Nhận từ 1 người và gửi cho tất cả người còn lại trong phòng
            float posX = (float)data["X"];
            float posY = (float)data["Y"];

            var moveData = new
            {
                Action = "MOVE_UPDATE",
                PlayerId = player.MaNguoiChoi,
                X = posX,
                Y = posY
            };

            // Gửi cho mọi người trong phòng trừ người vừa di chuyển để tránh lag
            foreach (var p in players.Where(p => p.MaPhong == player.MaPhong && p != player))
            {
                SendMessage(p, moveData);
            }
        }

        private void SendMessage(Player player, object data)
        {
            try
            {
                string json = JsonConvert.SerializeObject(data) + "\n";
                byte[] buffer = Encoding.UTF8.GetBytes(json);
                var stream = player.Connection.GetStream();
                // ensure writes for this player are serialized to avoid partial/corrupt packets
                lock (player)
                {
                    stream.Write(buffer, 0, buffer.Length);
                    try { stream.Flush(); } catch { /* best-effort */ }
                }
            }
            catch { }
        }

        // Broadcast message with Action property (used by BombTimer)
        public void BroadcastMessage(string messageType, object data)
        {
            foreach (var player in players)
            {
                SendMessage(player, new { Action = messageType, Payload = data });
            }
        }

        // Broadcast to specific room
        internal void BroadcastToRoom(int roomId, string action, object data)
        {
            foreach (var p in players.Where(p => p.MaPhong == roomId))
            {
                SendMessage(p, new { Action = action, Payload = data });
            }
        }
        // Add this method inside the GameServer class

        private void ConsoleStatusLoop()
        {
            while (true)
            {
                // Example: print the number of connected players every 10 seconds
                Console.WriteLine($"[STATUS]: {players.Count} player(s) connected.");
                Thread.Sleep(10000);
            }
        }
        // Add this method inside the GameServer class

        private string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var random = new Random();
            return new string(Enumerable.Repeat(chars, 6)
                .Select(s => s[random.Next(s.Length)]).ToArray());
        }

        // New helper to persist match results (simple server-side ranking by TimesReceivedBomb)
        public void SaveMatchResults(int roomId)
        {
            try
            {
                var playersInRoom = players.Where(p => p.MaPhong == roomId).ToList();
                if (!playersInRoom.Any()) return;

                // Create match record in DB (if possible) to get MaTran
                int maTran = dbHelper.TaoTran(roomId);

                // Order alive first by TimesReceivedBomb ascending (fewer receives is better)
                var ordered = playersInRoom
                    .OrderByDescending(p => p.DangTrongTran) // prefer still-in-match first
                    .ThenBy(p => p.TimesReceivedBomb)
                    .ToList();

                int rank = 1;
                foreach (var p in ordered)
                {
                    string status = p.DangTrongTran ? "ALIVE" : "OUT";
                    dbHelper.LuuKetQuaTran(maTran, roomId, p.MaNguoiChoi, p.DisplayName, status, p.TimesReceivedBomb, rank);
                    rank++;
                }

                // broadcast final ranking to room
                BroadcastToRoom(roomId, "MATCH_RESULTS", new
                {
                    Results = ordered.Select((p, idx) => new { PlayerId = p.MaNguoiChoi, Name = p.DisplayName, p.TimesReceivedBomb, Rank = idx + 1 })
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine("[ERROR]: SaveMatchResults: " + ex.Message);
            }
        }
    }
}