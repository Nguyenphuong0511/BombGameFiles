using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using TMPro;
using UnityEngine;
using System.Collections.Generic;

public class AuthUIHandler : MonoBehaviour
{
    public static AuthUIHandler Instance { get; private set; }

    public NetworkManager networkManager;
    public MainMenuManager mainMenuManager;
    public UIStatusManager uiStatus;

    // Client-side numeric id assigned after login
    public int ma { get; set; }

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(this.gameObject);
            return;
        }

        Instance = this;

        if (networkManager != null) networkManager.OnMessage += HandleServerMessage;

        if (mainMenuManager != null)
        {
            mainMenuManager.authUIHandler = this;
            if (mainMenuManager.authManager != null)
                mainMenuManager.authManager.authUIHandler = this;
        }
    }

    private void OnDestroy()
    {
        if (Instance == this) Instance = null;
        if (networkManager != null) networkManager.OnMessage -= HandleServerMessage;
    }

    // --- Authentication / Room RPCs used by UI code ---

    // Login called by UI code: sends login request to server
    public void Login(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            uiStatus?.SetStatus("Vui lòng nhập tên và mật khẩu.", Color.yellow);
            return;
        }

        var data = new
        {
            Action = "LOGIN",
            Username = username.Trim(),
            Password = password
        };
        uiStatus?.SetStatus("Đang đăng nhập...", Color.white);
        networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");
    }

    // Register called by UI
    public void Register(string username, string password)
    {
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(password))
        {
            uiStatus?.SetStatus("Vui lòng nhập tên và mật khẩu.", Color.yellow);
            return;
        }

        var data = new
        {
            Action = "REGISTER",
            Username = username.Trim(),
            Password = password
        };
        uiStatus?.SetStatus("Đang đăng ký...", Color.white);
        networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");
    }

    // Reset password / forgot
    public void ResetPassword(string username)
    {
        if (string.IsNullOrWhiteSpace(username))
        {
            uiStatus?.SetStatus("Vui lòng nhập tên.", Color.yellow);
            return;
        }

        var data = new
        {
            Action = "RESET_PASSWORD",
            Username = username.Trim()
        };
        uiStatus?.SetStatus("Đang xử lý...", Color.white);
        networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");
    }

    // Join a room (used by MainMenuManager)
    public void JoinRoom(string nickname, string roomCode)
    {
        if (string.IsNullOrWhiteSpace(nickname) || string.IsNullOrWhiteSpace(roomCode))
        {
            uiStatus?.SetStatus("Vui lòng nhập tên và mã phòng!", Color.red);
            return;
        }

        var data = new
        {
            Action = "JOIN_ROOM",
            Nickname = nickname.Trim(),
            RoomCode = roomCode.Trim(),
            MaID = this.ma
        };
        uiStatus?.SetStatus("Đang tham gia phòng...", Color.white);
        networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");
    }

    // Create room convenience (MainMenuManager may call CREATE_ROOM directly; provided here for completeness)
    public void CreateRoom(string nickname, string roomName, int maxPlayers)
    {
        var data = new
        {
            Action = "CREATE_ROOM",
            Nickname = (nickname ?? "").Trim(),
            RoomName = (roomName ?? "").Trim(),
            MaxPlayers = maxPlayers,
            MaID = this.ma
        };
        uiStatus?.SetStatus("Đang tạo phòng...", Color.white);
        networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");
    }

    // --- Server message handling ---

    private void HandleServerMessage(string message)
    {
        try
        {
            var response = JObject.Parse(message);
            string action = (string)response["Action"];

            switch (action)
            {
                case "LOGIN_SUCCESS":
                    uiStatus?.SetStatus("Đăng nhập thành công!", Color.green);
                    JToken maToken = response["MaID"] ?? response["MaId"] ?? response["Ma"];
                    if (maToken != null)
                    {
                        if (maToken.Type == JTokenType.Integer)
                        {
                            this.ma = maToken.Value<int>();
                        }
                        else
                        {
                            // cannot use property as out parameter — parse into local first
                            int parsed;
                            int.TryParse(maToken.ToString(), out parsed);
                            this.ma = parsed;
                        }
                    }
                    if (mainMenuManager != null)
                    {
                        string user = response["username"]?.ToString() ?? response["Username"]?.ToString() ?? "Player";
                        mainMenuManager.LoginSuccess(user);
                    }
                    break;

                case "CREATE_ROOM_SUCCESS":
                    uiStatus?.SetStatus("Tạo phòng thành công! Mã phòng: " + (string)response["RoomCode"], Color.green);
                    if (mainMenuManager != null)
                    {
                        string createdCode = response["RoomCode"]?.ToString() ?? "";
                        int maxCount = (int?)response["MaxCount"] ?? (int?)response["MaxPlayers"] ?? -1;
                        mainMenuManager.OnRoomCreated(createdCode, true, maxCount);
                        mainMenuManager.ConfirmSelection();
                    }
                    break;

                case "JOIN_ROOM_SUCCESS":
                    uiStatus?.SetStatus("Tham gia phòng thành công!", Color.green);
                    if (mainMenuManager != null)
                    {
                        string code = response["RoomCode"]?.ToString() ?? "";
                        int maxCount = (int?)response["MaxCount"] ?? (int?)response["MaxPlayers"] ?? -1;
                        mainMenuManager.OnRoomJoined(code, maxCount);
                        mainMenuManager.ConfirmJoinRoom();
                    }
                    break;

                case "ROOM_UPDATE":
                    // ROOM_UPDATE may be top-level or nested in Payload depending how server sent it
                    JArray playersArray = (JArray)(response["Players"] ?? response["PlayerList"] ?? response["Payload"]?["PlayerList"]);
                    var players = ParsePlayers(playersArray);
                    int currentCount = (int?)response["CurrentCount"] ?? (int?)response["CurrentCountPlayers"] ?? players.Count;
                    int maxPlayers = (int?)response["MaxCount"] ?? (int?)response["MaxPlayers"] ?? 10;
                    string roomCode = response["RoomCode"]?.ToString() ?? response["RoomCodeString"]?.ToString() ?? "";
                    if (mainMenuManager != null)
                        mainMenuManager.UpdateLobbyUI(players, currentCount, maxPlayers, roomCode);

                    foreach (var pd in players) // players = ParsePlayers(...)
                    {
                        var existing = PlayerRegistry.Instance?.GetById(pd.maId);
                        if (existing != null)
                        {
                            // Remove pd.timesReceivedBomb, as PlayerData does not have this property
                            existing.PlayerName = pd.name;
                            existing.MaId = pd.maId;
                            existing.   ApplyVisualCharacter(pd.selectedIndex);
                        }
                        else
                        {
                            // spawn or map an existing GameObject for this player, then call UpdateFromServer on it
                            // Example pseudo:
                            // var go = GameManager.Instance.SpawnPlayerAtSlot(...);
                            // var pc = go.GetComponent<PlayerController>();
                            // pc.UpdateFromServer(pd.maId, pd.name, pd.selectedIndex, 0);
                        }
                    }
                    break;

                case "GAME_START":
                    uiStatus?.SetStatus("Trò chơi bắt đầu!", Color.green);
                    break;

                case "BOMB_PASSED":
                    // payload may be nested in Payload
                    JObject bombPayload = (JObject)(response["Payload"] ?? response);
                    int toId = bombPayload["toId"] != null ? bombPayload["toId"].Value<int>() : 0;
                    double dt = bombPayload["bombTimeLeft"] != null ? bombPayload["bombTimeLeft"].Value<double>() : 0.0;
                    float bombTimeLeft = (float)dt;

                    // update local scene: find PlayerController by MaID/toId and apply bomb locally
                    var pc = PlayerRegistry.Instance?.GetById(toId);
                    if (pc != null)
                    {
                        // Ensure character sprites were applied (server should have informed client of selection earlier).
                        // GiveBomb will also switch sprite to characterWithBomb.
                        pc.GiveBomb(bombTimeLeft);
                        uiStatus?.SetStatus($"Bomb passed to {pc.PlayerName} (time left {bombTimeLeft}s)", Color.white);
                    }
                    else
                    {
                        uiStatus?.SetStatus($"Bomb passed to {toId} (time left {bombTimeLeft}s) - player not found locally", Color.white);
                    }
                    break;

                case "PLAYER_OUT":
                    JObject outPayload = (JObject)(response["Payload"] ?? response);
                    string loser = outPayload["player"]?.ToString() ?? "";
                    // remove / mark player in scene
                    uiStatus?.SetStatus($"Player out: {loser}", Color.yellow);
                    break;

                case "MATCH_TICK":
                    JObject tickPayload = (JObject)(response["Payload"] ?? response);
                    int remainingTime = tickPayload["remainingTime"] != null ? tickPayload["remainingTime"].Value<int>() : 0;
                    // update UI
                    uiStatus?.SetStatus($"Time left: {remainingTime}s", Color.white);
                    break;

                case "GAME_OVER":
                    JObject overPayload = (JObject)(response["Payload"] ?? response);
                    string reason = overPayload["reason"]?.ToString() ?? "";
                    string loserName = overPayload["loserName"]?.ToString() ?? "";
                    // show game over UI
                    uiStatus?.SetStatus($"Game over: {reason} - {loserName}", Color.red);
                    break;

                case "BOMB_TRANSFER_OK":
                    uiStatus?.SetStatus("Truyền bom thành công.", Color.green);
                    break;

                case "BOMB_TRANSFER_FAIL":
                    uiStatus?.SetStatus((string)response["message"] ?? "Truyền bom thất bại.", Color.red);
                    break;

                case "MATCH_RESULTS":
                    // payload may be nested
                    JObject resultsPayload = (JObject)(response["Payload"] ?? response);
                    JArray resultsArray = (JArray)(resultsPayload["Results"] ?? resultsPayload["results"]);
                    // you can show results in your RankingUI
                    uiStatus?.SetStatus("Match results received.", Color.green);
                    break;
            }
        }
        catch (System.Exception ex)
        {
            Debug.LogError("❌ Lỗi parse JSON: " + ex.Message);
        }
    }

    // If server sends player list as array of objects, convert into MainMenuManager.PlayerData
    private List<MainMenuManager.PlayerData> ParsePlayers(JArray playersArray)
    {
        var list = new List<MainMenuManager.PlayerData>();
        if (playersArray == null) return list;

        foreach (var token in playersArray)
        {
            if (!(token is JObject jo)) continue;

            // name
            string name = jo["Nickname"]?.ToString()
                          ?? jo["username"]?.ToString()
                          ?? jo["Name"]?.ToString()
                          ?? jo["nick"]?.ToString()
                          ?? "Player";

            // ma id
            int maId = 0;
            if (jo["MaID"] != null) int.TryParse(jo["MaID"].ToString(), out maId);
            else if (jo["layerId"] != null) int.TryParse(jo["layerId"].ToString(), out maId);
            else if (jo["PlayerId"] != null) int.TryParse(jo["PlayerId"].ToString(), out maId);

            // selected index (many servers use different keys)
            int selectedIndex = -1;
            if (jo["CharacterIndex"] != null) int.TryParse(jo["CharacterIndex"].ToString(), out selectedIndex);
            else if (jo["SelectedIndex"] != null) int.TryParse(jo["SelectedIndex"].ToString(), out selectedIndex);
            else if (jo["Character"] != null) int.TryParse(jo["Character"].ToString(), out selectedIndex);

            // ready / host flags
            bool isReady = false;
            if (jo["IsReady"] != null) bool.TryParse(jo["IsReady"].ToString(), out isReady);
            else if (jo["DaSanSang"] != null) bool.TryParse(jo["DaSanSang"].ToString(), out isReady);

            bool isHost = false;
            if (jo["IsHost"] != null) bool.TryParse(jo["IsHost"].ToString(), out isHost);
            else if (jo["Host"] != null) bool.TryParse(jo["Host"].ToString(), out isHost);

            // times received bomb if provided
            int timesRecv = 0;
            if (jo["TimesReceivedBomb"] != null) int.TryParse(jo["TimesReceivedBomb"].ToString(), out timesRecv);

            // pick sprite from MainMenuManager if available
            Sprite sprite = null;
            if (selectedIndex >= 0 && mainMenuManager != null && mainMenuManager.characterSprites != null
                && selectedIndex < mainMenuManager.characterSprites.Length)
            {
                sprite = mainMenuManager.characterSprites[selectedIndex];
            }

            var pd = new MainMenuManager.PlayerData
            {
                maId = maId,
                name = name,
                selectedIndex = selectedIndex,
                selectedSprite = sprite,
                isReady = isReady,
                isHost = isHost
            };

            list.Add(pd);
        }

        return list;
    }
}
