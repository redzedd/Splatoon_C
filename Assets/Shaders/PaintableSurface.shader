// 可塗表面 shader:採樣 ink map(由 PaintableSurface 以 MPB 注入)以 alpha 混合底色。
// 原型限制:僅主光源 lambert + SH 環境光,不接收陰影。
Shader "SplatoonC/PaintableSurface"
{
    Properties
    {
        _BaseColor("底色", Color) = (0.55, 0.55, 0.55, 1)
        _InkMap("墨水圖(執行期由 PaintableSurface 注入)", 2D) = "black" {}
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            CBUFFER_END

            TEXTURE2D(_InkMap);
            SAMPLER(sampler_InkMap);

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 ink = SAMPLE_TEXTURE2D(_InkMap, sampler_InkMap, input.uv);
                half3 albedo = lerp(_BaseColor.rgb, ink.rgb, ink.a);
                Light mainLight = GetMainLight();
                half3 normal = normalize(input.normalWS);
                half ndotl = saturate(dot(normal, mainLight.direction));
                half3 lighting = mainLight.color * ndotl + SampleSH(normal);
                return half4(albedo * lighting, 1);
            }
            ENDHLSL
        }
    }
}
