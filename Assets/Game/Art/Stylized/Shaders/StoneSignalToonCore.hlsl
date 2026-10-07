// StoneSignal stylized toon core (spec 4.2 / 4.6). Shared by ToonLit and ToonLitOutline so every
// pass sees the same UnityPerMaterial CBUFFER (SRP Batcher compatible).
#ifndef STONESIGNAL_TOON_CORE_INCLUDED
#define STONESIGNAL_TOON_CORE_INCLUDED
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
TEXTURE2D(_RuneAtlas); SAMPLER(sampler_RuneAtlas);
TEXTURE2D(_RampTex); SAMPLER(sampler_RampTex);

CBUFFER_START(UnityPerMaterial)
    float4 _BaseMap_ST;
    half4 _BaseColor, _ShadowColor, _LitColor, _RimColor, _InnerTint, _OuterTint, _EmissionColor, _FlashColor, _OutlineColor;
    half _UseRamp, _ShadowThreshold, _ShadowSmooth, _ShadowStrength, _AmbientStrength;
    half _RimMin, _RimMax, _RimIntensity, _RimLitBias, _LayerTint, _WindStrength, _HitFlash;
    half _Dissolve, _DissolveEdge, _Wobble, _WobbleFreq, _BaseAO, _BaseAOHeight, _Mottle;
    half _VColorEmission;                // v17.3 vertex R emission (core enclosure crack/rune glow; only with _WindStrength 0)
    half4 _DissolveColor;
    float _OutlineWidthPx, _OutlineZOffset;
#if !defined(UNITY_INSTANCING_ENABLED)
    half4 _HiColor; half _HiAmount;      // v15 placement wall highlight (per renderer via MPB, WallHighlight)
    half4 _RuneColor; half _RuneIdx;     // v15 rune inlay: glyph index into 4x2 _RuneAtlas (-1 = none), per renderer via MPB (RuneInlay)
    float4 _GroundClip;
    half4 _StatusTint, _StatusRim;       // v16.1 EnemyStatusFx: rgb colour, a amount (MPB per enemy)                  // v16.1 spawn portal: x = world Y clip plane, y = enabled (0 = off). Set via MPB by SpawnPortal
#endif
CBUFFER_END
// v16.1: in the instanced variant the per-wall props live in an instancing buffer, so walls carrying an MPB still GPU-instance
// (material enableInstancing). The non-instanced variant keeps them in UnityPerMaterial (SRP Batcher compatible).
#if defined(UNITY_INSTANCING_ENABLED)
UNITY_INSTANCING_BUFFER_START(SSWallProps)
    UNITY_DEFINE_INSTANCED_PROP(half4, _HiColor)
    UNITY_DEFINE_INSTANCED_PROP(half,  _HiAmount)
    UNITY_DEFINE_INSTANCED_PROP(half4, _RuneColor)
    UNITY_DEFINE_INSTANCED_PROP(half,  _RuneIdx)
    UNITY_DEFINE_INSTANCED_PROP(float4, _GroundClip)
    UNITY_DEFINE_INSTANCED_PROP(half4, _StatusTint)
    UNITY_DEFINE_INSTANCED_PROP(half4, _StatusRim)
UNITY_INSTANCING_BUFFER_END(SSWallProps)
#define SS_HI_COLOR   UNITY_ACCESS_INSTANCED_PROP(SSWallProps, _HiColor)
#define SS_HI_AMOUNT  UNITY_ACCESS_INSTANCED_PROP(SSWallProps, _HiAmount)
#define SS_RUNE_COLOR UNITY_ACCESS_INSTANCED_PROP(SSWallProps, _RuneColor)
#define SS_RUNE_IDX   UNITY_ACCESS_INSTANCED_PROP(SSWallProps, _RuneIdx)
#define SS_GROUND_CLIP UNITY_ACCESS_INSTANCED_PROP(SSWallProps, _GroundClip)
#define SS_STATUS_TINT UNITY_ACCESS_INSTANCED_PROP(SSWallProps, _StatusTint)
#define SS_STATUS_RIM  UNITY_ACCESS_INSTANCED_PROP(SSWallProps, _StatusRim)
#else
#define SS_HI_COLOR   _HiColor
#define SS_HI_AMOUNT  _HiAmount
#define SS_RUNE_COLOR _RuneColor
#define SS_RUNE_IDX   _RuneIdx
#define SS_GROUND_CLIP _GroundClip
#define SS_STATUS_TINT _StatusTint
#define SS_STATUS_RIM  _StatusRim
#endif
void SS_GroundClipTest(float wsY) { float4 g = SS_GROUND_CLIP; if (g.y > 0.5) clip(wsY - g.x); }   // v16.1 portal rise: hide below the tile
float _OutlineGlobalScale; // optional global from camera script; 0/unset -> 1

