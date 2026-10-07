// StoneSignal stylized water (URP 14): depth-based shallow->deep gradient, animated ripple bands,
// caustic-ish cell noise, depth shoreline foam and sparkles. Needs the camera depth texture.
Shader "StoneSignal/Water"
{
    Properties
    {
        _ShallowColor ("Shallow", Color) = (0.11, 0.62, 0.72, 1)
        _DeepColor ("Deep", Color) = (0.02, 0.2, 0.52, 1)
        _DepthRange ("Depth Range (m)", Float) = 2.2
        _FoamColor ("Foam", Color) = (0.95, 0.97, 1, 1)
        _FoamDepth ("Foam Depth (m)", Float) = 0.35
        _RippleScale ("Ripple Scale", Float) = 2.4
        _RippleSpeed ("Ripple Speed", Float) = 0.35
        _CausticStrength ("Caustic Strength", Range(0,1)) = 0.35
        _SparkleStrength ("Sparkle", Range(0,3)) = 1.2
        _BaseColor ("Base Color (fallback)", Color) = (0.03, 0.33, 0.63, 1)
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
                half4 _ShallowColor, _DeepColor, _FoamColor, _BaseColor;
                float _DepthRange, _FoamDepth, _RippleScale, _RippleSpeed, _CausticStrength, _SparkleStrength;
            CBUFFER_END
            struct A { float4 pos : POSITION; };
            struct V { float4 pos : SV_POSITION; float3 ws : TEXCOORD0; float4 sp : TEXCOORD1; half fog : TEXCOORD2; };
            V vert(A i)
            {
                V o; o.ws = TransformObjectToWorld(i.pos.xyz); o.pos = TransformWorldToHClip(o.ws);
                o.sp = ComputeScreenPos(o.pos); o.fog = ComputeFogFactor(o.pos.z); return o;
            }
            float2 hash2(float2 p) { p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3))); return frac(sin(p) * 43758.5453); }
            float cellular(float2 p, float t)
            {
                float2 i = floor(p), f = frac(p); float d = 8;
                for (int y = -1; y <= 1; y++) for (int x = -1; x <= 1; x++)
                {
                    float2 g = float2(x, y); float2 o = hash2(i + g); o = 0.5 + 0.5 * sin(t + 6.2831 * o);
                    d = min(d, length(g + o - f));
                }
                return d;
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
                float t = _Time.y * _RippleSpeed;
                float2 p = i.ws.xz * _RippleScale;
                float c1 = cellular(p + float2(t, t * 0.6), _Time.y * 0.8);
                float c2 = cellular(p * 1.9 - float2(t * 0.7, t), _Time.y * 1.1);
                float caustic = pow(saturate(1 - min(c1, c2) * 1.6), 4) * 0.6;
                half3 col = lerp(_ShallowColor.rgb, _DeepColor.rgb, saturate(wd / _DepthRange));
                col += caustic * _CausticStrength * (1 - saturate(wd / (_DepthRange * 1.4))) * half3(0.7, 0.95, 1);
                // open-water ripple lines (subtle, broken by noise)
                float cf = cellular(p * 2.2 + float2(t * 1.3, -t), _Time.y * 1.4);  // fine, small ripple glints
                float band = smoothstep(0.9, 0.97, frac(cf * 3.0 + t)) * smoothstep(0.35, 0.65, c2) * saturate(wd / (_FoamDepth * 4));
                col += band * 0.045;
                // shoreline: soft depth band + 2 soft wave lines travelling outward along depth contours
                float shoreMask = 1 - smoothstep(0, _FoamDepth * 2.5, wd);
                float soft = 1 - smoothstep(0, _FoamDepth, wd);                        // soft fade at contact, no hard edge
                float wave = frac(wd / (_FoamDepth * 1.6) - _Time.y * 0.35);
                float lines = smoothstep(0.0, 0.06, wave) * (1 - smoothstep(0.06, 0.16, wave));  // thin
                lines *= smoothstep(0.25, 0.7, c2 + c1 * 0.5) * shoreMask;             // broken, fades with distance
                float foam = saturate(soft * 0.5 + lines * 0.3);
                col = lerp(col, _FoamColor.rgb, foam);
                // sparkle
                float sp = step(0.99, hash2(floor(i.ws.xz * 6) + floor(_Time.y * 2)).x) * step(c1, 0.2) * 0.6;
                col += sp * _SparkleStrength;
                col = MixFog(col, i.fog);
                return half4(col, saturate(0.78 + foam * 0.2 + saturate(wd / _DepthRange) * 0.2) * saturate(wd * 12 + 0.6));
            }
            ENDHLSL
        }
    }
}
