using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Audio;
using SplatoonC.Gameplay.Combat;
using SplatoonC.Gameplay.Painting;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // 戰鬥基礎設施(I1)驗收,試射場。全部走真實執行期路徑(InkBullet 池、Explosion、PlayerLocomotion、DamageableRegistry):
    //   (a) InkBullet 以 Splattershot 規格平射:落點/落地幀 = §3.1 三狀態彈道預測;落點塗成拉長的橢圓、9.2 m 滴墨
    //       (墨滴有落下動畫 InkBulletDrip:滴下時地面還沒塗、著地才塗);
    //       依飛行幀數衰減:第 4F 命中 36、第 12F 命中 33.7
    //   (b) 同一 attackId 的 5 發只算一次;傘式上限 81
    //   (c) Explosion:近/遠傷害量到假人最近點、牆擋視線、爆炸塗地、閃光
    //   (d) 擊退:Splat Bomb 級爆炸在 3 m 處把玩家推開 1.5–2 m;同隊爆炸不推
    //   (e) DeployableObject:只吃敵方、依 DamageRates 倍率扣血、歸零被打壞;擋敵方爆炸視線的裝置;擁有者死亡一起消失
    //   (f) 真實角色:敵方墨彈打玩家 36(DamageInfo 帶 AttackClass);同隊墨彈穿過玩家打到後方假人
    //   (g) 打牆:圓形塗墨 PaintRadiusShock、ink_hit_wall 音效
    //   (h) 爆破槍式規格:第 13F 空爆線性傷害;直擊 125 且直擊目標不再吃爆炸、旁邊的假人吃爆炸
    // 站位全在場地西半部(x < 0),不動到靶場與既有測試的射道。
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 200)]
    public sealed class CombatInfraAutoTest : AutoTestBase
    {
        private readonly List<InkBulletImpact> _impacts = new List<InkBulletImpact>();
        private readonly List<string> _heard = new List<string>();
        private readonly List<Object> _spawned = new List<Object>();
        private TargetDummy _template;

        public static void Run()
        {
            Launch<CombatInfraAutoTest>();
        }

        private void OnEnable()
        {
            InkBullet.Impacted += OnImpact;
            GameAudio.Played += OnPlayed;
        }

        private void OnDisable()
        {
            InkBullet.Impacted -= OnImpact;
            GameAudio.Played -= OnPlayed;
        }

        private void OnImpact(in InkBulletImpact impact)
        {
            _impacts.Add(impact);
        }

        private void OnPlayed(string id)
        {
            _heard.Add(id);
        }

        private IEnumerator Start()
        {
            GameObject playerGo = FindPlayer();
            var player = playerGo != null ? playerGo.GetComponent<Inkling>() : null;
            var health = playerGo != null ? playerGo.GetComponent<InklingHealth>() : null;
            var locomotion = playerGo != null ? playerGo.GetComponent<PlayerLocomotion>() : null;
            var lifecycle = playerGo != null ? playerGo.GetComponent<InklingLifecycle>() : null;
            GameObject groundGo = GameObject.Find("Ground");
            var ground = groundGo != null ? groundGo.GetComponent<PaintableSurface>() : null;
            GameObject templateGo = GameObject.Find("Dummy_Near");
            _template = templateGo != null ? templateGo.GetComponent<TargetDummy>() : null;
            CombatAssets assets = CombatAssets.Instance;
            if (player == null || health == null || locomotion == null || lifecycle == null || ground == null
                || _template == null || assets == null || assets.InkBulletPrefab == null
                || assets.ExplosionFlashPrefab == null)
            {
                FailSetup("找不到 Player / Ground / Dummy_Near / Resources/Weapons/CombatAssets(先跑 Tools/SplatoonC/M7 Setup)");
                yield break;
            }
            ScriptedIntent.Drive(playerGo.transform);
            health.Invulnerable = true;
            Teleport(playerGo.transform, new Vector3(-3f, 0.1f, 23f));
            yield return new WaitForSeconds(0.3f);

            yield return BallisticsAndFrameDamage(player, ground);
            yield return MaxDistanceAndGrowingRadius(player, ground);
            yield return PerAttackDedup(player);
            yield return ExplosionDamageAndPaint(ground);
            yield return Knockback(playerGo, health, locomotion);
            yield return PlayerDamageAndTeammatePassThrough(playerGo, health);
            yield return Deployables(player);
            yield return WallPaint(player);
            yield return BlasterLike(player);
            yield return OwnerDeathDespawn(playerGo, player, health, lifecycle);

            foreach (Object obj in _spawned)
            {
                if (obj != null)
                {
                    Destroy(obj is Component component ? component.gameObject : obj);
                }
            }
            health.Invulnerable = false;
            playerGo.GetComponent<PlayerInputRouter>().ClearOverrideSource();
            Teleport(playerGo.transform, new Vector3(0f, 0.1f, 0f));
            Finish();
        }

        // ── (a) 彈道、塗墨、依幀衰減 ──
        private IEnumerator BallisticsAndFrameDamage(Inkling player, PaintableSurface ground)
        {
            InkBulletSpec spec = InkBulletSpec.Splattershot;
            Vector3 origin = new Vector3(-23f, 1f, -20f);
            BulletBallistics.PredictFlatLanding(spec.Motion, origin.y, spec.RadiusForField, out int predictedFrames,
                out float predictedDistance);
            int start = _impacts.Count;
            byte alpha = (byte)Team.Alpha;
            var dripPoint = new Vector3(origin.x + spec.DripSpacing, 0f, origin.z);
            InkBullet.Fire(spec, origin, Vector3.right, Team.Alpha, player);
            // 墨滴落下動畫:彈飛過 9.2 m 時滴下一滴,墨滴還在空中時正下方的地面還沒塗
            yield return WaitUntil(() => InkBulletDrip.ActiveCount > 0, 0.5f);
            bool dripFalling = InkBulletDrip.ActiveCount > 0 && ground.SampleOwnership(dripPoint) != alpha;
            yield return WaitUntil(() => _impacts.Count > start, 3f);
            InkBulletImpact landing = _impacts.Count > start ? _impacts[start] : default;
            float distance = landing.Point.x - origin.x;
            Check("InkBullet 平射落點 = §3.1 三狀態彈道預測(10–16F 落地)",
                landing.End == InkBulletEnd.Terrain && landing.Frame == predictedFrames
                && Mathf.Abs(distance - predictedDistance) < 0.15f && landing.Frame >= 10 && landing.Frame <= 16
                && landing.Phase == BulletPhase.Free,
                $"實測 {landing.End} 第 {landing.Frame}F {distance:0.00} m({landing.Phase});預測 {predictedFrames}F {predictedDistance:0.00} m");

            Vector3 p = landing.Point;
            yield return WaitUntil(() => InkBulletDrip.ActiveCount == 0, 2f);
            bool center = ground.SampleOwnership(p) == alpha;
            // Splattershot 落點(≈ 11.75 m、自由落體 ≈ 38°):半寬 1.81、DepthScale 1.31 → 沿飛行方向半長 ≈ 2.37
            bool along = ground.SampleOwnership(p + Vector3.right * 2.0f) == alpha
                && ground.SampleOwnership(p - Vector3.right * 2.0f) == alpha;
            bool sideInside = ground.SampleOwnership(p + Vector3.forward * 1.2f) == alpha
                && ground.SampleOwnership(p - Vector3.forward * 1.2f) == alpha;
            bool sideOutside = ground.SampleOwnership(p + Vector3.forward * 2.3f) != alpha
                && ground.SampleOwnership(p - Vector3.forward * 2.3f) != alpha;
            bool drip = ground.SampleOwnership(dripPoint) == alpha;
            Check("落點塗成沿飛行方向拉長的橢圓 + 途中每 9.2 m 滴墨",
                center && along && sideInside && sideOutside && drip,
                $"中心={center} 前後 2.0 m={along} 左右 1.2 m={sideInside} 左右 2.3 m 無墨={sideOutside} 9.2 m 滴墨={drip}");
            Check("墨滴有落下動畫:滴下時地面還沒塗、著地才塗", dripFalling && drip,
                $"空中墨滴時地面未塗={dripFalling} 著地後有墨={drip}");

            // 第 4F 命中(直線段):全額 36
            TargetDummy near = SpawnDummy(new Vector3(-15f, 0f, -16f));
            yield return null;
            start = _impacts.Count;
            InkBullet.Fire(spec, new Vector3(-23f, 1f, -16f), Vector3.right, Team.Alpha, player);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            InkBulletImpact hit4 = _impacts.Count > start ? _impacts[start] : default;
            Check("第 4F 命中假人:全額 36",
                hit4.End == InkBulletEnd.Target && ReferenceEquals(hit4.Target, near) && hit4.Frame == 4
                && Mathf.Abs(near.LastDamage - 36f) < 0.001f,
                $"{hit4.End} 第 {hit4.Frame}F 假人受傷 {near.LastDamage}");

            // 第 12F 命中(自由落體段):36 − 0.5625 × 4 = 33.75 → 33.7。假人擺在第 11F 與第 12F 位置之間
            Vector3 high = new Vector3(-23f, 1.9f, -13f);
            BulletState sim = BulletBallistics.Launch(high, Vector3.right, spec.Motion);
            float x11 = 0f;
            float x12 = 0f;
            for (int i = 1; i <= 12; i++)
            {
                BulletBallistics.Step(ref sim, spec.Motion);
                if (i == 11)
                {
                    x11 = sim.Position.x;
                }
            }
            x12 = sim.Position.x;
            float contactX = (x11 + x12) * 0.5f;
            TargetDummy late = SpawnDummy(new Vector3(contactX + spec.RadiusForPlayer + _template.BodyRadius, 0f, high.z));
            yield return null;
            start = _impacts.Count;
            InkBullet.Fire(spec, high, Vector3.right, Team.Alpha, player);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            InkBulletImpact hit12 = _impacts.Count > start ? _impacts[start] : default;
            float expected12 = spec.DamageAtFrame(hit12.Frame);
            Check("第 12F 命中假人:依飛行幀數衰減到 33.7",
                hit12.End == InkBulletEnd.Target && hit12.Frame == 12 && Mathf.Abs(late.LastDamage - 33.7f) < 0.001f
                && Mathf.Abs(expected12 - 33.7f) < 0.001f,
                $"{hit12.End} 第 {hit12.Frame}F 假人受傷 {late.LastDamage}(第 12F 期望 33.7)");
        }

        // ── (a') 狙擊式最大距離 + 端點塗墨;滾筒式判定半徑在前幾幀長大 ──
        private IEnumerator MaxDistanceAndGrowingRadius(Inkling player, PaintableSurface ground)
        {
            InkBulletSpec charger = InkBulletSpec.Splattershot;
            charger.MaxDistance = 6f;
            charger.EndPaintRadius = 1.5f;
            charger.DripSpacing = 0f;
            Vector3 origin = new Vector3(-23f, 1f, 20f);
            int start = _impacts.Count;
            InkBullet.Fire(charger, origin, Vector3.right, Team.Alpha, player);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            InkBulletImpact end = _impacts.Count > start ? _impacts[start] : default;
            bool painted = ground.SampleOwnership(new Vector3(origin.x + 6f, 0f, origin.z)) == (byte)Team.Alpha
                && ground.SampleOwnership(new Vector3(origin.x + 6f, 0f, origin.z + 1.2f)) == (byte)Team.Alpha;
            Check("最大距離:直線飛到 Distance 就結束,端點往下塗墨",
                end.End == InkBulletEnd.MaxDistance && Mathf.Abs(end.Travelled - 6f) < 0.001f
                && Mathf.Abs(end.Point.x - (origin.x + 6f)) < 0.01f && painted,
                $"{end.End} 飛行 {end.Travelled:0.000} m 終點 x={end.Point.x - origin.x:0.00} 端點塗墨={painted}");

            // S3 Splat Roller 甩墨:對人判定 4F 內 0.12 → 1.02。側偏 1.3 m 的假人:第 1F(r ≈ 0.35)擦不到、第 4F(r 1.02)打得到
            InkBulletSpec grow = InkBulletSpec.Splattershot;
            grow.RadiusForPlayer = 0.12f;
            grow.RadiusForPlayerEnd = 1.02f;
            grow.RadiusForPlayerChangeFrames = 4;
            grow.DripSpacing = 0f;
            Vector3 lane = new Vector3(-23f, 1f, 16.5f);
            TargetDummy early = SpawnDummy(lane + new Vector3(1.2f, -1f, 1.3f));
            TargetDummy later = SpawnDummy(lane + new Vector3(8.5f, -1f, 1.3f));
            yield return null;
            int earlyBefore = early.PopupVersion;
            start = _impacts.Count;
            InkBullet.Fire(grow, lane, Vector3.right, Team.Alpha, player);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            InkBulletImpact hit = _impacts.Count > start ? _impacts[start] : default;
            Check("對人判定半徑在前 4F 從 0.12 長到 1.02(近的側偏假人擦過、遠的打中)",
                hit.End == InkBulletEnd.Target && ReferenceEquals(hit.Target, later) && hit.Frame == 4
                && early.PopupVersion == earlyBefore,
                $"{hit.End} 第 {hit.Frame}F 目標={NameOf(hit.Target)}(遠={ReferenceEquals(hit.Target, later)})近的受擊={early.PopupVersion != earlyBefore}");

            // 自訂傷害委派(潑桶落差、滾筒距離衰減用):收到依幀的傷害與發射點,回傳值取整到 0.1
            TargetDummy custom = SpawnDummy(new Vector3(-15f, 0f, -23f));
            yield return null;
            float received = -1f;
            Vector3 receivedOrigin = Vector3.zero;
            InkBulletDamageFunc half = (bullet, target, frameDamage) =>
            {
                received = frameDamage;
                receivedOrigin = bullet.Origin;
                return frameDamage * 0.5f + 0.27f;
            };
            Vector3 customOrigin = new Vector3(-23f, 1f, -23f);
            start = _impacts.Count;
            InkBullet.Fire(InkBulletSpec.Splattershot, customOrigin, Vector3.right, Team.Alpha, player, 0, default, null, half);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            Check("自訂傷害委派:收到依幀傷害 36 與發射點,結果取整到 0.1(18.27 → 18.2)",
                Mathf.Abs(received - 36f) < 0.001f && receivedOrigin == customOrigin
                && Mathf.Abs(custom.LastDamage - 18.2f) < 0.001f,
                $"委派收到 {received} 發射點 {receivedOrigin} 假人受傷 {custom.LastDamage}");
        }

        // ── (b) 同一次攻擊去重 / 上限 ──
        private IEnumerator PerAttackDedup(Inkling player)
        {
            InkBulletSpec spec = InkBulletSpec.Splattershot;
            TargetDummy once = SpawnDummy(new Vector3(-15f, 0f, -9f));
            TargetDummy capped = SpawnDummy(new Vector3(-15f, 0f, -6f));
            yield return null;

            int versionBefore = once.PopupVersion;
            int attack = CombatLedger.NewAttackId();
            int start = _impacts.Count;
            for (int i = 0; i < 5; i++)
            {
                InkBullet.Fire(spec, new Vector3(-23f, 1f, -9.2f + i * 0.1f), Vector3.right, Team.Alpha, player, attack);
            }
            yield return WaitUntil(() => _impacts.Count >= start + 5, 2f);
            int onTarget = 0;
            float dealt = 0f;
            for (int i = start; i < _impacts.Count; i++)
            {
                if (ReferenceEquals(_impacts[i].Target, once))
                {
                    onTarget++;
                    dealt += _impacts[i].Damage;
                }
            }
            Check("同一 attackId 的 5 發:同一目標只吃一次",
                onTarget == 5 && Mathf.Abs(once.PopupTotal - 36f) < 0.001f && once.PopupVersion == versionBefore + 1
                && Mathf.Abs(dealt - 36f) < 0.001f,
                $"命中 {onTarget} 發,結算 {dealt},假人累計 {once.PopupTotal}(受擊 {once.PopupVersion - versionBefore} 次)");

            InkBulletSpec brella = spec;
            brella.PerAttackDamageCap = 81f;
            attack = CombatLedger.NewAttackId();
            start = _impacts.Count;
            for (int i = 0; i < 5; i++)
            {
                InkBullet.Fire(brella, new Vector3(-23f, 1f, -6.2f + i * 0.1f), Vector3.right, Team.Alpha, player, attack);
            }
            yield return WaitUntil(() => _impacts.Count >= start + 5, 2f);
            Check("傘式上限:同一 attackId 各發累加但不超過 81",
                Mathf.Abs(capped.PopupTotal - 81f) < 0.001f && capped.IsAlive,
                $"5 × 36 → 假人累計 {capped.PopupTotal}(期望 81)");
        }

        // ── (c) 爆炸:近/遠傷害、視線、塗地 ──
        private IEnumerator ExplosionDamageAndPaint(PaintableSurface ground)
        {
            Vector3 c = new Vector3(-14f, 0.3f, 8f);
            float r = _template.BodyRadius;
            TargetDummy nearA = SpawnDummy(c + new Vector3(2f + r, -0.3f, 0f));
            TargetDummy nearB = SpawnDummy(c + new Vector3(0f, -0.3f, 2.5f + r));
            TargetDummy far = SpawnDummy(c + new Vector3(-5f - r, -0.3f, 0f));
            TargetDummy outside = SpawnDummy(c + new Vector3(0f, -0.3f, 8f + r));
            TargetDummy covered = SpawnDummy(c + new Vector3(0f, -0.3f, -2.5f - r));
            GameObject wall = GameObject.CreatePrimitive(PrimitiveType.Cube);
            wall.name = "CombatInfra_Wall";
            wall.transform.position = new Vector3(c.x, 1.5f, c.z - 1.5f);
            wall.transform.localScale = new Vector3(3f, 3f, 0.4f);
            _spawned.Add(wall);
            // 執行期新建/移動的碰撞體要等下一個物理步才進查詢;批次模式幀率很高,兩幀內可能還沒跑 FixedUpdate
            Physics.SyncTransforms();
            yield return null;
            yield return null;

            int flashes = ExplosionFlash.SpawnCount;
            int heardBefore = _heard.Count;
            int[] before = { nearA.PopupVersion, nearB.PopupVersion, far.PopupVersion, outside.PopupVersion, covered.PopupVersion };
            ExplosionSpec spec = ExplosionSpec.SplatBomb;
            ExplosionResult result = Explosion.Detonate(spec, c, Team.Bravo, null);
            yield return null;

            float dA = Vector3.Distance(c, nearA.ClosestPoint(c));
            float dB = Vector3.Distance(c, nearB.ClosestPoint(c));
            float dFar = Vector3.Distance(c, far.ClosestPoint(c));
            float dOut = Vector3.Distance(c, outside.ClosestPoint(c));
            bool nearOk = nearA.PopupVersion != before[0] && Mathf.Abs(nearA.LastDamage - spec.Evaluate(dA)) < 0.001f
                && Mathf.Abs(nearA.LastDamage - 180f) < 0.001f
                && nearB.PopupVersion != before[1] && Mathf.Abs(nearB.LastDamage - 180f) < 0.001f;
            bool farOk = far.PopupVersion != before[2] && Mathf.Abs(far.LastDamage - 30f) < 0.001f;
            bool outsideOk = outside.PopupVersion == before[3];
            Check("爆炸近/遠傷害量到目標最近點(180 ≤ 3.6 m、30 ≤ 7.0 m、再遠無傷)",
                nearOk && farOk && outsideOk && result.TargetsHit == 3,
                $"{dA:0.00} m → {nearA.LastDamage}、{dB:0.00} m → {nearB.LastDamage}、{dFar:0.00} m → {far.LastDamage}、"
                + $"{dOut:0.00} m 受擊={outside.PopupVersion != before[3]};命中數 {result.TargetsHit}");
            Check("牆擋住爆炸視線(牆後 2.5 m 的假人無傷)",
                covered.PopupVersion == before[4] && result.BlockedByCover >= 1,
                $"牆後假人受擊={covered.PopupVersion != before[4]} 被遮擋 {result.BlockedByCover}");

            byte bravo = (byte)Team.Bravo;
            int ring15 = 0;
            int ring30 = 0;
            for (int k = 0; k < 8; k++)
            {
                float angle = k * Mathf.PI * 0.25f;
                Vector3 dir = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
                if (ground.SampleOwnership(new Vector3(c.x, 0f, c.z) + dir * 1.5f) == bravo)
                {
                    ring15++;
                }
                if (ground.SampleOwnership(new Vector3(c.x, 0f, c.z) + dir * 3f) == bravo)
                {
                    ring30++;
                }
            }
            bool centerInked = ground.SampleOwnership(new Vector3(c.x, 0f, c.z)) == bravo;
            Check("爆炸塗地(主塗墨 r 4.0)+ 閃光 + 音效",
                centerInked && ring15 == 8 && ring30 == 8 && result.Splats >= 1 && result.PaintedArea > 30f
                && ExplosionFlash.SpawnCount > flashes && HeardSince(heardBefore, "bomb_explode"),
                $"中心={centerInked} r1.5 {ring15}/8 r3.0 {ring30}/8 塗墨 {result.Splats} 次 {result.PaintedArea:0.0} m² "
                + $"閃光={ExplosionFlash.SpawnCount - flashes} 音效={HeardSince(heardBefore, "bomb_explode")}");
        }

        // ── (d) 擊退 ──
        private IEnumerator Knockback(GameObject playerGo, InklingHealth health, PlayerLocomotion locomotion)
        {
            Teleport(playerGo.transform, new Vector3(-8f, 0.1f, -1f));
            yield return new WaitForSeconds(0.4f);
            Vector3 center = PlaceAtDistance(health, playerGo.transform.position + new Vector3(-3.3f, 0.4f, 0f), 3f);
            Vector3 closest = health.ClosestPoint(center);
            float measured = Vector3.Distance(center, closest);
            float predicted = KnockbackModel.PredictFlatDistance(
                KnockbackModel.Impulse(center, closest, 700f, 12f), -10.8f);
            Vector3 before = playerGo.transform.position;
            ExplosionResult result = Explosion.Detonate(ExplosionSpec.SplatBomb, center, Team.Bravo, null);
            yield return new WaitForSeconds(1.2f);
            Vector3 moved = playerGo.transform.position - before;
            Vector3 flat = new Vector3(moved.x, 0f, moved.z);
            Vector3 away = new Vector3(before.x - center.x, 0f, before.z - center.z).normalized;
            Check("擊退:Splat Bomb 級爆炸在 3 m 處推開 1.5–2 m(方向背離爆心、之後停下)",
                result.Knockbacks == 1 && flat.magnitude >= 1.5f && flat.magnitude <= 2.0f
                && Vector3.Dot(flat.normalized, away) > 0.95f && locomotion.ExternalVelocity.sqrMagnitude < 1e-4f
                && Mathf.Abs(moved.y) < 0.1f,
                $"距離 {measured:0.00} m → 推 {flat.magnitude:0.00} m(模型 {predicted:0.00} m)方向 {Vector3.Dot(flat.normalized, away):0.00} "
                + $"殘速 {locomotion.ExternalVelocity.magnitude:0.00} Δy {moved.y:0.00}");

            before = playerGo.transform.position;
            center = PlaceAtDistance(health, before + new Vector3(-3.3f, 0.4f, 0f), 3f);
            result = Explosion.Detonate(ExplosionSpec.SplatBomb, center, Team.Alpha, null);
            yield return new WaitForSeconds(0.6f);
            moved = playerGo.transform.position - before;
            Check("同隊爆炸不擊退", result.Knockbacks == 0 && new Vector2(moved.x, moved.z).magnitude < 0.05f,
                $"位移 {new Vector2(moved.x, moved.z).magnitude:0.000} m 擊退數 {result.Knockbacks}");
        }

        // 沿 guess → 目標方向微調爆心,讓「爆心到目標最近點」剛好是 distance
        private static Vector3 PlaceAtDistance(IDamageable target, Vector3 guess, float distance)
        {
            Vector3 center = guess;
            for (int i = 0; i < 4; i++)
            {
                Vector3 closest = target.ClosestPoint(center);
                Vector3 offset = center - closest;
                float current = offset.magnitude;
                if (current < 1e-4f)
                {
                    break;
                }
                center = closest + offset / current * distance;
            }
            return center;
        }

        // ── (f) 真實角色受傷、隊友穿透 ──
        private IEnumerator PlayerDamageAndTeammatePassThrough(GameObject playerGo, InklingHealth health)
        {
            Teleport(playerGo.transform, new Vector3(-3f, 0.1f, -10f));
            TargetDummy behind = SpawnDummy(new Vector3(0.5f, 0f, -10f));
            yield return new WaitForSeconds(0.4f);
            health.ResetForRespawn();
            health.ClearArmor();
            health.Invulnerable = false;
            DamageInfo captured = default;
            bool got = false;
            void OnDamaged(float dealt, DamageInfo info)
            {
                captured = info;
                got = true;
            }
            health.Damaged += OnDamaged;
            InkBulletSpec spec = InkBulletSpec.Splattershot;
            int start = _impacts.Count;
            InkBullet.Fire(spec, new Vector3(-10f, 1f, -10f), Vector3.right, Team.Bravo, null);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            health.Damaged -= OnDamaged;
            InkBulletImpact hit = _impacts.Count > start ? _impacts[start] : default;
            Check("敵方墨彈打中玩家:36 傷害,DamageInfo 帶 AttackClass.Shooter",
                hit.End == InkBulletEnd.Target && ReferenceEquals(hit.Target, health) && Mathf.Abs(health.Damage - 36f) < 0.001f
                && got && captured.AttackClass == AttackClass.Shooter && captured.Kind == DamageKind.Main,
                $"{hit.End} 第 {hit.Frame}F 玩家累積傷害 {health.Damage} AttackClass={captured.AttackClass}");
            health.ResetForRespawn();
            health.Invulnerable = true;

            float damageBefore = health.Damage;
            start = _impacts.Count;
            InkBullet.Fire(spec, new Vector3(-10f, 1f, -10f), Vector3.right, Team.Alpha, null);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            hit = _impacts.Count > start ? _impacts[start] : default;
            Check("同隊墨彈穿過玩家,打到後方假人",
                hit.End == InkBulletEnd.Target && ReferenceEquals(hit.Target, behind) && health.Damage == damageBefore
                && Mathf.Abs(behind.LastDamage - 36f) < 0.001f,
                $"{hit.End} 目標={NameOf(hit.Target)} 假人受傷 {behind.LastDamage}");
        }

        // ── (e) 場上裝置 ──
        private IEnumerator Deployables(Inkling player)
        {
            TestDeployable device = TestDeployable.Create("CombatInfra_Device", new Vector3(-6f, 0.5f, 14f), Vector3.one);
            _spawned.Add(device);
            device.Setup(null, Team.Bravo, 120f, TargetClass.Sprinkler, false);
            Physics.SyncTransforms();
            yield return null;
            bool registered = DamageableRegistry.Active.Contains(device);

            InkBulletSpec shooter = InkBulletSpec.Splattershot;
            Vector3 origin = new Vector3(-13f, 0.5f, 14f);
            int start = _impacts.Count;
            InkBullet.Fire(shooter, origin, Vector3.right, Team.Alpha, player);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            bool flashing = device.IsFlashing;
            float hpAfterShooter = device.Hp;
            Check("裝置吃敵方墨彈:Shooter × Sprinkler 1.0 → 120 − 36 = 84,受擊閃白",
                registered && Mathf.Abs(hpAfterShooter - 84f) < 0.001f && flashing,
                $"註冊={registered} HP {hpAfterShooter} 閃白={flashing}");

            InkBulletSpec blaster = shooter;
            blaster.AttackClass = AttackClass.Blaster;
            blaster.DamageMax = 30f;
            blaster.DamageMin = 30f;
            blaster.ReduceStartFrame = -1;
            start = _impacts.Count;
            InkBullet.Fire(blaster, origin, Vector3.right, Team.Alpha, player);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            Check("依攻擊類別倍率:Blaster × Sprinkler 2.4 → 30 打掉 72",
                Mathf.Abs(device.Hp - 12f) < 0.001f && Mathf.Abs(device.LastDealt - 72f) < 0.001f,
                $"HP {device.Hp} 本次扣 {device.LastDealt}");

            start = _impacts.Count;
            InkBullet.Fire(shooter, origin, Vector3.right, Team.Bravo, null);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            InkBulletImpact friendly = _impacts.Count > start ? _impacts[start] : default;
            Check("同隊墨彈穿過自家裝置、不扣血",
                Mathf.Abs(device.Hp - 12f) < 0.001f && !ReferenceEquals(friendly.Target, device)
                && friendly.End == InkBulletEnd.Terrain,
                $"HP {device.Hp} 墨彈結束={friendly.End} 落點 x={friendly.Point.x:0.0}");

            int brokenBefore = DeployableObject.BrokenCount;
            ExplosionResult blast = Explosion.Detonate(ExplosionSpec.SplatBomb, new Vector3(-8f, 0.3f, 14f), Team.Alpha, player);
            bool brokenNow = !device.IsAlive && device.BrokenCallbacks == 1 && device.Hp <= 0f;
            yield return null;
            yield return null;
            bool gone = device == null && !DamageableRegistry.Active.Contains(device);
            Check("敵方炸彈(Bomb × 2.0)打壞裝置:回呼一次、移出註冊表並回收",
                brokenNow && gone && DeployableObject.BrokenCount == brokenBefore + 1 && blast.TargetsHit >= 1,
                $"打壞={brokenNow} 已回收={gone} 命中 {blast.TargetsHit}");

            // 擋敵方攻擊的裝置(Splash Wall 型):敵方爆炸打在它身上(Bomb × SplashWall 2.0),牆後的假人無傷;
            // 同隊的爆炸不打它、也不被它擋
            TestDeployable shield = TestDeployable.Create("CombatInfra_Shield", new Vector3(-8f, 1.5f, 20f),
                new Vector3(3f, 3f, 0.4f));
            _spawned.Add(shield);
            shield.Setup(null, Team.Alpha, 800f, TargetClass.SplashWall, false, true);
            TargetDummy guarded = SpawnDummy(new Vector3(-8f, 0f, 22.2f));
            Physics.SyncTransforms();
            yield return null;
            int guardedBefore = guarded.PopupVersion;
            Vector3 center = new Vector3(-8f, 0.3f, 18.5f);
            ExplosionResult enemyBlast = Explosion.Detonate(ExplosionSpec.SplatBomb, center, Team.Bravo, null);
            Check("擋敵方的裝置:吃 180 × 2.0 = 360、牆後假人無傷",
                Mathf.Abs(shield.Hp - 440f) < 0.001f && guarded.PopupVersion == guardedBefore && enemyBlast.BlockedByCover >= 1,
                $"牆 HP {shield.Hp}(期望 440)牆後假人受擊={guarded.PopupVersion != guardedBefore} 遮擋 {enemyBlast.BlockedByCover}");
            ExplosionResult ownBlast = Explosion.Detonate(ExplosionSpec.SplatBomb, center, Team.Alpha, null);
            Check("同隊爆炸:不傷自家牆、也不被它擋(牆後中立假人吃 180)",
                Mathf.Abs(shield.Hp - 440f) < 0.001f && guarded.PopupVersion != guardedBefore
                && Mathf.Abs(guarded.LastDamage - 180f) < 0.001f && ownBlast.BlockedByCover == 0,
                $"牆 HP {shield.Hp} 假人受傷 {guarded.LastDamage}");
        }

        // ── (g) 打牆 ──
        private IEnumerator WallPaint(Inkling player)
        {
            GameObject wallGo = GameObject.Find("ClimbWall_High");
            var wall = wallGo != null ? wallGo.GetComponent<PaintableSurface>() : null;
            var collider = wallGo != null ? wallGo.GetComponent<Collider>() : null;
            if (wall == null || collider == null)
            {
                Check("打牆塗圓", false, "找不到 ClimbWall_High");
                yield break;
            }
            Bounds bounds = collider.bounds;
            // 薄的那一軸就是牆面法線方向;從負側 8 m 外水平打過去
            Vector3 axis = bounds.size.x < bounds.size.z ? Vector3.right : Vector3.forward;
            Vector3 target = bounds.center;
            Vector3 origin = target - axis * 8f;
            int heardBefore = _heard.Count;
            int start = _impacts.Count;
            InkBulletSpec spec = InkBulletSpec.Splattershot;
            InkBullet.Fire(spec, origin, axis, Team.Alpha, player);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            InkBulletImpact hit = _impacts.Count > start ? _impacts[start] : default;
            byte alpha = (byte)Team.Alpha;
            bool atPoint = wall.SampleOwnership(hit.Point, hit.Normal) == alpha;
            Vector3 up = hit.Point + Vector3.up * 1.2f;
            bool circle = wall.SampleOwnership(up, hit.Normal) == alpha;
            bool outside = wall.SampleOwnership(hit.Point + Vector3.up * 1.9f, hit.Normal) != alpha;
            bool sound = HeardSince(heardBefore, "ink_hit_wall");
            Check("打牆:牆面塗半徑 1.56 的圓 + ink_hit_wall",
                hit.End == InkBulletEnd.Terrain && Mathf.Abs(hit.Normal.y) < 0.3f && atPoint && circle && outside && sound,
                $"{hit.End} 法線 {hit.Normal} 命中點有墨={atPoint} 上 1.2 m={circle} 上 1.9 m 無墨={outside} 音效={sound}");
        }

        // ── (h) 爆破槍式規格 ──
        private IEnumerator BlasterLike(Inkling player)
        {
            var blast = new ExplosionSpec
            {
                NearDamage = 70f,
                NearRadius = 1.025f,
                FarDamage = 50f,
                FarRadius = 3.385f,
                DamageLinear = true,
                AttackClass = AttackClass.Blaster,
                Kind = DamageKind.Main,
                PaintRadius = 2f,
                FxBursts = 2,
                PaintRayCount = 4,
                PaintRayRadius = 0.8f,
                KnockbackAccel = 700f,
                KnockbackRange = 3.385f,
            };
            InkBulletSpec spec = InkBulletSpec.BlasterFamily(0.945f, 13, 125f, blast, 0.4234f, 0.3f);

            // 空爆:第 13F 在空中爆,線性 70 → 50
            Vector3 origin = new Vector3(-23f, 1.5f, -3f);
            BulletState sim = BulletBallistics.Launch(origin, Vector3.right, spec.Motion);
            for (int i = 0; i < 13; i++)
            {
                BulletBallistics.Step(ref sim, spec.Motion);
            }
            TargetDummy side = SpawnDummy(new Vector3(sim.Position.x, 0f, origin.z + 2.2f + _template.BodyRadius));
            yield return null;
            int start = _impacts.Count;
            int detonations = Explosion.DetonationCount;
            InkBullet.Fire(spec, origin, Vector3.right, Team.Alpha, player);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            InkBulletImpact burst = _impacts.Count > start ? _impacts[start] : default;
            float d = Vector3.Distance(burst.Point, side.ClosestPoint(burst.Point));
            float expected = blast.Evaluate(d);
            Check("爆破槍式:第 13F 空爆(≈ 9.9 m),線性爆炸傷害",
                burst.End == InkBulletEnd.AirBurst && burst.Frame == 13 && burst.Exploded
                && Explosion.DetonationCount == detonations + 1 && Mathf.Abs(burst.Point.x - sim.Position.x) < 0.01f
                && expected > 50f && expected < 70f && Mathf.Abs(side.LastDamage - expected) < 0.001f,
                $"{burst.End} 第 {burst.Frame}F 距離 {burst.Point.x - origin.x:0.00} m;旁邊假人 {d:0.00} m 受傷 {side.LastDamage}(期望 {expected})");

            // 直擊:125,直擊目標不再吃同一發的爆炸;旁邊的假人吃爆炸
            TargetDummy direct = SpawnDummy(new Vector3(-15f, 0f, 0f));
            TargetDummy beside = SpawnDummy(new Vector3(-15.5f, 0f, 1.7f));
            yield return null;
            int besideBefore = beside.PopupVersion;
            start = _impacts.Count;
            InkBullet.Fire(spec, new Vector3(-23f, 1f, 0f), Vector3.right, Team.Alpha, player);
            yield return WaitUntil(() => _impacts.Count > start, 2f);
            InkBulletImpact hit = _impacts.Count > start ? _impacts[start] : default;
            yield return null;
            Check("爆破槍式直擊:125(不衰減)、直擊目標不疊爆炸、旁邊假人吃爆炸",
                hit.End == InkBulletEnd.Target && ReferenceEquals(hit.Target, direct) && hit.Exploded
                && Mathf.Abs(direct.PopupTotal - 125f) < 0.001f && beside.PopupVersion != besideBefore
                && beside.LastDamage >= 50f && beside.LastDamage <= 70f,
                $"{hit.End} 第 {hit.Frame}F 直擊目標累計 {direct.PopupTotal} 旁邊受傷 {beside.LastDamage}");
        }

        // ── (e') 擁有者死亡 → 裝置消失 ──
        private IEnumerator OwnerDeathDespawn(GameObject playerGo, Inkling player, InklingHealth health,
            InklingLifecycle lifecycle)
        {
            Teleport(playerGo.transform, new Vector3(-3f, 0.1f, 18f));
            yield return new WaitForSeconds(0.2f);
            TestDeployable owned = TestDeployable.Create("CombatInfra_Owned", new Vector3(-6f, 0.5f, 10f), Vector3.one);
            _spawned.Add(owned);
            owned.Setup(player, player.Team, 120f, TargetClass.Sprinkler, true);
            TestDeployable keeper = TestDeployable.Create("CombatInfra_Keeper", new Vector3(-6f, 0.5f, 7f), Vector3.one);
            _spawned.Add(keeper);
            keeper.Setup(player, player.Team, 120f, TargetClass.Beakon, false);
            Physics.SyncTransforms();
            yield return null;
            health.Invulnerable = false;
            health.ClearArmor();
            health.ApplyDamage(new DamageInfo(150f, playerGo.transform.position, Team.Bravo, null, DamageKind.Main));
            bool died = lifecycle.IsDead;
            yield return null;
            yield return null;
            Check("擁有者被擊倒:DespawnWhenOwnerDies 的裝置消失,其他保留",
                died && owned == null && keeper != null && keeper.IsAlive,
                $"擊倒={died} 會消失的裝置已回收={owned == null} 保留的裝置存活={keeper != null && keeper.IsAlive}");
            lifecycle.ForceRespawnNow();
            health.Invulnerable = true;
            yield return null;
        }

        private bool HeardSince(int index, string prefix)
        {
            for (int i = index; i < _heard.Count; i++)
            {
                if (_heard[i].StartsWith(prefix, System.StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        private static string NameOf(IDamageable target)
        {
            var component = target as Component;
            return component != null ? component.name : "無";
        }

        private TargetDummy SpawnDummy(Vector3 position)
        {
            TargetDummy dummy = Instantiate(_template, position, Quaternion.identity);
            dummy.name = "CombatInfra_Dummy";
            _spawned.Add(dummy.gameObject);
            return dummy;
        }
    }
}