struct Attributes
{
    float4 positionOS : POSITION;
    float3 normalOS   : NORMAL;
    float4 tangentOS  : TANGENT;
    float2 uv         : TEXCOORD0;
    float4 uv3        : TEXCOORD3; // tangent-space smoothed normal (xyz) + width factor (w)
    half4  color      : COLOR;     // R wind, G AO, B layer/variation, A phase (spec 3.6)
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

float3 SS_Wind(float3 positionWS, half4 c)
{
    float w = c.r * _WindStrength;
    float t = _Time.y * 1.6 + c.a * 6.2832;
    return float3(sin(t), 0, cos(t * 0.8)) * w * 0.035;
}

// Shard-style vertex animation (no bones): squash/stretch + spin wobble, phase from object origin.
float3 SS_Deform(float3 p)
{
    if (_Wobble <= 0) return p;
    float3 o = UNITY_MATRIX_M._m03_m13_m23;
    float t = _Time.y * _WobbleFreq + dot(o, float3(1.7, 0, 2.3));
    float s = sin(t);
    p.y *= 1 + s * 0.12 * _Wobble;
    p.xz *= 1 - s * 0.06 * _Wobble;
    float a = sin(t * 0.5) * 0.35 * _Wobble; float ca = cos(a), sa = sin(a);
    p.xz = float2(p.x * ca - p.z * sa, p.x * sa + p.z * ca);
    p.y += (sin(t * 2) * 0.5 + 0.5) * 0.08 * _Wobble;
    p.y += 0.02 * SS_HI_AMOUNT;   // highlight lift
    return p;
}
float3 SS_World(float4 posOS, half4 c) { return TransformObjectToWorld(SS_Deform(posOS.xyz)) + SS_Wind(0, c); }
// Death dissolve: cheap 3D value noise in object space; returns edge glow amount and clips.
float SS_Hash(float3 p) { p = frac(p * 0.3183099 + 0.1); p *= 17.0; return frac(p.x * p.y * p.z * (p.x + p.y + p.z)); }
float SS_Noise(float3 x)
{
    float3 i = floor(x), f = frac(x); f = f * f * (3 - 2 * f);
    return lerp(lerp(lerp(SS_Hash(i), SS_Hash(i + float3(1,0,0)), f.x), lerp(SS_Hash(i + float3(0,1,0)), SS_Hash(i + float3(1,1,0)), f.x), f.y),
                lerp(lerp(SS_Hash(i + float3(0,0,1)), SS_Hash(i + float3(1,0,1)), f.x), lerp(SS_Hash(i + float3(0,1,1)), SS_Hash(i + 1), f.x), f.y), f.z);
}
half SS_DissolveClip(float3 posOS)
{
    if (_Dissolve <= 0) return 0;
    float n = SS_Noise(posOS * 6) * 0.65 + SS_Noise(posOS * 13) * 0.35;
    float d = n - _Dissolve * 1.05;
    clip(d);
    return 1 - saturate(d / max(_DissolveEdge, 1e-3));
}

struct Varyings
{
    float4 positionCS : SV_POSITION;
    float2 uv         : TEXCOORD0;
    float3 positionWS : TEXCOORD1;
    float3 normalWS   : TEXCOORD2;
    half4  color      : TEXCOORD3;
    half   fog        : TEXCOORD4;
    float3 positionOS : TEXCOORD5;
    UNITY_VERTEX_INPUT_INSTANCE_ID
    UNITY_VERTEX_OUTPUT_STEREO
};

Varyings ToonVert(Attributes i)
{
    Varyings o = (Varyings)0;
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_TRANSFER_INSTANCE_ID(i, o);
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
    float3 ws = SS_World(i.positionOS, i.color);
    o.positionWS = ws; o.positionOS = i.positionOS.xyz;
    o.positionCS = TransformWorldToHClip(ws);
    o.normalWS = TransformObjectToWorldNormal(i.normalOS);
    o.uv = TRANSFORM_TEX(i.uv, _BaseMap);
    o.color = i.color;
    o.fog = ComputeFogFactor(o.positionCS.z);
    return o;
}

half4 ToonFrag(Varyings i) : SV_Target
{
    UNITY_SETUP_INSTANCE_ID(i);
    SS_GroundClipTest(i.positionWS.y);
    half edge = SS_DissolveClip(i.positionOS);
    half runeIdx = SS_RUNE_IDX, hiAmount = SS_HI_AMOUNT; half3 runeCol = SS_RUNE_COLOR.rgb, hiCol = SS_HI_COLOR.rgb;
    float3 N = normalize(i.normalWS);
    Light light = GetMainLight(TransformWorldToShadowCoord(i.positionWS), i.positionWS, half4(1, 1, 1, 1));
    float ndl = dot(N, light.direction);
    float lightTerm = (ndl * 0.5 + 0.5) * lerp(1, light.shadowAttenuation, _ShadowStrength);
    float ramp = _UseRamp > 0.5
        ? SAMPLE_TEXTURE2D(_RampTex, sampler_RampTex, float2(lightTerm, 0.5)).r
        : smoothstep(_ShadowThreshold - _ShadowSmooth, _ShadowThreshold + _ShadowSmooth, lightTerm);
    half3 baseCol = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).rgb * _BaseColor.rgb;
    if (_LayerTint > 0.5) baseCol *= lerp(_InnerTint.rgb, _OuterTint.rgb, i.color.b);
    half3 diffuse = baseCol * lerp(_ShadowColor.rgb, _LitColor.rgb, ramp) * light.color;
    half3 ambient = baseCol * SampleSH(N) * _AmbientStrength * i.color.g;
    float3 V = normalize(GetWorldSpaceViewDir(i.positionWS));
    float fres = 1 - saturate(dot(N, V));
    half3 rim = smoothstep(_RimMin, _RimMax, fres) * saturate(ndl + _RimLitBias) * _RimColor.rgb * _RimIntensity;
    half3 c = diffuse + ambient + rim + _EmissionColor.rgb;
    c *= 1 + (SS_Noise(i.positionWS * 2.3) * 0.7 + SS_Noise(i.positionWS * 7.1) * 0.3 - 0.5) * _Mottle; // weathered mottling
    c *= lerp(1 - _BaseAO, 1, saturate(i.positionOS.y / max(_BaseAOHeight, 1e-3))); // contact AO at object base
    if (_VColorEmission > 0)   // v17.3: palette colour x vertex R x strength (HDR -> bloom), slow ember breathe
        c += baseCol * i.color.r * _VColorEmission * (0.82 + 0.18 * sin(_Time.y * 2.4 + i.positionWS.x * 1.7 + i.positionWS.z * 1.3));
    if (runeIdx > -0.5)   // rune glyph on the block top (object-space xz -> 4x2 atlas cell), emissive + slow breathe
    {
        float2 uv = saturate(i.positionOS.xz / 0.84 + 0.5); uv.y = 1 - uv.y;
        float id = floor(runeIdx + 0.5); float2 cell = float2(fmod(id, 4), 1 - floor(id / 4));
        half g = SAMPLE_TEXTURE2D(_RuneAtlas, sampler_RuneAtlas, (uv + cell) * float2(0.25, 0.5)).a * saturate(N.y * 2 - 1);
        c = lerp(c, runeCol * 0.55, g * 0.6) + runeCol * g * (0.9 + 0.25 * sin(_Time.y * 2.0));
    }
    if (hiAmount > 0.001)   // rim glow on block top edges + faint fill, pulsing
    {
        float2 q = abs(i.positionOS.xz); float e = smoothstep(0.34, 0.46, max(q.x, q.y)) * saturate(N.y * 2 - 0.6);
        half pulse = 0.75 + 0.25 * sin(_Time.y * 6.0);
        c += hiCol * hiAmount * (e * 1.8 + 0.15) * pulse;
    }
    {   // v16.1 status tint (frost crust / poison green / burn char) + coloured fresnel rim
        half4 st = SS_STATUS_TINT, sr = SS_STATUS_RIM;
        c = lerp(c, c * 0.55 + st.rgb * 0.45, st.a);
        c += sr.rgb * sr.a * smoothstep(0.45, 0.95, fres) * (0.85 + 0.15 * sin(_Time.y * 5.0));
    }
    c = lerp(c, _FlashColor.rgb, _HitFlash);
    c = lerp(c, _DissolveColor.rgb, edge);
    c = MixFog(c, i.fog);
    return half4(c, 1);
}

