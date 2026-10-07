// v17.3 ground print (footprints, PF_VFX_EnemyGroundSystem): horizontal particle billboards laid on the resolved walk surface.
// Queue 2995 (after opaque, before the route flow / other transparents), ZTest LEqual, ZWrite Off, small depth offset so a print
// 2 cm above a plank / tile never z-fights or sinks into the plank bevel. Colour = _BaseColor (gamma-converted like any colour
// property); particle vertex colour supplies only the alpha (lifetime fade), so the print colour is the same in gamma/linear projects.
// v17.5: per-surface tint - vertex colour R = 0 marks a print on bridge planks (EnemyGroundFxSystem, WalkSurface.Kind.Plank) and swaps
// to _PlankColor (light dusty #C9A57A, a 0.55: dark prints vanish on the dark Trunk/Wood deck). R = 1 (default white) = _BaseColor as before.
Shader "StoneSignal/SS_GroundPrint"
{
    Properties
    {
        _BaseMap ("Print (alpha)", 2D) = "white" {}
        _BaseColor ("Print colour (rgb) / opacity (a)", Color) = (0.227, 0.141, 0.078, 1)
        _PlankColor ("v17.5 print on planks (rgb) / opacity (a)", Color) = (0.788, 0.647, 0.478, 0.55)
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
                float4 _BaseMap_ST; half4 _BaseColor; half4 _PlankColor;
            CBUFFER_END
            struct A { float4 pos : POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; };
            struct V { float4 pos : SV_POSITION; half4 color : COLOR; float2 uv : TEXCOORD0; half fog : TEXCOORD1; };
            V vert(A i) { V o; o.pos = TransformObjectToHClip(i.pos.xyz); o.color = i.color; o.uv = TRANSFORM_TEX(i.uv, _BaseMap); o.fog = ComputeFogFactor(o.pos.z); return o; }
            half4 frag(V i) : SV_Target
            {
                half4 col = lerp(_PlankColor, _BaseColor, step(0.5, i.color.r));   // v17.5: R < .5 -> plank print
                half a = SAMPLE_TEXTURE2D(_BaseMap, sampler_BaseMap, i.uv).a * i.color.a * col.a;
                return half4(MixFog(col.rgb, i.fog), a);
            }
            ENDHLSL
        }
    }
}
