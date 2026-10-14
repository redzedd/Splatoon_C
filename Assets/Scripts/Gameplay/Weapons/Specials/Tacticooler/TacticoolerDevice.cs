using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.AI;

namespace SplatoonC.Gameplay.Combat
{
    // Tacticooler 的飲料機(research/04 §2.15、JP wiki エナジースタンド):
    //   飛行:TacticoolerSpecial 算好的初速 + 投擲物單一重力(與預覽/鎖定預測同一步進),穿過角色(GuideHitCollisionType
    //     EnemyOff);碰到坡度 ≤ 30° 的地面 → 立起;牆/陡坡 → 抵消垂直表面的速度、彈回一點(FlyHitVerticalWallReboundMaxRt 0.3)。
    //   立起:打不壞(Indestructible)、碰撞體在 Default 圖層 = 雙方的墨(子彈、爆炸視線、炸彈)都被擋、角色走不過去(掩體);
    //     NavMeshObstacle 讓 bot 繞開;在 1.8 u 內的其他飲料機消失(VanishDistance)。展開 30F [EST] 後開始供應:
    //     同隊(含丟的人)以人形、受擊膠囊碰到水平 7 / 往上 3 的圓柱 → 拿一罐(每台 4 罐、一人一罐,死了回來也不能再拿),
    //     得到 ModifierSpec.Tacticooler(1020F,死亡失效;同一個來源鍵 → 喝第二台只刷新時間、不疊加)+ 發光。
    //   消失:存在 900F、被新的擠掉、掉出場外 → 擁有者的能量鎖定立刻歸 0(舊序號不影響新的鎖定)。
    //   bot:登記成 IBotAttractor,沒喝過的隊友會走過來。
    public sealed class TacticoolerDevice : DeployableObject, IBotAttractor
    {
        private const float FlightRadius = 0.35f;
        private const float MaxFlightSeconds = 6f;
        private const float BodyRadius = 0.35f;
        private const float BodyHeight = 1.65f;
        private const float AttractRange = 24f;
        private const int MaxDrinkers = 8;

        // 增益的來源鍵:所有飲料機共用 → 喝第二台只刷新,不會疊兩層(S3 不疊加)
        public static readonly object BuffSource = new object();

        private static readonly List<TacticoolerDevice> Deployed = new List<TacticoolerDevice>();

        [SerializeField, Tooltip("實心碰撞體(Default 圖層;飛行時關閉)")]
        private Collider _solid;

        [SerializeField, Tooltip("NavMesh 障礙(立起後 carve,bot 繞開)")]
        private NavMeshObstacle _obstacle;

        [SerializeField, Tooltip("機身(展開時從扁罐子長高)")]
        private Transform _body;

        [SerializeField, Tooltip("四支拿飲料的手臂(展開時伸出)")]
        private Transform[] _arms;

        [SerializeField, Tooltip("四罐飲料(被拿走就藏起來)")]
        private GameObject[] _cans;

        [SerializeField, Tooltip("頂端燈泡(展開後亮、快消失時閃)")]
        private Renderer _bulb;

        private TacticoolerConfig _config;
        private PlayerSpecialGauge _gauge;
        private int _lockToken;
        private bool _flying;
        private Vector3 _velocity;
        private float _thrownAt;
        private float _placedAt;
        private int _drinksLeft;
        private readonly Inkling[] _drinkers = new Inkling[MaxDrinkers];
        private int _drinkerCount;
        private Team _flightTeam;
        private Renderer[] _neutralRenderers;
        private bool _flashState;
        private bool _bulbLit;
        private float _bulbPhase = -1f;

        public override float MaxHp => 1f;
        public override TargetClass TargetClass => TargetClass.Device;
        protected override bool Indestructible => true;

        // ── 狀態(測試、bot)──
        public bool Flying => _flying;
        public float PlacedAt => _placedAt;
        public float ThrownAt => _thrownAt;
        public int DrinksLeft => _drinksLeft;
        public int DrinkerCount => _drinkerCount;
        public bool Serving => IsDeployed && _config != null && Time.time - _placedAt >= _config.UnfoldSeconds;
        public float ElapsedSeconds => IsDeployed ? Time.time - _placedAt : 0f;
        public Collider Solid => _solid;

