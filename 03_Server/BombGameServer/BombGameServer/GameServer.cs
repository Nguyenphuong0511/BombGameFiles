using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;

namespace BombGameServer
{
    class GameServer
    {
        private TcpListener listener;
        private List<Player> players = new List<Player>();
        private int port;
        private int maxPlayers;
        public DatabaseHelper dbHelper = new DatabaseHelper();
        private BombTimer bombTimer;

        // In-memory rooms information: populated when CREATE_ROOM is called
        private readonly Dictionary<int, RoomInfo> rooms = new Dictionary<int, RoomInfo>();

        private class RoomInfo
        {
            public int Id { get; set; }
            public string Name { get; set; }
            public int TargetPlayers { get; set; } // value chosen by room creator (4..10)
            public int HostPlayerId { get; set; }
            public string Code { get; set; } // join-by-code
        }

        // Snapshot DTO used to compute and re-use room state consistently
        private class RoomSnapshot
        {
            public int RoomId { get; set; }
            public int TargetPlayers { get; set; }
            public List<Player> Members { get; set; }
            public List<object> MemberDtos { get; set; }
            public int CurrentCount { get; set; }
            public bool AllMembersReady { get; set; }
            public bool StartEnabled { get; set; }
            public string RoomCode { get; set; }
        }

        public GameServer(int port, int maxPlayers = 10)
        {
            this.port = port;
            this.maxPlayers = maxPlayers;
            listener = new TcpListener(IPAddress.Any, port);
            _ = MonitorStatusLoop();
        }

        public async Task StartAsync()
        {
            listener.Start();
            Console.WriteLine($@"[SYSTEM]: Server started on port {port}");

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
                            if (players.Count == 1) newPlayer.LaChuPhong = true;
                        }
                        else
                        {
                            // refuse connection if server reached global capacity
                            try { client.Close(); }
                            catch { }
                        }
                    }

