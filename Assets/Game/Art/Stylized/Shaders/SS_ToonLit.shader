// Environment toon (no outline): tiles, stones, trees, props.
// Hand-written URP 14 HLSL (spec 4.2 SG_Toon_Lit equivalent; 4.6 option A two-pass outline).
Shader "StoneSignal/ToonLit"
{
    Properties
    {
        _BaseMap ("Base Map (palette)", 2D) = "white" {}
        _BaseColor ("Base Color", Color) = (1,1,1,1)
        [NoScaleOffset] _RampTex ("Ramp (T_Ramp_Toon_3Step)", 2D) = "white" {}
        [Toggle] _UseRamp ("Use Ramp Texture", Float) = 1
        _ShadowThreshold ("Shadow Threshold", Range(0.3,0.6)) = 0.45
        _ShadowSmooth ("Shadow Smooth", Range(0.005,0.1)) = 0.03
        _ShadowStrength ("Shadow Strength", Range(0,1)) = 0.85
        _ShadowColor ("Shadow Color", Color) = (0.431,0.353,0.549,1)
        _LitColor ("Lit Color", Color) = (1,1,1,1)
        _AmbientStrength ("Ambient Strength", Range(0,1)) = 0.35
        _RimColor ("Rim Color", Color) = (1,0.902,0.78,1)
        _RimMin ("Rim Min", Range(0,1)) = 0.55
        _RimMax ("Rim Max", Range(0,1)) = 0.75
        _RimIntensity ("Rim Intensity", Range(0,1)) = 0.35
        _RimLitBias ("Rim Lit Bias", Range(0,1)) = 0.3
        [Toggle] _LayerTint ("Foliage Layer Tint (vertex B)", Float) = 0
        _InnerTint ("Inner Tint", Color) = (0.69,0.44,0.565,1)
        _OuterTint ("Outer Tint", Color) = (1,0.94,0.784,1)
        _WindStrength ("Wind Strength (vertex R)", Range(0,2)) = 0
        [HDR] _EmissionColor ("Emission", Color) = (0,0,0,1)
        _HitFlash ("Hit Flash", Range(0,1)) = 0
        _FlashColor ("Flash Color", Color) = (1,1,1,1)
        _Dissolve ("Dissolve", Range(0,1)) = 0
        _DissolveEdge ("Dissolve Edge Width", Range(0.01,0.3)) = 0.08
        [HDR] _DissolveColor ("Dissolve Edge Color", Color) = (4,2.2,0.8,1)
        _Mottle ("World Mottle (weathering)", Range(0,1)) = 0
        _VColorEmission ("Vertex R Emission (v17.3 core enclosure; needs Wind 0)", Float) = 0
        [HideInInspector] _RuneIdx ("Rune Index", Float) = -1
        [HideInInspector] [HDR] _RuneColor ("Rune Color", Color) = (1,1,1,1)
        [NoScaleOffset] _RuneAtlas ("Rune Glyph Atlas (4x2, alpha)", 2D) = "black" {}
        [HideInInspector] _HiAmount ("Placement Highlight", Range(0,1)) = 0
        [HideInInspector] [HDR] _HiColor ("Placement Highlight Color", Color) = (0.4,1.6,0.6,1)
        _BaseAO ("Base AO Strength", Range(0,1)) = 0
        _BaseAOHeight ("Base AO Height (m, object Y)", Float) = 0.3
        _Wobble ("Vertex Wobble (Shard)", Range(0,2)) = 0
        _WobbleFreq ("Wobble Frequency", Range(0,12)) = 5
        _OutlineColor ("Outline Color", Color) = (0.118,0.102,0.227,1)
        _OutlineWidthPx ("Outline Width (px @1080p)", Range(0,6)) = 3
        _OutlineZOffset ("Outline Z Offset", Range(0,0.002)) = 0.0004
    }
    SubShader
    {
        Tags { "RenderType" = "Opaque" "RenderPipeline" = "UniversalPipeline" "Queue" = "Geometry" }

        Pass
        {
            Name "ToonForward"
            Tags { "LightMode" = "UniversalForward" }
            Cull Back ZWrite On
            HLSLPROGRAM
            #pragma vertex ToonVert
            #pragma fragment ToonFrag
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile_fragment _ _SHADOWS_SOFT
            #pragma multi_compile_fog
            #pragma multi_compile_instancing
            #include "StoneSignalToonCore.hlsl"
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On ZTest LEqual ColorMask 0 Cull Back
            HLSLPROGRAM
            #pragma vertex ShadowVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "StoneSignalToonCore.hlsl"
            ENDHLSL
        }
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On ColorMask R Cull Back
            HLSLPROGRAM
            #pragma vertex DepthVert
            #pragma fragment DepthFrag
            #pragma multi_compile_instancing
            #include "StoneSignalToonCore.hlsl"
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
