// splat 注入 shader:把表面 mesh 以 UV 空間攤平重繪進 ink RenderTexture,
// fragment 以「世界座標距離」決定筆刷遮罩——不依賴 hit UV,天然處理 UV 縫與任意 collider。
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
            // RGB 覆蓋、Alpha 朝 1 累積(alpha = 已塗遮罩)
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
            };

            float4 _SplatCenter;   // xyz = 世界座標,w = 半徑
            half4 _SplatColor;
            float _SplatHardness;  // 0~1,內圈實心比例

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
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                float dist = distance(input.positionWS, _SplatCenter.xyz);
                float radius = max(_SplatCenter.w, 0.0001);
                float mask = 1.0 - smoothstep(radius * saturate(_SplatHardness), radius, dist);
                return half4(_SplatColor.rgb, mask);
            }
            ENDHLSL
        }
    }
}
