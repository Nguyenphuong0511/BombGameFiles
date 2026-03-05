using Newtonsoft.Json;
using System;
using System.Net.Sockets;
using System.Text;
using System.Threading.Tasks;
using System.Collections.Generic;

namespace BombGameClient
{
    class Program
    {
        static TcpClient client;        
        static NetworkStream stream;
        static int maNguoiChoiHienTai = 0;
        static int maPhongHienTai = 0;
        static bool laChuPhong = false;
        static string nhanVatDaChon = "";
        static string toaDoHienTai = "10,0,10"; // Giả lập tọa độ mặc định để test

        // Preserve input fields across menus
        static string savedUsername = "";
        static string savedPassword = "";
        static string savedNickname = "";

        // simple flags for response
        static bool lastLoginFailed = false;

        // current room status (populated from server ROOM_STATUS or JOIN_ROOM_SUCCESS/CREATE_ROOM_SUCCESS)
        static int currentRoomTarget = 0;
        static int currentRoomCount = 0;
        static bool currentRoomAllReady = false;
        static bool currentRoomStartEnabled = false;
        static string currentRoomCode = "";
        static List<dynamic> currentRoomMembers = new List<dynamic>();

        static async Task Main(string[] args)
        {
            Console.OutputEncoding = Encoding.UTF8;
            try
            {
                client = new TcpClient("127.0.0.1", 8888);
                stream = client.GetStream();
                Console.WriteLine("✅ Đã kết nối tới Server!");

                // Chạy luồng nhận tin nhắn từ Server
                _ = ReceiveMessages();

                // 1. GIAI ĐOẠN ĐĂNG NHẬP / ĐĂNG KÝ
                await AuthMenu();

                // 2. GIAI ĐOẠN CHỌN NHÂN VẬT
                await SelectCharacterMenu();

                // 3. GIAI ĐOẠN QUẢN LÝ PHÒNG (Tạo/Vào phòng)
                await RoomMenu();

                // 4. GIAI ĐOẠN PHÒNG CHỜ (Sẵn sàng & Bắt đầu)
                await LobbyWaitingMenu();

                // 5. GIAI ĐOẠN CHƠI GAME (Giả lập truyền bom kèm tọa độ)
                await GameplayLoop();
            }
            catch (Exception ex)
            {
                Console.WriteLine("❌ Lỗi kết nối: " + ex.Message);
            }
        }

        #region Các Menu Chức Năng

        // Helper: read input but keep previous value when user presses Enter
        static string ReadWithDefault(string prompt, string current)
        {
            if (string.IsNullOrEmpty(current))
                Console.Write($"{prompt}: ");
            else
                Console.Write($"{prompt} (Enter để giữ '{current}'): ");

            string input = Console.ReadLine();
            if (string.IsNullOrEmpty(input)) return current;
            return input;
        }

