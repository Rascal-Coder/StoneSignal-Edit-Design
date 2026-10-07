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
            CBUFFER_END
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
                col = MixFog(col, i.fog);
                return half4(col, 1);
            }
            ENDHLSL
        }
    }
}
