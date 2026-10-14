// 真格區域「佔領瞬間重塗」shader:與 InkSplat 同樣把表面 mesh 以 UV 空間攤平重繪進 ink RenderTexture,
// 但遮罩不是球,而是「俯視在區域內(遮罩貼圖)+ 高度在範圍內 + 朝上的地面」——整個區域一次變成佔領方的墨,
// 邊界與多邊形對齊(S3:確保した瞬間はエリア内全体が確保したチームのインクで染まる)。
// 區域形狀由 SplatZone 以 CPU 光柵化成俯視遮罩貼圖(0.1m/texel,與 ink map 同密度),shader 只做一次取樣:
// 不需要 uniform 陣列,參數全部是材質屬性(每個區域一個材質實例)。
// 混合與 InkSplat 相同:RGB 覆蓋、Alpha 朝 1 累積,R + G ≤ 1。
Shader "SplatoonC/InkZoneFill"
{
    Properties
    {
        _ZoneMask ("區域俯視遮罩(R:1 = 區域內)", 2D) = "black" {}
        _ZoneBounds ("遮罩範圍(xy = 最小 XZ,zw = 1 / 尺寸)", Vector) = (0, 0, 1, 1)
        _ZoneRange ("高度與法線(x = 最低 y,y = 最高 y,z = 地面法線 y 門檻)", Vector) = (0, 0, 0.7, 0)
        _ZoneChannel ("隊伍通道遮罩(同 InkSplat 的 _SplatColor)", Vector) = (1, 0, 0, 0)
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" }
        Pass
        {
            Name "InkZoneFill"
            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha, One OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

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

            TEXTURE2D(_ZoneMask);
            SAMPLER(sampler_ZoneMask);

            CBUFFER_START(UnityPerMaterial)
                float4 _ZoneMask_ST;
                float4 _ZoneBounds;
                float4 _ZoneRange;
                half4 _ZoneChannel;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output;
                // 與 InkSplat 相同:UV → clip,D3D 翻 v
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
                float3 n = normalize(input.normalWS);
                float mask = step(_ZoneRange.z, n.y);
                mask *= step(_ZoneRange.x - 0.05, input.positionWS.y) * step(input.positionWS.y, _ZoneRange.y + 0.05);
                float2 uv = (input.positionWS.xz - _ZoneBounds.xy) * _ZoneBounds.zw;
                // 遮罩外(uv 超出 0~1)一律不塗;遮罩邊緣雙線性 + smoothstep 反鋸齒
                float insideUv = step(0.0, uv.x) * step(uv.x, 1.0) * step(0.0, uv.y) * step(uv.y, 1.0);
                float m = SAMPLE_TEXTURE2D_LOD(_ZoneMask, sampler_ZoneMask, saturate(uv), 0).r;
                mask *= insideUv * smoothstep(0.3, 0.7, m);
                return half4(_ZoneChannel.rgb, mask);
            }
            ENDHLSL
        }
    }
}
