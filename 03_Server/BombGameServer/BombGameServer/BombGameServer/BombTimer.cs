using System;
using System.Numerics;
using System.Timers;

namespace BombGameServer
{
    class BombTimer
    {
        private Timer timer;
        private Player currentHolder;
        private double duration = 15000; // 15 giây
        private DateTime startTime;
        private GameServer server;

        public BombTimer(Player holder, GameServer serverRef)
        {
            currentHolder = holder;
            server = serverRef;
            timer = new Timer(duration);
            timer.Elapsed += OnBombExplode;
            StartTimer();
        }

        private void StartTimer()
        {
            startTime = DateTime.Now;
            timer.Interval = duration;
            timer.Start();
        }

        private void OnBombExplode(object sender, ElapsedEventArgs e)
        {
            Console.WriteLine("Bomb exploded! " + currentHolder.Name + " loses.");
            server.BroadcastMessage("PLAYER_OUT", new { player = currentHolder.Name });

            // ✅ Lưu kết quả vào SQL
            server.dbHelper.AddMatch(currentHolder.PlayerId, "Lose");
        }

        public void Reset(Player newHolder)
        {
            double elapsed = (DateTime.Now - startTime).TotalMilliseconds;
            double remaining = duration - elapsed;

            currentHolder = newHolder;

            timer.Stop();

            if (remaining > 3000)
            {
                StartTimer(); // reset lại 15 giây
            }
            else
            {
                timer.Interval = remaining;
                startTime = DateTime.Now;
                timer.Start();
            }

            // ✅ Broadcast kèm thời gian còn lại
            server.BroadcastMessage("PASS_BOMB", new
            {
                from = "?", // bạn có thể truyền tên người chơi cũ nếu cần
                to = newHolder.Name,
                timeLeft = remaining / 1000 // gửi thời gian còn lại tính bằng giây
            });
        }
    }
}
