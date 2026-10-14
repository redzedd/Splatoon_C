using System.Collections.Generic;
using SplatoonC.Core;
using SplatoonC.Gameplay.Player;
using UnityEngine;

namespace SplatoonC.Gameplay.Combat
{
    // 大招預兆(讓 bot 閃避用)的提供者:場上的大招物件在存活期間登記自己(DESIGN_REFERENCE §5.3)。
    public interface IBotHazard
    {
        // team 方站在 position 的角色,是否該在接下來 leadSeconds 秒內起跳躲過(Wave Breaker 的波前)
        bool WantsJump(Team team, Vector3 position, float leadSeconds);
    }

    // 範圍型預兆(§5.3「N 幀內離開圓圈」:Ink Vac 落地 50F、之後的 Booyah Bomb 30F、Triple Inkstrike 70F…):
    // 同時實作 IBotHazard 並登記;bot 在圈內就往圈外跑(不分模式,大招啟動中除外)。
    public interface IBotFleeHazard
    {
        // team 方站在 position 的角色在危險範圍內嗎;是 → 範圍中心與半徑(bot 跑到半徑外加一點)
        bool Threatens(Team team, Vector3 position, out Vector3 center, out float radius);
    }

    // 吸引 bot 過去的點(隊友的 Tacticooler 飲料機:沒喝過的隊友走過去、以人形靠近才拿得到)。
    // 登記方式同 IBotHazard(Register);bot 沒在交戰時才會去。
    public interface IBotAttractor
    {
        // bot 該不該過來;是 → 要走到的點(bot 以人形走到附近)
        bool Attracts(Inkling bot, out Vector3 point);
    }

    // bot 躲避大招預兆的查詢:BotBrain 每幀問一次「現在該不該跳」「在不在危險圈內」。靜態清單、零配置;換場景清空。
    // 目前的提供者:Wave Breaker 的波(每一波在第 90 / 240 / 390F 放出,波前以 7.5 m/s 擴張、跳起來就躲得過)、
    // Ink Vac 反擊彈落地後的爆炸圈(50F 內跑出圈)。吸引點:Tacticooler 飲料機(IBotAttractor)。
    public static class BotHazards
    {
        private static readonly List<IBotHazard> Active = new List<IBotHazard>();
        private static readonly List<IBotAttractor> Attractors = new List<IBotAttractor>();

        public static int Count => Active.Count;
        public static int AttractorCount => Attractors.Count;

        // 測試用:bot 因預兆而起跳 / 逃離危險圈 / 被吸引點叫過去的累計次數
        public static int JumpsTriggered { get; private set; }
        public static int FleesTriggered { get; private set; }
        public static int VisitsTriggered { get; private set; }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void ResetOnLoad()
        {
            Active.Clear();
            Attractors.Clear();
            JumpsTriggered = 0;
            FleesTriggered = 0;
            VisitsTriggered = 0;
        }

        public static void RegisterAttractor(IBotAttractor attractor)
        {
            if (attractor != null && !Attractors.Contains(attractor))
            {
                Attractors.Add(attractor);
            }
        }

        public static void UnregisterAttractor(IBotAttractor attractor)
        {
            Attractors.Remove(attractor);
        }

        // team 方站在 position 的角色在某個範圍型預兆內嗎;是 → 往哪裡跑(水平單位向量,背離最近的圈心)與還要跑多遠
        public static bool TryGetEscape(Team team, Vector3 position, out Vector3 away, out float distanceToSafety)
        {
            away = Vector3.zero;
            distanceToSafety = 0f;
            float bestMargin = float.MaxValue;
            for (int i = 0; i < Active.Count; i++)
            {
                if (!(Active[i] is IBotFleeHazard flee) || !flee.Threatens(team, position, out Vector3 center, out float radius))
                {
                    continue;
                }
                Vector3 flat = position - center;
                flat.y = 0f;
                float margin = radius - flat.magnitude;
                if (margin < bestMargin)
                {
                    bestMargin = margin;
                    away = flat.sqrMagnitude > 1e-4f ? flat.normalized : Vector3.forward;
                    distanceToSafety = Mathf.Max(0f, margin);
                }
            }
            return bestMargin < float.MaxValue;
        }

        // 有沒有吸引點要這個 bot 過去(最近的一個)
        public static bool TryGetAttraction(Inkling bot, out Vector3 point)
        {
            point = Vector3.zero;
            if (bot == null)
            {
                return false;
            }
            float best = float.MaxValue;
            for (int i = 0; i < Attractors.Count; i++)
            {
                IBotAttractor attractor = Attractors[i];
                if (attractor == null || !attractor.Attracts(bot, out Vector3 candidate))
                {
                    continue;
                }
                float sq = (candidate - bot.transform.position).sqrMagnitude;
                if (sq < best)
                {
                    best = sq;
                    point = candidate;
                }
            }
            return best < float.MaxValue;
        }

        // BotBrain 真的因範圍預兆開始逃 / 因吸引點出發時回報(測試量測)
        public static void ReportFlee()
        {
            FleesTriggered++;
        }

        public static void ReportVisit()
        {
            VisitsTriggered++;
        }

        public static void Register(IBotHazard hazard)
        {
            if (hazard != null && !Active.Contains(hazard))
            {
                Active.Add(hazard);
            }
        }

        public static void Unregister(IBotHazard hazard)
        {
            Active.Remove(hazard);
        }

        // 任何一個預兆要 team 方在 position 的角色起跳
        public static bool ShouldJump(Team team, Vector3 position, float leadSeconds)
        {
            for (int i = 0; i < Active.Count; i++)
            {
                IBotHazard hazard = Active[i];
                if (hazard != null && hazard.WantsJump(team, position, leadSeconds))
                {
                    return true;
                }
            }
            return false;
        }

        // BotBrain 真的因預兆起跳時回報(測試量測)
        public static void ReportJump()
        {
            JumpsTriggered++;
        }
    }
}
