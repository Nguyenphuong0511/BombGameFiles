using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;

public class UIStatusManager : MonoBehaviour
{
    public TextMeshProUGUI StatusTextGlobal;

    public void SetStatus(string message, Color color)
    {
        if (StatusTextGlobal != null)
        {
            StatusTextGlobal.text = message;
            StatusTextGlobal.color = color;
        }
        else
        {
            Debug.LogWarning("⚠ StatusTextGlobal chưa được gán!");
        }
    }

    void Start()
    {
        var net = Object.FindFirstObjectByType<NetworkManager>();
        if (net != null)
            net.OnMessage += HandleServerMessage;
    }

    void HandleServerMessage(string msg)
    {
        if (string.IsNullOrWhiteSpace(msg)) return;

        try
        {
            var jo = JObject.Parse(msg);
            string action = (string)jo["Action"] ?? (string)jo["action"] ?? "";

            // Prefer server-provided human message if present
            string serverMessage = (string)jo["message"] ?? (string)jo["Message"] ?? "";

            switch (action)
            {
                // Authentication
                case "LOGIN_SUCCESS":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Đăng nhập thành công!", Color.green);
                    break;
                case "LOGIN_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Sai tên đăng nhập hoặc mật khẩu!", Color.red);
                    break;
                case "REGISTER_SUCCESS":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Đăng ký thành công!", Color.green);
                    break;
                case "REGISTER_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Đăng ký thất bại!", Color.red);
                    break;
                case "RESET_SUCCESS":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Lấy lại mật khẩu thành công.", Color.green);
                    break;
                case "RESET_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Không tìm thấy người chơi!", Color.red);
                    break;

                // Nickname
                case "SET_NICKNAME_SUCCESS":
                    {
                        bool persisted = (bool?)(jo["Persisted"] ?? jo["persisted"]) ?? false;
                        string nick = (string)jo["Nickname"] ?? serverMessage;
                        if (persisted)
                            SetStatus($"Nickname '{nick}' đã lưu vào database.", Color.green);
                        else
                            SetStatus($"Nickname '{nick}' đã đổi cục bộ. Đăng nhập để lưu vào DB.", Color.yellow);
                    }
                    break;
                case "SET_NICKNAME_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Lỗi khi lưu nickname!", Color.red);
                    break;

                // Room
                case "CREATE_ROOM_SUCCESS":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Tạo phòng thành công!", Color.green);
                    break;
                case "CREATE_ROOM_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Không thể tạo phòng.", Color.red);
                    break;
                case "JOIN_ROOM_SUCCESS":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Bạn đã tham gia phòng thành công!", Color.green);
                    break;
                case "JOIN_ROOM_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Không thể tham gia phòng.", Color.red);
                    break;
                case "ROOM_UPDATE":
                    // Optionally show brief info about room update
                    int current = (int?)(jo["CurrentCount"] ?? jo["Current"] ) ?? -1;
                    int max = (int?)(jo["MaxPlayers"] ?? jo["MaxCount"]) ?? -1;
                    string code = (string)(jo["RoomCode"] ?? jo["RoomCodeString"]) ?? "";
                    if (current >= 0 && max > 0)
                        SetStatus($"Cập nhật phòng {code}: {current}/{max} người.", Color.white);
                    else
                        SetStatus("Cập nhật danh sách phòng.", Color.white);
                    break;

                // Character selection
                case "CHAR_SELECT_SUCCESS":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Bạn đã chọn nhân vật!", Color.green);
                    break;
                case "CHAR_SELECT_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Chọn nhân vật thất bại.", Color.red);
                    break;

                // Ready / Start
                case "PLAYER_READY":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Bạn đã ấn nút Ready!", Color.green);
                    break;
                case "PLAYER_UNREADY":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Bạn đã hủy trạng thái Ready.", Color.yellow);
                    break;
                case "READY_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Không thể thay đổi trạng thái Ready.", Color.red);
                    break;
                case "GAME_START":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Trò chơi bắt đầu!", Color.cyan);
                    break;
                case "START_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Không thể bắt đầu trận!", Color.red);
                    break;

                // Gameplay events
                case "BOMB_TRANSFER_OK":
                    SetStatus("Truyền bom thành công.", Color.green);
                    break;
                case "BOMB_TRANSFER_FAIL":
                    SetStatus(!string.IsNullOrEmpty(serverMessage) ? serverMessage : "Truyền bom thất bại.", Color.red);
                    break;
                case "PLAYER_OUT":
                    {
                        // Payload may contain player/name
                        string who = (string)jo["player"] ?? (string)jo["Payload"]?["player"] ?? serverMessage;
                        SetStatus(!string.IsNullOrEmpty(who) ? $"Player out: {who}" : "Có người bị loại.", Color.yellow);
                    }
                    break;
                case "MATCH_TICK":
                    {
                        int remaining = (int?)(jo["Payload"]?["remainingTime"] ?? jo["remainingTime"]) ?? -1;
                        if (remaining >= 0) SetStatus($"Thời gian trận: {remaining}s", Color.white);
                        else SetStatus("Cập nhật thời gian trận.", Color.white);
                    }
                    break;
                case "MATCH_RESULTS":
                    SetStatus("Kết quả trận đấu đã được gửi.", Color.cyan);
                    break;

                // Default: show server message field if any, otherwise raw action
                default:
                    if (!string.IsNullOrEmpty(serverMessage))
                        SetStatus(serverMessage, Color.white);
                    else
                        SetStatus($"Nhận server action: {action}", Color.white);
                    break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogWarning("UIStatusManager: Không thể parse server message: " + ex.Message);
        }
    }
}
