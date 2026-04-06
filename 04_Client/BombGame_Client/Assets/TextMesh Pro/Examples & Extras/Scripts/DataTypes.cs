using UnityEngine;

public class DataTypes : MonoBehaviour
{
    [System.Serializable]
    public class GameRequest
    {
        public string Command; // LOGIN, REGISTER, SELECT_CHAR, CREATE_ROOM...
        public string Username;
        public string Password;
        public string Character;
        public int RoomID;
    }
}
