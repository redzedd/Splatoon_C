// 標記(tracked)疊加 shader:被標記的敵人對標記方「穿牆可見」——頭上菱形標記、身形輪廓、隊友指向線共用。
// 無光照、半透明、ZWrite Off;ZTest 可由材質設定(預設 Always = 穿牆)。
// 邊光(rim):_RimPower > 0 時以視線與法線夾角加亮邊緣,讓實心的膠囊看起來像發光輪廓;平面圖形設 0 即可。
// _ScrollSpeed:沿 UV.x 捲動貼圖(指向線的虛線流動);_Pulse:整體亮度呼吸(0 = 不閃)。
Shader "SplatoonC/MarkOverlay"
{
    Properties
    {
        _BaseMap ("貼圖", 2D) = "white" {}
        _BaseColor ("顏色", Color) = (1, 1, 1, 1)
        _FillAlpha ("內部不透明度", Range(0, 1)) = 1
        _RimPower ("邊光指數(0 = 無邊光)", Float) = 0
        _RimBoost ("邊光強度", Float) = 0
        _ScrollSpeed ("貼圖捲動速度(UV/秒)", Float) = 0
        _Pulse ("呼吸強度", Range(0, 1)) = 0
        [Enum(UnityEngine.Rendering.CompareFunction)] _ZTest ("ZTest", Float) = 8
        [Enum(UnityEngine.Rendering.CullMode)] _Cull ("Cull", Float) = 2
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType" = "Transparent"
            "Queue" = "Transparent+100"
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "MarkOverlay"
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest [_ZTest]
            Cull [_Cull]

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST;
                half4 _BaseColor;
                half _FillAlpha;
                half _RimPower;
                half _RimBoost;
                half _ScrollSpeed;
                half _Pulse;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                half4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float3 viewDirWS : TEXCOORD2;
                half4 color : COLOR;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                output.uv = TRANSFORM_TEX(input.uv, _BaseMap) + float2(_Time.y * _ScrollSpeed, 0.0);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.viewDirWS = GetWorldSpaceViewDir(positionWS);
                output.color = input.color;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 tex = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, input.uv);
                half4 color = _BaseColor * tex * input.color;
                half alpha = color.a * _FillAlpha;
                if (_RimPower > 0.0h)
                {
                    float3 n = normalize(input.normalWS);
                    float3 v = normalize(input.viewDirWS);
                    half rim = pow(1.0h - saturate(abs(dot(n, v))), _RimPower);
                    alpha = saturate(alpha + rim * _RimBoost * color.a);
                    color.rgb = lerp(color.rgb, 1.0h, rim * 0.35h);
                }
                half pulse = 1.0h - _Pulse * (0.5h + 0.5h * sin(_Time.y * 7.0h));
                return half4(color.rgb, alpha * pulse);
            }
            ENDHLSL
        }
    }
}
