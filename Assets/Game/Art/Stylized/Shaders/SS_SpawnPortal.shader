// v17.2 spawn portal ground (calm idle + readable spawn flare). One quad, 1 DC, GPU-instanced per-portal props.
//  _ScorchTex  RGBA  soft warm sun-baked burn (painted, light, no black)
//  _CrackTex   R     crack glow mask (spawn only)
//  _RuneTex    linear masks: R = outer ring band (notched, rotates), G = inner glyph (faint, counter-rotates),
//              B = soft halo around the ring (pulses), A = dark warm rim under the ring (contrast on sand)
// Per-portal MPB (instanced): _Flare (0..1 spawn envelope), _FlareT (0..1 normalised spawn time, drives the ring wave),
//                             _Active (idle level), _Crack (crack glow)
Shader "StoneSignal/SS_SpawnPortal"
{
    Properties
    {
        _ScorchTex ("Scorch decal (RGBA)", 2D) = "black" {}
        _CrackTex ("Crack glow mask (R)", 2D) = "black" {}
        _RuneTex ("Rune masks R ring G glyph B halo A rim (linear)", 2D) = "black" {}
        _RuneScale ("Rune size vs decal", Float) = 0.7
        _ScorchAlpha ("Scorch opacity", Range(0,1)) = 0.85
        [HDR] _RingColor ("Ring core (emissive)", Color) = (1.25, 0.78, 0.32, 1)
        [HDR] _RingEdge ("Ring edge", Color) = (1.0, 0.42, 0.1, 1)
        _RimColor ("Rim (dark, under ring)", Color) = (0.42, 0.17, 0.07, 0.55)
        [HDR] _HaloColor ("Halo", Color) = (1.0, 0.5, 0.15, 1)
        _HaloAlpha ("Halo alpha min/max", Vector) = (0.10, 0.30, 0, 0)
        _GlyphColor ("Glyph", Color) = (1.0, 0.72, 0.42, 1)
        _GlyphAlpha ("Glyph alpha (idle)", Range(0,1)) = 0.26
        _Spin ("Ring spin (rad/s)", Float) = 0.15
        _GlyphSpin ("Glyph spin (rad/s)", Float) = -0.05
        _PulseSpeed ("Pulse speed (rad/s)", Float) = 1.6
        [HDR] _CrackColor ("Crack glow", Color) = (3.2, 1.3, 0.35, 1)
        [HDR] _FlashColor ("Spawn flash", Color) = (1.7, 0.7, 0.2, 1)
        [HDR] _WaveColor ("Spawn ring wave", Color) = (1.6, 0.95, 0.35, 1)
        _Flare ("Flare", Range(0,1)) = 0
        _FlareT ("Flare time 0..1", Range(0,1)) = 1
        _Active ("Active", Range(0,1)) = 1
        _Crack ("Crack", Range(0,1)) = 0
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-20" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Name "Portal" Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Off Offset -1, -1
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_ScorchTex); SAMPLER(sampler_ScorchTex); TEXTURE2D(_CrackTex); TEXTURE2D(_RuneTex); SAMPLER(sampler_RuneTex);
            CBUFFER_START(UnityPerMaterial)
                float4 _ScorchTex_ST; half4 _RingColor, _RingEdge, _RimColor, _HaloColor, _HaloAlpha, _GlyphColor, _CrackColor, _FlashColor, _WaveColor;
                half _RuneScale, _ScorchAlpha, _GlyphAlpha, _Spin, _GlyphSpin, _PulseSpeed;
            #if !defined(UNITY_INSTANCING_ENABLED)
                half _Flare, _FlareT, _Active, _Crack;
            #endif
            CBUFFER_END
            #if defined(UNITY_INSTANCING_ENABLED)
            UNITY_INSTANCING_BUFFER_START(PortalProps)
                UNITY_DEFINE_INSTANCED_PROP(half, _Flare)
                UNITY_DEFINE_INSTANCED_PROP(half, _FlareT)
                UNITY_DEFINE_INSTANCED_PROP(half, _Active)
                UNITY_DEFINE_INSTANCED_PROP(half, _Crack)
            UNITY_INSTANCING_BUFFER_END(PortalProps)
            #define P(n) UNITY_ACCESS_INSTANCED_PROP(PortalProps, n)
            #else
            #define P(n) n
            #endif
            struct A { float4 pos : POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; UNITY_VERTEX_INPUT_INSTANCE_ID };
            V vert(A i) { V o; UNITY_SETUP_INSTANCE_ID(i); UNITY_TRANSFER_INSTANCE_ID(i, o); o.pos = TransformObjectToHClip(i.pos.xyz); o.uv = i.uv; return o; }
            float2 Rot(float2 q, float a) { float c = cos(a), s = sin(a); return float2(q.x * c - q.y * s, q.x * s + q.y * c) + 0.5; }
            half InBox(float2 uv) { return step(abs(uv.x - 0.5), 0.5) * step(abs(uv.y - 0.5), 0.5); }
            // premultiplied "over"
            void Over(inout half3 c, inout half a, half3 lc, half la) { la = saturate(la); c = lc * la + c * (1 - la); a = la + a * (1 - la); }
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half flare = P(_Flare), ft = P(_FlareT), act = P(_Active), crack = P(_Crack);
                half4 sc = SAMPLE_TEXTURE2D(_ScorchTex, sampler_ScorchTex, i.uv);
                half cm = SAMPLE_TEXTURE2D(_CrackTex, sampler_ScorchTex, i.uv).r;
                float2 q = (i.uv - 0.5) / _RuneScale;
                float t = _Time.y;
                float2 ruv = Rot(q, t * _Spin * (1 + flare * 3));
                float2 guv = Rot(q, t * _GlyphSpin);
                half4 rm = SAMPLE_TEXTURE2D(_RuneTex, sampler_RuneTex, ruv) * InBox(ruv);   // ring, halo, rim
                half gm = SAMPLE_TEXTURE2D(_RuneTex, sampler_RuneTex, guv).g * InBox(guv);  // glyph
                half pulse = 0.5 + 0.5 * sin(t * _PulseSpeed);                               // gentle ~4 s breathe
                half lvl = lerp(0.55, 1.0, act);

                half3 c = 0; half a = 0;
                Over(c, a, sc.rgb, sc.a * _ScorchAlpha);
                // halo (soft, pulsing) under the ring
                Over(c, a, _HaloColor.rgb * (1 + flare), rm.b * lvl * (lerp(_HaloAlpha.x, _HaloAlpha.y, pulse) + flare * 0.5));
                // dark warm rim gives the ring a toon edge on bright sand
                Over(c, a, _RimColor.rgb, rm.a * _RimColor.a * lvl);
                // ring band: edge -> core colour, emissive pulse
                half core = smoothstep(0.55, 1.0, rm.r);
                half3 ringC = lerp(_RingEdge.rgb, _RingColor.rgb, core) * (0.9 + 0.25 * pulse + flare * 1.4);
                Over(c, a, ringC, rm.r * lvl);
                // inner glyph: faint at idle, lights up on spawn
                Over(c, a, _GlyphColor.rgb * (1 + flare * 1.5), gm * saturate(_GlyphAlpha * lvl + flare * 0.85));
                // cracks glow on spawn
                half crackK = saturate(crack + 0.05 * act * pulse);
                Over(c, a, _CrackColor.rgb, cm * crackK);
                // spawn flash: hot centre bloom (0-0.3 s) + expanding ring wave
                float r = length(i.uv - 0.5) * 2;                                             // 0 centre .. 1 quad edge
                half flash = flare * (exp(-r * r / 0.06) + 0.45 * exp(-r * r / 0.30));
                Over(c, a, _FlashColor.rgb, flash);
                float rw = lerp(0.12, 0.95, 1 - (1 - ft) * (1 - ft));
                float dw = (r - rw) / 0.06; half wave = exp(-dw * dw) * pow(saturate(1 - ft), 1.3) * step(0.001, 1 - ft);
                Over(c, a, _WaveColor.rgb, wave * 0.95);
                return half4(c / max(a, 1e-4), a);
            }
            ENDHLSL
        }
    }
}