// ---- Outline (spec 4.6.3): smoothed normal from UV3, constant screen-space pixel width
struct OutlineVaryings { float4 positionCS : SV_POSITION; half fog : TEXCOORD0; float3 positionOS : TEXCOORD1; float wsY : TEXCOORD2; UNITY_VERTEX_INPUT_INSTANCE_ID };
OutlineVaryings OutlineVert(Attributes i)
{
    OutlineVaryings o = (OutlineVaryings)0;
    UNITY_SETUP_INSTANCE_ID(i);
    UNITY_TRANSFER_INSTANCE_ID(i, o);
    float3 nWS = TransformObjectToWorldNormal(i.normalOS);
    float3 tWS = TransformObjectToWorldDir(i.tangentOS.xyz);
    float3 bWS = cross(nWS, tWS) * i.tangentOS.w;
    float3 smoothWS = dot(i.uv3.xyz, i.uv3.xyz) > 1e-4 ? normalize(i.uv3.x * tWS + i.uv3.y * bWS + i.uv3.z * nWS) : nWS;
    float width = i.uv3.w > 0 ? i.uv3.w : 1;
    float3 ws = SS_World(i.positionOS, i.color);
    float4 posCS = TransformWorldToHClip(ws);
    float3 nCS = mul((float3x3)GetWorldToHClipMatrix(), smoothWS);
    float2 dir = normalize(nCS.xy + 1e-6);
    dir.x *= _ScreenParams.y / _ScreenParams.x;
    float g = _OutlineGlobalScale > 0 ? _OutlineGlobalScale : 1;
    float px = _OutlineWidthPx * width * g * (_ScreenParams.y / 1080.0);
    posCS.xy += dir * px * 2.0 / _ScreenParams.y * posCS.w;
#if UNITY_REVERSED_Z
    posCS.z -= _OutlineZOffset * posCS.w;
#else
    posCS.z += _OutlineZOffset * posCS.w;
#endif
    o.positionCS = posCS; o.positionOS = i.positionOS.xyz; o.wsY = ws.y;
    o.fog = ComputeFogFactor(posCS.z);
    return o;
}
half4 OutlineFrag(OutlineVaryings i) : SV_Target { UNITY_SETUP_INSTANCE_ID(i); SS_GroundClipTest(i.wsY); SS_DissolveClip(i.positionOS); return half4(MixFog(_OutlineColor.rgb, i.fog), 1); }

