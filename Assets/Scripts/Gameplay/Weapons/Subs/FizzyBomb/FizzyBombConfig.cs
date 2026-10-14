using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // Fizzy Bomb(タンサンボム / Bomb_Fizzy;S3 v11.3.0,research 03 §2.6、DESIGN_REFERENCE §4.3):
    // 60% 墨、85F 墨量鎖;投擲 1.36 u/F(通用 S3 彈道,比斯普拉炸彈遠:JP 4.2 線);
    // 按住蓄力:炸彈 5F 後出現在手上,40F → 第 2 段、再 80F → 第 3 段(按跳躍 −8F、甩搖桿 −11F,每幀最多兌現 2.5F);
    // 段數 = 爆炸次數:第 1 爆 = 第一次著地 30F 後,之後每爆完彈跳一次、再次著地 8F 後下一爆;
    // 每爆 50 / 35(半徑 1.6/3.8 → 1.95/4.5 → 2.6/5.45)、塗 3.0 → 3.4 → 3.8 + 6 滴 r 0.7、擊退 470 / 8;
    // 飛行途中每 2.2 u 在正下方落一筆墨(半寬 1.2 / 1.26 / 1.32,依段數)。對物件倍率走 AttackClass.Fizzy(Splash Wall ×3.6…)。
    [CreateAssetMenu(fileName = "Sub_fizzy_bomb", menuName = "SplatoonC/副武器/碳酸炸彈")]
    public sealed class FizzyBombConfig : S3ThrownSubConfig
    {
        [Header("碳酸炸彈(S3 參數)")]
        [SerializeField, Tooltip("S3 原始參數(u/F、F)")]
        private FizzyBombParams _s3 = FizzyBombParams.S3;

        [Header("幾何")]
        [SerializeField, Tooltip("碰撞球半徑(公尺;[EST])")]
        private float _radius = 0.18f;

        [SerializeField, Tooltip("可滾上的台階高度(公尺;[EST])")]
        private float _stepHeight = 0.25f;

        [SerializeField, Tooltip("彈跳中最多幾 F 沒著地就直接爆([EST] 保險)")]
        private int _hopTimeoutFrames = 150;

        [Header("爆炸")]
        [SerializeField, Tooltip("爆炸樣板(傷害值、擊退、墨滴、音效;半徑與塗墨半徑依第幾爆由 S3 參數覆寫)")]
        private ExplosionSpec _blast;

        [Header("音效")]
        [SerializeField, Tooltip("蓄力每升一段的「喀」聲 id")]
        private string _levelSound = "fizzy_charge_level";

        [SerializeField, Tooltip("蓄滿(第 3 段)音效 id")]
        private string _chargedSound = "fizzy_charge_full";

        [SerializeField, Tooltip("第 1 爆前的嘶嘶警告音 id")]
        private string _hissSound = "fizzy_hiss";

        public FizzyBombParams S3 => _s3;
        public float Radius => _radius;
        public float StepHeight => _stepHeight;
        public int HopTimeoutFrames => _hopTimeoutFrames;
        public string LevelSound => _levelSound;
        public string HissSound => _hissSound;

        public override S3ThrowParams ThrowParams => _s3.Throw;
        public override float BodyRadius => _radius;

        // ── 蓄力(ThrowController 通用蓄力 + 連打折抵)──
        public override int MaxChargeFrames => FizzyBombModel.MaxChargeFrames(_s3);
        public override int ChargeWindupFrames => _s3.WindupFrames;
        public override string ChargedSound => _chargedSound;
        public override float ChargeMashJumpFrames => _s3.MashJumpFrames;
        public override float ChargeMashStickFrames => _s3.MashStickFrames;
        public override float ChargeMashMaxPerFrame => _s3.MashMaxPerFrame;

        // 蓄力幀 → 段數(1~3 = 爆炸次數;HUD / 手持指示燈用)
        public int LevelAt(float chargeFrames)
        {
            return FizzyBombModel.LevelFor(_s3, chargeFrames);
        }

        public int LevelAt01(float charge01)
        {
            return FizzyBombModel.LevelFor01(_s3, charge01);
        }

        // 第 burstIndex 爆(0 起算)的爆炸規格
        public ExplosionSpec BlastAt(int burstIndex)
        {
            ExplosionSpec spec = _blast;
            spec.NearDamage = _s3.NearDamage;
            spec.FarDamage = _s3.FarDamage;
            spec.NearRadius = FizzyBombModel.NearRadius(_s3, burstIndex);
            spec.FarRadius = FizzyBombModel.FarRadius(_s3, burstIndex);
            spec.PaintRadius = FizzyBombModel.PaintRadius(_s3, burstIndex);
            spec.PaintRayReach = spec.FarRadius;
            return spec;
        }

        // bot:落地後會滾、爆完往前彈 → 瞄目標前 1.5 m [EST]
        protected override float BotLandOffset => -1.5f;

        // bot:近距離蓄到第 2 段(0.8 s),其餘蓄滿第 3 段(約 2.1 s;bot 不連打)
        public override float BotHoldSeconds(float targetDistance)
        {
            int level = targetDistance < 7f ? 2 : 3;
            return (FizzyBombModel.HoldFramesForLevel(_s3, level) + 3f) / 60f;
        }

        // 編輯器設定(ThrowablesSetup)呼叫:全部欄位寫成 S3 0 AP 數值(冪等)
        public void ResetToS3()
        {
            _s3 = FizzyBombParams.S3;
            _displayName = "碳酸炸彈";
            _maxActive = 0;
            ResetThrowFields(0.6f, 85, "bomb_throw");
            _botUsage = BotSubUsage.ThrowAtEnemy;
            _botRange = new Vector2(5f, 18f);

            _radius = 0.18f;
            _stepHeight = 0.25f;
            _hopTimeoutFrames = 150;
            _levelSound = "fizzy_charge_level";
            _chargedSound = "fizzy_charge_full";
            _hissSound = "fizzy_hiss";
            _blast = new ExplosionSpec
            {
                NearDamage = _s3.NearDamage,
                NearRadius = _s3.NearRadius1,
                FarDamage = _s3.FarDamage,
                FarRadius = _s3.FarRadius1,
                AttackClass = AttackClass.Fizzy,
                Kind = DamageKind.Sub,
                PaintRadius = _s3.PaintRadius1,
                // CrossPaintRadius 2.0:以幾條短噴墨射線近似(牆腳也吃得到)[EST]
                PaintRayCount = 4,
                PaintRayRadius = 1f,
                PaintRayReach = _s3.FarRadius1,
                // 6 滴 r 0.7(S3 SplashAroundParam)
                DropCount = 6,
                DropRadius = 0.7f,
                PaintHardness = 0.7f,
                KnockbackAccel = 470f,
                KnockbackRange = 8f,
                FxBursts = 3,
                Sound = "fizzy_burst",
                SoundVolume = 0.8f,
            };
        }
    }
}
