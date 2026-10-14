using UnityEngine;

namespace SplatoonC.Core.Menu
{
    // 選單選項的小數學:循環切換、音量格數換算。
    public static class MenuMath
    {
        // 選項循環:wrap = 到底回頭(開/關、難度按確定鍵輪替);不 wrap = 停在兩端(左右鍵調整)
        public static int Cycle(int index, int delta, int count, bool wrap)
        {
            if (count <= 0)
            {
                return 0;
            }
            int next = index + delta;
            if (wrap)
            {
                return ((next % count) + count) % count;
            }
            return Mathf.Clamp(next, 0, count - 1);
        }

        // 音量(0~1)↔ 格位(0~steps);顯示成 0%、10%…
        public static int VolumeToStep(float volume, int steps)
        {
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(volume) * steps), 0, steps);
        }

        public static float StepToVolume(int step, int steps)
        {
            return steps <= 0 ? 0f : Mathf.Clamp01((float)step / steps);
        }

        // 條狀選項的滑鼠點擊:x 在條上的比例 → 最近的格位
        public static int StepFromFraction(float fraction, int count)
        {
            if (count <= 1)
            {
                return 0;
            }
            return Mathf.Clamp(Mathf.RoundToInt(Mathf.Clamp01(fraction) * (count - 1)), 0, count - 1);
        }
    }
}
