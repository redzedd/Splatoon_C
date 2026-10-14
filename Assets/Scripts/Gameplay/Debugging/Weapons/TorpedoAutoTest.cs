using System.Collections;
using SplatoonC.Core;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Torpedo 驗收(S3 v11.3.0,research 03 §2.14),試射場,全走真實路徑(意圖 → ThrowController → Torpedo → Explosion → 假人):
    //   (a) 換裝:手持模型、bot 掛勾、同時 1 顆(BlockWhileMaxActive)
    //   (b) 搜尋 → 變身 → 衝刺:第 6F 開始搜尋、鎖定最近的假人;變身 45F(0.75 s)期間 HP 20、Target 圖層碰撞體、
    //       登記受擊、會引爆敵方炸彈;衝刺命中假人 60 + 散彈 12 × k、8 顆散彈各自落地爆炸;耗墨 65%、墨量鎖 88F;
    //       投出時腳下塗墨;場上還有自己的 Torpedo 時「使用不可」(不扣墨);爆炸後(散彈還在飛)就能再丟
    //   (c) 打壞:敵方爆炸遠距 30 × 0.6 = 18 → HP 2;敵方 Splattershot 一發 → 壞掉,不爆不塗
    //   (d) 先碰地:失去搜尋、落地 30F(0.5 s)後爆 60/35(沒有散彈)、落點 = 預覽、滾動中打不到
    //   (e) bot 用法(模擬 BotBrain):看目標腳下、按住 BotHoldSeconds 放開 → 搜尋、衝刺命中
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 240)]
    public sealed class TorpedoAutoTest : ThrowableTestBase
    {
        private const string Label = "Torpedo";

        public static void Run()
        {
            Launch<TorpedoAutoTest>();
        }

        private IEnumerator Start()
        {
            if (!Prepare("torpedo", out TorpedoConfig config))
            {
                yield break;
            }
            yield return MoveTo(new Vector3(-6f, 0.1f, 20f), Vector3.back);
            yield return CheckHeldModel(config, Label);
            Vector2 range = config.BotRange;
            Check($"{Label} bot 掛勾:丟向交戰中的敵人(投擲 ≈ 20 m + 搜尋 9 m)、同時 1 顆且達上限不能丟",
                config.BotUsage == BotSubUsage.ThrowAtEnemy && range.x >= 4f && range.y > 20f
                && config.BotHoldSeconds(12f) > 0f && config.MaxActive == 1 && config.BlockWhileMaxActive,
                $"用法={config.BotUsage} 距離 {range.x:0}~{range.y:0} m、上限 {config.MaxActive}、擋投擲={config.BlockWhileMaxActive}");
            // 手持測試丟出的那顆落地 30F 就爆;等它結束
            yield return WaitUntil(() => !Thrower.SubUnavailable, 3f);
            yield return WaitSeconds(0.3f);

            yield return SearchTransformChase(config);
            yield return ShootDown(config);
            yield return GroundRoll(config);

            yield return BotUse(config);
            yield return WaitSeconds(1.2f);
            CheckNoExceptions(Label);
            Debug.Log($"[AUTOTEST] INFO {Label} 量測期間最大幀時間 {MaxDeltaTime * 1000f:0.0} ms");
            Cleanup();
            Finish();
        }

        // ── (e) bot 用法 ──
        private IEnumerator BotUse(TorpedoConfig config)
        {
            yield return WaitUntil(() => !Thrower.SubUnavailable, 3f);
            TargetDummy dummy = SpawnDummy(new Vector3(-10f, 0f, 14f));
            int transforms = Torpedo.TransformCount;
            int chases = Torpedo.ChaseExplosionCount;
            bool threw = false;
            float hold = 0f;
            yield return BotAimThrow(config, new Vector3(-22f, 0.1f, 14f), Vector3.right, 12f, (t, h) =>
            {
                threw = t;
                hold = h;
            });
            yield return WaitUntil(() => Torpedo.ChaseExplosionCount > chases, 3f);
            yield return Frames(3);
            float dealt = dummy.PopupVisible ? dummy.PopupTotal : 0f;
            Check($"{Label} bot 用法(模擬 BotBrain:BotAimPoint 解 S3 彈道、按住 BotHoldSeconds)→ 搜尋、變身、衝刺命中 12 m 外的目標",
                threw && Torpedo.TransformCount == transforms + 1 && ReferenceEquals(Torpedo.LastTarget, dummy)
                && Torpedo.ChaseExplosionCount == chases + 1 && dealt >= 60f,
                $"投出={threw} 按住 {hold:0.00} s 變身 +{Torpedo.TransformCount - transforms} 假人受傷 {dealt}");
            yield return WaitSeconds(1.2f);
            Remove(dummy);
        }

        private static Torpedo Live()
        {
            foreach (Torpedo torpedo in FindObjectsByType<Torpedo>(FindObjectsSortMode.None))
            {
                if (torpedo.IsActive && torpedo.State != TorpedoState.Droplets)
                {
                    return torpedo;
                }
            }
            return null;
        }

        // ── (b) 搜尋 → 變身 → 衝刺 ──
        private IEnumerator SearchTransformChase(TorpedoConfig config)
        {
            yield return MoveTo(new Vector3(-22f, 0.1f, -12f), Vector3.right);
            Tank.RefillFull();
            // 兩個假人都在第 6F 的搜尋窗內(水平 ≈ 6.1 / 6.8 m),近的那個要被鎖定
            TargetDummy near = SpawnDummy(new Vector3(-9f, 0f, -10.5f));
            TargetDummy other = SpawnDummy(new Vector3(-8f, 0f, -14.5f));
            yield return Frames(3);
            Vector3 feet = PlayerGo.transform.position;
            bool feetInkedBefore = GroundInked(feet);
            yield return AimToLand(near.transform.position);
            int transforms = Torpedo.TransformCount;
            int chases = Torpedo.ChaseExplosionCount;
            int droplets = Torpedo.DropletBlastCount;
            int refused = Thrower.RefusedThrowCount;
            yield return Release();
            float spent = LastInkBefore - LastInkAfter;
            yield return Frames(2);
            Check($"{Label} 耗墨 65%;投出時投擲者腳下塗一小片", LastThrew && Mathf.Abs(spent - 0.65f) < 0.005f
                && !feetInkedBefore && GroundInked(feet),
                $"扣墨 {spent:0.000} 腳下 {feetInkedBefore} → {GroundInked(feet)}");

            // 變身中:HP 20、受擊碰撞體、登記、引爆敵方炸彈;同時「使用不可」
            yield return WaitUntil(() => Torpedo.TransformCount > transforms, 1f);
            yield return Frames(2);
            Torpedo live = Live();
            Team enemy = Player.Team == Team.Alpha ? Team.Bravo : Team.Alpha;
            Collider hull = live != null ? live.GetComponentInChildren<SphereCollider>() : null;
            Check($"{Label} 第 6F 開始搜尋(S3 投出後 5~6F 不搜尋)、鎖定最近的敵方目標",
                Torpedo.TransformCount == transforms + 1 && Torpedo.LastTransformFrame == 6
                && ReferenceEquals(Torpedo.LastTarget, near),
                $"變身於第 {Torpedo.LastTransformFrame}F 目標={(ReferenceEquals(Torpedo.LastTarget, near) ? "近的假人" : "其他")}");
            Check($"{Label} 變身中:HP 20、Target 圖層受擊碰撞體、登記受擊、會引爆敵方炸彈(不引爆自隊)",
                live != null && live.State == TorpedoState.Transforming && Mathf.Abs(live.Hp - 20f) < 1e-4f
                && hull != null && hull.enabled && hull.gameObject.layer == GameLayers.Target
                && DamageableRegistry.Active.Contains(live) && BombRules.Detonates(hull, enemy)
                && !BombRules.Detonates(hull, Player.Team),
                live == null ? "找不到變身中的 Torpedo"
                    : $"狀態 {live.State} HP {live.Hp} 碰撞體 {(hull != null && hull.enabled)} 圖層 {(hull != null ? hull.gameObject.layer : -1)}" +
                      $" 登記={DamageableRegistry.Active.Contains(live)} 敵方炸彈引爆={(hull != null && BombRules.Detonates(hull, enemy))}");

            // 自己的 Torpedo 還在:再按一次副武器 → 使用不可(不扣墨、不算投擲)
            yield return WaitUntil(() => Time.time - LastThrowTime >= 0.55f, 1f);
            float inkBefore = Tank.Normalized;
            int throws = Thrower.ThrowCount;
            bool unavailable = Thrower.SubUnavailable;
            Intent.Sub = true;
            yield return Frames(3);
            Intent.Sub = false;
            yield return Frames(2);
            Check($"{Label} 同時 1 顆:自己的還在場上時放開副武器 → 使用不可(不扣墨、不投擲)",
                unavailable && Thrower.RefusedThrowCount == refused + 1 && Thrower.ThrowCount == throws
                && Mathf.Abs(Tank.Normalized - inkBefore) < 1e-4f,
                $"使用不可={unavailable} 拒絕 {Thrower.RefusedThrowCount - refused} 次 投擲數 {throws} → {Thrower.ThrowCount} 墨 {inkBefore:0.000} → {Tank.Normalized:0.000}");

            // 衝刺命中:變身 45F 後開始衝刺
            float unlockedAt = -1f;
            float level = Tank.Normalized;
            bool availableAtBlast = false;
            float deadline = Time.time + 2.5f;
            while (Time.time < deadline && (Torpedo.ChaseExplosionCount == chases || unlockedAt < 0f))
            {
                yield return null;
                Track();
                if (unlockedAt < 0f && Tank.Normalized > level + 1e-5f)
                {
                    unlockedAt = Time.time;
                }
                level = Tank.Normalized;
                if (Torpedo.ChaseExplosionCount > chases && !availableAtBlast
                    && Torpedo.DropletBlastCount == droplets)
                {
                    // 爆炸當下(散彈還在飛)就不再佔用「同時 1 顆」
                    availableAtBlast = !Thrower.SubUnavailable;
                }
            }
            int transformFrames = Torpedo.LastChaseStartFrame - Torpedo.LastTransformFrame;
            float transformSeconds = Torpedo.LastChaseStartTime - Torpedo.LastTransformTime;
            Check($"{Label} 變身 45F(0.75 s)後朝目標直線衝刺、命中爆炸",
                transformFrames == 45 && Mathf.Abs(transformSeconds - 0.75f) <= 0.05f
                && Torpedo.ChaseExplosionCount == chases + 1 && Torpedo.LastWasChase,
                $"變身 {transformFrames}F / {transformSeconds:0.000} s;追蹤爆炸 {Torpedo.ChaseExplosionCount - chases} 次");
            float lockSeconds = unlockedAt > 0f ? unlockedAt - LastThrowTime : -1f;
            Check($"{Label} 墨量鎖 88F = 1.467 s", Mathf.Abs(lockSeconds - 88f / 60f) <= LockTolerance,
                $"丟出後 {lockSeconds:0.000} s 開始回墨(容許 ±{LockTolerance:0.000})");
            Check($"{Label} 爆炸後(散彈還在飛)就不再「使用不可」", availableAtBlast,
                $"爆炸當下可再丟={availableAtBlast}");
            yield return WaitUntil(() => Torpedo.DropletBlastCount >= droplets + 8, 2.5f);
            yield return Frames(3);
            Vector3 center = Torpedo.LastExplosionCenter;
            float dealt = near.PopupVisible ? near.PopupTotal : 0f;
            float extra = dealt - 60f;
            int drops = Mathf.RoundToInt(extra / 12f);
            Check($"{Label} 衝刺命中假人:近距 60(2.6)+ 散彈各 12(8 顆各自碰地爆炸)",
                Torpedo.DropletBlastCount == droplets + 8 && Closest(near, center) < 0.5f && drops >= 1
                && Mathf.Abs(extra - drops * 12f) < 0.01f && dealt <= 60f + 96f,
                $"散彈爆炸 {Torpedo.DropletBlastCount - droplets} 次;假人最近點 {Closest(near, center):0.00} m 累計 {dealt}(= 60 + 12 × {drops})");
            int ring = RingInked(center, 2.8f);
            Check($"{Label} 追蹤爆炸塗墨 r 3.5", ring >= 7, $"半徑 2.8 m 圓周 8 點中 {ring} 點是自隊墨");
            Remove(near);
            Remove(other);
        }

        // ── (c) 變身中被打壞 ──
        private IEnumerator ShootDown(TorpedoConfig config)
        {
            yield return WaitUntil(() => !Thrower.SubUnavailable, 3f);
            yield return MoveTo(new Vector3(-22f, 0.1f, 6f), Vector3.right);
            Tank.RefillFull();
            TargetDummy dummy = SpawnDummy(new Vector3(-8f, 0f, 6f));
            yield return Frames(2);
            yield return AimToLand(dummy.transform.position);
            int transforms = Torpedo.TransformCount;
            yield return Release();
            yield return WaitUntil(() => Torpedo.TransformCount > transforms, 1f);
            yield return Frames(2);
            Torpedo live = Live();
            if (live == null || live.State != TorpedoState.Transforming)
            {
                Check($"{Label} 打壞測試前提:Torpedo 變身中", false, live == null ? "沒有 Torpedo" : live.State.ToString());
                Remove(dummy);
                yield break;
            }
            Vector3 p = live.SimulatedPosition;
            int chase = Torpedo.ChaseExplosionCount;
            int thrown = Torpedo.ThrowExplosionCount;
            int droplets = Torpedo.DropletBlastCount;
            int broken = Torpedo.BrokenCount;
            // 敵方炸彈遠距 30:對變身中的 Torpedo × 0.6 = 18(S3 DamageRate Bomb → Bomb_TorpedoBullet)
            var enemyBlast = new ExplosionSpec
            {
                NearDamage = 180f, NearRadius = 1f, FarDamage = 30f, FarRadius = 7f,
                AttackClass = AttackClass.Bomb, Kind = DamageKind.Sub, FlashRadius = -1f,
            };
            Explosion.Detonate(enemyBlast, p + new Vector3(0f, 0f, 5f), Team.Bravo, null);
            float hpAfterBlast = live.Hp;
            int detonations = Explosion.DetonationCount;
            InkBulletSpec spec = InkBulletSpec.Splattershot;
            spec.DripSpacing = 0f;
            InkBullet.Fire(spec, p + new Vector3(0f, 0f, 3f), Vector3.back, Team.Bravo, null);
            yield return WaitUntil(() => Torpedo.BrokenCount > broken, 1f);
            yield return Frames(3);
            Check($"{Label} HP 20:敵方炸彈遠距 30 × 0.6 = 18 → 剩 2;敵方 Splattershot 一發(36)打壞",
                Mathf.Abs(hpAfterBlast - 2f) < 0.01f && Torpedo.BrokenCount == broken + 1 && !live.IsActive,
                $"爆炸後 HP {hpAfterBlast};打壞 {Torpedo.BrokenCount - broken} 次;還在場上={live.IsActive}");
            Check($"{Label} 被打壞:不爆炸、不噴散彈,也不再「使用不可」",
                Torpedo.ChaseExplosionCount == chase && Torpedo.ThrowExplosionCount == thrown
                && Torpedo.DropletBlastCount == droplets && Explosion.DetonationCount == detonations
                && !Thrower.SubUnavailable && !DamageableRegistry.Active.Contains(live),
                $"追蹤爆炸 +{Torpedo.ChaseExplosionCount - chase} 落地爆炸 +{Torpedo.ThrowExplosionCount - thrown}" +
                $" 散彈 +{Torpedo.DropletBlastCount - droplets} 爆炸解算 +{Explosion.DetonationCount - detonations} 使用不可={Thrower.SubUnavailable}");
            Remove(dummy);
        }

        // ── (d) 先碰地:滾 30F 後爆 ──
        private IEnumerator GroundRoll(TorpedoConfig config)
        {
            yield return WaitUntil(() => !Thrower.SubUnavailable, 3f);
            yield return MoveTo(new Vector3(-22f, 0.1f, -22f), Vector3.right);
            // 兩次副武器之間至少 30F(上一顆剛被打壞)
            yield return WaitSeconds(0.55f);
            Tank.RefillFull();
            AimYawPitch(Vector3.right, -30f);
            Intent.Sub = true;
            yield return Frames(3);
            Vector3 predicted = Thrower.PredictedLanding;
            bool hasPrediction = Thrower.HasPredictedLanding;
            int transforms = Torpedo.TransformCount;
            int thrown = Torpedo.ThrowExplosionCount;
            int droplets = Torpedo.DropletBlastCount;
            float landBefore = Torpedo.LastLandTime;
            yield return Release();
            yield return WaitUntil(() => Torpedo.LastLandTime > landBefore, 1f);
            yield return Frames(2);
            Torpedo live = Live();
            bool rollingUntouchable = live != null && live.State == TorpedoState.Rolling && !live.IsAlive
                                      && !DamageableRegistry.Active.Contains(live) && live.SearchLost;
            Vector3 land = Torpedo.LastLandPoint;
            // 假人擺在落點側邊 4.5 m(滾動方向 +x 的垂直方向,往場地內側 +z)
            TargetDummy dummy = SpawnDummy(new Vector3(land.x, 0f, land.z) + Vector3.forward * 4.5f);
            yield return WaitUntil(() => Torpedo.ThrowExplosionCount > thrown, 1f);
            yield return Frames(3);
            int fuse = Torpedo.LastExplosionFrame - Torpedo.LastLandFrame;
            float seconds = Torpedo.LastExplosionTime - Torpedo.LastLandTime;
            Check($"{Label} 先碰地:失去搜尋、滾動中打不到(不登記受擊)", rollingUntouchable,
                live == null ? "找不到 Torpedo" : $"狀態 {live.State} 可受擊={live.IsAlive} 失去搜尋={live.SearchLost}");
            Check($"{Label} 落地 30F(0.5 s)後爆炸(BlastParamThrow,沒有散彈、沒有變身)",
                fuse == 30 && Mathf.Abs(seconds - 0.5f) <= 0.05f && !Torpedo.LastWasChase
                && Torpedo.TransformCount == transforms && Torpedo.DropletBlastCount == droplets,
                $"著地 → 爆炸 {fuse}F / {seconds:0.000} s;變身 +{Torpedo.TransformCount - transforms} 散彈 +{Torpedo.DropletBlastCount - droplets}");
            Check($"{Label} 著地點 = 預覽落點(同一個 S3ThrownFlight)", hasPrediction && Flat(land, predicted) < 0.35f,
                $"著地 {land} 預覽 {predicted} 誤差 {Flat(land, predicted):0.00} m");
            Vector3 center = Torpedo.LastExplosionCenter;
            float distance = Closest(dummy, center);
            float expected = config.ThrowBlast.Evaluate(distance);
            float dealt = dummy.PopupVisible ? dummy.PopupTotal : 0f;
            Check($"{Label} 落地爆炸 60(2.6)/ 35(6.0):假人傷害 = 模型",
                expected > 0f && Mathf.Abs(dealt - expected) < 0.01f,
                $"最近點 {distance:0.00} m 模型 {expected} 實得 {dealt}");
            Remove(dummy);
        }
    }
}
