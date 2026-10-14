using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 池化吸盤炸彈(S3 Bomb_Suction):拋物線飛行(與 ThrowController 預覽同一個 SimpleBallistics 步進,60 Hz),
    // 只和地形碰撞(穿過角色與假人)→ 第一個碰到的面就黏住(吸盤朝面),黏住後才開始算 120F 引信,
    // 剩 60F 起警告音、指示燈快閃,剩 30F 膨脹 → 爆炸:傷害中心離面 +0.6,平面塗墨只塗黏住的面(r 5.0 + 交叉 2.5)。
    // 碰到會引爆炸彈的物件(敵方 Splash Wall 等)立刻爆炸。
    public sealed class SuctionBomb : SimulatedBomb
    {
        private SuctionBombConfig _config;
        private Vector3 _velocity;
        private bool _stuck;
        private int _stuckFrame;
        private bool _warned;
        private Vector3 _stickPoint;
        private Vector3 _stickNormal = Vector3.up;

        // 測試/除錯:累計黏住與爆炸數、最近一次的黏住點/法線/時間、爆心與爆炸時間、警告音時間
        public static int StickCount { get; private set; }
        public static int ExplosionCount { get; private set; }
        public static Vector3 LastStickPoint { get; private set; }
        public static Vector3 LastStickNormal { get; private set; }
        public static float LastStickTime { get; private set; }
        public static float LastWarningTime { get; private set; }
        public static float LastExplosionTime { get; private set; }
        public static Vector3 LastExplosionCenter { get; private set; }
        public static int LastFuseFrames { get; private set; }
        public static float LastSurfacePaintArea { get; private set; }

        public bool IsStuck => _stuck;
        public bool IsWarning => _warned;
        public Vector3 StickPoint => _stickPoint;
        public Vector3 StickNormal => _stickNormal;
        public int FuseRemainingFrames => _stuck && _config != null ? _config.FuseFrames - (Frame - _stuckFrame) : -1;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetCounters()
        {
            StickCount = 0;
            ExplosionCount = 0;
            LastStickTime = float.NegativeInfinity;
            LastWarningTime = float.NegativeInfinity;
            LastExplosionTime = float.NegativeInfinity;
        }

        public override void Launch(Vector3 position, Vector3 velocity, SubWeaponConfig config, Inkling owner)
        {
            _config = config as SuctionBombConfig;
            if (_config == null)
            {
                Debug.LogError($"SuctionBomb:設定型別不符({config})", this);
                Despawn();
                return;
            }
            Begin(position, owner);
            _velocity = velocity;
            _stuck = false;
            _warned = false;
            _stuckFrame = 0;
            _stickPoint = Vector3.zero;
            _stickNormal = Vector3.up;
            SetLightColor(new Color(1f, 0.25f, 0.2f));
            OrientAlongVelocity();
        }

        protected override bool Step()
        {
            if (!_stuck)
            {
                return Fly();
            }
            int rest = _config.FuseFrames - (Frame - _stuckFrame);
            if (!_warned && rest <= _config.WarningRestFrames)
            {
                _warned = true;
                LastWarningTime = Time.time;
                GameAudio.Play(_config.WarningSound, SimPosition, 0.9f, 0f);
            }
            if (rest <= 0)
            {
                Explode();
                return false;
            }
            return true;
        }

        private bool Fly()
        {
            if (Frame > SimFrames.ToFrames(_config.Lifetime))
            {
                Release();
                return false;
            }
            Vector3 from = SimPosition;
            Vector3 to = from;
            Vector3 velocity = _velocity;
            SimpleBallistics.Step(ref to, ref velocity, _config.Gravity, SimFrames.Dt);
            BombContact contact = BombRules.CastPath(from, to, _config.Radius, BombTeam, false, out RaycastHit hit,
                out _);
            if (contact == BombContact.Detonator)
            {
                SimPosition = CenterAt(from, to, hit.distance);
                _stickPoint = hit.point;
                _stickNormal = hit.normal;
                Explode();
                return false;
            }
            if (contact == BombContact.Terrain)
            {
                Stick(hit);
                return true;
            }
            SimPosition = to;
            _velocity = velocity;
            return true;
        }

        private static Vector3 CenterAt(Vector3 from, Vector3 to, float distance)
        {
            Vector3 delta = to - from;
            float length = delta.magnitude;
            return length > 1e-6f ? from + delta / length * distance : from;
        }

        // 黏住:吸盤貼面(炸彈中心在接觸點沿法線外 Radius),從這一幀起算引信
        private void Stick(in RaycastHit hit)
        {
            Vector3 normal = hit.normal.sqrMagnitude > 1e-6f ? hit.normal.normalized : Vector3.up;
            _stuck = true;
            _stuckFrame = Frame;
            _stickPoint = hit.point;
            _stickNormal = normal;
            _velocity = Vector3.zero;
            SimPosition = hit.point + normal * _config.Radius;
            // 黏住這一步不插值(不要看起來滑進定位)
            PrevPosition = SimPosition;
            transform.SetPositionAndRotation(SimPosition, Quaternion.FromToRotation(Vector3.up, normal));
            StickCount++;
            LastStickPoint = _stickPoint;
            LastStickNormal = normal;
            LastStickTime = Time.time;
            GameAudio.Play(_config.StickSound, _stickPoint, 0.8f, 0.05f);
        }

        private void Explode()
        {
            // 黏住(或碰到引爆物)的面往外 +0.6 = 傷害中心(S3 DamageOffsetY)
            Vector3 center = _stuck || _stickPoint != Vector3.zero
                ? _stickPoint + _stickNormal * _config.DamageOffset
                : SimPosition;
            ExplosionCount++;
            LastExplosionTime = Time.time;
            LastExplosionCenter = center;
            LastFuseFrames = _stuck ? Frame - _stuckFrame : 0;
            // 主塗墨:只塗黏住的那一面(平面圓盤 r 5.0 + 交叉 2.5);傷害/擊退/噴墨滴交給共用 Explosion
            if (_stuck)
            {
                var source = Owner != null ? Owner.InkSource() : new InkSource(BombTeam);
                LastSurfacePaintArea = BombPaint.PaintDisc(_stickPoint + _stickNormal * 0.05f, _stickNormal,
                    _config.SurfacePaintRadius, _config.DiscStampRadius, _config.CrossPaintRadius,
                    _config.PaintHardness, source);
            }
            Explosion.Detonate(_config.Blast, center, BombTeam, Owner);
            Release();
        }

        protected override void OnReleased()
        {
            _config = null;
        }

        protected override void UpdateVisual()
        {
            if (!_stuck)
            {
                OrientAlongVelocity();
                return;
            }
            int rest = _config.FuseFrames - (Frame - _stuckFrame);
            float seconds = Time.time;
            // 指示燈:平時慢閃,警告後快閃;最後 30F 膨脹脈動(S2 Warn 30)
            SetLights(Blink(seconds, _warned ? 0.12f : 0.5f));
            if (rest <= _config.SwellRestFrames)
            {
                float t = 1f - Mathf.Clamp01((float)rest / Mathf.Max(1, _config.SwellRestFrames));
                SetSwell(1f + 0.22f * t + 0.05f * Mathf.Sin(seconds * 60f));
            }
        }

        // 飛行中吸盤朝前(模型 −Y 對準速度)
        private void OrientAlongVelocity()
        {
            if (_velocity.sqrMagnitude > 1e-4f)
            {
                transform.rotation = Quaternion.FromToRotation(Vector3.up, -_velocity.normalized);
            }
        }
    }
}
