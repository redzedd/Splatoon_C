using System;
using UnityEngine;

namespace SplatoonC.Core.Combat
{
    [Serializable]
    public struct VitalsSettings
    {
        public float MaxHp;
        // 最後一次受傷後多久開始回血(秒);碰到敵墨也會重置
        public float RegenDelay;
        // 潛在自家墨中(地面或牆上)的回血速率(HP/秒)
        public float RegenSubmerged;
        // 其他狀態(人形、乾地上的烏賊)的回血速率(HP/秒)
        public float RegenExposed;
        // 敵墨持續傷害(HP/秒)與上限:累積傷害達上限後不再增加,持續傷害永遠不會致死
        public float SlipDamagePerSecond;
        public float SlipDamageCap;

        // Splatoon 3 預設(0 AP):60F 延遲、100/s 潛墨、12.6/s 其他;敵墨 0.3/F = 18/s,上限 40
        public static VitalsSettings Splatoon3 => new VitalsSettings
        {
            MaxHp = 100f,
            RegenDelay = 1f,
            RegenSubmerged = 100f,
            RegenExposed = 12.6f,
            SlipDamagePerSecond = 18f,
            SlipDamageCap = 40f,
        };
    }

    // 角色血量純邏輯:以「累積傷害」表示(0 = 滿血,≥ MaxHp = 擊倒),傷害取整到 0.1(Splatoon 的 99.9 規則)。
    // 護甲(出生/翻滾/衝牆):先吸收;單發 > 100 的攻擊穿甲,超出 100 的部分打本體。
    public struct VitalsModel
    {
        public float Damage { get; private set; }
        public float Armor { get; private set; }
        public float ArmorUntil { get; private set; }
        public float LastDamageTime { get; private set; }
        public bool IsDead { get; private set; }

        public static VitalsModel Create()
        {
            return new VitalsModel { LastDamageTime = float.NegativeInfinity };
        }

        public float Hp(in VitalsSettings settings)
        {
            return Mathf.Max(0f, settings.MaxHp - Damage);
        }

        public bool HasArmor(float time)
        {
            return Armor > 0f && time < ArmorUntil;
        }

        public void GrantArmor(float amount, float until)
        {
            Armor = amount;
            ArmorUntil = until;
        }

        public void ClearArmor()
        {
            Armor = 0f;
        }

        // 回傳這一擊是否造成擊倒(只有「活 → 死」那一次回 true)。
        public bool ApplyHit(float amount, float time, in VitalsSettings settings, out float dealt)
        {
            dealt = 0f;
            if (IsDead || amount <= 0f)
            {
                return false;
            }
            float hit = Floor01(amount);
            if (hit <= 0f)
            {
                return false;
            }
            LastDamageTime = time;
            if (HasArmor(time))
            {
                if (hit > 100f)
                {
                    // 穿甲:護甲破,超出 100 的部分打本體
                    Armor = 0f;
                    hit = Floor01(hit - 100f);
                }
                else
                {
                    float absorbed = Mathf.Min(Armor, hit);
                    Armor -= absorbed;
                    // 同一擊溢出的部分漏到本體
                    hit = Floor01(hit - absorbed);
                    if (hit <= 0f)
                    {
                        return false;
                    }
                }
            }
            float before = Damage;
            Damage = Mathf.Min(settings.MaxHp, Floor01(Damage + hit));
            dealt = Damage - before;
            if (Damage >= settings.MaxHp)
            {
                IsDead = true;
                return true;
            }
            return false;
        }

        // 每幀:敵墨持續傷害 + 回血。回傳這一幀造成的持續傷害量(供 HUD 做受傷回饋)。
        public float Tick(float time, float dt, bool submergedInOwnInk, bool onEnemyInk, in VitalsSettings settings)
        {
            if (IsDead || dt <= 0f)
            {
                return 0f;
            }
            float slip = 0f;
            if (onEnemyInk)
            {
                // 碰到敵墨:回血中斷(延遲重置);持續傷害只加到上限為止,永遠不當最後一擊
                LastDamageTime = time;
                if (Damage < settings.SlipDamageCap)
                {
                    float next = Mathf.Min(settings.SlipDamageCap, Damage + settings.SlipDamagePerSecond * dt);
                    slip = next - Damage;
                    Damage = next;
                }
                return slip;
            }
            if (Damage > 0f && time - LastDamageTime >= settings.RegenDelay)
            {
                float rate = submergedInOwnInk ? settings.RegenSubmerged : settings.RegenExposed;
                Damage = Mathf.Max(0f, Damage - rate * dt);
            }
            return 0f;
        }

        public void ResetFull()
        {
            Damage = 0f;
            Armor = 0f;
            IsDead = false;
            LastDamageTime = float.NegativeInfinity;
        }

        // 取整到 0.1(向下),加一點容差避免 36.0 因浮點變成 35.9
        public static float Floor01(float value)
        {
            return Mathf.Floor(value * 10f + 1e-3f) / 10f;
        }
    }
}
