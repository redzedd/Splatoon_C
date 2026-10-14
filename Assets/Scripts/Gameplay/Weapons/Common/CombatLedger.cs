using SplatoonC.Core.Combat;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 全場共用的「一次攻擊」帳本(AttackLedger 的執行期實例)。武器在一次揮動/潑灑/齊射開始時:
    //   int attackId = CombatLedger.NewAttackId();
    // 把同一個 attackId 傳給這次攻擊的所有 InkBullet.Fire / Explosion.Detonate;同一目標在同一次攻擊只吃一次
    // (或在 InkBulletSpec.PerAttackDamageCap > 0 時累加到上限,例:傘 81)。進 Play / 換場景時清空。
    public static class CombatLedger
    {
        public static readonly AttackLedger Ledger = new AttackLedger(256);

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            Ledger.Clear();
        }

        public static int NewAttackId()
        {
            return Ledger.NewAttackId();
        }

        // 回傳允許的傷害(0 = 本次攻擊已打過此目標 / 已達上限)
        public static float Register(int attackId, IDamageable target, float damage, float cap = 0f)
        {
            return Ledger.Register(attackId, target, damage, cap);
        }

        public static bool HasHit(int attackId, IDamageable target)
        {
            return Ledger.HasHit(attackId, target);
        }
    }
}
