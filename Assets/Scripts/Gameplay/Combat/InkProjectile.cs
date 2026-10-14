using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;
using UnityEngine.Pool;

namespace SplatoonC.Gameplay.Combat
{
    // 池化墨彈:兩段式彈道(Splatoon 式)——射程內近乎直線維持準心高度,
    // 到達射程極限後重力驟增高速墜地。飛行途中沿路滴下 1~3 滴墨(InkDrip),
    // 連射時那些墨滴在地面鋪出一條墨路。每幀「前一位置→新位置」線段 raycast,不靠 collider。
    public sealed class InkProjectile : MonoBehaviour
    {
        // 滴墨排程緩衝:預先配置,避免每發射擊產生 GC
        private const int MaxDrips = 8;

        // 線段命中緩衝(穿過隊友要看第二個命中):全墨彈共用,主執行緒單線使用
        private static readonly RaycastHit[] HitBuffer = new RaycastHit[8];

        private WeaponConfig _config;
        private IObjectPool<InkProjectile> _pool;
        private IObjectPool<InkDrip> _dripPool;
        private Vector3 _velocity;
        private float _remainingLifetime;
        private bool _released;
        private TrailRenderer _trail;
        // 沿彈道的總飛行距離(非水平距離):如此仰射時水平射程自然縮短,平射才是最遠的
        private float _travelledDistance;
        private readonly float[] _dripDistances = new float[MaxDrips];
        private readonly float[] _dripSamples = new float[MaxDrips];
        private int _dripCount;
        private int _dripIndex;
        private Inkling _owner;
        private Team _team;
        private Renderer[] _renderers;
        private Color _appliedColor = Color.clear;

        private void Awake()
        {
            _trail = GetComponent<TrailRenderer>();
            _renderers = GetComponentsInChildren<Renderer>(true);
        }

        public void Launch(Vector3 position, Vector3 velocity, WeaponConfig config,
            IObjectPool<InkProjectile> pool, IObjectPool<InkDrip> dripPool, int shotIndex, Inkling owner)
        {
            _owner = owner;
            _team = owner != null ? owner.Team : Team.Alpha;
            TeamTint.ApplyCached(_renderers, ref _appliedColor, TeamPalette.ColorOf(_team));
            transform.position = position;
            if (velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(velocity);
            }
            _velocity = velocity;
            _config = config;
            _pool = pool;
            _dripPool = dripPool;
            _remainingLifetime = config.ProjectileLifetime;
            _released = false;
            _travelledDistance = 0f;
            PlanDrips(config, shotIndex);
            // 池化重用鐵律:清掉上一輪殘留拖尾,否則會從回收點畫一條線到槍口
            if (_trail != null)
            {
                _trail.Clear();
            }
        }

        private void PlanDrips(WeaponConfig config, int shotIndex)
        {
            _dripIndex = 0;
            _dripCount = 0;
            if (_dripPool == null || config.DripsPerShot <= 0)
            {
                return;
            }
            int count = Mathf.Min(config.DripsPerShot, MaxDrips);
            for (int i = 0; i < count; i++)
            {
                _dripSamples[i] = Random.value;
            }
            // 逐發相位錯開:第 s 發整組往後推 (s mod K)/K 個間距,後面的發次填前面的縫
            int cycle = Mathf.Max(1, config.DripPhaseCycle);
            float phase = (shotIndex % cycle) / (float)cycle;
            float maxDistance = config.StraightRange * Mathf.Max(1f, config.DripRangeMultiplier);
            _dripCount = DripPlanner.Plan(_dripDistances, count,
                config.DripStartDistance, maxDistance, phase, _dripSamples,
                config.DripJitterDistance);
        }