// ---- Shadow caster / depth
float3 _LightDirection;
float3 _LightPosition;
struct DepthVaryings { float4 positionCS : SV_POSITION; float3 positionOS : TEXCOORD0; };
DepthVaryings ShadowVert(Attributes i)
{
    DepthVaryings o;
    UNITY_SETUP_INSTANCE_ID(i);
    float3 ws = SS_World(i.positionOS, i.color);
    float3 nWS = TransformObjectToWorldNormal(i.normalOS);
    float4 cs = TransformWorldToHClip(ApplyShadowBias(ws, nWS, _LightDirection));
#if UNITY_REVERSED_Z
    cs.z = min(cs.z, UNITY_NEAR_CLIP_VALUE);
#else
    cs.z = max(cs.z, UNITY_NEAR_CLIP_VALUE);
#endif
    o.positionCS = cs; o.positionOS = i.positionOS.xyz;
    return o;
}
DepthVaryings DepthVert(Attributes i)
{
    DepthVaryings o;
    UNITY_SETUP_INSTANCE_ID(i);
    o.positionCS = TransformWorldToHClip(SS_World(i.positionOS, i.color)); o.positionOS = i.positionOS.xyz;
    return o;
}
half4 DepthFrag(DepthVaryings i) : SV_Target { SS_DissolveClip(i.positionOS); return 0; } // shadow + depth dissolve with the mesh
#endif