        public static int LandCount { get; private set; }
        public static int DrinkCount { get; private set; }
        public static int VanishCount { get; private set; }
        public static IReadOnlyList<TacticoolerDevice> Active => Deployed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounts()
        {
            LandCount = 0;
            DrinkCount = 0;
            VanishCount = 0;
            Deployed.Clear();
        }

        protected override void Awake()
        {
            base.Awake();
            var neutral = new List<Renderer>();
            foreach (Renderer r in GetComponentsInChildren<Renderer>(true))
            {
                if (!r.name.StartsWith("Ink", System.StringComparison.Ordinal))
                {
                    neutral.Add(r);
                }
            }
            _neutralRenderers = neutral.ToArray();
        }

        // SubProjectile 介面(Tacticooler 由大招丟出,改用 Throw)
        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            Debug.LogError("TacticoolerDevice:請用 Throw(由 TacticoolerSpecial 丟出)", this);
            Expire();
        }

        public void Throw(Vector3 origin, Vector3 velocity, TacticoolerConfig config, Inkling owner,
            PlayerSpecialGauge gauge, int lockToken)
        {
            _config = config;
            _gauge = gauge;
            _lockToken = lockToken;
            Owner = owner;
            _flightTeam = owner != null ? owner.Team : Team.Alpha;
            _velocity = velocity;
            _thrownAt = Time.time;
            _flying = true;
            _drinksLeft = config.Drinks;
            _drinkerCount = 0;
            SetSolid(false);
            transform.SetPositionAndRotation(origin, Quaternion.Euler(0f, Random.Range(0f, 360f), 0f));
            TeamTint.ApplyAll(this, TeamPalette.ColorOf(_flightTeam));
            RetintNeutral();
            SetUnfold(0f);
            SetBulb(false);
            for (int i = 0; i < _cans.Length; i++)
            {
                if (_cans[i] != null)
                {
                    _cans[i].SetActive(i < _drinksLeft);
                }
            }
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }
            if (_flying)
            {
                Fly(Time.deltaTime);
                return;
            }
            if (!IsDeployed)
            {
                return;
            }
            float elapsed = Time.time - _placedAt;
            SetUnfold(Mathf.Clamp01(elapsed / Mathf.Max(0.01f, _config.UnfoldSeconds)));
            if (elapsed >= _config.StandSeconds)
            {
                Vanish();
                return;
            }
            if (elapsed >= _config.UnfoldSeconds)
            {
                SetBulb(true);
                AnimateBulb(elapsed);
                if (_drinksLeft > 0)
                {
                    Serve();
                }
            }
        }

        // ── 飛行 ──

        private void Fly(float dt)
        {
            Vector3 previous = transform.position;
            Vector3 next = previous;
            Vector3 velocity = _velocity;
            SimpleBallistics.Step(ref next, ref velocity, _config.Gravity, dt);
            Vector3 delta = next - previous;
            float distance = delta.magnitude;
            if (distance > 1e-5f && Physics.SphereCast(previous, FlightRadius, delta / distance, out RaycastHit hit,
                    distance, CombatMasks.Terrain, QueryTriggerInteraction.Ignore))
            {
                Vector3 contact = previous + delta / distance * hit.distance;
                if (hit.normal.y >= 0.6f)
                {
                    if (TacticoolerModel.CanPlaceOn(hit.normal))
                    {
                        Land(hit.point);
                        return;
                    }
                    // 30° 以上的坡:沿坡滑下去再找平地(抵消垂直坡面的速度)
                    transform.position = contact + hit.normal * 0.02f;
                    _velocity = velocity - hit.normal * Vector3.Dot(velocity, hit.normal);
                    return;
                }
                // 牆/天花板:抵消垂直表面的速度並彈回 30%(FlyHitVerticalWallReboundMaxRt)
                transform.position = contact + hit.normal * 0.02f;
                float into = Vector3.Dot(velocity, hit.normal);
                _velocity = velocity - hit.normal * (into * 1.3f);
                if (hit.normal.y < -0.5f)
                {
                    _velocity.y = Mathf.Min(0f, _velocity.y);
                }
                return;
            }
            transform.position = next;
            _velocity = velocity;
            if (Time.time - _thrownAt > MaxFlightSeconds || next.y < -30f)
            {
                // 掉出場外:沒有飲料機,能量歸 0
                _flying = false;
                EndOwnerLock();
                VanishCount++;
                Despawn();
            }
        }

        private void Land(Vector3 point)
        {
            _flying = false;
            transform.SetPositionAndRotation(point, Quaternion.Euler(0f, transform.eulerAngles.y, 0f));
            _placedAt = Time.time;
            Deploy(Owner, _flightTeam);
            RetintNeutral();
            SetSolid(true);
            // 碰撞體同一幀就要擋墨(專案關閉 autoSyncTransforms)
            Physics.SyncTransforms();
            LandCount++;
            // 1.8 u 內的其他飲料機消失(S3 VanishDistance;同一人的舊機器也一樣)
            for (int i = Deployed.Count - 1; i >= 0; i--)
            {
                TacticoolerDevice other = Deployed[i];
                if (other != this && other != null && other.IsDeployed
                    && (other.transform.position - point).sqrMagnitude <= _config.VanishDistance * _config.VanishDistance)
                {
                    other.Vanish();
                }
            }
            Deployed.Add(this);
            BotHazards.RegisterAttractor(this);
            GameAudio.Play(GameAudio.Has(_config.DeploySound) ? _config.DeploySound : "special_activate",
                point + Vector3.up, 1f, 0f);
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(point + Vector3.up * 0.1f, Vector3.up, TeamPalette.ColorOf(Team));
            }
        }

        // ── 供應 ──

        private void Serve()
        {
            Vector3 standBase = transform.position;
            var all = Inkling.All;
            for (int i = 0; i < all.Count && _drinksLeft > 0; i++)
            {
                Inkling mate = all[i];
                if (mate == null || mate.Team != Team || !mate.isActiveAndEnabled || HasDrunk(mate))
                {
                    continue;
                }
                // 只有人形拿得到(S3:烏賊態經過不算)
                var squid = mate.GetComponent<SquidController>();
                if (squid != null && squid.IsSquid)
                {
                    continue;
                }
                var life = mate.GetComponent<InklingLifecycle>();
                if (life != null && life.IsDead)
                {
                    continue;
                }
                if (!TacticoolerModel.InServeArea(standBase, mate.transform.position, BodyRadius, BodyHeight)
                    || !InConfiguredArea(standBase, mate.transform.position))
                {
                    continue;
                }
                Give(mate);
            }
        }

        // 設定資產的範圍(預設與 TacticoolerModel 相同;可在資產調整)
        private bool InConfiguredArea(Vector3 standBase, Vector3 feet)
        {
            float dx = feet.x - standBase.x;
            float dz = feet.z - standBase.z;
            float reach = _config.ServeRadius + BodyRadius;
            return dx * dx + dz * dz <= reach * reach && feet.y <= standBase.y + _config.ServeHeightUp
                   && feet.y + BodyHeight >= standBase.y;
        }

        private void Give(Inkling mate)
        {
            if (_drinkerCount < MaxDrinkers)
            {
                _drinkers[_drinkerCount++] = mate;
            }
            _drinksLeft--;
            DrinkCount++;
            int can = _drinksLeft;
            if (can >= 0 && can < _cans.Length && _cans[can] != null)
            {
                _cans[can].SetActive(false);
            }
            mate.Modifiers.Add(BuffSource, ModifierSpec.Tacticooler, _config.BuffSeconds);
            if (_config.AuraPrefab != null)
            {
                TacticoolerAura.Attach(_config.AuraPrefab, mate, _config.BuffSeconds);
            }
            GameAudio.Play(GameAudio.Has(_config.DrinkSound) ? _config.DrinkSound : "special_ready",
                mate.transform.position + Vector3.up, 0.8f, 0.03f);
        }

        public bool HasDrunk(Inkling inkling)
        {
            for (int i = 0; i < _drinkerCount; i++)
            {
                if (_drinkers[i] == inkling)
                {
                    return true;
                }
            }
            return false;
        }

        // ── bot 吸引點:沒喝過、還有罐、在 24 m 內的隊友 → 走到機器旁 ──

        public bool Attracts(Inkling bot, out Vector3 point)
        {
            point = transform.position;
            if (!IsDeployed || _drinksLeft <= 0 || bot == null || bot.Team != Team || HasDrunk(bot)
                || bot.Modifiers.Has(BuffSource))
            {
                return false;
            }
            Vector3 toBot = bot.transform.position - transform.position;
            toBot.y = 0f;
            if (toBot.sqrMagnitude > AttractRange * AttractRange)
            {
                return false;
            }
            // 機器是實心的:目標點放在朝 bot 那一側 2 m
            point = transform.position + (toBot.sqrMagnitude > 0.01f ? toBot.normalized : Vector3.forward) * 2f;
            return true;
        }

        // ── 外觀 ──

        private void SetUnfold(float t)
        {
            float eased = t * t * (3f - 2f * t);
            if (_body != null)
            {
                _body.localScale = new Vector3(1f, Mathf.Lerp(0.45f, 1f, eased), 1f);
            }
            if (_arms != null)
            {
                float arm = Mathf.Clamp01((t - 0.3f) / 0.7f);
                for (int i = 0; i < _arms.Length; i++)
                {
                    if (_arms[i] != null)
                    {
                        _arms[i].localScale = Vector3.one * Mathf.Max(0.001f, arm);
                    }
                }
            }
        }

        private void SetBulb(bool lit)
        {
            if (_bulb == null || lit == _bulbLit)
            {
                return;
            }
            _bulbLit = lit;
            _bulbPhase = -1f;
            Color team = TeamPalette.ColorOf(Team);
            TeamTint.Apply(_bulb, lit ? Color.Lerp(team, Color.white, 0.35f) : team * 0.5f);
        }

        // 最後 2 秒閃爍(快消失的預告)
        private void AnimateBulb(float elapsed)
        {
            if (_bulb == null)
            {
                return;
            }
            float remaining = _config.StandSeconds - elapsed;
            float phase = remaining < 2f ? (Mathf.Repeat(elapsed * 4f, 1f) < 0.5f ? 1f : 0f) : 1f;
            if (Mathf.Approximately(phase, _bulbPhase))
            {
                return;
            }
            _bulbPhase = phase;
            Color team = TeamPalette.ColorOf(Team);
            TeamTint.Apply(_bulb, phase > 0.5f ? Color.Lerp(team, Color.white, 0.35f) : team * 0.4f);
        }

        private void SetSolid(bool solid)
        {
            if (_solid != null)
            {
                _solid.enabled = solid;
            }
            if (_obstacle != null)
            {
                _obstacle.enabled = solid;
            }
        }

        // ── 消失 ──

        public void Vanish()
        {
            if (!IsDeployed && !_flying)
            {
                return;
            }
            VanishCount++;
            if (InkSplashFxPool.Instance != null)
            {
                Color color = TeamPalette.ColorOf(Team);
                for (int i = 0; i < 3; i++)
                {
                    InkSplashFxPool.Instance.Spawn(transform.position + Vector3.up * (0.6f + i * 0.5f), Vector3.up, color);
                }
            }
            if (_config != null)
            {
                GameAudio.Play(_config.VanishSound, transform.position + Vector3.up, 0.8f, 0.03f);
            }
            EndOwnerLock();
            Expire();
        }

        private void EndOwnerLock()
        {
            if (_gauge != null)
            {
                _gauge.EndLock(_lockToken);
                _gauge = null;
            }
        }

        public override void Expire()
        {
            _flying = false;
            Deployed.Remove(this);
            BotHazards.UnregisterAttractor(this);
            SetSolid(false);
            for (int i = 0; i < MaxDrinkers; i++)
            {
                _drinkers[i] = null;
            }
            _drinkerCount = 0;
            _bulbLit = false;
            base.Expire();
        }

        protected override void OnDisable()
        {
            Deployed.Remove(this);
            BotHazards.UnregisterAttractor(this);
            base.OnDisable();
        }

        private void RetintNeutral()
        {
            for (int i = 0; i < _neutralRenderers.Length; i++)
            {
                if (_neutralRenderers[i] != null)
                {
                    _neutralRenderers[i].SetPropertyBlock(null);
                }
            }
            _bulbLit = !_bulbLit;
            SetBulb(!_bulbLit);
        }

        protected override void LateUpdate()
        {
            base.LateUpdate();
            if (IsDeployed && IsFlashing != _flashState)
            {
                _flashState = IsFlashing;
                RetintNeutral();
            }
        }
    }
}