        private void Update()
        {
            if (_config == null)
            {
                return;
            }

            float dt = Time.deltaTime;
            Vector3 previous = transform.position;

            // 兩段式:射程內幾乎不掉(維持準心高度),超過射程才急墜。
            // 以「沿彈道飛行距離」判定 → 仰射把射程花在高度上,水平距離變短(平射最遠)
            bool beyondRange = _travelledDistance >= _config.StraightRange;
            _velocity.y += (beyondRange ? _config.DropGravity : _config.StraightGravity) * dt;
            if (beyondRange && _config.DropHorizontalDrag > 0f)
            {
                // 墜落時煞停水平速度 → 近乎垂直落下,仰射不會因滯空而飛得比平射遠
                float damp = Mathf.Exp(-_config.DropHorizontalDrag * dt);
                _velocity.x *= damp;
                _velocity.z *= damp;
                // 「向上」的殘餘速度同樣要煞:只靠重力拉的話,仰射會帶著數十 m/s
                // 直線衝上天,飛行距離數倍於射程。向下速度不煞,急墜手感才留得住。
                if (_velocity.y > 0f)
                {
                    _velocity.y *= damp;
                }
            }

            Vector3 next = previous + _velocity * dt;
            Vector3 delta = next - previous;
            float distance = delta.magnitude;

            if (distance > 0.0001f)
            {
                Vector3 direction = delta / distance;
                bool found = TryFindHit(previous, direction, distance, out RaycastHit hit);
                // Ink Vac 吸引漩渦(敵方):路徑先進入吸引範圍 → 被吸走(比牆/目標更近時)
                if (InkVacuum.Active.Count > 0 && InkVacuum.Segment(_team, previous, direction,
                        found ? hit.distance : distance, 0.1f, out InkVacuum vacuum, out float vacuumAt))
                {
                    OnVacuumed(vacuum, previous + direction * vacuumAt, vacuumAt);
                    return;
                }
                // 單向屏障(Big Bubbler):從罩外打進敵方屏障 → 停在球面上(比牆/目標更近時)
                if (BarrierSphere.Raycast(_team, previous, direction, found ? hit.distance : distance, 0f,
                        out BarrierHit barrier))
                {
                    OnBarrierHit(barrier);
                    return;
                }
                if (found)
                {
                    OnHit(hit);
                    return;
                }
            }

            transform.position = next;
            // 墨彈是橢球(prefab 的 z 軸被拉長),必須讓長軸對齊飛行方向
            if (_velocity.sqrMagnitude > 0.0001f)
            {
                transform.rotation = Quaternion.LookRotation(_velocity);
            }
            _travelledDistance += distance;
            ReleaseDueDrips(next);
            _remainingLifetime -= dt;
            if (_remainingLifetime <= 0f)
            {
                Release();
            }
        }

        // 最近的有效命中:同隊角色(含自己)與自家場上裝置直接穿過——Splatoon 的墨彈不會被隊友擋下。
        private bool TryFindHit(Vector3 origin, Vector3 direction, float distance, out RaycastHit best)
        {
            best = default;
            // 資產裡的 HitMask 排除了角色圖層(舊單人場景);這裡一律加回,敵我由下面過濾
            int count = Physics.RaycastNonAlloc(origin, direction, HitBuffer, distance,
                _config.HitMask | GameLayers.InklingMask, QueryTriggerInteraction.Ignore);
            float bestDistance = float.MaxValue;
            bool found = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = HitBuffer[i];
                if (hit.distance >= bestDistance)
                {
                    continue;
                }
                // 同隊的可受傷物(隊友角色、自家場上裝置)直接穿過
                var damageable = hit.collider.GetComponentInParent<IDamageable>();
                if (damageable != null && damageable.Team == _team)
                {
                    continue;
                }
                best = hit;
                bestDistance = hit.distance;
                found = true;
            }
            return found;
        }

        // 沿路滴墨:飛行距離越過排定點就滴一滴。排程是遞增的,單一游標即可。
        private void ReleaseDueDrips(Vector3 position)
        {
            while (_dripIndex < _dripCount && _travelledDistance >= _dripDistances[_dripIndex])
            {
                SpawnDrip(position);
                _dripIndex++;
            }
        }

