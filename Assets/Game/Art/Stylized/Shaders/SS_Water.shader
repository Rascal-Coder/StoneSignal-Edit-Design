// StoneSignal stylized water (URP 14): depth-based shallow->deep gradient, animated ripple bands,
// v9: smooth depth gradient only (no noise/foam/textures). Needs the camera depth texture.
Shader "StoneSignal/Water"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.11, 0.62, 0.72, 1)
        _DeepColor ("Deep", Color) = (0.02, 0.2, 0.52, 1)
        _DepthRange ("Shore Band Depth (m)", Float) = 0.08
        [HDR] _GlowColor ("Shore Glow (HDR)", Color) = (0.55, 1.05, 1.25, 1)
        _BaseColor ("Base Color (fallback)", Color) = (0.03, 0.33, 0.63, 1)
        // v16.2 subtle shore ripples: thin low-contrast rings that slowly travel outward from shores/board edge (depth distance field)
        _RippleColor ("Ripple Color", Color) = (0.55, 0.78, 0.95, 1)
        _RippleStrength ("Ripple Strength", Range(0, 0.5)) = 0.14
        _RippleReach ("Ripple Reach (m depth)", Float) = 0.55
        _RippleSpacing ("Ripple Spacing", Float) = 0.16
        _RippleSpeed ("Ripple Speed", Float) = 0.035
        _RippleWidth ("Ripple Line Width", Range(0.02, 0.4)) = 0.12
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-50" "RenderPipeline" = "UniversalPipeline" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off Cull Back
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"
            CBUFFER_START(UnityPerMaterial)
                half4 _ShallowColor, _DeepColor, _BaseColor, _GlowColor;
                float _DepthRange;
                half4 _RippleColor; float _RippleStrength, _RippleReach, _RippleSpacing, _RippleSpeed, _RippleWidth;
            CBUFFER_END
            // v16.2 optional spawn ripples (global, set by SpawnPortal.PlaySpawn): xyz = world pos, w = start time (_Time.y); w<0 = off
            float4 _SS_SpawnRipples[4];
            half Ring(float x, float w) { float f = frac(x); return smoothstep(0, w, f) * (1 - smoothstep(w, w * 2, f)); }
            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; float4 sp : TEXCOORD1; half fog : TEXCOORD2; };
            V vert(A i)
            {
                V o; o.ws = TransformObjectToWorld(i.pos.xyz); o.pos = TransformWorldToHClip(o.ws);
                o.sp = ComputeScreenPos(o.pos); o.fog = ComputeFogFactor(o.pos.z); return o;
            }
            half4 frag(V i) : SV_Target
            {
                float2 uv = i.sp.xy / i.sp.w;
                float rawD = SampleSceneDepth(uv);
                float sceneEye = unity_OrthoParams.w > 0.5
                    ? lerp(_ProjectionParams.y, _ProjectionParams.z, UNITY_REVERSED_Z ? 1 - rawD : rawD)
                    : LinearEyeDepth(rawD, _ZBufferParams);
                float surfEye = unity_OrthoParams.w > 0.5
                    ? lerp(_ProjectionParams.y, _ProjectionParams.z, UNITY_REVERSED_Z ? 1 - i.pos.z : i.pos.z)
                    : LinearEyeDepth(i.pos.z, _ZBufferParams);
                float depth = max(0, sceneEye - surfEye);
                // view-depth -> approx vertical depth for a ~45 deg camera
                float wd = depth * 0.7;
                // v9: uniform deep blue, slight darkening toward the far distance; only a thin soft light band hugging shores.
                float far = smoothstep(10, 30, length(i.ws.xz));
                half3 col = lerp(_DeepColor.rgb, _DeepColor.rgb * 0.82, far);
                // v10: narrow (~0.1 m) glowing shore line - HDR emissive, smooth falloff, slow subtle pulse (pure ALU, WebGL safe)
                float band = 1 - smoothstep(_DepthRange * 0.35, _DepthRange, wd);   // v11: thin bright line, tight falloff
                band *= band;                                                      // softer outer falloff
                float pulse = 1 + 0.12 * sin(_Time.y * 1.3 + (i.ws.x + i.ws.z) * 0.15);
                col = lerp(col, _ShallowColor.rgb, band * 0.6);
                col += _GlowColor.rgb * band * pulse;                              // HDR > 1 -> picked up by existing bloom
                // v16.2 shore ripples: concentric iso-lines of the shore distance (wd), moving outward; fade in/out so no hard edge
                float rd = wd / max(_RippleSpacing, 1e-3) - _Time.y * _RippleSpeed / max(_RippleSpacing, 1e-3);
                float wob = 0.18 * sin(i.ws.x * 1.7 + _Time.y * 0.4) * sin(i.ws.z * 1.3 - _Time.y * 0.3);
                half ring = Ring(rd + wob, _RippleWidth);
                half fade = smoothstep(_DepthRange * 0.8, _DepthRange * 1.6, wd) * (1 - smoothstep(_RippleReach * 0.5, _RippleReach, wd));
                col = lerp(col, _RippleColor.rgb, ring * fade * _RippleStrength);
                // spawn ripples: 2 expanding rings over ~1.6 s
                [unroll] for (int k = 0; k < 4; k++)
                {
                    float4 sr = _SS_SpawnRipples[k]; float age = _Time.y - sr.w;
                    if (sr.w >= 0 && age > 0 && age < 1.6)
                    {
                        float r = length(i.ws.xz - sr.xz); float R = age * 1.1;
                        half rr = (1 - smoothstep(0.0, 0.06, abs(r - R))) + 0.6 * (1 - smoothstep(0.0, 0.05, abs(r - R * 0.6)));
                        col = lerp(col, _RippleColor.rgb, saturate(rr) * (1 - age / 1.6) * _RippleStrength * 1.6);
                    }
                }
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
