using UnityEngine;
using TMPro;
using System.Collections.Generic;

public class MatchManager : MonoBehaviour
{
    public float gameTimer = 300f;
    public TextMeshProUGUI timerText;

    void Update()
    {
        if (gameTimer > 0)
        {
            gameTimer -= Time.deltaTime;
            UpdateTimerUI();
        }
        else EndGame();
    }

    void UpdateTimerUI()
    {
        if (timerText == null) return;
        int min = Mathf.FloorToInt(gameTimer / 60);
        int sec = Mathf.FloorToInt(gameTimer % 60);
        timerText.text = string.Format("{0:00}:{1:00}", min, sec);
    }

    void EndGame()
    {
        if (PlayerRegistry.Instance == null) return;
        var players = PlayerRegistry.Instance.GetAll();

        // Sắp xếp: Ai còn IsAlive đứng trước, ai chết đứng sau. Sau đó xét số lần nhận bom.
        players.Sort((a, b) => {
            if (a.IsAlive && !b.IsAlive) return -1;
            if (!a.IsAlive && b.IsAlive) return 1;
            return a.TimesReceivedBomb.CompareTo(b.TimesReceivedBomb);
        });

        Debug.Log("Winner: " + (players.Count > 0 ? players[0].PlayerName : "None"));
    }
}