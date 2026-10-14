using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招「Killer Wail 5.1」(S3 SpMicroLaser,research/04 §2.9、JP wiki メガホンレーザー5.1ch;v11.3.0 0 AP):
    // 身後浮出 6 支喇叭(六角形,中心相對角色 (0, 1.7, −0.6)、半徑 1.3),由上而下每 36F 一組兩支:
    // 鎖定準心範圍內(±80°、離準心射線 ≤ 10 u)的敵人 → 20F 預告線 → 穿牆無限長光束 180F,每 5F 3.5 傷(42/s/支)。
    // 光束追著目標約 0.5 秒前的位置;沒鎖到人就朝準心方向、之後隨機往左或右掃。發動 40F 後主/副武器照常可用。
    // 持續型(能量就是計時器):發動到最後一組照射結束 288F;中途被擊倒 → 還沒開始照射的組不再發射。
    [CreateAssetMenu(fileName = "Special_killer_wail", menuName = "SplatoonC/大招/Killer Wail 5.1")]
    public sealed class KillerWailConfig : SpecialWeaponConfig
    {
        [Header("預製物")]
        [SerializeField, Tooltip("喇叭 prefab(KillerWailSpeaker:喇叭模型 + 光束/預告線 LineRenderer)")]
        private KillerWailSpeaker _speakerPrefab;

        [Header("時間(幀)")]
        [SerializeField, Tooltip("每組間隔(ChargeFrame 36F)")]
        private int _pairIntervalFrames = KillerWailModel.PairIntervalFrames;

        [SerializeField, Tooltip("發動到第一組鎖定(喇叭飛出排好隊形)[EST 16F]")]
        private int _firstLockFrame = KillerWailModel.FirstLockFrame;

        [SerializeField, Tooltip("鎖定後的預告線時間(JP wiki レーザー予兆時間 20F)")]
        private int _warningFrames = KillerWailModel.WarningFrames;

        [SerializeField, Tooltip("每支照射時間(LaserFrame 180F = 3 s;SPU 240F)")]
        private int _laserFrames = KillerWailModel.LaserFrames;

        [SerializeField, Tooltip("發動後主/副武器與烏賊不能用(AfterShot_No*Frm 40F)")]
        private int _userLockFrames = KillerWailModel.UserLockFrames;

        [Header("傷害")]
        [SerializeField, Tooltip("每次傷害(LaserDamage 35 → 3.5)")]
        private float _damagePerTick = KillerWailModel.DamagePerTick;

        [SerializeField, Tooltip("傷害間隔(LaserDamageIntervalFrame 5F → 42/s)")]
        private int _damageIntervalFrames = KillerWailModel.DamageIntervalFrames;

        [SerializeField, Tooltip("光束判定半徑(公尺)[EST:細光束,S3 未公開]")]
        private float _beamRadius = 0.4f;

        [SerializeField, Tooltip("光束長度(公尺;S3 無限長、穿牆)")]
        private float _beamLength = 150f;

        [Header("鎖定與追蹤")]
        [SerializeField, Tooltip("鎖定角(LockonDeg 80 → 全角 160°)")]
        private float _lockonDeg = KillerWailModel.LockonDeg;

        [SerializeField, Tooltip("鎖定半徑:離準心射線的垂直距離(LockonRadius 10 u = 2 條試射線)")]
        private float _lockonRadius = KillerWailModel.LockonRadius;

        [SerializeField, Tooltip("光束追蹤落後秒數(Inkipedia:約半秒前的位置)")]
        private float _trackDelaySeconds = 0.5f;

        [SerializeField, Tooltip("光束每幀往目標方向內插的比例(InterpRate 0.4)")]
        private float _trackInterp = 0.4f;

        [SerializeField, Tooltip("光束最大轉速(度/幀;MaxAngularVel 30)")]
        private float _maxTurnDegPerFrame = 30f;

        [SerializeField, Tooltip("沒有目標時左右掃的轉速(度/幀;FreeMoveRotSpeed 0.3)")]
        private float _freeSweepDegPerFrame = 0.3f;

        [Header("隊形")]
        [SerializeField, Tooltip("六角形中心相對角色(右、上、前;LaserBitCenterOffset (0, 1.7, −0.6))")]
        private Vector3 _formationCenter = new Vector3(0f, 1.7f, -0.6f);

        [SerializeField, Tooltip("六角形半徑(LaserBitRadius 1.3)")]
        private float _formationRadius = 1.3f;

        [Header("音效")]
        [SerializeField, Tooltip("喇叭飛出")]
        private string _deploySound = "killer_wail_deploy";

        [SerializeField, Tooltip("鎖定(S3 v5.0.0 加的鎖定提示音)")]
        private string _lockSound = "killer_wail_lock";

        [SerializeField, Tooltip("被鎖定的本機玩家聽到的警告")]
        private string _warningSound = "killer_wail_warning";

        [SerializeField, Tooltip("光束照射(循環,每支一個)")]
        private string _beamLoop = "killer_wail_beam";

        public override System.Type BehaviourType => typeof(KillerWailSpecial);

        // 發動到最後一組照射結束(288F = 4.8 s;JP wiki「4.2 秒 + α」的 α = 發動到第一組照射的 36F)
        public override float ActiveDuration => TotalFrames / 60f;

        public int TotalFrames => _firstLockFrame + (KillerWailModel.PairCount - 1) * _pairIntervalFrames
                                  + _warningFrames + _laserFrames;

        public KillerWailSpeaker SpeakerPrefab => _speakerPrefab;
        public int PairIntervalFrames => _pairIntervalFrames;
        public int FirstLockFrame => _firstLockFrame;
        public int WarningFrames => _warningFrames;
        public int LaserFrames => _laserFrames;
        public int UserLockFrames => _userLockFrames;
        public float DamagePerTick => _damagePerTick;
        public int DamageIntervalFrames => _damageIntervalFrames;
        public float BeamRadius => _beamRadius;
        public float BeamLength => _beamLength;
        public float LockonDeg => _lockonDeg;
        public float LockonRadius => _lockonRadius;
        public float TrackDelaySeconds => _trackDelaySeconds;
        public float TrackInterp => _trackInterp;
        public float MaxTurnDegPerFrame => _maxTurnDegPerFrame;
        public float FreeSweepDegPerFrame => _freeSweepDegPerFrame;
        public Vector3 FormationCenter => _formationCenter;
        public float FormationRadius => _formationRadius;
        public string DeploySound => _deploySound;
        public string LockSound => _lockSound;
        public string WarningSound => _warningSound;
        public string BeamLoop => _beamLoop;

        public int LockFrame(int pair)
        {
            return _firstLockFrame + pair * _pairIntervalFrames;
        }

        public int FireFrame(int pair)
        {
            return LockFrame(pair) + _warningFrames;
        }

        public int EndFrame(int pair)
        {
            return FireFrame(pair) + _laserFrames;
        }
    }
}
