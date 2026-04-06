using Fusion;
using TMPro;
using UnityEngine;

public class PlayerController : NetworkBehaviour
{
    [Header("UI & Visuals")]
    public TextMeshProUGUI playerNameText;
    public TextMeshProUGUI bombTimerText;
    public GameObject[] characterObjects;

    [Header("Settings")]
    public float moveSpeed = 5f;

    // --- CÁC BIẾN ĐỒNG BỘ MẠNG (QUAN TRỌNG) ---
    [Networked] public NetworkBool IsAlive { get; set; } = true;
    [Networked, Capacity(32)] public string PlayerName { get; set; }
    [Networked] public int TimesReceivedBomb { get; set; }
    [Networked] public NetworkBool hasBomb { get; set; }
    [Networked] public float bombTimeRemaining { get; set; }
    public int MaId { get; set; }

    public override void Spawned()
    {
        if (PlayerRegistry.Instance != null) PlayerRegistry.Instance.Register(this);
        // Hiển thị tên ngay khi vừa sinh ra
        if (playerNameText != null) playerNameText.text = PlayerName;
    }

    public override void Despawned(NetworkRunner runner, bool hasState)
    {
        if (PlayerRegistry.Instance != null) PlayerRegistry.Instance.Unregister(this);
    }

    public override void FixedUpdateNetwork()
    {
        if (HasInputAuthority && IsAlive)
        {
            float h = Input.GetAxisRaw("Horizontal");
            float v = Input.GetAxisRaw("Vertical");
            Vector3 moveDir = new Vector3(h, v, 0).normalized;
            transform.position += moveDir * moveSpeed * Runner.DeltaTime;
        }
        UpdateUI();
    }

    void UpdateUI()
    {
        if (bombTimerText != null)
        {
            bombTimerText.gameObject.SetActive(hasBomb);
            if (hasBomb) bombTimerText.text = bombTimeRemaining.ToString("F1") + "s";
        }
        // Luôn cập nhật text theo biến mạng
        if (playerNameText != null && playerNameText.text != PlayerName)
            playerNameText.text = PlayerName;
    }

    public void InitPlayer(string name, int index)
    {
        this.PlayerName = name;
        this.IsAlive = true;
        this.TimesReceivedBomb = 0;
        ApplyVisualCharacter(index);
    }

    public void ApplyVisualCharacter(int index)
    {
        if (characterObjects == null || characterObjects.Length == 0) return;
        foreach (var obj in characterObjects) if (obj != null) obj.SetActive(false);
        int targetIndex = (index >= 0 && index < characterObjects.Length) ? index : 0;
        if (characterObjects[targetIndex] != null) characterObjects[targetIndex].SetActive(true);
    }

    public void GiveBomb(float timeLeft)
    {
        this.hasBomb = true;
        this.bombTimeRemaining = timeLeft;
        this.TimesReceivedBomb++;
    }

    public static PlayerController GetPlayerById(int toId)
    {
        PlayerController p = null;
        // Use GetAll() instead of Players property
        foreach (var player in PlayerRegistry.Instance.GetAll())
        {
            if (player != null && player.MaId == toId)
            {
                p = player;
                break;
            }
        }
        return p;
    }
}