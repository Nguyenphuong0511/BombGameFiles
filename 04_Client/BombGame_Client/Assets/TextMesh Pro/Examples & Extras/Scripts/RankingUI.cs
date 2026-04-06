using UnityEngine;
using UnityEngine.UI;
using System.Collections.Generic;
using TMPro; // added for TextMeshProUGUI

public class RankingUI : MonoBehaviour
{
    [Header("UI thành phần")]
    public GameObject rankingPanel;          // Panel chứa bảng xếp hạng
    public TextMeshProUGUI rankingTitleText; // Tiêu đề "KẾT QUẢ TRẬN ĐẤU"
    public TextMeshProUGUI rankingListText;  // Danh sách xếp hạng
    public Button exitButton;                // Nút thoát
    public Button replayButton;              // Nút chơi lại

    void Start()
    {
        // Ẩn bảng khi chưa dùng
        rankingPanel.SetActive(false);

        // Gán sự kiện nút
        exitButton.onClick.AddListener(OnExitClicked);
        replayButton.onClick.AddListener(OnReplayClicked);
    }

    // Hàm hiển thị bảng xếp hạng
    public void ShowRanking(List<PlayerController> players)
    {
        rankingPanel.SetActive(true);
        rankingTitleText.text = "🏆 KẾT QUẢ TRẬN ĐẤU 🏆";

        // Tách người sống và chết (use existing field 'isAlive')
        List<PlayerController> alive = players.FindAll(p => p.IsAlive.Equals(true));
        List<PlayerController> dead = players.FindAll(p => p.IsAlive.Equals(false));

        // Sắp xếp người sống theo số lần bị truyền bom (use TimesReceivedBomb on PlayerController)
        alive.Sort((a, b) => a.TimesReceivedBomb.CompareTo(b.TimesReceivedBomb));

        string result = "";
        int rank = 1;

        foreach (var p in alive)
        {
            result += $"Hạng {rank}: {p.playerNameText.text} (sống, bị truyền {GetTimesReceivedBomb(p)} lần)\n";
            rank++;
        }

        foreach (var p in dead)
        {
            result += $"Thua: {p.playerNameText.text}\n";
        }

        rankingListText.text = result;
    }

    void OnExitClicked()
    {
        Debug.Log("Thoát về menu...");
        // TODO: Load scene menu hoặc rời phòng
    }

    void OnReplayClicked()
    {
        Debug.Log("Chơi lại trận mới...");
        // TODO: Reset game hoặc reload scene
    }

    // Helper method to get TimesReceivedBomb from PlayerController
    private int GetTimesReceivedBomb(PlayerController player)
    {
        return player != null ? player.TimesReceivedBomb : 0;
    }
}
