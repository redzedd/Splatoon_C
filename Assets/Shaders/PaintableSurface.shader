// 可塗表面 shader:採樣 ink map(由 PaintableSurface 以 MPB 注入)以覆蓋率混合底色。
// ink map 存的是隊伍佔比(R = Alpha、G = Bravo),墨色由全域 _InkColorAlpha/_InkColorBravo(TeamPalette)合成。
// 墨面質感三層:邊界噪聲起伏 → 邊緣暗環(厚度感)→ specular + Fresnel(濕潤感);全部只作用在墨上。
// 底材:世界座標程序化花紋(磁磚/木板/岩石),不需要貼圖;地面取 xz、牆面取水平切線 × y。
// 光照:主光源(含陰影)+ 額外光源 + SH 環境光 + SSAO + 霧;另有 ShadowCaster / DepthOnly / DepthNormals pass,
// 可塗方塊才投得出影子、SSAO 才吃得到它。
Shader "SplatoonC/PaintableSurface"
{
    Properties
    {
        _BaseColor("底色", Color) = (0.55, 0.55, 0.55, 1)
        _InkMap("墨水圖(執行期由 PaintableSurface 注入)", 2D) = "black" {}

        [Header(Base Pattern)]
        [Enum(None,0,Tiles,1,Planks,2,Rock,3)] _PatternType("底材花紋", Float) = 0
        _PatternColor("花紋第二色(溝縫/木紋/岩斑)", Color) = (0.4, 0.4, 0.4, 1)
        _PatternScale("花紋尺寸(公尺)", Range(0.2, 8)) = 1.5
        _PatternStrength("花紋強度", Range(0, 1)) = 0.5
        _TileVariation("逐塊明暗變化", Range(0, 0.3)) = 0.06

        [Header(Side Faces)]
        [Toggle] _UseSideLook("側面(牆)使用另一組底材", Float) = 0
        _SideColor("側面底色", Color) = (0.6, 0.35, 0.25, 1)
        _SidePatternColor("側面花紋第二色", Color) = (0.45, 0.25, 0.18, 1)
        [Enum(None,0,Tiles,1,Planks,2,Rock,3)] _SidePatternType("側面花紋", Float) = 3
        _SidePatternScale("側面花紋尺寸(公尺)", Range(0.2, 8)) = 2

        [Header(Ink Edge)]
        _EdgeSoftness("邊緣柔度(0 = 硬切,越大越糊)", Range(0.01, 0.5)) = 0.12
        _EdgeWarp("邊緣起伏振幅(擾動遮罩邊界)", Range(0, 1.5)) = 0.7
        _EdgeNoiseScale("邊緣噪聲密度(每公尺格數)", Range(0.2, 12)) = 1.5
        _EdgeDarken("邊緣暗環強度(墨的厚度感)", Range(0, 1)) = 0.4

        [Header(Ink Wetness)]
        _RippleScale("表面微起伏密度(每公尺格數)", Range(0.2, 12)) = 1.2
        _RippleStrength("表面微起伏強度(法線擾動)", Range(0, 1)) = 0.05
        _SpecularStrength("高光強度", Range(0, 3)) = 0.28
        _SpecularPower("高光銳利度", Range(4, 256)) = 28
        _SpecularTint("高光染墨色(0 = 純白光,1 = 全墨色)", Range(0, 1)) = 0.5
        _FresnelStrength("邊緣反光強度(掠角變亮)", Range(0, 2)) = 0.2
        _FresnelPower("邊緣反光收斂度", Range(1, 12)) = 5
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        HLSLINCLUDE
        #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

        // 每個 pass 的 CBUFFER 必須一致,SRP Batcher 才會合批
        CBUFFER_START(UnityPerMaterial)
        half4 _BaseColor;
        half4 _PatternColor;
        half _PatternType;
        half _PatternScale;
        half _PatternStrength;
        half _TileVariation;
        half _UseSideLook;
        half4 _SideColor;
        half4 _SidePatternColor;
        half _SidePatternType;
        half _SidePatternScale;
        half _EdgeSoftness;
        half _EdgeWarp;
        half _EdgeNoiseScale;
        half _EdgeDarken;
        half _RippleScale;
        half _RippleStrength;
        half _SpecularStrength;
        half _SpecularPower;
        half _SpecularTint;
        half _FresnelStrength;
        half _FresnelPower;
        CBUFFER_END
        ENDHLSL

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT _SHADOWS_SOFT_LOW _SHADOWS_SOFT_MEDIUM _SHADOWS_SOFT_HIGH
            #pragma multi_compile_fragment _ _SCREEN_SPACE_OCCLUSION
            #pragma multi_compile _ _CLUSTER_LIGHT_LOOP
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Include/InkCommon.hlsl"

            // 全域(TeamPalette 設定,linear 值);不進 UnityPerMaterial,SRP Batcher 相容
            float4 _InkColorAlpha;
            float4 _InkColorBravo;

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
                float3 positionWS : TEXCOORD2;
                half fogFactor : TEXCOORD3;
            };

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.fogFactor = ComputeFogFactor(output.positionCS.z);
                return output;
            }

            // 表面座標:地面用 xz,牆面沿水平切線展開(牆的花紋才會橫向連續)
            float2 SurfaceCoord(float3 positionWS, float3 normalWS)
            {
                float3 n = abs(normalWS);
                if (n.y > 0.7)
                {
                    return positionWS.xz;
                }
                float3 tangent = normalize(float3(-normalWS.z, 0.0, normalWS.x) + float3(1e-5, 0.0, 0.0));
                return float2(dot(positionWS, tangent), positionWS.y);
            }

            // 程序化底材:回傳 0~1 的「第二色」權重與逐塊亮度偏移
            void BasePattern(float2 p, int type, float scale, out half weight, out half variation)
            {
                weight = 0.0h;
                variation = 0.0h;
                scale = max(scale, 0.05);
                if (type == 1)
                {
                    // 磁磚:溝縫線 + 逐塊明暗
                    float2 q = p / scale;
                    float2 cell = floor(q);
                    float2 f = frac(q);
                    float2 edge = min(f, 1.0 - f);
                    float groove = 1.0 - smoothstep(0.015, 0.04, min(edge.x, edge.y));
                    weight = (half)groove;
                    variation = (half)(InkHash21(cell) - 0.5);
                }
                else if (type == 2)
                {
                    // 木板:沿 x 的長條,板縫錯位 + 木紋
                    float2 q = p / scale;
                    float row = floor(q.y);
                    float along = q.x * 0.25 + InkHash21(float2(row, 7.1)) * 3.0;
                    float fy = frac(q.y);
                    float fx = frac(along);
                    float seam = 1.0 - smoothstep(0.02, 0.05, min(fy, 1.0 - fy));
                    float butt = 1.0 - smoothstep(0.006, 0.015, min(fx, 1.0 - fx));
                    float grain = InkValueNoise(float2(q.x * 0.6, q.y * 9.0 + row * 3.7));
                    weight = (half)saturate(max(seam, butt) + grain * 0.35);
                    variation = (half)(InkHash21(float2(row, floor(along))) - 0.5);
                }
                else if (type == 3)
                {
                    // 岩石/水泥:大斑 + 細碎點
                    float2 q = p / scale;
                    float blotch = InkValueNoise(q) * 0.6 + InkValueNoise(q * 2.3 + 11.7) * 0.4;
                    float speck = step(0.93, InkHash21(floor(q * 14.0)));
                    weight = (half)saturate(smoothstep(0.45, 0.8, blotch) + speck * 0.5);
                    variation = (half)(InkValueNoise(q * 0.35 + 3.3) - 0.5);
                }
            }

            half4 frag(Varyings input) : SV_Target
            {
                half4 inkSample = SAMPLE_TEXTURE2D(_InkMap, sampler_InkMap, input.uv);
                // 兩隊佔比 → 覆蓋率 + 加權墨色(邊界兩隊交錯處自然混色)
                half teamSum = inkSample.r + inkSample.g;
                half4 ink;
                ink.rgb = (inkSample.r * (half3)_InkColorAlpha.rgb + inkSample.g * (half3)_InkColorBravo.rgb)
                    / max(teamSum, 0.001h);
                ink.a = saturate(teamSum);

                half3 flatNormal = normalize(input.normalWS);

                // 底材:頂面(地面)一組、側面(牆)可選另一組——同一個方塊「沙土頂、岩壁側」
                bool side = _UseSideLook > 0.5h && flatNormal.y < 0.7h;
                half3 color1 = side ? _SideColor.rgb : _BaseColor.rgb;
                half3 color2 = side ? _SidePatternColor.rgb : _PatternColor.rgb;
                int patternType = (int)round(side ? _SidePatternType : _PatternType);
                float patternScale = side ? _SidePatternScale : _PatternScale;
                half patternWeight;
                half variation;
                BasePattern(SurfaceCoord(input.positionWS, flatNormal), patternType, patternScale, patternWeight, variation);
                half3 baseColor = lerp(color1, color2, patternWeight * _PatternStrength);
                baseColor *= 1.0h + variation * _TileVariation * 2.0h;

                // 邊緣起伏:擾動的是遮罩「值」不是 UV——ink RT 約 8~10cm/texel,
                // 位移 UV 會把階梯放大,加減噪聲則是讓邊界線在既有的漸層帶內擺動,與貼圖尺度無關。
                half noise = (half)InkSurfaceNoise(input.positionWS, _EdgeNoiseScale) - 0.5h;
                half raw = saturate(ink.a + noise * _EdgeWarp);
                half mask = smoothstep(0.5h - _EdgeSoftness, 0.5h + _EdgeSoftness, raw);

                half3 albedo = lerp(baseColor, ink.rgb, mask);
                // 邊界暗環要套在混色「之後」:套在墨色上會被底色 lerp 稀釋掉一半而看不見。
                // mask*(1-mask)*4 在邊界正中為 1、兩端為 0,自然形成一圈輪廓。
                half rim = mask * (1.0h - mask) * 4.0h;
                albedo *= 1.0h - rim * _EdgeDarken;

                // 主光源(含陰影)+ SSAO
                float4 shadowCoord = TransformWorldToShadowCoord(input.positionWS);
                Light mainLight = GetMainLight(shadowCoord);
                half ao = 1.0h;
                #if defined(_SCREEN_SPACE_OCCLUSION)
                float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
                AmbientOcclusionFactor aoFactor = GetScreenSpaceAmbientOcclusion(screenUV);
                ao = aoFactor.indirectAmbientOcclusion;
                mainLight.color *= aoFactor.directAmbientOcclusion;
                #endif
                half shadow = mainLight.shadowAttenuation * mainLight.distanceAttenuation;
                half3 lighting = mainLight.color * (saturate(dot(flatNormal, mainLight.direction)) * shadow)
                    + SampleSH(flatNormal) * ao;

                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                // cluster 光源迴圈巨集會讀 inputData(名稱寫死),非 cluster 模式下未使用也無妨
                InputData inputData = (InputData)0;
                inputData.positionWS = input.positionWS;
                inputData.normalizedScreenSpaceUV = GetNormalizedScreenSpaceUV(input.positionCS);
                LIGHT_LOOP_BEGIN(lightCount)
                    Light light = GetAdditionalLight(lightIndex, input.positionWS, half4(1, 1, 1, 1));
                    lighting += light.color * (light.distanceAttenuation * light.shadowAttenuation
                        * saturate(dot(flatNormal, light.direction)));
                LIGHT_LOOP_END
                #endif

                // 高光用擾動法線、漫射用原法線:微起伏只該影響反光的碎裂感,不該讓地面明暗變髒
                half3 wetNormal = (half3)InkPerturbedNormal(flatNormal, input.positionWS, _RippleScale, _RippleStrength);
                half3 viewDir = normalize(_WorldSpaceCameraPos - input.positionWS);
                half3 halfDir = normalize(viewDir + mainLight.direction);
                half spec = pow(saturate(dot(wetNormal, halfDir)), _SpecularPower) * _SpecularStrength;
                half fresnel = pow(1.0h - saturate(dot(wetNormal, viewDir)), _FresnelPower) * _FresnelStrength;
                half ndotlWet = saturate(dot(wetNormal, mainLight.direction));
                // 只有濕墨會反光,乾地維持啞光;陰影裡的高光要跟著消失
                // 純白高光會把墨洗成無色,染回一部分墨色才維持得住色相
                half3 specTint = lerp(half3(1.0h, 1.0h, 1.0h), ink.rgb, _SpecularTint);
                half3 wetness = mainLight.color * specTint * (spec * ndotlWet * shadow + fresnel) * mask;

                half3 color = albedo * lighting + wetness;
                color = MixFog(color, input.fogFactor);
                return half4(color, 1);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual
            ColorMask 0
            Cull Back

            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment ShadowFrag
            #pragma multi_compile_vertex _ _CASTING_PUNCTUAL_LIGHT_SHADOW
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            float3 _LightDirection;
            float3 _LightPosition;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings ShadowVert(Attributes input)
            {
                Varyings output;
                float3 positionWS = TransformObjectToWorld(input.positionOS.xyz);
                float3 normalWS = TransformObjectToWorldNormal(input.normalOS);
                #if _CASTING_PUNCTUAL_LIGHT_SHADOW
                float3 lightDirectionWS = normalize(_LightPosition - positionWS);
                #else
                float3 lightDirectionWS = _LightDirection;
                #endif
                float4 positionCS = TransformWorldToHClip(ApplyShadowBias(positionWS, normalWS, lightDirectionWS));
                #if UNITY_REVERSED_Z
                positionCS.z = min(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #else
                positionCS.z = max(positionCS.z, UNITY_NEAR_CLIP_VALUE);
                #endif
                output.positionCS = positionCS;
                return output;
            }

            half4 ShadowFrag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask R
            Cull Back

            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings DepthVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half DepthFrag(Varyings input) : SV_Target
            {
                return input.positionCS.z;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On
            Cull Back

            HLSLPROGRAM
            #pragma vertex NormalsVert
            #pragma fragment NormalsFrag
            #pragma multi_compile_fragment _ _GBUFFER_NORMALS_OCT

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
            };

            Varyings NormalsVert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }

            half4 NormalsFrag(Varyings input) : SV_Target
            {
                float3 normalWS = normalize(input.normalWS);
                #if defined(_GBUFFER_NORMALS_OCT)
                float2 octNormalWS = PackNormalOctQuadEncode(normalWS);
                float2 remappedOctNormalWS = saturate(octNormalWS * 0.5 + 0.5);
                half3 packedNormalWS = PackFloat2To888(remappedOctNormalWS);
                return half4(packedNormalWS, 0.0);
                #else
                return half4(normalWS, 0.0);
                #endif
            }
            ENDHLSL
        }
    }
}
