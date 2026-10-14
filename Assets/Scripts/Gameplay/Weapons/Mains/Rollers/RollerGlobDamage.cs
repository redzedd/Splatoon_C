using SplatoonC.Core.Combat;

namespace SplatoonC.Gameplay.Combat
{
    // 一類甩墨墨團的傷害委派(InkBullet.Fire 的 damageFunc):依接觸瞬間的飛行距離(沿彈道)、飛行幀數算 S3 分段傷害,
    // 並實作「同一甩對同一目標只算最高的一顆」——
    //   CombatLedger 以負的 attackId 記這一甩對該目標目前結算過的最高值,回傳「補到這顆傷害」的差額(比較低的墨團 = 0)。
    //   InkBullet 本身以正的 attackId + PerAttackDamageCap(大值)累加,不會再擋掉補差額。
    // 例:先中一顆側邊 100、再中一顆中央 150 → 100 + 50 = 150,兩顆 100 → 100(不疊加)。
    // 每把滾筒在換裝時建好固定幾個實例(甩法 × 單位 × 中央/側邊 × 弱甩),執行期零配置。
    public sealed class RollerGlobDamage
    {
        private readonly RollerFlingDamage _damage;
        private readonly bool _inside;
        private readonly bool _depleted;
        private readonly float _unitRate;

        public readonly InkBulletDamageFunc Func;

        public RollerGlobDamage(in RollerFlingDamage damage, bool inside, bool depleted, float unitRate)
        {
            _damage = damage;
            _inside = inside;
            _depleted = depleted;
            _unitRate = unitRate;
            Func = Evaluate;
        }

        public bool Inside => _inside;
        public bool Depleted => _depleted;

        // 這顆墨團(不含去重)在 travelled 公尺、第 frame 幀的傷害
        public float Raw(float travelled, int frame)
        {
            return _damage.Evaluate(travelled, frame, _inside, _depleted, _unitRate);
        }

        private float Evaluate(InkBullet bullet, IDamageable target, float frameDamage)
        {
            // 距離 = 接觸瞬間的沿彈道距離(不是本步終點;Dynamo 墨團一步可達 2 m,必殺邊緣差很多)
            float damage = Raw(bullet.ContactTravelled, bullet.State.Frame);
            if (damage <= 0f || bullet.AttackId == 0)
            {
                return damage;
            }
            // cap = 本顆傷害:已記錄 t → 允許 min(d, d − t) = 補差額;第一次 = d
            return CombatLedger.Register(-bullet.AttackId, target, damage, damage);
        }
    }
}
