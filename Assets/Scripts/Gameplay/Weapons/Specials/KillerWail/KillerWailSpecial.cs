using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Killer Wail 5.1 的執行期行為(research/04 §2.9、JP wiki メガホンレーザー5.1ch):
    //   發動 → 身後飛出 6 支喇叭(KillerWailSpeaker)跟著施放者;第 16 / 52 / 88F 由上而下各鎖定一組(兩支):
    //   準心 ±80°、離準心射線 ≤ 10 u 內離射線最近的敵人(沒有 → 最後一個待過範圍內的敵人,S3 v5.0.0;再沒有 → 準心方向)。
    //   鎖定後 20F 預告線、接著照射 180F(喇叭停在原地、光束自己追)。發動 40F 後主/副武器、烏賊照常可用。
    //   持續型:發動到最後一組照射結束 288F 是能量計時;中途被擊倒 → 還沒開始照射的喇叭消失(已在照射的照完)。
    public sealed class KillerWailSpecial : SpecialWeapon
    {
        private const int SpeakerCount = 6;

        private KillerWailConfig _config;
        private readonly KillerWailSpeaker[] _speakers = new KillerWailSpeaker[SpeakerCount];
        private readonly int[] _speakerGeneration = new int[SpeakerCount];
        private readonly Inkling[] _pairTargets = new Inkling[KillerWailModel.PairCount];
        private float _activatedAt = float.NegativeInfinity;
        private int _pairsLocked;
        private Inkling _lastInRange;
        private float _lastInRangeAt = float.NegativeInfinity;

        // bot
        private float _botStrafeSign = 1f;
        private float _botStrafeFlipAt;

        public override SpecialWeaponConfig BaseConfig => _config;
        public KillerWailConfig Config => _config;

        // 發動後 40F 內不能用主/副武器與烏賊(AfterShot_No*Frm 40)
        private bool InUserLock => _config != null && Time.time - _activatedAt < _config.UserLockFrames / 60f;
        public override bool BlocksMainWeapon => InUserLock;
        public override bool BlocksSubWeapon => InUserLock;
        public override bool BlocksSquid => InUserLock;

        // 累計(測試用):鎖到敵人的組數 / 沒鎖到人的組數
        public static int TargetLocks { get; private set; }
        public static int EmptyLocks { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounts()
        {
            TargetLocks = 0;
            EmptyLocks = 0;
        }

        // ── 狀態(測試、HUD)──
        public float ActivatedAt => _activatedAt;
        public int PairsLocked => _pairsLocked;
        public KillerWailSpeaker Speaker(int slot) => slot >= 0 && slot < SpeakerCount ? Mine(slot) : null;
        public Inkling PairTarget(int pair) => pair >= 0 && pair < _pairTargets.Length ? _pairTargets[pair] : null;

        public override void Configure(SpecialWeaponConfig config)
        {
            _config = config as KillerWailConfig;
            if (_config == null)
            {
                Debug.LogError($"KillerWailSpecial:設定型別不符({config})", this);
                return;
            }
            if (_config.SpeakerPrefab == null)
            {
                Debug.LogError("KillerWailSpecial:設定缺少喇叭 prefab(先跑 Tools/SplatoonC/M7 Setup)", this);
                return;
            }
            SharedPool.Prewarm(_config.SpeakerPrefab, SpeakerCount);
        }

        public override void OnActivated()
        {
            if (_config == null || _config.SpeakerPrefab == null)
            {
                EndSpecial();
                return;
            }
            _activatedAt = Time.time;
            _pairsLocked = 0;
            _lastInRange = null;
            _lastInRangeAt = float.NegativeInfinity;
            for (int p = 0; p < _pairTargets.Length; p++)
            {
                _pairTargets[p] = null;
            }
            for (int i = 0; i < SpeakerCount; i++)
            {
                KillerWailSpeaker speaker = SharedPool.Spawn(_config.SpeakerPrefab);
                speaker.Begin(Owner, _config, i);
                _speakers[i] = speaker;
                _speakerGeneration[i] = speaker.Generation;
            }
            GameAudio.PlayFrom(Owner != null && Owner.IsLocalPlayer, _config.DeploySound, transform.position + Vector3.up * 1.7f,
                1f, 0f);
        }

        public override void OnEnded()
        {
            // 死亡/提早結束:還沒開始照射的喇叭取消(正常跑完時全部都已經照完)
            for (int i = 0; i < SpeakerCount; i++)
            {
                KillerWailSpeaker speaker = Mine(i);
                if (speaker != null && speaker.isActiveAndEnabled && speaker.Phase != KillerWailSpeaker.State.Firing
                    && speaker.Phase != KillerWailSpeaker.State.Idle)
                {
                    speaker.Cancel();
                }
            }
        }

        private void Update()
        {
            if (!IsActive || _config == null)
            {
                return;
            }
            TrackInRange();
            float frames = (Time.time - _activatedAt) * 60f;
            while (_pairsLocked < KillerWailModel.PairCount && frames >= _config.LockFrame(_pairsLocked))
            {
                LockPair(_pairsLocked);
                _pairsLocked++;
            }
        }

        private void LockPair(int pair)
        {
            Inkling target = PickTarget(out Vector3 forward);
            _pairTargets[pair] = target;
            if (target != null)
            {
                TargetLocks++;
            }
            else
            {
                EmptyLocks++;
            }
            float fireAt = _activatedAt + _config.FireFrame(pair) / 60f;
            float endAt = _activatedAt + _config.EndFrame(pair) / 60f;
            for (int k = 0; k < 2; k++)
            {
                KillerWailSpeaker speaker = Mine(pair * 2 + k);
                if (speaker != null)
                {
                    speaker.Lock(target, forward, fireAt, endAt);
                }
            }
            bool local = Owner != null && Owner.IsLocalPlayer;
            GameAudio.PlayFrom(local, _config.LockSound, transform.position + Vector3.up * 1.7f, 0.9f, 0f);
            if (target != null && target.IsLocalPlayer)
            {
                // 被鎖定的本機玩家:警告音(預告線是大家都看得到的預兆)
                GameAudio.Play2D(_config.WarningSound, 1f);
            }
        }

        // 這次發動生成的第 slot 支(池化物件已被別人重用 → null)
        private KillerWailSpeaker Mine(int slot)
        {
            KillerWailSpeaker speaker = _speakers[slot];
            return speaker != null && speaker.Generation == _speakerGeneration[slot] ? speaker : null;
        }

        // 鎖定範圍內離準心射線最近的敵人
        private Inkling FindInRange(out Vector3 forward)
        {
            IAimView view = Owner != null ? Owner.AimView : null;
            Vector3 origin = view != null ? view.AimOrigin : transform.position + Vector3.up * 1.5f;
            forward = view != null ? view.AimForward : transform.forward;
            Team team = Owner != null ? Owner.Team : Team.Alpha;
            Inkling best = null;
            float bestLateral = float.MaxValue;
            var all = Inkling.All;
            for (int i = 0; i < all.Count; i++)
            {
                Inkling other = all[i];
                if (other == null || other.Team == team || other.Team == Team.None)
                {
                    continue;
                }
                var life = other.GetComponent<InklingLifecycle>();
                if (life != null && life.IsDead)
                {
                    continue;
                }
                Vector3 point = other.transform.position + Vector3.up * 0.8f;
                if (KillerWailModel.InLockon(origin, forward, point, out float lateral, _config.LockonDeg,
                        _config.LockonRadius) && lateral < bestLateral)
                {
                    bestLateral = lateral;
                    best = other;
                }
            }
            return best;
        }

        // S3 v5.0.0:範圍內沒人 → 鎖定最後一個待過範圍內的敵人
        private void TrackInRange()
        {
            Inkling current = FindInRange(out _);
            if (current != null)
            {
                _lastInRange = current;
                _lastInRangeAt = Time.time;
            }
        }

        private Inkling PickTarget(out Vector3 forward)
        {
            Inkling target = FindInRange(out forward);
            if (target != null)
            {
                return target;
            }
            if (_lastInRange != null && Time.time - _lastInRangeAt < 5f)
            {
                var life = _lastInRange.GetComponent<InklingLifecycle>();
                if (life == null || !life.IsDead)
                {
                    return _lastInRange;
                }
            }
            return null;
        }

        // ── bot:有交戰目標(40 m 內)就開;鎖定期間盯著目標,40F 後邊橫移邊用主武器 ──

        // bot 看著目標 → 目標就在準心鎖定範圍內。光束穿牆,所以剛躲到掩體後(bot 還記得位置 3 秒)或被標記的目標也照開——
        // 這正是 S3 喇叭雷射的用法(逼出掩體);bot 不知道位置的敵人(沒看到、沒標記)不算,不作弊
        public override bool BotWantsActivate(IBotControl bot)
        {
            return bot.HasTarget && bot.TargetDistance <= 40f;
        }

        public override void BotDrive(IBotControl bot)
        {
            if (!bot.HasTarget)
            {
                bot.LookAt(bot.Position + bot.EnemyBaseDirection * 20f + Vector3.up * 1.2f);
                return;
            }
            bot.LookAt(bot.TargetPosition + Vector3.up * 0.9f);
            if (!InUserLock)
            {
                bot.HoldAttack(bot.TargetVisible && bot.AimError < 7f && bot.TargetDistance < 14f);
            }
            if (Time.time >= _botStrafeFlipAt)
            {
                _botStrafeFlipAt = Time.time + Random.Range(0.7f, 1.4f);
                _botStrafeSign = Random.value < 0.5f ? -1f : 1f;
            }
            Vector3 toTarget = bot.TargetPosition - bot.Position;
            toTarget.y = 0f;
            Vector3 right = toTarget.sqrMagnitude > 0.01f ? Vector3.Cross(Vector3.up, toTarget.normalized) : Vector3.right;
            bot.Move(right * _botStrafeSign, 0.8f);
        }
    }
}
