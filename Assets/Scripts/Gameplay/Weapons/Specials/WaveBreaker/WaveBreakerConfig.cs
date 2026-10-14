using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招「Wave Breaker」(S3 SpShockSonar,research/04 §2.7、JP wiki ホップソナー;v11.3.0 0 AP):
    // 發動後把裝置舉著(時間不限),按副武器鍵(R)往前丟(不繼承慣性,短拋:0.3 u/F、上 0.07 u/F、重力 0.009 u/F²)。
    // 著地:設置感應在 57F 內擴到半徑 12.3(20 × 0.615)標記附近敵人;第 90 / 240 / 390F 各放一波,
    // 每波 160F 內沿地形擴到半徑 20(0.125 u/F、穿牆、穿 Big Bubbler、不塗墨),垂直範圍 +2.8 / −5.2;
    // 碰到的敵人吃 45 並被標記 480F(每波每人一次),跳起來就躲得過。裝置耐久 480、碰到裝置 30 傷(每 30F)。
    // 鎖定型:丟出的瞬間起約 390F 自動倒數,死亡不中斷;裝置被打壞 → 立刻歸 0(已放出的波照樣擴張)。
    [CreateAssetMenu(fileName = "Special_wave_breaker", menuName = "SplatoonC/大招/Wave Breaker")]
    public sealed class WaveBreakerConfig : SpecialWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("裝置 prefab(WaveBreakerDevice)")]
        private WaveBreakerDevice _devicePrefab;

        [SerializeField, Tooltip("衝擊波 prefab(WaveBreakerWave:沿地形的環)")]
        private WaveBreakerWave _wavePrefab;

        [SerializeField, Tooltip("舉在手上的裝置模型(名稱 Ink* 的部位上隊伍色)")]
        private GameObject _heldPrefab;

        [Header("裝置")]
        [SerializeField, Tooltip("耐久(MaxHP 4800 → 480,v10.0.0)")]
        private float _maxHp = 480f;

        [SerializeField, Tooltip("接觸傷害 / 間隔秒數(HitDamage 300 → 30、HitDamageInterval 0.5 s)")]
        private Vector2 _contact = new Vector2(30f, 0.5f);

        [SerializeField, Tooltip("接觸判定:頭部半徑 / 柱高(HeadColRadius 0.75、PoleColHeight 2.75)")]
        private Vector2 _contactShape = new Vector2(0.75f, 2.75f);

        [SerializeField, Tooltip("接觸擊退強度(HitKnockback 400)")]
        private float _contactKnockback = 400f;

        [SerializeField, Tooltip("能量鎖定倒數(幀;丟出起 ~390F,JP wiki 約 6.5 秒)")]
        private int _lockFrames = 390;

        [Header("衝擊波")]
        [SerializeField, Tooltip("每波傷害(WaveParam.Damage 450 → 45)")]
        private float _waveDamage = WaveBreakerModel.WaveDamage;

        [SerializeField, Tooltip("每波最大半徑(MaxRadius 20;SPU 27)")]
        private float _waveMaxRadius = WaveBreakerModel.WaveMaxRadius;

        [SerializeField, Tooltip("每波擴到最大的幀數(MaxFrame 160;SPU 216,速度不變)")]
        private int _waveExpandFrames = WaveBreakerModel.WaveExpandFrames;

        [SerializeField, Tooltip("被波打到的標記秒數(480F)")]
        private float _waveMarkSeconds = WaveBreakerModel.WaveMarkSeconds;

        [SerializeField, Tooltip("波前判定帶的半寬(公尺)[EST:S3 未公開,地面上一圈細環]")]
        private float _waveHalfBand = 0.35f;

        [SerializeField, Tooltip("腳離地面超過這個高度(公尺)就躲過(JP wiki:判定很低,跳起來就能躲)[EST]")]
        private float _waveHitHeight = 0.5f;

        [Header("設置感應")]
        [SerializeField, Tooltip("感應標記在設置後這一幀一律結束(判定 57F + MarkingFrame 45F)[EST 詮釋]")]
        private int _sensorMarkEndFrame = WaveBreakerModel.SensorEndFrame + 45;

        [Header("音效")]
        [SerializeField, Tooltip("丟出")]
        private string _throwSound = "wave_breaker_throw";

        [SerializeField, Tooltip("著地設置(感應)")]
        private string _landSound = "wave_breaker_land";

        [SerializeField, Tooltip("放波")]
        private string _waveSound = "wave_breaker_wave";

        public override System.Type BehaviourType => typeof(WaveBreakerSpecial);

        // 舉著裝置沒有時間限制(S3):由行為在丟出時結束,能量改走鎖定倒數
        public override float ActiveDuration => 0f;

        public WaveBreakerDevice DevicePrefab => _devicePrefab;
        public WaveBreakerWave WavePrefab => _wavePrefab;
        public GameObject HeldPrefab => _heldPrefab;
        public float MaxHp => _maxHp;
        public float ContactDamage => _contact.x;
        public float ContactInterval => _contact.y;
        public float ContactRadius => _contactShape.x;
        public float ContactHeight => _contactShape.y;
        public float ContactKnockback => _contactKnockback;
        public float LockSeconds => _lockFrames / 60f;
        public float WaveDamage => _waveDamage;
        public float WaveMaxRadius => _waveMaxRadius;
        public int WaveExpandFrames => _waveExpandFrames;
        public float WaveMarkSeconds => _waveMarkSeconds;
        public float WaveHalfBand => _waveHalfBand;
        public float WaveHitHeight => _waveHitHeight;
        public int SensorMarkEndFrame => _sensorMarkEndFrame;
        public string ThrowSound => _throwSound;
        public string LandSound => _landSound;
        public string WaveSound => _waveSound;
    }
}
