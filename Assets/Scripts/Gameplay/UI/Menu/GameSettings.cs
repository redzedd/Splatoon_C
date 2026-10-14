using SplatoonC.Core.Menu;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Match;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.UI.Menu
{
    // 玩家設定(PlayerPrefs 保存,改了立即生效):
    //   音量:主/音樂/音效(GameAudio 自己存,這裡只是入口)
    //   視角:水平/垂直靈敏度 -5~+5(S3 選項,LookSensitivity 換算倍率)、反轉上下/左右 → PlayerInputRouter
    //   色彩鎖定(S3 Color Lock)→ MatchSettings.ColorLock
    // 遊戲啟動(第一個場景載入前)就套用,直接開對戰/試射場景也吃得到設定。
    public static class GameSettings
    {
        private const string PrefLookX = "SplatoonC.Look.SensitivityX";
        private const string PrefLookY = "SplatoonC.Look.SensitivityY";
        private const string PrefInvertX = "SplatoonC.Look.InvertX";
        private const string PrefInvertY = "SplatoonC.Look.InvertY";
        private const string PrefColorLock = "SplatoonC.Match.ColorLock";

        public const float DefaultMaster = 1f;
        public const float DefaultMusic = 0.8f;
        public const float DefaultSfx = 1f;

        private static float _lookX;
        private static float _lookY;
        private static bool _invertX;
        private static bool _invertY;
        private static bool _colorLock;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void LoadOnStartup()
        {
            Load();
        }

        public static void Load()
        {
            _lookX = LookSensitivity.Clamp(PlayerPrefs.GetFloat(PrefLookX, 0f));
            _lookY = LookSensitivity.Clamp(PlayerPrefs.GetFloat(PrefLookY, 0f));
            _invertX = PlayerPrefs.GetInt(PrefInvertX, 0) != 0;
            _invertY = PlayerPrefs.GetInt(PrefInvertY, 0) != 0;
            _colorLock = PlayerPrefs.GetInt(PrefColorLock, 0) != 0;
            ApplyLook();
            MatchSettings.ColorLock = _colorLock;
        }

        public static float MasterVolume
        {
            get => GameAudio.MasterVolume;
            set => GameAudio.MasterVolume = value;
        }

        public static float MusicVolume
        {
            get => GameAudio.MusicVolume;
            set => GameAudio.MusicVolume = value;
        }

        public static float SfxVolume
        {
            get => GameAudio.SfxVolume;
            set => GameAudio.SfxVolume = value;
        }

        public static float LookSensitivityX
        {
            get => _lookX;
            set
            {
                _lookX = LookSensitivity.Clamp(value);
                PlayerPrefs.SetFloat(PrefLookX, _lookX);
                ApplyLook();
            }
        }

        public static float LookSensitivityY
        {
            get => _lookY;
            set
            {
                _lookY = LookSensitivity.Clamp(value);
                PlayerPrefs.SetFloat(PrefLookY, _lookY);
                ApplyLook();
            }
        }

        public static bool InvertX
        {
            get => _invertX;
            set
            {
                _invertX = value;
                PlayerPrefs.SetInt(PrefInvertX, value ? 1 : 0);
                ApplyLook();
            }
        }

        public static bool InvertY
        {
            get => _invertY;
            set
            {
                _invertY = value;
                PlayerPrefs.SetInt(PrefInvertY, value ? 1 : 0);
                ApplyLook();
            }
        }

        // 下一場開始時生效(本場配色不變)
        public static bool ColorLock
        {
            get => _colorLock;
            set
            {
                _colorLock = value;
                PlayerPrefs.SetInt(PrefColorLock, value ? 1 : 0);
                MatchSettings.ColorLock = value;
            }
        }

        public static void ResetToDefaults()
        {
            MasterVolume = DefaultMaster;
            MusicVolume = DefaultMusic;
            SfxVolume = DefaultSfx;
            LookSensitivityX = 0f;
            LookSensitivityY = 0f;
            InvertX = false;
            InvertY = false;
            ColorLock = false;
        }

        // 離開設定頁時寫入磁碟
        public static void Save()
        {
            PlayerPrefs.Save();
        }

        private static void ApplyLook()
        {
            PlayerInputRouter.LookScale = new Vector2(LookSensitivity.Multiplier(_lookX), LookSensitivity.Multiplier(_lookY));
            PlayerInputRouter.InvertLookX = _invertX;
            PlayerInputRouter.InvertLookY = _invertY;
        }

        // 測試用:整組快照/還原(PlayerPrefs 在同一台機器的所有工作樹共用,測試改完要放回去)
        public struct Snapshot
        {
            public float Master;
            public float Music;
            public float Sfx;
            public float LookX;
            public float LookY;
            public bool InvertX;
            public bool InvertY;
            public bool ColorLock;
        }

        public static Snapshot Capture()
        {
            return new Snapshot
            {
                Master = MasterVolume,
                Music = MusicVolume,
                Sfx = SfxVolume,
                LookX = _lookX,
                LookY = _lookY,
                InvertX = _invertX,
                InvertY = _invertY,
                ColorLock = _colorLock,
            };
        }

        public static void Restore(Snapshot snapshot)
        {
            MasterVolume = snapshot.Master;
            MusicVolume = snapshot.Music;
            SfxVolume = snapshot.Sfx;
            LookSensitivityX = snapshot.LookX;
            LookSensitivityY = snapshot.LookY;
            InvertX = snapshot.InvertX;
            InvertY = snapshot.InvertY;
            ColorLock = snapshot.ColorLock;
            Save();
        }
    }
}
