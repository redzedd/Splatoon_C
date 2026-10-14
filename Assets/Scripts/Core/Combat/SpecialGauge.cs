using UnityEngine;

namespace SplatoonC.Core.Combat
{
    // 大招能量純邏輯(0~100 點):塗地充能 → 滿了可啟動 → 啟動後持續衰減,歸零或丟滿裝置即結束。
    // 啟動中不充能(Splatoon 規則);Tick / RegisterDeviceThrown 在「這次結束了」時回 true。
    //
    // 兩種消耗型態(research/04 §1.4,DESIGN_REFERENCE §5.1):
    //   持續型(能量 = 計時器;Trizooka、Killer Wail、三重龍捲風…):啟動中 Tick 衰減。
    //   鎖定型(Big Bubbler、Wave Breaker、Tacticooler…):放出後 BeginLock → 能量自己倒數到 0(TickLock),
    //     期間不能充能也不能再啟動;裝置被破壞 → EndLock 立刻歸 0。
    // 死亡(ApplyDeathPenalty,S3):持續型在死亡瞬間停止衰減、剩下的再套保留比例;鎖定型不中斷也不懲罰。
    public struct SpecialGauge
    {
        public const float Full = 100f;

        public float Points { get; private set; }
        public bool IsActive { get; private set; }
        public int DevicesThrown { get; private set; }

        // 鎖定型大招放出後的自動倒數中(不能充能、不能啟動)
        public bool IsLocked { get; private set; }

        public float Normalized => Points / Full;
        public bool IsFull => Points >= Full - 0.001f;

        public void Charge(float points)
        {
            if (IsActive || IsLocked || points <= 0f)
            {
                return;
            }
            Points = Mathf.Min(Full, Points + points);
        }

        public bool TryActivate()
        {
            if (!IsFull || IsActive || IsLocked)
            {
                return false;
            }
            IsActive = true;
            DevicesThrown = 0;
            return true;
        }

        // 啟動中每幀呼叫;衰減到 0 就結束(不管裝置有沒有丟完)。
        public bool Tick(float deltaTime, float drainPerSecond)
        {
            if (!IsActive)
            {
                return false;
            }
            Points -= drainPerSecond * deltaTime;
            if (Points <= 0f)
            {
                Points = 0f;
                IsActive = false;
                return true;
            }
            return false;
        }

        public bool CanThrow(int maxDevices)
        {
            return IsActive && DevicesThrown < maxDevices;
        }

        // 丟出一個裝置;丟滿 → 能量直接歸零並結束。
        public bool RegisterDeviceThrown(int maxDevices)
        {
            if (!IsActive)
            {
                return false;
            }
            DevicesThrown++;
            if (DevicesThrown >= maxDevices)
            {
                Points = 0f;
                IsActive = false;
                return true;
            }
            return false;
        }

        // ── 鎖定型 ──

        // 放出鎖定型大招(Wave Breaker 丟出、Big Bubbler 開始設置):離開啟動狀態,能量從目前值開始自己倒數。
        // 能量已經是 0 就不鎖(沒有東西可倒數)。
        public void BeginLock()
        {
            IsActive = false;
            IsLocked = Points > 0f;
        }

        // 鎖定倒數;倒數到 0 解除鎖定並回 true
        public bool TickLock(float deltaTime, float drainPerSecond)
        {
            if (!IsLocked)
            {
                return false;
            }
            Points -= drainPerSecond * deltaTime;
            if (Points <= 0f)
            {
                Points = 0f;
                IsLocked = false;
                return true;
            }
            return false;
        }

        // 裝置被破壞(或被新的擠掉):鎖定立刻結束、能量歸 0,可以重新充能
        public void EndLock()
        {
            if (!IsLocked)
            {
                return;
            }
            IsLocked = false;
            Points = 0f;
        }

        // 測試/除錯:直接設定能量(不改啟動狀態)。
        public void SetPoints(float points)
        {
            Points = Mathf.Clamp(points, 0f, Full);
        }

        // 測試/除錯:清掉鎖定(能量不動)
        public void ClearLock()
        {
            IsLocked = false;
        }

        // 大招行為自行結束(丟完/打完)或被中斷:能量歸零、退出啟動狀態(鎖定倒數不受影響)
        public void ForceEnd()
        {
            IsActive = false;
            Points = 0f;
        }

        // 被擊倒(S3,research/04 §1.4–1.5):
        //   鎖定倒數中 → 不中斷、不懲罰(倒數照走到 0);
        //   持續型啟動中 → 衰減在死亡瞬間停止,剩下的能量再保留 keepRatio;
        //   未啟動 → 保留 keepRatio(0 AP 50%)。
        public void ApplyDeathPenalty(float keepRatio)
        {
            if (IsLocked)
            {
                return;
            }
            IsActive = false;
            Points = Mathf.Clamp(Points * keepRatio, 0f, Full);
        }
    }
}
