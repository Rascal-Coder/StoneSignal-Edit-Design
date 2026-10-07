// v16.2 art-led spawn portal ground: hand-painted scorch decal + crack glow mask + painted rune ring, one quad, 1 DC.
// Per-portal state via MPB: _Flare (0..1 spawn flash), _Active (idle level), _Crack (crack glow) - instanced props.
Shader "StoneSignal/SS_SpawnPortal"
{
    Properties
    {
        _ScorchTex ("Scorch decal (RGBA)", 2D) = "black" {}
        _CrackTex ("Crack glow mask (R)", 2D) = "black" {}
        _RuneTex ("Rune circle (RGBA)", 2D) = "black" {}
        _RuneScale ("Rune size vs decal", Float) = 0.7
        [HDR] _CrackColor ("Crack glow", Color) = (3.2, 1.2, 0.3, 1)
        [HDR] _RuneTint ("Rune tint", Color) = (1.4, 1.0, 0.8, 1)
        _Spin ("Rune spin (rad/s)", Float) = 0.12
        _Flare ("Flare", Range(0,1)) = 0
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
                float4 _ScorchTex_ST; half4 _CrackColor, _RuneTint; half _RuneScale, _Spin;
            #if !defined(UNITY_INSTANCING_ENABLED)
                half _Flare, _Active, _Crack;
            #endif
            CBUFFER_END
            #if defined(UNITY_INSTANCING_ENABLED)
            UNITY_INSTANCING_BUFFER_START(PortalProps)
                UNITY_DEFINE_INSTANCED_PROP(half, _Flare)
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
            half4 frag(V i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                half flare = P(_Flare), act = P(_Active), crack = P(_Crack);
                half4 sc = SAMPLE_TEXTURE2D(_ScorchTex, sampler_ScorchTex, i.uv);
                half cm = SAMPLE_TEXTURE2D(_CrackTex, sampler_ScorchTex, i.uv).r;
                float2 q = (i.uv - 0.5) / _RuneScale; float a = _Time.y * _Spin * (1 + flare * 4); float ca = cos(a), sa = sin(a);
                float2 ruv = float2(q.x * ca - q.y * sa, q.x * sa + q.y * ca) + 0.5;
                half4 ru = SAMPLE_TEXTURE2D(_RuneTex, sampler_RuneTex, ruv) * step(abs(ruv.x - 0.5), 0.5) * step(abs(ruv.y - 0.5), 0.5);
                half breathe = 0.8 + 0.2 * sin(_Time.y * 2.0);
                half runeK = lerp(0.45, 0.85, act) * breathe + flare * 1.6;
                half crackK = saturate(crack + 0.08 * act * breathe);
                half3 c = sc.rgb;
                c = lerp(c, ru.rgb * _RuneTint.rgb * runeK, ru.a);
                c += _CrackColor.rgb * cm * crackK;
                half alpha = saturate(max(sc.a, ru.a) + cm * crackK);
                return half4(c, alpha);
            }
            ENDHLSL
        }
    }
}