        static async Task AuthMenu()
        {
            while (maNguoiChoiHienTai == 0)
            {
                Console.WriteLine("\n--- 1. ĐĂNG NHẬP / ĐĂNG KÝ ---");
                Console.WriteLine("1. Đăng ký tài khoản mới");
                Console.WriteLine("2. Đăng nhập hệ thống");
                Console.WriteLine("3. Quên mật khẩu");
                Console.Write("Lựa chọn [1/2/3]: ");
                string choice = Console.ReadLine();

                if (choice == "1")
                {
                    // Require non-empty username/password/nickname before sending
                    do
                    {
                        savedUsername = ReadWithDefault("Tên đăng nhập", savedUsername);
                        savedPassword = ReadWithDefault("Mật khẩu", savedPassword);
                        savedNickname = ReadWithDefault("Biệt danh nhân vật", savedNickname);

                        if (string.IsNullOrWhiteSpace(savedUsername) || string.IsNullOrWhiteSpace(savedPassword) || string.IsNullOrWhiteSpace(savedNickname))
                        {
                            Console.WriteLine("Vui lòng điền đầy đủ Tên đăng nhập, Mật khẩu và Biệt danh (không được để trống).");
                        }
                    } while (string.IsNullOrWhiteSpace(savedUsername) || string.IsNullOrWhiteSpace(savedPassword) || string.IsNullOrWhiteSpace(savedNickname));

                    SendMessage(new { Action = "REGISTER", Username = savedUsername, Password = savedPassword, Character = savedNickname });

                    // wait response
                    int wait = 0;
                    while (maNguoiChoiHienTai == 0 && wait < 50)
                    {
                        await Task.Delay(100);
                        wait++;
                    }

                    if (maNguoiChoiHienTai == 0)
                    {
                        Console.WriteLine("Đăng ký chưa thành công, thử lại.");
                    }
                }
                else if (choice == "2")
                {
                    // Login: require username/password
                    do
                    {
                        savedUsername = ReadWithDefault("Tên đăng nhập", savedUsername);
                        savedPassword = ReadWithDefault("Mật khẩu", savedPassword);
                        if (string.IsNullOrWhiteSpace(savedUsername) || string.IsNullOrWhiteSpace(savedPassword))
                            Console.WriteLine("Vui lòng nhập Tên đăng nhập và Mật khẩu.");
                    } while (string.IsNullOrWhiteSpace(savedUsername) || string.IsNullOrWhiteSpace(savedPassword));

                    SendMessage(new { Action = "LOGIN", Username = savedUsername, Password = savedPassword });

                    // wait response or fail
                    lastLoginFailed = false;
                    int wait = 0;
                    while (maNguoiChoiHienTai == 0 && !lastLoginFailed && wait < 50)
                    {
                        await Task.Delay(100);
                        wait++;
                    }

                    if (maNguoiChoiHienTai == 0)
                    {
                        if (lastLoginFailed)
                        {
                            Console.WriteLine("Đăng nhập thất bại. Thử lại.");
                        }
                        else
                        {
                            Console.WriteLine("Không nhận được phản hồi. Thử lại.");
                        }
                    }
                }
                else if (choice == "3")
                {
                    Console.Write("Nhập Tên đăng nhập cần đặt lại mật khẩu: ");
                    string uname = Console.ReadLine()?.Trim();
                    if (!string.IsNullOrEmpty(uname))
                    {
                        SendMessage(new { Action = "FORGOT_PASSWORD", Username = uname });
                        // wait for response handled in ReceiveMessages -> HandleServerAction
                        await Task.Delay(500);
                    }
                }
                else
                {
                    Console.WriteLine("Lựa chọn không hợp lệ.");
                }
            }
        }

        static async Task SelectCharacterMenu()
        {
            if (maNguoiChoiHienTai == 0) return;
            Console.WriteLine("\n--- 2. CHỌN NHÂN VẬT ---");
            Console.WriteLine($"[Tài khoản hiện tại] Tên đăng nhập: {savedUsername} | ID: {maNguoiChoiHienTai}");
            Console.WriteLine("Các mẫu có sẵn: [Sói] [Thỏ] [Gấu] [Chim Ưng]");
            nhanVatDaChon = ReadWithDefault("Nhập tên nhân vật bạn thích", nhanVatDaChon);
            savedNickname = nhanVatDaChon;
            SendMessage(new { Action = "SELECT_CHARACTER", CharacterName = nhanVatDaChon });
            await Task.Delay(500);
        }

        static async Task RoomMenu()
        {
            Console.WriteLine("\n--- 3. QUẢN LÝ PHÒNG ---");
            Console.WriteLine($"Tài khoản: {savedUsername} | Nhân vật: {savedNickname}");
            Console.WriteLine("1. Tạo phòng mới");
            Console.WriteLine("2. Vào phòng bằng mã (host cung cấp mã)");
            Console.WriteLine("3. Vào phòng ngẫu nhiên");
            Console.Write("Lựa chọn: ");
            string choice = Console.ReadLine();

            if (choice == "1")
            {
                Console.Write("Tên phòng muốn đặt: ");
                string rName = Console.ReadLine();

                Console.Write("Số người chơi tối đa cho phòng (4 - 10) [mặc định 10]: ");
                int target = 10;
                string t = Console.ReadLine();
                if (!int.TryParse(t, out target)) target = 10;
                if (target < 4) target = 4;
                if (target > 10) target = 10;

                SendMessage(new { Action = "CREATE_ROOM", RoomName = rName, TargetPlayers = target });
            }
            else if (choice == "2")
            {
                Console.Write("Nhập mã phòng (ví dụ: ABC123): ");
                string code = Console.ReadLine()?.Trim().ToUpper();
                if (!string.IsNullOrEmpty(code))
                    SendMessage(new { Action = "JOIN_BY_CODE", RoomCode = code });
            }
            else if (choice == "3")
            {
                SendMessage(new { Action = "JOIN_RANDOM" });
            }
            await Task.Delay(500);
        }

