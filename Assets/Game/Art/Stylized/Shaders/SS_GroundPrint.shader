// v17.3 ground print (footprints, PF_VFX_EnemyGroundSystem): horizontal particle billboards laid on the resolved walk surface.
// Queue 2995 (after opaque, before the route flow / other transparents), ZTest LEqual, ZWrite Off, small depth offset so a print
// 2 cm above a plank / tile never z-fights or sinks into the plank bevel. Colour = _BaseColor (gamma-converted like any colour
// property); particle vertex colour supplies only the alpha (lifetime fade), so the print colour is the same in gamma/linear projects.
Shader "StoneSignal/SS_GroundPrint"
{
    Properties
    {
        _BaseMap ("Print (alpha)", 2D) = "white" {}
        _BaseColor ("Print colour (rgb) / opacity (a)", Color) = (0.227, 0.141, 0.078, 1)
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent-5" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" "PreviewType" = "Plane" }
        Pass
        {
            Name "Print" Tags { "LightMode" = "UniversalForward" }
            Blend SrcAlpha OneMinusSrcAlpha ZWrite Off ZTest LEqual Cull Off Offset -1, -2
            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_fog
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            TEXTURE2D(_BaseMap); SAMPLER(sampler_BaseMap);
            CBUFFER_START(UnityPerMaterial)
                float4 _BaseMap_ST; half4 _BaseColor;
            CBUFFER_END
            struct A { float4 pos : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.color = i.color; o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.fog = ComputeFogFactor(o.pos.z); return o; }
            half4 frag(V i) : SV_Target
            {
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * i.color.a * _BaseColor.a;
                return half4(MixFog(_BaseColor.rgb, i.fog), a);
            }
            ENDHLSL
        }
    }
}
