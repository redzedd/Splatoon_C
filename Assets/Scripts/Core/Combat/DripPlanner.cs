namespace SplatoonC.Core.Combat
{
    // 沿彈道滴墨的排程純邏輯。
    //
    // 為什麼是確定性而非機率:0.5 秒只有 6~7 發,機率制的滴落位置隨機,
    // 缺口是機率保證會出現的,調高機率只會讓缺口變少而不會消失。
    // 改成「每發固定滴數、位置由規則決定、逐發相位錯開」,後面的發次自動填前面的縫。
    //
    // 位置公式:distance_i = min + ((i + phase) / count) × span
    // phase ∈ [0,1) 逐發輪替。如此 i 的值域彼此不重疊,最遠一滴也永遠不會超出 max。
    // 遞增是硬需求——墨彈用單一游標依序觸發滴落,亂序會讓後面的滴永遠不觸發。
    public static class DripPlanner
    {
        public static int Plan(float[] distances, int count, float minDistance, float maxDistance,
            float phase, float[] jitterSamples, float jitterDistance)
        {
            if (distances == null || count <= 0)
            {
                return 0;
            }
            if (count > distances.Length)
            {
                count = distances.Length;
            }

            float span = maxDistance - minDistance;
            if (span < 0f)
            {
                span = 0f;
            }
            // 區間倒置時 span 被夾成 0,夾制上限必須用 min+span(而非較小的 max),否則會被拉回 max
            float end = minDistance + span;
            if (phase < 0f)
            {
                phase = 0f;
            }
            else if (phase >= 1f)
            {
                phase -= (int)phase;
            }

            for (int i = 0; i < count; i++)
            {
                float d = minDistance + (i + phase) / count * span;
                if (jitterSamples != null && i < jitterSamples.Length && jitterDistance > 0f)
                {
                    float s = jitterSamples[i];
                    if (s < 0f)
                    {
                        s = 0f;
                    }
                    else if (s > 1f)
                    {
                        s = 1f;
                    }
                    d += (s * 2f - 1f) * jitterDistance;
                }
                if (d < minDistance)
                {
                    d = minDistance;
                }
                else if (d > end)
                {
                    d = end;
                }
                distances[i] = d;
            }

            // 插入排序:抖動可能讓相鄰兩滴互換,排序保住「游標依序觸發」的不變式。
            for (int i = 1; i < count; i++)
            {
                float key = distances[i];
                int j = i - 1;
                while (j >= 0 && distances[j] > key)
                {
                    distances[j + 1] = distances[j];
                    j--;
                }
                distances[j + 1] = key;
            }
            return count;
        }
    }
}