        static async Task LobbyWaitingMenu()
        {
            bool isReady = false;
            while (maPhongHienTai != 0)
            {
                Console.WriteLine();
                Console.WriteLine($"=== PHÒNG CHỜ ID: {maPhongHienTai} ===");
                Console.WriteLine($"Tài khoản: {savedUsername} | Nhân vật: {savedNickname} | Vai trò: {(laChuPhong ? "CHỦ PHÒNG" : "Thành viên")}");
                Console.WriteLine($"Mã phòng: {(string.IsNullOrEmpty(currentRoomCode) ? "-" : currentRoomCode)}");
                Console.WriteLine($"Số người trong phòng: {currentRoomCount} / {(currentRoomTarget > 0 ? currentRoomTarget : 10)} | Tất cả sẵn sàng: {currentRoomAllReady}");
                if (laChuPhong)
                {
                    Console.WriteLine(currentRoomStartEnabled ? "Bạn có thể gõ [START] để bắt đầu trận." : "Chờ tất cả người chơi sẵn sàng và đủ người để bật START.");
                }
                Console.WriteLine("---------------------------------------------");
                Console.WriteLine("Gõ [READY] để sẵn sàng / hủy");
                if (laChuPhong && currentRoomStartEnabled) Console.WriteLine("Gõ [START] để bắt đầu trận đấu");
                Console.WriteLine("Gõ [OUT] để thoát phòng");
                Console.Write("\nNhập lệnh: ");

                string input = Console.ReadLine()?.ToUpper();
                if (input == "READY")
                {
                    isReady = !isReady;
                    SendMessage(new { Action = "READY", Status = isReady });
                }
                else if (input == "START" && laChuPhong && currentRoomStartEnabled)
                {
                    SendMessage(new { Action = "START_GAME" });
                }
                else if (input == "OUT")
                {
                    SendMessage(new { Action = "LEAVE_ROOM" });
                    maPhongHienTai = 0;
                    laChuPhong = false;
                    currentRoomCode = "";
                    return;
                }
                await Task.Delay(200);
            }
        }

        static async Task GameplayLoop()
        {
            while (true)
            {
                Console.WriteLine("\n[1] Di chuyển | [2] Truyền bom | [3] Thoát");
                string c = Console.ReadLine();

                if (c == "1")
                {
                    Console.Write("Nhập tọa độ mới (x,y,z): ");
                    toaDoHienTai = Console.ReadLine();
                    SendMessage(new { Action = "MOVE", Position = toaDoHienTai });
                }
                else if (c == "2")
                {
                    Console.Write("ID người nhận: ");
                    if (int.TryParse(Console.ReadLine(), out int id))
                    {
                        SendMessage(new { Action = "PASS_BOMB", TargetId = id, Position = toaDoHienTai });
                    }
                }
                else if (c == "3")
                {
                    Console.WriteLine("Thoát client.");
                    break;
                }
                await Task.Delay(100);
            }
        }

        #endregion

        static void SendMessage(object payload)
        {
            try
            {
                string json = JsonConvert.SerializeObject(payload);
                byte[] data = Encoding.UTF8.GetBytes(json);
                stream.Write(data, 0, data.Length);
            }
            catch (Exception ex) { Console.WriteLine("❌ Lỗi gửi: " + ex.Message); }
        }

        static async Task ReceiveMessages()
        {
            byte[] buffer = new byte[4096];
            while (true)
            {
                try
                {
                    int bytesRead = await stream.ReadAsync(buffer, 0, buffer.Length);
                    if (bytesRead == 0) break;

                    string json = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    var packet = JsonConvert.DeserializeObject<dynamic>(json);

                    HandleServerAction((string)packet.Action, packet.Data);
                }
                catch { break; }
            }
        }

