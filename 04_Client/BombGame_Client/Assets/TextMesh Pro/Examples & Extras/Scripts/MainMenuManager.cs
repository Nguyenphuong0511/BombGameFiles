using Newtonsoft.Json;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

public class MainMenuManager : MonoBehaviour
{
    public AuthManager authManager;
    [Header("--- PANELS ---")]
    public GameObject panelMain;
    public GameObject panelCreateRoom;
    public GameObject panelJoinRoom;
    public GameObject panelCharSelect;
    public GameObject panelLobby;

    [Header("--- INPUTS ---")]
    public TMP_InputField inpNickname;
    public TMP_InputField inpRoomName;    // Nhập tên phòng (khi tạo)
    public TMP_InputField inpMaxPlayers; // Nhập số người 4-10 (khi tạo)
    public TMP_InputField inpRoomCode;   // Nhập mã phòng (khi tham gia)

    [Header("--- CHARACTER SELECT ---")]
    public Image imgBigPreview;                // Ảnh preview lớn
    public Sprite[] characterSprites;          // Danh sách sprite nhân vật
    public List<Image> charSlotsUI = new List<Image>(); // Các slot nhân vật (chọn)
    public Color normalColor = Color.white;    // Màu bình thường
    public Color selectedColor = Color.yellow; // Màu khi chọn
    public static string chosenNickname;
    public static int chosenCharacterIndex;

    [Header("--- LOBBY UI ---")]
    public TextMeshProUGUI TxtLobbyID;        // will show "GAME LOBBY: #xxxx"
    public TextMeshProUGUI txtPlayerCount;    // will show "PLAYERS: x / y"
    public List<GameObject> playerSlots = new List<GameObject>();
    // NEW: explicit Image references for the character image inside each player slot.
    // Assign these in inspector (Element 0 -> Slot_01's character Image, etc.)
    public List<Image> playerSlotCharImages = new List<Image>();

    public Button btnStart;
    public Button btnReady;
    public TextMeshProUGUI txtReadyBtn;
    public TextMeshProUGUI txtLobbyRoomCode;

    private int currentSelectedIndex = -1;
    private string currentRoomCode = "";
    private bool isHost = false;
    private bool iAmReady = false;
    private int currentMaxPlayers = 10;

    [Header("--- BUTTONS ---")]
    public Button btnOpenCreatePanel;
    public Button btnOpenJoinPanel;

    public AuthUIHandler authUIHandler; // prefer direct reference to the UI handler
    private string loggedInUsername = "";

    // Host initiated start flag so gameplay can know who triggered start locally (useful for testing)
    public static bool LocalHostInitiatedStart = false;

    void Start()
    {
        // ensure we have a usable authUIHandler reference
        if (authUIHandler == null && authManager != null)
            authUIHandler = authManager.authUIHandler;
        if (authUIHandler == null && AuthUIHandler.Instance != null)
            authUIHandler = AuthUIHandler.Instance;

        // Lắng nghe sự kiện thay đổi text của Nickname và submit (Enter)
        if (inpNickname != null)
        {
            inpNickname.onValueChanged.AddListener(delegate { CheckNickname(); });
            inpNickname.onEndEdit.AddListener((s) => {
                // gửi update nickname khi người chơi kết thúc edit (nhấn Enter hoặc rời focus)
                UpdateNickname();
            });
        }

        // Khởi đầu kiểm tra luôn
        CheckNickname();
    }

    // Called by AuthUIHandler when room created successfully
    public void OnRoomCreated(string roomCode, bool youAreHost, int maxPlayers)
    {
        currentRoomCode = roomCode ?? "";
        isHost = youAreHost;
        currentMaxPlayers = (maxPlayers > 0) ? maxPlayers : currentMaxPlayers;
        if (TxtLobbyID != null) TxtLobbyID.text = "GAME LOBBY: #" + currentRoomCode;
    }

    // Called by AuthUIHandler when joined room
    public void OnRoomJoined(string roomCode, int maxPlayers)
    {
        currentRoomCode = roomCode ?? "";
        currentMaxPlayers = (maxPlayers > 0) ? maxPlayers : currentMaxPlayers;
        if (TxtLobbyID != null) TxtLobbyID.text = "GAME LOBBY: #" + currentRoomCode;
    }

