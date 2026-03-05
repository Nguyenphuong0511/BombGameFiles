using System;
using System.Timers;

namespace BombGameServer
{
    class BombTimer
    {
        // Timer cho quả bom (15s)
        private Timer bombTimer;
        private double bombDuration = 15000; // 15 giây
        private DateTime bombStartTime;

        // Timer cho tổng trận đấu (300s)
        private Timer matchTimer;
        private int matchTimeRemaining = 300; // 5 phút

        private Player currentHolder;
        private GameServer server;

        public BombTimer(Player initialHolder, GameServer serverRef)
        {
            this.currentHolder = initialHolder;
            this.server = serverRef;

            // 1. Khởi tạo Timer cho quả bom (Logic cũ)
            bombTimer = new Timer(bombDuration);
            bombTimer.Elapsed += OnBombExplode;

            // 2. Khởi tạo Timer cho trận đấu (Logic mới 5 phút)
            matchTimer = new Timer(1000); // Chạy mỗi 1 giây để đồng bộ decrement
            matchTimer.Elapsed += OnMatchTick;

            StartGame();
        }

        private void StartGame()
        {
            // Bắt đầu đếm ngược trận đấu
            matchTimer.Start();

            // Bắt đầu đếm ngược quả bom
            bombStartTime = DateTime.Now;
            bombTimer.Interval = bombDuration;
            bombTimer.Start();

            Console.WriteLine("[GAME]: Match started - 300s remaining.");
        }

        // Mỗi giây gửi thời gian về Unity một lần
        private void OnMatchTick(object sender, ElapsedEventArgs e)
        {
            matchTimeRemaining--;

            if (matchTimeRemaining <= 0)
            {
                StopAllTimers();
                Console.WriteLine("[GAME]: Time up! Holder loses: " + currentHolder.TenDangNhap);

                // Kết thúc trận: Người cầm bom cuối cùng thua
                server.BroadcastMessage("GAME_OVER", new
                {
                    reason = "TIME_UP",
                    loserId = currentHolder.MaNguoiChoi,
                    loserName = currentHolder.TenDangNhap
                });

                server.dbHelper.LuuKetQuaTranDau(currentHolder.MaNguoiChoi, "Lose", currentHolder.MaPhong);
                matchTimer.Stop();
                bombTimer.Stop();
                return;
            }
            else
            {
                // Gửi thời gian trận đấu về Unity để hiển thị Slider/Text
                server.BroadcastMessage("MATCH_TICK", new
                {
                    remainingTime = matchTimeRemaining,
                    currentHolderId = currentHolder.MaNguoiChoi
                });

                // bổ sung kênh khác
                server.BroadcastMessage("MATCH_TIME", new { remainingTime = matchTimeRemaining });
            }
        }

        // Logic khi quả bom nổ (Hết 15s mà chưa truyền)
        private void OnBombExplode(object sender, ElapsedEventArgs e)
        {
            StopAllTimers();
            Console.WriteLine("[BOMB]: Exploded! " + currentHolder.TenDangNhap + " loses.");

            server.BroadcastMessage("PLAYER_OUT", new
            {
                player = currentHolder.TenDangNhap,
                playerId = currentHolder.MaNguoiChoi
            });

            server.dbHelper.LuuKetQuaTranDau(currentHolder.MaNguoiChoi, "Lose", currentHolder.MaPhong);
        }

        // Logic truyền bom (Giữ nguyên logic thời gian cũ của bạn)
        public void Reset(Player newHolder)
        {
            if (currentHolder != null) currentHolder.CoBom = false;
            currentHolder = newHolder;
            currentHolder.CoBom = true;

            double elapsed = (DateTime.Now - bombStartTime).TotalMilliseconds;
            double remaining = bombTimer.Interval - elapsed;

            bombTimer.Stop();

            if (remaining > 3000)
            {
                bombTimer.Interval = bombDuration;
            }
            else
            {
                bombTimer.Interval = Math.Max(1000, remaining);
            }

            bombStartTime = DateTime.Now;
            bombTimer.Start();

            server.BroadcastMessage("BOMB_PASSED", new
            {
                toId = newHolder.MaNguoiChoi,
                toName = newHolder.TenDangNhap,
                bombTimeLeft = bombTimer.Interval / 1000,
                isRandom = true
            });
        }

        public void StopAllTimers()
        {
            bombTimer?.Stop();
            matchTimer?.Stop();
            Console.WriteLine("[TIMER]: Tất cả bộ đếm đã dừng.");
        }
    }
}