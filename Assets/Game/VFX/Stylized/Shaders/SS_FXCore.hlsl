// StoneSignal stylized particle shading: procedural shapes (no textures), vertex colour * tint * intensity.
#ifndef SS_FX_CORE
#define SS_FX_CORE
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
CBUFFER_START(UnityPerMaterial)
    half4 _TintColor; half _Intensity, _Shape, _Softness, _RingWidth;
CBUFFER_END
struct A { float4 pos : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
struct V { float4 pos : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
V FXVert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.color = i.color; o.uv = i.uv; return o; }
float FXHash(float2 p) { return frac(sin(dot(p, float2(127.1, 311.7))) * 43758.5453); }
float FXNoise(float2 p) { float2 i = floor(p), f = frac(p); f = f * f * (3 - 2 * f);
    return lerp(lerp(FXHash(i), FXHash(i + float2(1, 0)), f.x), lerp(FXHash(i + float2(0, 1)), FXHash(i + 1), f.x), f.y); }
half FXShape(float2 uv)
{
    float2 p = uv * 2 - 1; float r = length(p); half s = _Softness;
    int shape = (int)round(_Shape);
    if (shape == 1) { float w = _RingWidth; return saturate(1 - abs(r - (1 - w)) / w) * step(r, 1); }            // ring
    if (shape == 2) { return saturate(1 - abs(p.x) * 2.2) * saturate(1 - abs(p.y)) ; }                            // streak (stretched billboard)
    if (shape == 3) { float n = FXNoise(uv * 4.5 + 3.1) * 0.45 + FXNoise(uv * 9 + 7) * 0.2;                       // toon smoke puff
                      return smoothstep(0.85 + n * 0.3, 0.7 + n * 0.3 - s, r); }
    if (shape == 4) { float a = atan2(p.y, p.x); float star = 0.35 + 0.65 * pow(abs(cos(a * 2)), 6);              // 4-point flash star
                      return saturate(saturate(1 - r / star) * 1.6 + saturate(1 - r * 2.2)); }
    if (shape == 5) { float n = FXNoise(uv * 5) * 0.35; return smoothstep(1.0, 0.45, r + n) * 0.85; }             // scorch decal
    if (shape == 6) { return step(r, 0.92) * lerp(1, 0.72, step(r, 0.62)); }                                      // coin disc with rim
    if (shape == 7) { return saturate(1 - abs(p.y)) ; }                                                           // trail / line strip
    return smoothstep(1, 1 - max(s, 0.02), r);                                                                    // soft/toon dot
}
half4 FXFragAdd(V i) : SV_Target { half a = FXShape(i.uv) * i.color.a; return half4(i.color.rgb * _TintColor.rgb * _Intensity * a, 0); }
half4 FXFragAlpha(V i) : SV_Target { half a = FXShape(i.uv) * i.color.a * _TintColor.a; return half4(i.color.rgb * _TintColor.rgb * _Intensity, a); }
#endif