    public void BackToMain()
    {
        panelCreateRoom.SetActive(false);
        panelJoinRoom.SetActive(false);
        panelCharSelect.SetActive(false);
        panelLobby.SetActive(false);

        panelMain.SetActive(true);
    }

    public void CheckNickname()
    {
        bool hasNickname = !string.IsNullOrWhiteSpace(inpNickname?.text);

        if(btnOpenCreatePanel != null) btnOpenCreatePanel.interactable = hasNickname;
        if(btnOpenJoinPanel != null) btnOpenJoinPanel.interactable = hasNickname;
    }

    // Hàm gọi khi Login thành công từ AuthUIHandler
    public void LoginSuccess(string username)
    {
        // ensure we have the correct handler
        if (authUIHandler == null && authManager != null)
            authUIHandler = authManager.authUIHandler;

        if (inpNickname != null) inpNickname.text = username;
        // prefer authoritative username from authUIHandler when available (login name)
        loggedInUsername = username ?? "";

        if (authManager != null && authManager.panelAuth != null)
        {
            authManager.panelAuth.SetActive(false);
        }

        if (panelMain != null) panelMain.SetActive(true);

        if (panelCreateRoom != null) panelCreateRoom.SetActive(false);
        if (panelJoinRoom != null) panelJoinRoom.SetActive(false);
        if (panelLobby != null) panelLobby.SetActive(false);

        // Immediately sync UI-modified fields back to server so SQL reflects UI changes
        // 1) Update nickname on server
        UpdateNickname();

        // 2) Ensure we have a selected index (try to infer from preview if needed)
        if (currentSelectedIndex == -1 && imgBigPreview != null && imgBigPreview.sprite != null && characterSprites != null)
        {
            for (int i = 0; i < characterSprites.Length; i++)
            {
                if (characterSprites[i] == imgBigPreview.sprite)
                {
                    currentSelectedIndex = i;
                    break;
                }
            }
        }

        // 3) If a character is selected locally, tell the server (so DB keeps the latest choice)
        if (currentSelectedIndex != -1)
        {
            SendSelectCharToServer(currentSelectedIndex);
        }

        // 4) If there's a room code in the input field, attempt to re-join using the UI value
        if (!string.IsNullOrWhiteSpace(inpRoomCode?.text) && authUIHandler != null)
        {
            string rc = inpRoomCode.text.Trim();
            authUIHandler.JoinRoom(inpNickname?.text?.Trim() ?? username, rc);
        }
    }

    public void UpdateNickname()
    {
        string nick = inpNickname?.text?.Trim() ?? "";
        if (string.IsNullOrEmpty(nick)) return;

        // Always include both MaID and Username (login name) so server can resolve client even if one missing
        var data = new
        {
            Action = "UPDATE_NICKNAME",
            Nickname = nick,
            MaID = authUIHandler?.ma ?? 0,
            Username = loggedInUsername
        };
        authUIHandler?.networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");
    }

    public void OnClickOpenCreate()
    {
        if (string.IsNullOrWhiteSpace(inpNickname.text))
        {
            authUIHandler?.uiStatus?.SetStatus("Hãy nhập Nickname trước!", Color.yellow);
            return;
        }
        panelMain.SetActive(false);
        panelCreateRoom.SetActive(true);
    }

    public void OnClickOpenJoin()
    {
        string nick = inpNickname.text.Trim();
        string code = inpRoomCode.text.Trim();

        if (string.IsNullOrEmpty(nick) || string.IsNullOrEmpty(code))
        {
            authUIHandler?.uiStatus?.SetStatus("Vui lòng nhập tên và mã phòng!", Color.red);
            return;
        }

        // proactively update nickname on server before joining
        UpdateNickname();

        if (authUIHandler != null)
        {
            // include Username for server resolution
            authUIHandler.JoinRoom(nick, code);
            authUIHandler.uiStatus?.SetStatus("Đang kiểm tra phòng...", Color.white);
        }
        panelMain.SetActive(false);
        panelJoinRoom.SetActive(true);
    }