                    _ = HandleClientAsync(newPlayer);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($@"[ERROR]: {ex.Message}");
                }
            }
        }

        private RoomSnapshot BuildRoomSnapshot(int roomId)
        {
            RoomInfo info = null;
            lock (rooms)
            {
                rooms.TryGetValue(roomId, out info);
            }

            List<Player> members;
            lock (players)
            {
                members = players.Where(p => p.MaPhong == roomId).ToList();
            }

            int currentCount = members.Count;
            int target = info != null ? info.TargetPlayers : 4;
            bool allReady = members.Count > 0 && members.All(m => m.DaSanSang);

            // Require that current count meets target AND everyone is ready
            bool startEnabled = allReady && currentCount >= target;

            var memberDtos = members
                .Select(m => new
                {
                    id = m.MaNguoiChoi,
                    name = m.TenDangNhap,
                    character = m.NhanVat,
                    ready = m.DaSanSang,
                    host = m.LaChuPhong
                })
                .ToList<object>();

            return new RoomSnapshot
            {
                RoomId = roomId,
                TargetPlayers = target,
                Members = members,
                MemberDtos = memberDtos,
                CurrentCount = currentCount,
                AllMembersReady = allReady,
                StartEnabled = startEnabled,
                RoomCode = info != null ? info.Code : null
            };
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
                    if (bytesRead <= 0) break;

                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var request = JsonConvert.DeserializeObject<dynamic>(json);
                    string action = request.Action;

                    switch (action)
                    {
                        case "REGISTER":
                            {
                                string userReg = (request.Username != null) ? ((string)request.Username).Trim() : "";
                                string passReg = (request.Password != null) ? ((string)request.Password) : "";
                                string charReg = (request.Character != null) ? ((string)request.Character).Trim() : "";

                                int newUserId = dbHelper.DangKyNguoiChoi(userReg, passReg, charReg);

                                if (newUserId > 0)
                                {
                                    player.MaNguoiChoi = newUserId;
                                    player.TenDangNhap = userReg;
                                    player.NhanVat = charReg ?? player.NhanVat;

                                    // populate last login from DB if any
                                    player.ThoiGianDangNhap = dbHelper.LayThoiGianDangNhap(newUserId);

                                    dbHelper.LuuNhatKyHoatDong(newUserId, "ĐĂNG KÝ", null, null);

                                    SendToSinglePlayer(player, "REGISTER_SUCCESS", new { userId = newUserId, message = "Đăng ký thành công!", username = userReg, character = charReg });
                                    BroadcastPlayerList();
                                }
                                else
                                {
                                    string reason = "Đăng ký thất bại.";
                                    if (newUserId == -1) reason = "Vui lòng điền đầy đủ Tên đăng nhập, Mật khẩu và Biệt danh.";
                                    else if (newUserId == -2) reason = "Tên đăng nhập đã tồn tại.";
                                    else if (newUserId == -3) reason = "Biệt danh đã được sử dụng. Vui lòng chọn biệt danh khác.";
                                    else reason = "Không thể đăng ký do lỗi hệ thống.";

                                    SendToSinglePlayer(player, "REGISTER_FAIL", new { message = reason });
                                }
                            }
                            break;

                        case "LOGIN":
                            {
                                string username = (request.Username != null) ? ((string)request.Username).Trim() : "";
                                string password = (request.Password != null) ? ((string)request.Password) : "";

                                if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
                                {
                                    SendToSinglePlayer(player, "LOGIN_FAIL", new { message = "Vui lòng nhập Tên đăng nhập và Mật khẩu." });
                                    break;
                                }

                                int userId = dbHelper.DangNhapNguoiChoi(username, password);
                                if (userId > 0)
                                {
                                    player.MaNguoiChoi = userId;
                                    player.TenDangNhap = username;

                                    // Record login action in activity log
                                    dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "ĐĂNG NHẬP", player.MaPhong, player.ToaDo);

                                    // Save login timestamp to DB (tries to update NguoiChoi.Thoigiandangnhap; falls back to logging)
                                    DateTime loginTime = DateTime.Now;
                                    dbHelper.CapNhatThoiGianDangNhap(player.MaNguoiChoi, loginTime);

                                    // store locally on player object for MonitorStatusLoop
                                    player.ThoiGianDangNhap = loginTime;

                                    // Show on server console
                                    Console.WriteLine($"[LOGIN]: User '{player.TenDangNhap}' (ID:{player.MaNguoiChoi}) logged in at {loginTime:yyyy-MM-dd HH:mm:ss}");

                                    // Send success + login time to client (ISO 8601 string)
                                    SendToSinglePlayer(player, "LOGIN_SUCCESS", new { id = player.MaNguoiChoi, username = player.TenDangNhap, character = player.NhanVat, loginTime = loginTime.ToString("o") });

                                    // Ensure other components know this player's login time and state
                                    BroadcastPlayerList();
                                    // if player is in a room update room status so clients see counts / ready state
                                    if (player.MaPhong > 0) BroadcastRoomStatus(player.MaPhong);
                                }
                                else
                                {
                                    SendToSinglePlayer(player, "LOGIN_FAIL", new { message = "Tên đăng nhập hoặc mật khẩu không đúng." });
                                }
                            }
                            break;

                        case "SELECT_CHARACTER":
                            player.NhanVat = (string)request.CharacterName;
                            dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "CHỌN NV: " + player.NhanVat, player.MaPhong, player.ToaDo);
                            Console.WriteLine($"[SQL]: Đã lưu nhân vật {player.NhanVat} cho ID {player.MaNguoiChoi}");
                            BroadcastPlayerList();
                            break;

                        case "CREATE_ROOM":
                            {
                                string tenPhong = request.RoomName;
                                // Read requested target players from client (if provided)
                                int requestedTarget = 10;
                                try { requestedTarget = (int)request.TargetPlayers; } catch { requestedTarget = 10; }

                                // Enforce allowed range [4..10]
                                if (requestedTarget < 4) requestedTarget = 4;
                                if (requestedTarget > 10) requestedTarget = 10;

                                int idPhongMoi = dbHelper.TaoPhongChoi(tenPhong, player.MaNguoiChoi);

                                if (idPhongMoi > 0)
                                {
                                    // set server-side room state and player's room immediately
                                    player.MaPhong = idPhongMoi;
                                    player.LaChuPhong = true;

                                    string code = GenerateRoomCode();

                                    lock (rooms)
                                    {
                                        rooms[idPhongMoi] = new RoomInfo
                                        {
                                            Id = idPhongMoi,
                                            Name = tenPhong,
                                            TargetPlayers = requestedTarget,
                                            HostPlayerId = player.MaNguoiChoi,
                                            Code = code
                                        };
                                    }

                                    // compute snapshot: host is already in room
                                    var snapshot = BuildRoomSnapshot(idPhongMoi);

                                    SendToSinglePlayer(player, "CREATE_ROOM_SUCCESS", new
                                    {
                                        roomId = idPhongMoi,
                                        targetPlayers = snapshot.TargetPlayers,
                                        currentCount = snapshot.CurrentCount,
                                        allReady = snapshot.AllMembersReady,
                                        startEnabled = snapshot.StartEnabled,
                                        roomCode = snapshot.RoomCode,
                                        members = snapshot.MemberDtos,
                                        message = "Đã tạo phòng."
                                    });

                                    dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "TẠO PHÒNG", idPhongMoi, player.ToaDo);

                                    BroadcastPlayerList();
                                    BroadcastRoomStatus(idPhongMoi);
                                }
                                else
                                {
                                    SendToSinglePlayer(player, "CREATE_ROOM_FAIL", new { message = "Không thể tạo phòng." });
                                }
                            }
                            break;

                        case "JOIN_ROOM":
                            {
                                int roomIdToJoin = (int)request.RoomId;

                                // If we have an in-memory room, enforce its target capacity
                                RoomInfo info = null;
                                lock (rooms)
                                {
                                    rooms.TryGetValue(roomIdToJoin, out info);
                                }
                                if (info != null)
                                {
                                    List<Player> current;
                                    lock (players) { current = players.Where(p => p.MaPhong == roomIdToJoin).ToList(); }
                                    if (current.Count >= info.TargetPlayers)
                                    {
                                        SendToSinglePlayer(player, "JOIN_ROOM_FAIL", new { message = "Phòng đã đầy theo cấu hình chủ phòng." });
                                        break;
                                    }
                                }

                                int resultJoin = dbHelper.ThamGiaPhongChoi(roomIdToJoin, player.MaNguoiChoi);

                                if (resultJoin > 0)
                                {
                                    // set local server state immediately
                                    player.MaPhong = roomIdToJoin;
                                    player.LaChuPhong = false;
                                    dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "VÀO PHÒNG", player.MaPhong, player.ToaDo);

                                    // build immediate room status to send in the same packet (prevents race)
                                    var snapshot = BuildRoomSnapshot(roomIdToJoin);

                                    SendToSinglePlayer(player, "JOIN_ROOM_SUCCESS", new
                                    {
                                        roomId = roomIdToJoin,
                                        targetPlayers = snapshot.TargetPlayers,
                                        currentCount = snapshot.CurrentCount,
                                        allReady = snapshot.AllMembersReady,
                                        startEnabled = snapshot.StartEnabled,
                                        roomCode = snapshot.RoomCode,
                                        members = snapshot.MemberDtos
                                    });

                                    BroadcastPlayerList();
                                    BroadcastRoomStatus(roomIdToJoin);
                                }
                                else
                                {
                                    SendToSinglePlayer(player, "JOIN_ROOM_FAIL", new { message = "Phòng không tồn tại hoặc đã đầy!" });
                                }
                            }
                            break;

                        case "JOIN_BY_CODE":
                            {
                                string code = ((string)request.RoomCode ?? "").Trim().ToUpper();
                                if (string.IsNullOrEmpty(code))
                                {
                                    SendToSinglePlayer(player, "JOIN_ROOM_FAIL", new { message = "Mã phòng không hợp lệ." });
                                    break;
                                }

                                int roomFound = 0;
                                RoomInfo foundInfo = null;
                                lock (rooms)
                                {
                                    foreach (var kv in rooms)
                                    {
                                        if (string.Equals(kv.Value.Code, code, StringComparison.OrdinalIgnoreCase))
                                        {
                                            roomFound = kv.Key;
                                            foundInfo = kv.Value;
                                            break;
                                        }
                                    }
                                }

                                if (roomFound == 0 || foundInfo == null)
                                {
                                    SendToSinglePlayer(player, "JOIN_ROOM_FAIL", new { message = "Không tìm thấy phòng với mã này." });
                                    break;
                                }

                                List<Player> currentMembers;
                                lock (players) { currentMembers = players.Where(p => p.MaPhong == roomFound).ToList(); }
                                if (currentMembers.Count >= foundInfo.TargetPlayers)
                                {
                                    SendToSinglePlayer(player, "JOIN_ROOM_FAIL", new { message = "Phòng đã đầy theo cấu hình chủ phòng." });
                                    break;
                                }

                                int resultJoin = dbHelper.ThamGiaPhongChoi(roomFound, player.MaNguoiChoi);
                                if (resultJoin > 0)
                                {
                                    player.MaPhong = roomFound;
                                    player.LaChuPhong = false;
                                    dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "VÀO PHÒNG (CODE)", player.MaPhong, player.ToaDo);

                                    var snapshot = BuildRoomSnapshot(roomFound);

                                    SendToSinglePlayer(player, "JOIN_ROOM_SUCCESS", new
                                    {
                                        roomId = roomFound,
                                        targetPlayers = snapshot.TargetPlayers,
                                        currentCount = snapshot.CurrentCount,
                                        allReady = snapshot.AllMembersReady,
                                        startEnabled = snapshot.StartEnabled,
                                        roomCode = snapshot.RoomCode,
                                        members = snapshot.MemberDtos
                                    });

                                    BroadcastPlayerList();
                                    BroadcastRoomStatus(roomFound);
                                }
                                else
                                {
                                    SendToSinglePlayer(player, "JOIN_ROOM_FAIL", new { message = "Không thể tham gia phòng bằng mã." });
                                }
                            }
                            break;

                        case "JOIN_RANDOM":
                            {
                                int selectedRoom = 0;
                                RoomInfo selectedInfo = null;
                                lock (rooms)
                                {
                                    foreach (var kv in rooms)
                                    {
                                        var roomId = kv.Key;
                                        var info = kv.Value;
                                        var current = players.Where(p => p.MaPhong == roomId).ToList();
                                        if (current.Count < info.TargetPlayers && !current.Any(p => p.DangTrongTran))
                                        {
                                            selectedRoom = roomId;
                                            selectedInfo = info;
                                            break;
                                        }
                                    }
                                }

                                if (selectedRoom > 0)
                                {
                                    int resultJoin = dbHelper.ThamGiaPhongChoi(selectedRoom, player.MaNguoiChoi);
                                    if (resultJoin > 0)
                                    {
                                        player.MaPhong = selectedRoom;
                                        dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "VÀO PHÒNG NGẪU NHIÊN", player.MaPhong, player.ToaDo);

                                        var snapshot = BuildRoomSnapshot(selectedRoom);

                                        SendToSinglePlayer(player, "JOIN_ROOM_SUCCESS", new
                                        {
                                            roomId = selectedRoom,
                                            targetPlayers = snapshot.TargetPlayers,
                                            currentCount = snapshot.CurrentCount,
                                            allReady = snapshot.AllMembersReady,
                                            startEnabled = snapshot.StartEnabled,
                                            roomCode = snapshot.RoomCode,
                                            members = snapshot.MemberDtos
                                        });

                                        BroadcastPlayerList();
                                        BroadcastRoomStatus(selectedRoom);
                                    }
                                    else
                                    {
                                        SendToSinglePlayer(player, "JOIN_ROOM_FAIL", new { message = "Không thể tham gia phòng ngẫu nhiên." });
                                    }
                                }
                                else
                                {
                                    SendToSinglePlayer(player, "JOIN_ROOM_FAIL", new { message = "Không tìm thấy phòng trống nào!" });
                                }
                            }
                            break;

                        case "READY":
                            {
                                // toggle ready
                                player.DaSanSang = !player.DaSanSang;

                                // persist in activity log (DB)
                                dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, player.DaSanSang ? "SẴN SÀNG" : "HỦY SẴN SÀNG", player.MaPhong, player.ToaDo);

                                // notify all clients; ensure room status updated (counts + members ready states)
                                BroadcastPlayerList();
                                if (player.MaPhong > 0) BroadcastRoomStatus(player.MaPhong);
                            }
                            break;

                        case "START_GAME":
                            {
                                if (!player.LaChuPhong)
                                {
                                    SendToSinglePlayer(player, "START_FAIL", new { message = "Chỉ chủ phòng mới có quyền bắt đầu trận." });
                                    break;
                                }

                                // Build a canonical snapshot and use it for start decision
                                var snapshot = BuildRoomSnapshot(player.MaPhong);

                                // If no room or no members, fail
                                if (snapshot == null || snapshot.CurrentCount == 0)
                                {
                                    SendToSinglePlayer(player, "START_FAIL", new { message = "Không có người chơi trong phòng để bắt đầu." });
                                    break;
                                }

                                // Use the unified StartEnabled flag (requires currentCount >= targetPlayers AND all ready)
                                if (snapshot.StartEnabled)
                                {
                                    // mark all members as in-match and persist
                                    foreach (var m in snapshot.Members)
                                    {
                                        m.DangTrongTran = true;
                                    }

                                    dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, "BẮT ĐẦU TRẬN ĐẤU", player.MaPhong, player.ToaDo);

                                    // Notify only members in this room that the game is starting
                                    var startPayload = new { message = "Trận đấu bắt đầu!", roomId = player.MaPhong };
                                    foreach (var member in snapshot.Members)
                                    {
                                        try { SendToSinglePlayer(member, "GAME_START", startPayload); }
                                        catch { }
                                    }

                                    // Keep server-wide GAME_START broadcast for other subsystems if needed
                                    BatDauTranDau();
                                    // Update room state to clients
                                    BroadcastRoomStatus(player.MaPhong);
                                }
                                else
                                {
                                    // Build helpful message listing who is not ready
                                    var notReady = snapshot.Members.Where(m => !m.DaSanSang).Select(m => m.TenDangNhap).ToList();
                                    string notReadyList = notReady.Count > 0 ? string.Join(", ", notReady) : "Chưa đủ số người";
                                    SendToSinglePlayer(player, "START_FAIL", new { message = $"Không thể bắt đầu: cần {snapshot.CurrentCount}/{snapshot.TargetPlayers} và tất cả phải sẵn sàng. Chưa ready: {notReadyList}" });
                                }
                            }
                            break;

                        case "MOVE":
                            player.ToaDo = (string)request.Position;
                            break;

                        case "PASS_BOMB":
                            {
                                int targetId = (int)request.TargetId;
                                string posAtMoment = (string)request.Position;
                                player.ToaDo = posAtMoment;

                                var targetPlayer = players.FirstOrDefault(p => p.MaNguoiChoi == targetId);
                                if (targetPlayer != null && player.CoBom)
                                {
                                    player.CoBom = false;
                                    targetPlayer.CoBom = true;

                                    dbHelper.LuuNhatKyHoatDong(player.MaNguoiChoi, $"TRUYỀN BOM CHO {targetId}", player.MaPhong, posAtMoment);
                                    dbHelper.LuuLichSuTruyenBom(player.MaNguoiChoi, targetId, dbHelper.LayTranDauTheoPhong(player.MaPhong), 0);

                                    BroadcastMessage("BOMB_ASSIGNED", new { holderId = targetId, holderName = targetPlayer.TenDangNhap, roomId = player.MaPhong });
                                }
                                else
                                {
                                    SendToSinglePlayer(player, "PASS_FAIL", new { message = "Không thể truyền bom." });
                                }
                            }
                            break;

                        case "LEAVE_ROOM":
                            {
                                int leftRoom = player.MaPhong;
                                if (player.LaChuPhong)
                                {
                                    // Host leaves -> close room and evacuate players in that room
                                    dbHelper.XoaPhongVaKetQua(player.MaPhong);
                                    lock (players)
                                    {
                                        var members = players.Where(p => p.MaPhong == leftRoom && p != player).ToList();
                                        foreach (var m in members)
                                        {
                                            m.MaPhong = 0;
                                            m.DaSanSang = false;
                                            m.LaChuPhong = false;
                                        }
                                    }

                                    lock (rooms) { rooms.Remove(leftRoom); }

                                    BroadcastMessage("ROOM_CLOSED", new { reason = "Chủ phòng đã thoát.", roomId = leftRoom });
                                    BroadcastPlayerList();
                                }
                                else
                                {
                                    player.MaPhong = 0;
                                    player.DaSanSang = false;
                                    player.LaChuPhong = false;
                                    BroadcastPlayerList();
                                    BroadcastRoomStatus(leftRoom);
                                }
                            }
                            break;

                        case "FORGOT_PASSWORD":
                            {
                                string username = (request.Username != null) ? ((string)request.Username).Trim() : "";
                                if (string.IsNullOrWhiteSpace(username))
                                {
                                    SendToSinglePlayer(player, "FORGOT_FAIL", new { message = "Vui lòng nhập Tên đăng nhập." });
                                    break;
                                }

                                int userId = dbHelper.LayMaNguoiChoi(username);
                                if (userId <= 0)
                                {
                                    SendToSinglePlayer(player, "FORGOT_FAIL", new { message = "Không tìm thấy tài khoản." });
                                    break;
                                }

                                // generate a temporary password (8 chars alphanumeric)
                                const string chars = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
                                var rnd = new Random();
                                var sb = new StringBuilder(8);
                                for (int i = 0; i < 8; i++) sb.Append(chars[rnd.Next(chars.Length)]);
                                string tempPassword = sb.ToString();

                                bool ok = dbHelper.DatLaiMatKhau(userId, tempPassword);
                                if (ok)
                                {
                                    dbHelper.LuuNhatKyHoatDong(userId, "ĐẶT LẠI MẬT KHẨU");
                                    // NOTE: For production do NOT send passwords in plain text — use email with token.
                                    SendToSinglePlayer(player, "FORGOT_SUCCESS", new { message = "Đặt lại mật khẩu thành công. Mật khẩu tạm thời:", tempPassword = tempPassword });
                                }
                                else
                                {
                                    SendToSinglePlayer(player, "FORGOT_FAIL", new { message = "Không thể đặt lại mật khẩu." });
                                }
                            }
                            break;

                        default:
                            break;
                    }
                }
            }
            catch { }
            finally { HandleDisconnect(player); }
        }

        private string GenerateRoomCode()
        {
            const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
            var rnd = new Random();
            string code;
            lock (rooms)
            {
                do
                {
                    var sb = new StringBuilder(6);
                    for (int i = 0; i < 6; i++) sb.Append(chars[rnd.Next(chars.Length)]);
                    code = sb.ToString();
                } while (rooms.Values.Any(r => string.Equals(r.Code, code, StringComparison.OrdinalIgnoreCase)));
            }
            return code;
        }

        private void BatDauTranDau()
        {
            var holders = players.Where(p => p.MaPhong > 0 && p.DangTrongTran).ToList();
            foreach (var pl in holders)
            {
                pl.DangTrongTran = true;
                pl.CoBom = false;
            }

            if (holders.Count == 0) return;

            Random rand = new Random();
            Player firstHolder = holders[rand.Next(holders.Count)];
            firstHolder.CoBom = true;

            BroadcastMessage("GAME_START", new
            {
                message = "Trận đấu bắt đầu!",
                map = "Map_01",
                firstHolderId = firstHolder.MaNguoiChoi
            });

            bombTimer = new BombTimer(firstHolder, this);
            Console.WriteLine($@"[GAME]: Started. Holder: {firstHolder.TenDangNhap}");
        }

        private void HandleDisconnect(Player p)
        {
            lock (players) { players.Remove(p); }

            // Nếu người bị disconnect đang giữ bom, chuyển cho người khác
            if (p.CoBom && bombTimer != null)
            {
                var alivePlayers = players.Where(pl => pl.DangTrongTran).ToList();
                if (alivePlayers.Count > 0)
                {
                    Random rand = new Random();
                    Player nextHolder = alivePlayers[rand.Next(alivePlayers.Count)];
                    bombTimer.Reset(nextHolder);
                }
                else
                {
                    bombTimer?.StopAllTimers();
                }
            }

            // Nếu người rời đang là chủ phòng -> đóng phòng
            if (p.LaChuPhong)
            {
                int roomId = p.MaPhong;
                dbHelper.XoaPhongVaKetQua(p.MaPhong);
                lock (rooms) { rooms.Remove(roomId); }

                // Evacuate members of that room
                lock (players)
                {
                    var members = players.Where(x => x.MaPhong == roomId).ToList();
                    foreach (var m in members)
                    {
                        m.MaPhong = 0;
                        m.DaSanSang = false;
                        m.LaChuPhong = false;
                    }
                }

                BroadcastMessage("ROOM_CLOSED", new { reason = "Chủ phòng đã thoát.", roomId = roomId });
                BroadcastPlayerList();
            }
            else
            {
                // notify room members if any
                if (p.MaPhong > 0) BroadcastRoomStatus(p.MaPhong);
                BroadcastPlayerList();
            }
        }

        public void BroadcastMessage(string action, object data)
        {
            string json = JsonConvert.SerializeObject(new { Action = action, Data = data });
            byte[] buffer = Encoding.UTF8.GetBytes(json);
            lock (players)
            {
                foreach (var p in players)
                {
                    try { if (p.Connection.Connected) p.Connection.GetStream().Write(buffer, 0, buffer.Length); }
                    catch { }
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

        private void BroadcastPlayerList()
        {
            // Ensure ThoiGianDangNhap populated for players when available in DB
            lock (players)
            {
                foreach (var p in players)
                {
                    if (p.MaNguoiChoi > 0 && !p.ThoiGianDangNhap.HasValue)
                    {
                        try
                        {
                            p.ThoiGianDangNhap = dbHelper.LayThoiGianDangNhap(p.MaNguoiChoi);
                        }
                        catch { /* ignore DB read errors here */ }
                    }
                }
            }

            var list = players.Select(p => new
            {
                id = p.MaNguoiChoi,
                name = p.TenDangNhap,
                ready = p.DaSanSang,
                host = p.LaChuPhong,
                character = p.NhanVat,
                roomId = p.MaPhong,
                lastLogin = p.ThoiGianDangNhap.HasValue ? p.ThoiGianDangNhap.Value.ToString("o") : null
            }).ToList();

            BroadcastMessage("UPDATE_PLAYER_LIST", list);
        }

        // Broadcast per-room lobby status so clients can know current count / target / whether start is enabled
        private void BroadcastRoomStatus(int roomId)
        {
            var snapshot = BuildRoomSnapshot(roomId);

            var payload = new
            {
                roomId = roomId,
                roomCode = snapshot.RoomCode,
                targetPlayers = snapshot.TargetPlayers,
                currentCount = snapshot.CurrentCount,
                allReady = snapshot.AllMembersReady,
                startEnabled = snapshot.StartEnabled,
                members = snapshot.MemberDtos
            };

            // Send update to each member currently in the room (so host and all members update immediately)
            foreach (var member in snapshot.Members)
            {
                try
                {
                    SendToSinglePlayer(member, "ROOM_STATUS", payload);
                }
                catch { /* Ignore per-client send errors */ }
            }
        }

        // --- GameServer.cs ---
        private async Task MonitorStatusLoop()
        {
            while (true)
            {
                Console.Clear();
                Console.WriteLine("=====================================================================");
                Console.WriteLine("                BẢNG ĐIỀU KHIỂN SERVER GAME BOMB               ");
                Console.WriteLine("=====================================================================");
                Console.WriteLine($"[TRẠNG THÁI]: {DateTime.Now:HH:mm:ss} | Online: {players.Count}/{maxPlayers}");
                Console.WriteLine("---------------------------------------------------------------------");
                Console.WriteLine(string.Format("{0,-5} | {1,-15} | {2,-6} | {3,-10} | {4,-6} | {5,-19} | {6}",
                    "ID", "Tên nguoi choi", "Phòng", "Trang thai", "Bom", "Toa do", "ThoiGianDangnhap"));
                Console.WriteLine("---------------------------------------------------------------------");

                lock (players)
                {
                    // Ensure last login data populated (avoid DB hits inside Console loop)
                    foreach (var p in players)
                    {
                        if (p.MaNguoiChoi > 0 && !p.ThoiGianDangNhap.HasValue)
                        {
                            try { p.ThoiGianDangNhap = dbHelper.LayThoiGianDangNhap(p.MaNguoiChoi); } catch { }
                        }

                        foreach (var p in players)
                        {
                            string status = p.DangTrongTran ? "Đang choi" : (p.DaSanSang ? "San sang" : "Cho...");
                            string bomb = p.CoBom ? "[BOM]" : "Không";
                            string role = p.LaChuPhong ? "(C) " : "    ";
                            string charDisplay = string.IsNullOrEmpty(p.NhanVat) ? "-" : p.NhanVat;
                            string lastLoginStr = p.ThoiGianDangNhap.HasValue ? p.ThoiGianDangNhap.Value.ToString("yyyy-MM-dd HH:mm:ss") : "-";

                            Console.WriteLine(string.Format("{0,-5} | {1,-15} | {2,-6} | {3,-10} | {4,-6} | {5,-19} | {6}",
                                p.MaNguoiChoi, role + p.TenDangNhap, p.MaPhong, status, bomb, p.ToaDo, lastLoginStr));
                        }
                    }
                }
                Console.WriteLine("===============================================================");
                await Task.Delay(2000);
            }
        }
    }
}