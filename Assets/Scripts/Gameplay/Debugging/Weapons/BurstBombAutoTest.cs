using System.Collections;
using System.Collections.Generic;
using SplatoonC.Core.Combat;
using SplatoonC.Gameplay.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Debugging
{
    // Burst Bomb 驗收(S3 v11.3.0,research 03 §2.3),試射場,全走真實路徑:
    //   (a) 換裝:手持模型、bot 掛勾
    //   (b) 直擊假人:碰到就爆(飛行中、沒有引信);直擊 25 + 近距爆炸 35 = 60(兩筆傷害事件)
    //   (c) 丟到地上:最近點 1.8 m → 35(≤ 2.8)、3.4 m → 25(≤ 4.0)、4.6 m → 0;爆炸塗地 r 4.0
    //   (d) 耗墨 45%:滿墨連丟兩顆、第三顆墨不夠不丟;兩次副武器至少間隔 30F;墨量鎖 60F
    [AutoTest(Scene = AutoTestAttribute.TrainingScene, Timeout = 120f, Order = 231)]
    public sealed class BurstBombAutoTest : BombTestBase
    {
        private const string Label = "Burst Bomb";

        private readonly List<float> _dealt = new List<float>();
        private IDamageable _watch;

        public static void Run()
        {
            Launch<BurstBombAutoTest>();
        }

        protected override void OnDisable()
        {
            base.OnDisable();
            if (Player != null)
            {
                Player.DamageDealt -= OnDealt;
            }
        }

        private void OnDealt(IDamageable target, float amount, bool splatted)
        {
            if (ReferenceEquals(target, _watch))
            {
                _dealt.Add(amount);
            }
        }

        private IEnumerator Start()
        {
            if (!Prepare("burst_bomb", out BurstBombConfig config))
            {
                yield break;
            }
            Player.DamageDealt += OnDealt;
            yield return MoveTo(new Vector3(-12f, 0.1f, 10f), Vector3.forward);
            yield return CheckHeldModel(config, Label);
            CheckBotUsage(config, Label);
            yield return WaitSeconds(0.8f);

            yield return DirectHit();
            yield return GroundBlast(config);
            yield return InkAndInterval();

            yield return BotStyleThrow(config, Label, new Vector3(-4f, 0.1f, 8f), Vector3.forward, 7f,
                () => BurstBomb.ExplosionCount, 2f);
            CheckNoExceptions(Label);
            Cleanup();
            Finish();
        }

        private IEnumerator DirectHit()
        {
            yield return MoveTo(new Vector3(-12f, 0.1f, -20f), Vector3.forward);
            Tank.RefillFull();
            // 假人放在槍口正前方(槍口在角色右側約 0.5 m)
            TargetDummy dummy = SpawnDummy(new Vector3(Muzzle.position.x, 0f, -14f));
            yield return Frames(2);
            // 掃俯仰角:找預覽落點打在假人身上(對人判定:預覽也會停在目標圖層)且高度最接近 1 m 的角度
            Intent.Sub = true;
            float bestPitch = 0f;
            float bestError = float.MaxValue;
            Vector3 dummyAxis = dummy.transform.position;
            for (float pitch = -25f; pitch <= 10f; pitch += 1f)
            {
                AimYawPitch(Vector3.forward, pitch);
                yield return Frames(2);
                Vector3 landing = Thrower.PredictedLanding;
                if (!Thrower.HasPredictedLanding || Flat(landing, dummyAxis) > 0.6f)
                {
                    continue;
                }
                float error = Mathf.Abs(landing.y - 1f);
                if (error < bestError)
                {
                    bestError = error;
                    bestPitch = pitch;
                }
            }
            AimYawPitch(Vector3.forward, bestPitch);
            yield return Frames(2);
            _watch = dummy;
            _dealt.Clear();
            int explosions = BurstBomb.ExplosionCount;
            int directs = BurstBomb.DirectHitCount;
            yield return Release();
            yield return WaitUntil(() => BurstBomb.ExplosionCount > explosions, 1.5f);
            yield return Frames(2);
            float flight = BurstBomb.LastExplosionTime - BurstBomb.LastLaunchTime;
            Vector3 center = BurstBomb.LastExplosionCenter;
            float sum = 0f;
            foreach (float d in _dealt)
            {
                sum += d;
            }
            Check($"{Label} 直擊:碰到假人就爆(沒有引信)",
                BurstBomb.DirectHitCount == directs + 1 && ReferenceEquals(BurstBomb.LastDirectTarget, dummy)
                && BurstBomb.LastContact == BombContact.Target && flight < 0.6f && Closest(dummy, center) < 0.05f,
                $"俯仰 {bestPitch}° 投出到爆炸 {flight:0.000} s 爆心 {center} 離假人 {Closest(dummy, center):0.000} m");
            Check($"{Label} 直擊 25 + 近距爆炸 35 = 60",
                _dealt.Count == 2 && Mathf.Abs(_dealt[0] - 25f) < 0.01f && Mathf.Abs(_dealt[1] - 35f) < 0.01f
                && Mathf.Abs(dummy.PopupTotal - 60f) < 0.01f,
                $"傷害事件 [{string.Join(", ", _dealt)}] 頭頂累計 {dummy.PopupTotal}");
            _watch = null;
            Remove(dummy);
        }

        private IEnumerator GroundBlast(BurstBombConfig config)
        {
            yield return MoveTo(new Vector3(-18f, 0.1f, -12f), Vector3.forward);
            Tank.RefillFull();
            yield return AimToLand(new Vector3(-18f, 0f, -4f));
            Vector3 predicted = Thrower.PredictedLanding;
            // 爆心在地面上 0.1 m;假人放在落點的側邊與後方(不在飛行路徑上)
            Vector3 center = predicted + Vector3.up * 0.1f;
            TargetDummy near = SpawnDummyAtDistance(center, Vector3.right, 1.8f, 0f);
            TargetDummy far = SpawnDummyAtDistance(center, Vector3.left, 3.4f, 0f);
            TargetDummy outside = SpawnDummyAtDistance(center, Vector3.forward, 4.6f, 0f);
            yield return Frames(3);
            int explosions = BurstBomb.ExplosionCount;
            yield return Release();
            yield return WaitUntil(() => BurstBomb.ExplosionCount > explosions, 1.5f);
            yield return Frames(2);
            Vector3 blast = BurstBomb.LastExplosionCenter;
            // 預覽與實飛同一個 60 Hz 步進、同半徑(r 0.16)的 sphere cast:預覽落點 = 實際接觸點
            Check($"{Label} 碰到地面就爆、落點 = 預覽",
                BurstBomb.LastContact == BombContact.Terrain && Flat(blast, predicted) < 0.15f,
                $"爆心 {blast} 預覽 {predicted} 誤差 {Flat(blast, predicted):0.00} m");
            CheckDamage(config, near, blast, 35f, "近距 35(≤ 2.8 m)");
            CheckDamage(config, far, blast, 25f, "遠距 25(2.8 ~ 4.0 m)");
            CheckDamage(config, outside, blast, 0f, "範圍外 0(> 4.0 m)");
            yield return Frames(3);
            int ring = RingInked(blast, 3f);
            int ringInner = RingInked(blast, 1.5f);
            Check($"{Label} 爆炸塗地 r 4.0", ring >= 6 && ringInner == 8,
                $"r 1.5 圓周 {ringInner}/8、r 3.0 圓周 {ring}/8 有墨");
            Remove(near);
            Remove(far);
            Remove(outside);
        }

        private IEnumerator InkAndInterval()
        {
            yield return MoveTo(new Vector3(-6f, 0.1f, -22f), Vector3.right);
            yield return WaitSeconds(0.6f);
            Tank.RefillFull();
            AimYawPitch(Vector3.right, -40f);
            Intent.Sub = true;
            yield return Frames(3);
            yield return Release();
            float first = LastThrowTime;
            float spent1 = LastInkBefore - LastInkAfter;
            bool threw1 = LastThrew;

            // 10F 後再按:30F 間隔內不丟
            yield return Frames(4);
            Intent.Sub = true;
            yield return Frames(3);
            yield return Release();
            bool threwEarly = LastThrew;

            // 間隔到了:第二顆
            while (Time.time - first < 0.55f)
            {
                yield return null;
            }
            Intent.Sub = true;
            yield return Frames(3);
            yield return Release();
            bool threw2 = LastThrew;
            float spent2 = LastInkBefore - LastInkAfter;
            float remaining = Tank.Normalized;
            Check($"{Label} 耗墨 45%、30F 內不能再丟、滿墨可連丟兩顆",
                threw1 && threw2 && !threwEarly && Mathf.Abs(spent1 - 0.45f) < 0.005f && Mathf.Abs(spent2 - 0.45f) < 0.005f
                && Mathf.Abs(remaining - 0.1f) < 0.01f,
                $"第一顆 {threw1}(扣 {spent1:0.000})10F 後={threwEarly} 第二顆 {threw2}(扣 {spent2:0.000})剩 {remaining:0.000}");

            float lockSeconds = -1f;
            yield return MeasureInkLock(1f, s => lockSeconds = s);
            Check($"{Label} 墨量鎖 60F = 1.000 s", Mathf.Abs(lockSeconds - 1f) <= LockTolerance,
                $"丟出後 {lockSeconds:0.000} s 開始回墨(容許 ±{LockTolerance:0.000})");

            // 第三顆:墨不夠(< 45%)不丟
            int throws = Thrower.ThrowCount;
            float ink = Tank.Normalized;
            Intent.Sub = true;
            yield return Frames(3);
            yield return Release();
            Check($"{Label} 墨不夠不丟", !LastThrew && Thrower.ThrowCount == throws && ink < 0.45f,
                $"墨 {ink:0.000} 投出={LastThrew}");
        }

        private void CheckDamage(BurstBombConfig config, TargetDummy dummy, Vector3 blast, float expected, string band)
        {
            float distance = Closest(dummy, blast);
            float model = config.Blast.Evaluate(distance);
            float dealt = dummy.PopupVisible ? dummy.PopupTotal : 0f;
            Check($"{Label} {band}",
                Mathf.Abs(model - expected) < 0.01f && Mathf.Abs(dealt - expected) < 0.01f,
                $"最近點距離 {distance:0.00} m 模型 {model} 實得 {dealt}");
        }
    }
}
