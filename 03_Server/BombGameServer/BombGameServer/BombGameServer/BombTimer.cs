using System;
using System.Timers;

namespace BombGameServer
{
    class BombTimer
    {
        private Timer bombTimer;
        private double bombDuration = 15000; // 15 seconds
        private DateTime bombStartTime;

        private Timer matchTimer;
        private int matchTimeRemaining = 300; // 5 minutes

        private Player currentHolder;
        private GameServer server;
        private int roomId;

        public BombTimer(Player initialHolder, GameServer serverRef)
        {
            this.currentHolder = initialHolder;
            this.server = serverRef;
            this.roomId = initialHolder.MaPhong;

            bombTimer = new Timer(bombDuration);
            bombTimer.Elapsed += OnBombExplode;

            matchTimer = new Timer(1000);
            matchTimer.Elapsed += OnMatchTick;

            StartGame();
        }

        private void StartGame()
        {
            matchTimer.Start();

            bombStartTime = DateTime.Now;
            bombTimer.Interval = bombDuration;
            bombTimer.Start();

            Console.WriteLine("[GAME]: Match started - 300s remaining.");

            // inform clients who initially holds the bomb
            server.BroadcastToRoom(roomId, "BOMB_PASSED", new
            {
                toId = currentHolder.MaNguoiChoi,
                toName = currentHolder.DisplayName,
                bombTimeLeft = bombTimer.Interval / 1000.0,
                isRandom = true
            });
        }

        private void OnMatchTick(object sender, ElapsedEventArgs e)
        {
            matchTimeRemaining--;

            if (matchTimeRemaining <= 0)
            {
                StopAllTimers();
                Console.WriteLine("[GAME]: Time up! Holder loses: " + currentHolder.DisplayName);

                server.BroadcastMessage("GAME_OVER", new
                {
                    reason = "TIME_UP",
                    loserId = currentHolder.MaNguoiChoi,
                    loserName = currentHolder.DisplayName
                });

                server.dbHelper.LuuNhatKyHoatDong(currentHolder.MaNguoiChoi, "THUA DO HẾT THỜI GIAN", currentHolder.MaPhong, currentHolder.ToaDo);

                // mark holder as out (server state)
                currentHolder.DangTrongTran = false;

                // Persist match results to DB (server computes ranking from in-memory counters)
                server.SaveMatchResults(roomId);

                matchTimer.Stop();
                bombTimer.Stop();
                return;
            }
            else
            {
                server.BroadcastMessage("MATCH_TICK", new
                {
                    remainingTime = matchTimeRemaining,
                    currentHolderId = currentHolder.MaNguoiChoi
                });

                server.BroadcastMessage("MATCH_TIME", new { remainingTime = matchTimeRemaining });
            }
        }

        private void OnBombExplode(object sender, ElapsedEventArgs e)
        {
            StopAllTimers();
            Console.WriteLine("[BOMB]: Exploded! " + currentHolder.DisplayName + " loses.");

            // mark current holder out
            currentHolder.DangTrongTran = false;

            server.BroadcastMessage("PLAYER_OUT", new
            {
                player = currentHolder.DisplayName,
                playerId = currentHolder.MaNguoiChoi
            });

            server.dbHelper.LuuNhatKyHoatDong(currentHolder.MaNguoiChoi, "THUA DO NỔ BOM", currentHolder.MaPhong, currentHolder.ToaDo);

            // Persist final match results now that someone lost by explosion (match ends here)
            server.SaveMatchResults(roomId);
        }

        public void Reset(Player newHolder)
        {
            if (currentHolder != null) currentHolder.CoBom = false;
            var previousHolder = currentHolder;
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

            // broadcast transfer to room
            server.BroadcastToRoom(roomId, "BOMB_PASSED", new
            {
                toId = newHolder.MaNguoiChoi,
                toName = newHolder.DisplayName,
                bombTimeLeft = bombTimer.Interval / 1000.0,
                isRandom = false,
                fromId = previousHolder?.MaNguoiChoi
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