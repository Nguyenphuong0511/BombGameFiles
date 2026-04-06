using UnityEngine;
using System.Collections.Generic;

public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }
    public GameObject playerPrefab;
    public Transform[] spawnPoints;

    void Awake() { Instance = this; }

    public void SpawnPlayers(List<MainMenuManager.PlayerData> players)
    {
        if (playerPrefab == null || spawnPoints == null) return;

        for (int i = 0; i < players.Count; i++)
        {
            if (i >= spawnPoints.Length) break;

            GameObject go = Instantiate(playerPrefab, spawnPoints[i].position, Quaternion.identity);
            PlayerController pc = go.GetComponent<PlayerController>();

            if (pc != null)
            {
                // Truyền đúng 2 tham số: Tên và Index nhân vật
                pc.InitPlayer(players[i].name, players[i].selectedIndex);
            }
        }
    }
}