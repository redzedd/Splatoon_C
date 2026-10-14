// splat 注入 shader:把表面 mesh 以 UV 空間攤平重繪進 ink RenderTexture,
// fragment 以「世界座標距離」決定筆刷遮罩——不依賴 hit UV,天然處理 UV 縫與任意 collider。
// ink map 通道:R = Alpha 隊、G = Bravo 隊(_SplatColor 是通道遮罩,不是顏色)、A = 總覆蓋。
// 混合保證 R + G ≤ 1:新隊伍的通道往 1 推,另一隊通道同比例往 0 縮(敵墨被蓋掉)。
Shader "SplatoonC/InkSplat"
{
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Name "InkSplat"
            Cull Off
            ZWrite Off
            ZTest Always
            // RGB 覆蓋(通道遮罩 × mask)、Alpha 朝 1 累積(alpha = 已塗遮罩)
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Include/InkCommon.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            float4 _SplatCenter;   // xyz = 世界座標,w = 半徑
            half4 _SplatColor;
            float _SplatHardness;  // 0~1,內圈實心比例
            // x = 振幅,y/z = 波瓣頻率(必須整數,否則 ±π 角度接縫跳變),w = 隨機相位
            float4 _SplatNoise;

            Varyings vert(Attributes input)
            {
                Varyings output;
                // D3D 需要 UV_STARTS_AT_TOP 翻轉,否則 v 軸鏡像
                //(2026-09-01 俯視三色探針實證:無翻轉時 +Z 藍點出現在畫面下方)。
                float2 clipXY = input.uv * 2.0 - 1.0;
                #if UNITY_UV_STARTS_AT_TOP
                clipXY.y = -clipXY.y;
                #endif
                output.positionCS = float4(clipXY, 0.0, 1.0);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float3 delta = input.positionWS - _SplatCenter.xyz;
                float dist = length(delta);
                // 有機潑濺輪廓(共用於未來的墨面 shader,函式在 Include/InkCommon.hlsl)
                float radius = InkLobeRadius(delta, max(_SplatCenter.w, 0.0001), _SplatNoise);

                // 硬度上限 0.995:hardness = 1 時 smoothstep 兩端相等(除以 0),編譯器展開成 x·INF − e0·INF = NaN,
                // saturate(NaN) = 0 → 遮罩 1 → 整張表面被塗滿(2026-09-28 區域測試實證:硬度 1 的一發塗滿整個台地)
                float mask = 1.0 - smoothstep(radius * min(saturate(_SplatHardness), 0.995), radius, dist);
                // 背面測試:球心在此面後方(超過 10cm)就不塗——同一張圖集裡薄牆的另一面、
                // 方塊背對爆心的面都不該透墨。與 PaintableSurface.BackfaceTolerance 一致。
                float front = dot(_SplatCenter.xyz - input.positionWS, normalize(input.normalWS));
                mask *= step(-0.1, front);
                return half4(_SplatColor.rgb, mask);
            }
            ENDHLSL
        }
    }
}
