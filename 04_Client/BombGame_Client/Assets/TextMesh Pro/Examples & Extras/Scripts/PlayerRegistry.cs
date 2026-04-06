using System.Collections.Generic;
using UnityEngine;

public class PlayerRegistry : MonoBehaviour
{
    public static PlayerRegistry Instance { get; private set; }
    private List<PlayerController> players = new List<PlayerController>();

    void Awake()
    {
        if (Instance == null) Instance = this;
        else Destroy(gameObject);
    }

    public void Register(PlayerController p)
    {
        if (!players.Contains(p)) players.Add(p);
    }

    public void Unregister(PlayerController p)
    {
        players.Remove(p);
    }

    public List<PlayerController> GetAll() => players;

    public PlayerController GetById(int id)
    {
        foreach (var player in players)
        {
            if (player.MaId == id)
                return player;
        }
        return null;
    }
}