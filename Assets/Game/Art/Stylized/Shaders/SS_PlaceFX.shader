// StoneSignal placement / path feedback. Unlit, transparent, UV-only maths (WebGL/mini-game cheap: no depth/opaque textures).
// _Mode 0 = ghost (tinted translucent + rim pulse), 1 = dashed animated range ring (quad UV 0..1), 2 = soft cell outline (quad),
//       3 = path flow chevrons (LineRenderer, textureMode Tile/Stretch; uv.x along path).
Shader "StoneSignal/PlaceFX"
{
    Properties
    {
        _Color ("Tint", Color) = (0.45, 1, 0.55, 0.45)
        _Mode ("Mode 0 ghost 1 ring 2 outline 3 path", Float) = 0
        _Speed ("Scroll Speed", Float) = 1
        _Dashes ("Dash Count / Chevron Freq", Float) = 32
        _Width ("Ring / Outline Width", Range(0.01, 0.3)) = 0.06
        _Pulse ("Pulse", Range(0, 1)) = 0.35
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent+10" "RenderPipeline" = "UniversalPipeline" }
        Blend SrcAlpha OneMinusSrcAlpha
        ZWrite Off Cull Off
        Pass
        {
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _Color; float _Mode, _Speed, _Dashes, _Width, _Pulse;
            CBUFFER_END
            struct A { float4 pos : POSITION; float3 n : NORMAL; float2 uv : TEXCOORD0; half4 col : COLOR; };
            struct V { float4 pos : SV_POSITION; float2 uv : TEXCOORD0; float3 n : TEXCOORD1; float3 vd : TEXCOORD2; half4 col : COLOR; };
            V vert(A i)
            {
                V o; float3 ws = TransformObjectToWorld(i.pos.xyz);
                o.pos = TransformWorldToHClip(ws); o.uv = i.uv; o.n = TransformObjectToWorldNormal(i.n);
                o.vd = GetWorldSpaceViewDir(ws); o.col = i.col; return o;
            }
            half4 frag(V i) : SV_Target
            {
                float t = _Time.y * _Speed;
                half4 c = _Color * i.col;
                if (_Mode < 0.5)        // ghost
                {
                    float rim = pow(1 - saturate(abs(dot(normalize(i.n), normalize(i.vd)))), 2);
                    float pulse = 1 + sin(_Time.y * 5) * _Pulse * 0.5;
                    return half4(c.rgb * (0.8 + rim * 0.8), saturate(c.a * pulse + rim * 0.35));
                }
                if (_Mode < 1.5)        // dashed ring
                {
                    float2 p = i.uv * 2 - 1; float r = length(p);
                    float ring = smoothstep(1 - _Width, 1 - _Width * 0.6, r) * (1 - smoothstep(0.97, 1, r));
                    float a = atan2(p.y, p.x) / 6.2831853 + 0.5;
                    float dash = smoothstep(0.35, 0.45, frac(a * _Dashes - t)) * (1 - smoothstep(0.75, 0.85, frac(a * _Dashes - t)));
                    float fill = (1 - smoothstep(0.9, 1, r)) * 0.06;  // faint area tint
                    return half4(c.rgb, (ring * dash + fill) * c.a);
                }
                if (_Mode < 2.5)        // soft cell outline
                {
                    float2 d = abs(i.uv - 0.5) * 2; float e = max(d.x, d.y);
                    float edge = smoothstep(1 - _Width * 2, 1 - _Width, e) * (1 - smoothstep(0.98, 1, e));
                    return half4(c.rgb, (edge + 0.12) * c.a);
                }
                // path flow chevrons scrolling along uv.x; soft edges across uv.y
                float across = abs(i.uv.y - 0.5) * 2;
                float chev = frac(i.uv.x * _Dashes * 0.1 - across * 0.35 - t);
                float band = smoothstep(0.0, 0.15, chev) * (1 - smoothstep(0.35, 0.5, chev));
                float soft = 1 - smoothstep(0.6, 1, across);
                return half4(c.rgb, (band * 0.85 + 0.15) * soft * c.a);
            }
            ENDHLSL
        }
    }
}