        private void SpawnDrip(Vector3 position)
        {
            if (_dripPool == null)
            {
                return;
            }
            Vector2 side = Random.insideUnitCircle * _config.DripSideSpeed;
            Vector3 right = Vector3.Cross(Vector3.up, _velocity);
            if (right.sqrMagnitude < 0.0001f)
            {
                right = Vector3.right;
            }
            right.Normalize();
            // 墨滴只繼承一小部分彈速,再加一點橫向擾動 → 幾乎原地垂直落下,墨路才不會變成一條直線
            Vector3 velocity = _velocity * _config.DripInheritSpeed
                + right * side.x + Vector3.up * side.y;
            InkDrip drip = _dripPool.Get();
            drip.Launch(position, velocity, _config, _dripPool, _owner);
        }


        private void OnHit(RaycastHit hit)
        {
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(hit.point, hit.normal, TeamPalette.ColorOf(_team));
            }
            // 傷害:命中可受傷目標就結算。超過全傷距離後依飛行距離衰減;_travelledDistance 尚未含最後這一小段,要補上。
            var target = hit.collider.GetComponentInParent<IDamageable>();
            if (target != null && target.IsAlive && DamageRules.CanDamage(_team, target))
            {
                float travelled = _travelledDistance + hit.distance;
                float damage = ProjectileDamageCurve.Evaluate(travelled, _config.DamageFalloffStart,
                    _config.Damage, _config.MinDamage, _config.DamageDecayDistance);
                target.ApplyDamage(new DamageInfo(damage, hit.point, _team, _owner, DamageKind.Main,
                    _config.AttackClass));
            }
            if (hit.collider.GetComponent<PaintableSurface>() != null)
            {
                InkSource source = InkShooter.InkSourceOf(_owner);
                InkPainter.PaintSphere(hit.point, _config.SplatRadius, _config.SplatHardness, source);
                GameAudio.Play(hit.normal.y > 0.5f ? "ink_hit_floor" : "ink_hit_wall", hit.point, 0.55f, 0.08f);

                // 噴濺小點:沿命中面的切平面隨機外擴,做出有機噴濺形狀。
                for (int i = 0; i < _config.SplashCount; i++)
                {
                    Vector2 circle = Random.insideUnitCircle;
                    Vector3 tangent = Vector3.Cross(hit.normal, Vector3.up);
                    if (tangent.sqrMagnitude < 0.001f)
                    {
                        tangent = Vector3.Cross(hit.normal, Vector3.forward);
                    }
                    tangent.Normalize();
                    Vector3 bitangent = Vector3.Cross(hit.normal, tangent);
                    Vector3 offset = (tangent * circle.x + bitangent * circle.y) * _config.SplashSpread;
                    InkPainter.PaintSphere(hit.point + offset, _config.SplashRadius, _config.SplatHardness, source);
                }
            }
            Release();
        }

        // 打在敵方屏障上:屏障吃依飛行距離衰減後的傷害(物件倍率由屏障擁有者換算),濺一下就消失(不塗罩內)
        private void OnBarrierHit(in BarrierHit hit)
        {
            if (InkSplashFxPool.Instance != null)
            {
                InkSplashFxPool.Instance.Spawn(hit.Point, hit.Normal, TeamPalette.ColorOf(_team));
            }
            float travelled = _travelledDistance + hit.Distance;
            float damage = ProjectileDamageCurve.Evaluate(travelled, _config.DamageFalloffStart,
                _config.Damage, _config.MinDamage, _config.DamageDecayDistance);
            hit.Barrier.ApplyDamage(new DamageInfo(damage, hit.Point, _team, _owner, DamageKind.Main,
                _config.AttackClass));
            Release();
        }

        // 被敵方 Ink Vac 吸走:吸入量 = 依飛行距離衰減後的傷害;不塗墨、不傷人
        private void OnVacuumed(InkVacuum vacuum, Vector3 point, float along)
        {
            float travelled = _travelledDistance + along;
            float damage = ProjectileDamageCurve.Evaluate(travelled, _config.DamageFalloffStart,
                _config.Damage, _config.MinDamage, _config.DamageDecayDistance);
            vacuum.Absorb(point, damage, 0);
            Release();
        }

        private void Release()
        {
            if (_released)
            {
                return;
            }
            _released = true;
            _config = null;
            _owner = null;
            if (_pool != null)
            {
                _pool.Release(this);
            }
            else
            {
                Destroy(gameObject);
            }
        }
    }
}