    // Gửi lệnh tạo phòng lên Server
    public void ConfirmCreateRoom()
    {
        int max = int.Parse(inpMaxPlayers.text);
        if (max < 4 || max > 10) return;

        string nick = string.IsNullOrWhiteSpace(inpNickname.text) ? "" : inpNickname.text.Trim();

        // proactively update nickname on server before creating
        UpdateNickname();

        var data = new
        {
            Action = "CREATE_ROOM",
            Nickname = nick,
            RoomName = inpRoomName.text,
            MaxPlayers = max,
            MaID = authUIHandler?.ma ?? 0,
            Username = loggedInUsername
        };

        authUIHandler?.networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");
        panelCreateRoom.SetActive(false);
        panelCharSelect.SetActive(true);
    }

    public void ConfirmJoinRoom()
    {
        if (panelJoinRoom != null) panelJoinRoom.SetActive(false);
        if (panelLobby != null) panelLobby.SetActive(true);

        if (TxtLobbyID != null) TxtLobbyID.text = "GAME LOBBY: #" + currentRoomCode;

        panelJoinRoom.SetActive(false);
        panelCharSelect.SetActive(true);
    }

    public void PreviewCharacter(int index)
    {
        if (index < 0 || index >= characterSprites.Length) return;

        currentSelectedIndex = index;

        if (imgBigPreview != null)
            imgBigPreview.sprite = characterSprites[index];

        for (int i = 0; i < charSlotsUI.Count; i++)
        {
            if (charSlotsUI[i] != null)
                charSlotsUI[i].color = (i == index) ? selectedColor : normalColor;
        }
    }

    public void ConfirmSelection()
    {
        if (currentSelectedIndex == -1)
        {
            if (imgBigPreview != null && imgBigPreview.sprite != null && characterSprites != null)
            {
                for (int i = 0; i < characterSprites.Length; i++)
                {
                    if (characterSprites[i] == imgBigPreview.sprite)
                    {
                        currentSelectedIndex = i;
                        break;
                    }
                }
            }
        }

        if (currentSelectedIndex == -1) return;

        panelCharSelect.SetActive(false);
        panelLobby.SetActive(true);

        if (txtLobbyRoomCode != null)
            txtLobbyRoomCode.text = "PHÒNG: #" + currentRoomCode;

        iAmReady = isHost;

        var data = new
        {
            Action = "SELECT_CHAR",
            CharacterIndex = currentSelectedIndex,
            Nickname = inpNickname?.text?.Trim() ?? "",
            RoomCode = currentRoomCode,
            MaID = authUIHandler?.ma ?? 0,
                Username = loggedInUsername
        };
        authUIHandler?.networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");

        var local = new List<PlayerData>
        {
            new PlayerData
            {
                name = inpNickname?.text?.Trim() ?? "Player",
                selectedIndex = currentSelectedIndex,
                selectedSprite = (currentSelectedIndex >=0 && currentSelectedIndex < characterSprites.Length) ? characterSprites[currentSelectedIndex] : null,
                isReady = iAmReady,
                isHost = isHost
            }
        };
        UpdateLobbyUI(local, 1, currentMaxPlayers, currentRoomCode);
    }
    public void OnConfirmSelection(string nickname, int characterIndex)
    {
        chosenNickname = nickname;
        chosenCharacterIndex = characterIndex;
        UnityEngine.SceneManagement.SceneManager.LoadScene("GamePlay");
    }
    private void SimulateLobbyData()
    {
        Debug.Log("[LOBBY]: Đang giả lập dữ liệu người chơi...");
    }

