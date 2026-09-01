namespace SplatoonC.Core.Combat
{
    // 沿彈道滴墨的排程純邏輯:把 0~1 隨機取樣映射成「沿彈道遞增的滴落距離」。
    // 遞增是硬需求——墨彈用單一游標依序觸發滴落,亂序會讓後面的滴永遠不觸發。
    // bias > 1 把取樣壓向近端(玩家腳邊也要塗得到);bias = 1 為均勻分佈。
    public static class DripPlanner
    {
        public static int Plan(float[] distances, int count, float minDistance, float maxDistance,
            float[] samples, float bias = 1f)
        {
            if (distances == null || samples == null || count <= 0)
            {
                return 0;
            }
            if (count > distances.Length)
            {
                count = distances.Length;
            }
            if (count > samples.Length)
            {
                count = samples.Length;
            }

            float span = maxDistance - minDistance;
            if (span < 0f)
            {
                span = 0f;
            }
            for (int i = 0; i < count; i++)
            {
                float s = samples[i];
                if (s < 0f)
                {
                    s = 0f;
                }
                else if (s > 1f)
                {
                    s = 1f;
                }
                if (bias > 0f && bias != 1f)
                {
                    s = (float)System.Math.Pow(s, bias);
                }
                distances[i] = minDistance + s * span;
            }

            // 插入排序:n ≤ 3,無配置、比任何通用排序都省。
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
