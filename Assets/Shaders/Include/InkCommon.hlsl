#ifndef SPLATOONC_INK_COMMON_INCLUDED
#define SPLATOONC_INK_COMMON_INCLUDED

// 墨系 shader 共用函式庫。相對路徑引用:#include "Include/InkCommon.hlsl"(相對於 Assets/Shaders/*.shader)。
// 注意:改本檔不會自動重編引用它的 shader——用 Tools/SplatoonC/Shader Compile Check 強制 reimport 後才算數。

// 墨漬波瓣半徑:以「方向角」的兩層正弦擾動基準半徑,做出有機潑濺輪廓而非正圓。
// noise: x = 振幅, y/z = 波瓣頻率(必須為整數,否則 ±π 的角度接縫會跳變), w = 隨機相位。
// delta 混入 y 分量,牆面(delta.z 近 0)才不會退化成同心圓。
float InkLobeRadius(float3 delta, float baseRadius, float4 noise)
{
    float angle = atan2(delta.z + 0.37 * delta.y, delta.x + 0.61 * delta.y);
    float lobe = sin(angle * noise.y + noise.w) + 0.5 * sin(angle * noise.z + noise.w * 1.7);
    return baseRadius * (1.0 + noise.x * lobe * 0.5);
}

float InkHash21(float2 p)
{
    p = frac(p * float2(123.34, 456.21));
    p += dot(p, p + 45.32);
    return frac(p.x * p.y);
}

// 便宜的 2D value noise(平滑插值),0~1。墨面只拿來擾動遮罩邊界,不需要更高品質的 gradient noise。
float InkValueNoise(float2 p)
{
    float2 cell = floor(p);
    float2 f = frac(p);
    f = f * f * (3.0 - 2.0 * f);
    float a = InkHash21(cell);
    float b = InkHash21(cell + float2(1.0, 0.0));
    float c = InkHash21(cell + float2(0.0, 1.0));
    float d = InkHash21(cell + float2(1.0, 1.0));
    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
}

// 以世界座標取樣點(兩個八度),讓地面與牆面的邊緣細節同尺度。
// 牆面的 xz 只有一軸在變,所以把 y 混進兩軸——與 InkLobeRadius 同樣的理由。
float InkSurfaceNoise(float3 positionWS, float scale)
{
    float2 p = positionWS.xz + positionWS.y * float2(0.71, 0.43);
    return InkValueNoise(p * scale) * 0.65 + InkValueNoise(p * scale * 2.7 + 19.3) * 0.35;
}

// 墨面法線擾動:平坦大面積上法線恆定,Blinn-Phong 會糊成一大片亮斑而不是「濕」的閃爍,
// 所以用世界座標 noise 的有限差分當微起伏。梯度先投影到切平面,牆面才不會被推歪。
float3 InkPerturbedNormal(float3 normalWS, float3 positionWS, float scale, float strength)
{
    const float e = 0.15;
    float n0 = InkSurfaceNoise(positionWS, scale);
    float3 grad = float3(
        InkSurfaceNoise(positionWS + float3(e, 0.0, 0.0), scale) - n0,
        InkSurfaceNoise(positionWS + float3(0.0, e, 0.0), scale) - n0,
        InkSurfaceNoise(positionWS + float3(0.0, 0.0, e), scale) - n0) / e;
    grad -= normalWS * dot(grad, normalWS);
    return normalize(normalWS - grad * strength);
}

#endif