    [Header("--- LOBBY ICONS ---")]
    public Image iconReady; // Kéo thả Sprite tích xanh vào đây
    public Image iconHost;  // Kéo thả Sprite ngôi sao vào đây
    public void UpdateLobbyUI(List<PlayerData> playerList, int currentCount = -1, int maxCount = -1, string roomCode = null)
    {
        foreach (var slot in playerSlots)
        {
            slot.SetActive(false);
        }

        bool allReady = true;
        bool iAmHostLocal = false;
        bool iAmReadyLocal = false;

        for (int i = 0; i < playerList.Count; i++)
        {
            if (i >= playerSlots.Count) break;

            var data = playerList[i];
            var slot = playerSlots[i];
            slot.SetActive(true);

            var txts = slot.GetComponentsInChildren<TextMeshProUGUI>(true);
            if (txts != null && txts.Length > 0)
            {
                TextMeshProUGUI nameField = null;
                foreach (var t in txts) if (t.name.ToLower().Contains("name") || t.name.ToLower().Contains("txt")) { nameField = t; break; }
                if (nameField == null) nameField = txts[0];
                nameField.text = data.name;
            }

            // FIRST: use explicit inspector-assigned Image for char if provided
            Image charImage = null;
            if (playerSlotCharImages != null && playerSlotCharImages.Count > i && playerSlotCharImages[i] != null)
            {
                charImage = playerSlotCharImages[i];
            }

            // If not assigned, fallback to searching children
            if (charImage == null)
            {
                Image hostImage = null;
                Image readyImage = null;
                Image[] images = slot.GetComponentsInChildren<Image>(true);
                foreach (var img in images)
                {
                    var n = img.gameObject.name.ToLowerInvariant();
                    if (charImage == null && (
                            n == "imgchar" || n == "img_char" ||
                            n.Contains("character") ||
                            n.Contains("char") ||
                            n == "imgavatar" || n.Contains("avatar") // <-- add avatar detection
                        ))
                    {
                        // avoid picking host/ready icons accidentally
                        if (!n.Contains("host") && !n.Contains("ready"))
                            charImage = img;
                        continue;
                    }
                    if (hostImage == null && (n == "imghosticon" || n == "img_hosticon" || n.Contains("host")))
                    {
                        hostImage = img;
                        continue;
                    }
                    if (readyImage == null && (n == "imgreadytick" || n == "img_readystatus" || n.Contains("ready")))
                    {
                        readyImage = img;
                        continue;
                    }
                }

                if (charImage == null)
                {
                    var t = slot.transform.Find("ImgChar") ?? slot.transform.Find("Img_Char") ?? slot.transform.Find("CharacterImage") ?? slot.transform.Find("ImgCharacter");
                    if (t != null) charImage = t.GetComponent<Image>();
                }

                // hostImage / readyImage handled below by separate discovery block (reuse images array)
                // find hostImage and readyImage if still null
                // (we already attempted to set them in the loop above)
            }

            // try to resolve sprite to show
            Sprite spriteToUse = data.selectedSprite;
            if (spriteToUse == null && data.selectedIndex >= 0 && characterSprites != null && data.selectedIndex < characterSprites.Length)
            {
                spriteToUse = characterSprites[data.selectedIndex];
            }

            Debug.Log($"UpdateLobbyUI: slot#{i} player='{data.name}' selectedIndex={data.selectedIndex} spriteToUse={(spriteToUse!=null?spriteToUse.name:"null")} charImage={(charImage!=null?charImage.gameObject.name:"null")}");

            // Assign the sprite to the slot image so selected character is visible in lobby
            if (charImage != null)
            {
                if (spriteToUse != null)
                {
                    charImage.type = Image.Type.Simple;
                    charImage.sprite = spriteToUse;
                    charImage.preserveAspect = true;
                    charImage.color = Color.white;
                    charImage.enabled = true;
                    charImage.gameObject.SetActive(true);
                }
                else
                {
                    charImage.sprite = null;
                    charImage.enabled = false;
                    charImage.gameObject.SetActive(false);
                }
            }

            // find & set host / ready images if present (best-effort)
            Image hostImg = null;
            Image readyImg = null;
            var allImages = slot.GetComponentsInChildren<Image>(true);
            foreach (var img in allImages)
            {
                var nm = img.gameObject.name.ToLowerInvariant();
                if (hostImg == null && (nm == "imghosticon" || nm == "img_hosticon" || nm.Contains("host"))) hostImg = img;
                if (readyImg == null && (nm == "imgreadytick" || nm == "img_readystatus" || nm.Contains("ready"))) readyImg = img;
            }

            if (hostImg != null)
            {
                hostImg.gameObject.SetActive(data.isHost);
                if (data.isHost && iconHost != null) hostImg.sprite = iconHost.sprite;
            }

            if (readyImg != null)
            {
                readyImg.gameObject.SetActive(data.isReady);
                if (data.isReady && iconReady != null) readyImg.sprite = iconReady.sprite;
            }

            if (!data.isReady && !data.isHost) allReady = false;

            // Robust local-player detection: prefer MaID (server id), fall back to login username or displayed nickname
            bool isLocal = false;
            if (authUIHandler != null)
            {
                if (data.maId > 0 && authUIHandler.ma > 0 && data.maId == authUIHandler.ma) isLocal = true;
            }
            if (!isLocal && !string.IsNullOrEmpty(loggedInUsername) && data.name == loggedInUsername) isLocal = true;
            if (!isLocal && !string.IsNullOrEmpty(inpNickname?.text) && data.name == inpNickname.text) isLocal = true;

            if (isLocal)
            {
                iAmHostLocal = data.isHost;
                iAmReadyLocal = data.isReady;
                // keep local isHost state consistent with server
                isHost = data.isHost;
            }
        }

        int usedCurrent = (currentCount >= 0) ? currentCount : playerList.Count;
        int usedMax = (maxCount > 0) ? maxCount : currentMaxPlayers;

        if (txtPlayerCount != null)
            txtPlayerCount.text = "PLAYERS: " + usedCurrent + " / " + usedMax;

        if (!string.IsNullOrEmpty(roomCode))
        {
            currentRoomCode = roomCode;
        }
        if (TxtLobbyID != null) TxtLobbyID.text = "GAME LOBBY: #" + currentRoomCode;
        if (txtLobbyRoomCode != null) txtLobbyRoomCode.text = "PHÒNG: #" + currentRoomCode;

        if (iAmHostLocal)
        {
            if (btnStart != null) btnStart.gameObject.SetActive(true);
            if (btnReady != null) btnReady.gameObject.SetActive(false);
            // FOR TESTING: allow start when >= 1 player; revert to 4 for production
            if (btnStart != null) btnStart.interactable = (usedCurrent >= 1 && allReady);

            // ensure Start button label says START
            if (btnStart != null)
            {
                var startLabel = btnStart.GetComponentInChildren<TextMeshProUGUI>();
                if (startLabel != null) startLabel.text = "START";
            }
        }
        else
        {
            if (btnStart != null) btnStart.gameObject.SetActive(false);
            if (btnReady != null) btnReady.gameObject.SetActive(true);
        }
    }