        static void HandleServerAction(string action, dynamic data)
        {
            switch (action)
            {
                case "REGISTER_SUCCESS":
                    maNguoiChoiHienTai = (int)data.userId;
                    Console.WriteLine($"\n✅ Đăng ký thành công! ID của bạn là: {maNguoiChoiHienTai}");
                    break;

                case "REGISTER_FAIL":
                    Console.WriteLine($"\n❌ Đăng ký thất bại: {data.message}");
                    break;

                case "LOGIN_SUCCESS":
                    maNguoiChoiHienTai = (int)data.id;
                    savedUsername = (string)data.username;
                    if (data.character != null) savedNickname = (string)data.character;

                    // display login time if provided
                    if (data.loginTime != null)
                    {
                        string loginTimeStr = (string)data.loginTime;
                        DateTime parsed;
                        if (DateTime.TryParse(loginTimeStr, out parsed))
                            Console.WriteLine($"\n[SERVER] Đăng nhập thành công. ID: {data.id}, Username: {savedUsername} | Login at: {parsed:yyyy-MM-dd HH:mm:ss}");
                        else
                            Console.WriteLine($"\n[SERVER] Đăng nhập thành công. ID: {data.id}, Username: {savedUsername} | LoginTime: {loginTimeStr}");
                    }
                    else
                    {
                        Console.WriteLine($"\n[SERVER] Đăng nhập thành công. ID: {data.id}, Username: {savedUsername}");
                    }
                    break;

                case "LOGIN_FAIL":
                    lastLoginFailed = true;
                    Console.WriteLine($"\n[SERVER] Đăng nhập thất bại: {data.message}");
                    break;

                case "FORGOT_SUCCESS":
                    {
                        string msg = data.message != null ? (string)data.message : "Đặt lại mật khẩu thành công.";
                        string temp = data.tempPassword != null ? (string)data.tempPassword : "";
                        Console.WriteLine($"\n✅ {msg} {temp}");
                        Console.WriteLine("Vui lòng đăng nhập lại bằng mật khẩu tạm thời và đổi mật khẩu trong tài khoản.");
                    }
                    break;

                case "FORGOT_FAIL":
                    {
                        Console.WriteLine($"\n❌ Đặt lại mật khẩu thất bại: {data.message}");
                    }
                    break;

                case "CREATE_ROOM_SUCCESS":
                    maPhongHienTai = (int)data.roomId;
                    laChuPhong = true;
                    currentRoomTarget = data.targetPlayers != null ? (int)data.targetPlayers : 10;
                    currentRoomCount = data.currentCount != null ? (int)data.currentCount : 1;
                    currentRoomAllReady = data.allReady != null ? (bool)data.allReady : false;
                    currentRoomStartEnabled = data.startEnabled != null ? (bool)data.startEnabled : false;
                    currentRoomCode = data.roomCode != null ? (string)data.roomCode : currentRoomCode;
                    currentRoomMembers.Clear();
                    if (data.members != null)
                    {
                        foreach (var m in data.members) currentRoomMembers.Add(m);
                    }
                    Console.WriteLine($"\n🏠 Đã tạo phòng {maPhongHienTai}. Bạn là CHỦ PHÒNG. (Target players: {currentRoomTarget})");
                    if (!string.IsNullOrEmpty(currentRoomCode))
                        Console.WriteLine($"Mã phòng: {currentRoomCode} (cho bạn bè để họ vào bằng mã)");
                    Console.WriteLine($"Số người trong phòng: {currentRoomCount} / {currentRoomTarget} | Tất cả sẵn sàng: {currentRoomAllReady}");
                    Console.WriteLine("Thành viên phòng:");
                    foreach (var m in currentRoomMembers) Console.WriteLine($"{((bool)m.ready ? "[X]" : "[ ]")} {m.name} (NV:{(m.character!=null?(string)m.character:"-")}) (ID:{m.id})");
                    break;

                case "JOIN_ROOM_SUCCESS":
                    maPhongHienTai = (int)data.roomId;
                    laChuPhong = false;
                    currentRoomTarget = data.targetPlayers != null ? (int)data.targetPlayers : currentRoomTarget;
                    currentRoomCount = data.currentCount != null ? (int)data.currentCount : currentRoomCount;
                    currentRoomAllReady = data.allReady != null ? (bool)data.allReady : currentRoomAllReady;
                    currentRoomStartEnabled = data.startEnabled != null ? (bool)data.startEnabled : currentRoomStartEnabled;
                    currentRoomCode = data.roomCode != null ? (string)data.roomCode : currentRoomCode;
                    currentRoomMembers.Clear();
                    if (data.members != null)
                    {
                        foreach (var m in data.members) currentRoomMembers.Add(m);
                    }
                    Console.WriteLine($"\n[SERVER] Tham gia phòng {maPhongHienTai} thành công.");
                    Console.WriteLine($"Số người trong phòng: {currentRoomCount} / {currentRoomTarget} | Tất cả sẵn sàng: {currentRoomAllReady}");
                    Console.WriteLine("Thành viên phòng:");
                    foreach (var m in currentRoomMembers) Console.WriteLine($"{((bool)m.ready ? "[X]" : "[ ]")} {m.name} (NV:{(m.character!=null?(string)m.character:"-")}) (ID:{m.id})");
                    break;

                case "ROOM_STATUS":
                    {
                        int roomId = (int)data.roomId;

                        if (maPhongHienTai == roomId)
                        {
                            currentRoomTarget = data.targetPlayers != null ? (int)data.targetPlayers : currentRoomTarget;
                            currentRoomCount = data.currentCount != null ? (int)data.currentCount : currentRoomCount;
                            currentRoomAllReady = data.allReady != null ? (bool)data.allReady : currentRoomAllReady;
                            currentRoomStartEnabled = data.startEnabled != null ? (bool)data.startEnabled : currentRoomStartEnabled;
                            currentRoomCode = data.roomCode != null ? (string)data.roomCode : currentRoomCode;

                            currentRoomMembers.Clear();
                            if (data.members != null)
                            {
                                foreach (var m in data.members) currentRoomMembers.Add(m);
                            }

                            Console.WriteLine($"\n=== PHÒNG CHỜ ID: {maPhongHienTai} ===");
                            Console.WriteLine($"Số người trong phòng: {currentRoomCount} / {currentRoomTarget} | Tất cả sẵn sàng: {currentRoomAllReady}");
                            Console.WriteLine("Thành viên phòng:");
                            foreach (var m in currentRoomMembers) Console.WriteLine($"{((bool)m.ready ? "[X]" : "[ ]")} {m.name} (NV:{(m.character!=null?(string)m.character:"-")}) (ID:{m.id})");

                            if (laChuPhong && currentRoomStartEnabled)
                                Console.WriteLine("TẤT CẢ ĐÃ SẴN SÀNG — Gõ [START] để bắt đầu trận!");
                        }
                    }
                    break;

                case "GAME_START":
                    maPhongHienTai = 999; // Cờ hiệu để thoát vòng lặp Lobby nếu cần
                    Console.WriteLine("\n🔥 TRẬN ĐẤU BẮT ĐẦU!");
                    break;

                case "BOMB_ASSIGNED":
                    Console.WriteLine($"\n[SERVER] 💣 Người chơi {data.holderName} đang cầm BOM trong phòng {data.roomId}!");
                    if ((int)data.holderId == maNguoiChoiHienTai)
                        Console.WriteLine("👉 Bạn là người đang giữ BOM, hãy cẩn thận!");
                    else
                        Console.WriteLine("⚠️ Bạn KHÔNG phải người giữ BOM, hãy chú ý di chuyển để tránh!");
                    break;

                case "PLAYER_OUT":
                    Console.WriteLine($"\n[SERVER] Người chơi {data.player} (ID:{data.playerId}) đã thua do bom nổ.");
                    break;

                case "ROOM_CLOSED":
                    Console.WriteLine($"\n[SERVER] Phòng {data.roomId} đã bị đóng: {data.reason}");
                    if (maPhongHienTai == (int)data.roomId)
                    {
                        maPhongHienTai = 0;
                        laChuPhong = false;
                        currentRoomCode = "";
                    }
                    break;

                case "START_FAIL":
                    Console.WriteLine($"\n[SERVER] không thể bắt đầu: {data.message}");
                    break;

                case "ERROR":
                    Console.WriteLine($"\n❌ LỖI TỪ SERVER: {data.message}");
                    break;

                default:
                    // Other messages
                    break;
            }
        }
    }
}