using System;
using SplatoonC.Core;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace SplatoonC.Gameplay.Combat
{
    // S3「標記」(tracked,§4.4 / §2.11):被標記的角色對「標記方全隊」穿牆可見(輪廓 + 頭上標記 + 每位隊友一條指向線),
    // 被標記者自己也知道(HUD「被標記」)。bot 感知把被標記的敵人視為已知位置。
    // 每個目標 × 每支標記隊伍一個計時器;再次標記取剩餘較長者(refresh = max)。死亡(InklingModifiers 訂閱 Died)與換場景清除。
    // 靜態、固定陣列,穩態零配置。來源:Point Sensor 480F、Ink Mine 300F、Angle Shooter 300F(從丟出起算)、Wave Breaker 480F。
    public static class Marking
    {
        public const float PointSensorSeconds = 480f / 60f;
        public const float InkMineSeconds = 300f / 60f;
        public const float AngleShooterSeconds = 300f / 60f;
        public const float WaveBreakerSeconds = 480f / 60f;

        // 本機玩家被標記 / 我方標記到敵人時的提示音(新 id,待合成)
        public const string TrackedSound = "mark_tracked";
        public const string PingSound = "mark_ping";

        private struct Entry
        {
            public Inkling Target;
            public float AlphaUntil;
            public float BravoUntil;
        }

        private static Entry[] _entries = new Entry[16];
        private static int _count;

        // 新被某隊標記(原本沒被該隊標記)的瞬間:目標, 標記方隊伍
        public static event Action<Inkling, Team> Marked;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            ClearAll();
            Marked = null;
            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        // 換場景 = 比賽重置:舊場景的角色都沒了
        private static void OnSceneUnloaded(Scene scene)
        {
            ClearAll();
        }

        // 標記 target(byTeam 方看得到),持續 seconds 秒;已被同隊標記時取剩餘較長者
        public static void Apply(Inkling target, Team byTeam, float seconds)
        {
            if (target == null || byTeam == Team.None || byTeam == target.Team || seconds <= 0f)
            {
                return;
            }
            var life = target.GetComponent<InklingLifecycle>();
            if (life != null && life.IsDead)
            {
                return;
            }
            float now = Time.time;
            int index = FindOrCreate(target);
            float until = now + seconds;
            bool wasMarked;
            if (byTeam == Team.Alpha)
            {
                wasMarked = _entries[index].AlphaUntil > now;
                _entries[index].AlphaUntil = Mathf.Max(_entries[index].AlphaUntil, until);
            }
            else
            {
                wasMarked = _entries[index].BravoUntil > now;
                _entries[index].BravoUntil = Mathf.Max(_entries[index].BravoUntil, until);
            }
            if (wasMarked)
            {
                return;
            }
            MarkIndicator.Ensure(target);
            PlayCue(target, byTeam);
            Marked?.Invoke(target, byTeam);
        }

        // viewerTeam 這一方看不看得到 target 的標記
        public static bool IsMarked(Inkling target, Team viewerTeam)
        {
            return Remaining(target, viewerTeam) > 0f;
        }

        // viewerTeam 對 target 的標記剩餘秒數(沒有 = 0)
        public static float Remaining(Inkling target, Team viewerTeam)
        {
            if (target == null || viewerTeam == Team.None || viewerTeam == target.Team)
            {
                return 0f;
            }
            int index = IndexOf(target);
            if (index < 0)
            {
                return 0f;
            }
            float until = viewerTeam == Team.Alpha ? _entries[index].AlphaUntil : _entries[index].BravoUntil;
            return Mathf.Max(0f, until - Time.time);
        }

        // 被敵方標記中(被標記者自己的 HUD 用)
        public static bool IsMarkedByEnemy(Inkling target)
        {
            return target != null && IsMarked(target, TeamUtil.Opponent(target.Team));
        }

        public static float RemainingByEnemy(Inkling target)
        {
            return target != null ? Remaining(target, TeamUtil.Opponent(target.Team)) : 0f;
        }

        public static void Clear(Inkling target)
        {
            int index = IndexOf(target);
            if (index >= 0)
            {
                _entries[index].AlphaUntil = 0f;
                _entries[index].BravoUntil = 0f;
            }
        }

        public static void ClearAll()
        {
            Array.Clear(_entries, 0, _entries.Length);
            _count = 0;
        }

        // 目前(任一方)仍在標記中的角色數(測試/除錯)
        public static int ActiveCount
        {
            get
            {
                float now = Time.time;
                int n = 0;
                for (int i = 0; i < _count; i++)
                {
                    if (_entries[i].Target != null && (_entries[i].AlphaUntil > now || _entries[i].BravoUntil > now))
                    {
                        n++;
                    }
                }
                return n;
            }
        }

        private static int IndexOf(Inkling target)
        {
            if (target == null)
            {
                return -1;
            }
            for (int i = 0; i < _count; i++)
            {
                if (ReferenceEquals(_entries[i].Target, target))
                {
                    return i;
                }
            }
            return -1;
        }

        private static int FindOrCreate(Inkling target)
        {
            int index = IndexOf(target);
            if (index >= 0)
            {
                return index;
            }
            // 重用已銷毀角色的格子(Unity 端 == null)
            for (int i = 0; i < _count; i++)
            {
                if (_entries[i].Target == null)
                {
                    _entries[i] = new Entry { Target = target };
                    return i;
                }
            }
            if (_count == _entries.Length)
            {
                Array.Resize(ref _entries, _entries.Length * 2);
            }
            _entries[_count] = new Entry { Target = target };
            return _count++;
        }

        private static void PlayCue(Inkling target, Team byTeam)
        {
            if (target.IsLocalPlayer)
            {
                GameAudio.Play2D(TrackedSound);
                return;
            }
            Inkling local = Inkling.Local;
            if (local != null && local.Team == byTeam)
            {
                GameAudio.Play2D(PingSound, 0.7f);
            }
        }
    }
}