    // Khi người chơi bấm Ready
    public void OnClickReady()
    {
        iAmReady = !iAmReady;

        var data = new
        {
            Action = "READY",
            IsReady = iAmReady,
            Nickname = inpNickname?.text?.Trim() ?? "",
            RoomCode = currentRoomCode,
            MaID = authUIHandler?.ma ?? 0,
            Username = loggedInUsername
        };
        authUIHandler?.networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");
    }

    public void OnClickStart()
    {
        // mark that the local host initiated start (useful for testing or local-only logic)
        LocalHostInitiatedStart = true;

        var data = new { Action = "START_GAME", MaID = authUIHandler?.ma ?? 0, Username = loggedInUsername };
        authUIHandler?.networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");

        chosenNickname = inpNickname?.text?.Trim() ?? "Player";
        chosenCharacterIndex = currentSelectedIndex;

        // Load GamePlay immediately (server will also broadcast GAME_START)
        UnityEngine.SceneManagement.SceneManager.LoadScene("GamePlay");
    }

    private void SendSelectCharToServer(int index)
    {
        var data = new
        {
            Action = "SELECT_CHAR",
            CharacterIndex = index,
            SelectedCharacterName = (index >= 0 && index < characterSprites.Length) ? characterSprites[index].name : null,
            Nickname = inpNickname?.text?.Trim() ?? "",
            RoomCode = currentRoomCode,
            MaID = authUIHandler?.ma ?? 0,
            Username = loggedInUsername
        };
        authUIHandler?.networkManager?.SendData(JsonConvert.SerializeObject(data) + "\n");
    }

    public class PlayerData
    {
        public int maId; // server id mapping
        public string name;
        public Sprite selectedSprite;
        public int selectedIndex = -1; // added: canonical index representation
        public bool isReady;
        public bool isHost;
    }

    public void LeaveLobbyToCharSelect()
    {
        panelLobby.SetActive(false);
        panelCharSelect.SetActive(true);
    }

}
