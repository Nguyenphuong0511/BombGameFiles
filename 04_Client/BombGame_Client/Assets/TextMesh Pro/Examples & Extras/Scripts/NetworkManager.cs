using UnityEngine;
using System.Net.Sockets;
using System;
using System.Text;
using System.Collections.Concurrent;

public class NetworkManager : MonoBehaviour
{
    private TcpClient client;

    // Nếu chạy cùng máy với server thì để "127.0.0.1"
    // Nếu chạy khác máy thì nhập IPv4 thật của server (ví dụ "192.150.31.103")
    [Header("Cấu hình Server")]
    public string serverIP = "127.0.0.1";
    public int serverPort = 8888;

    public event Action<string> OnMessage;

    private NetworkStream stream;
    private readonly ConcurrentQueue<string> mainThreadQueue = new ConcurrentQueue<string>();
    private byte[] readBuffer;
    private string recvBuffer = "";

    void Start()
    {
        Debug.Log($"[CLIENT]: Đang thử kết nối tới {serverIP}:{serverPort} ...");
        ConnectToServer();
    }

    public void ConnectToServer()
    {
        try
        {
            client = new TcpClient();

            var result = client.BeginConnect(serverIP, serverPort, null, null);
            bool success = result.AsyncWaitHandle.WaitOne(TimeSpan.FromSeconds(5));

            if (!success)
                throw new Exception($"⏱ Timeout khi kết nối {serverIP}:{serverPort} (quá 5 giây).");

            client.EndConnect(result);

            Debug.Log($"<color=green>✅ Kết nối Server thành công tại {serverIP}:{serverPort}!</color>");

            stream = client.GetStream();
            readBuffer = new byte[4096];
            BeginRead();
        }
        catch (Exception e)
        {
            Debug.LogError("❌ Lỗi kết nối: " + e.Message);
        }
    }

    public void SendData(string message)
    {
        try
        {
            if (client != null && client.Connected)
            {
                if (stream == null) stream = client.GetStream();

                string line = message.EndsWith("\n") ? message : message + "\n";
                byte[] data = Encoding.UTF8.GetBytes(line);
                stream.Write(data, 0, data.Length);
                Debug.Log("📤 Gửi lên Server: " + message);
            }
        }
        catch (Exception e)
        {
            Debug.LogError("❌ Lỗi gửi tin: " + e.Message);
        }
    }

    private void BeginRead()
    {
        try
        {
            if (stream == null || !stream.CanRead) return;
            stream.BeginRead(readBuffer, 0, readBuffer.Length, OnRead, null);
        }
        catch (Exception e)
        {
            Debug.LogError("❌ Lỗi bắt đầu nhận dữ liệu: " + e.Message);
        }
    }

    private void OnRead(IAsyncResult ar)
    {
        try
        {
            if (stream == null) return;

            int bytesRead = stream.EndRead(ar);
            if (bytesRead <= 0) return;

            recvBuffer += Encoding.UTF8.GetString(readBuffer, 0, bytesRead);

            while (true)
            {
                int nl = recvBuffer.IndexOf('\n');
                if (nl < 0) break;

                string line = recvBuffer.Substring(0, nl).TrimEnd('\r');
                recvBuffer = recvBuffer.Substring(nl + 1);

                if (!string.IsNullOrWhiteSpace(line))
                    mainThreadQueue.Enqueue(line);
            }

            BeginRead();
        }
        catch (ObjectDisposedException)
        {
            // ignore on shutdown
        }
        catch (Exception e)
        {
            Debug.LogError("❌ Lỗi nhận dữ liệu: " + e.Message);
        }
    }

    private void Update()
    {
        while (mainThreadQueue.TryDequeue(out var msg))
        {
            Debug.Log("📥 Nhận từ Server: " + msg);
            OnMessage?.Invoke(msg);
        }
    }

    private void OnDestroy()
    {
        try { stream?.Close(); } catch { }
        try { client?.Close(); } catch { }
        stream = null;
        client = null;
    }
}
