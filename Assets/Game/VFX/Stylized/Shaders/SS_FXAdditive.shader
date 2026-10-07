Shader "StoneSignal/FXAdditive"
{
    Properties
    {
        [HDR] _TintColor ("Tint", Color) = (1,1,1,1)
        _Intensity ("Intensity", Range(0,8)) = 1
        _Shape ("Shape 0Dot 1Ring 2Streak 3Smoke 4Flash 5Scorch 6Coin 7Trail", Float) = 0
        _Softness ("Softness", Range(0,1)) = 0.5
        _RingWidth ("Ring Width", Range(0.02,0.5)) = 0.15
    }
    SubShader
    {
        Tags { "RenderType" = "Transparent" "Queue" = "Transparent" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }
        Pass
        {
            Tags { "LightMode" = "UniversalForward" }
            Blend One One
            ZWrite Off Cull Off
            HLSLPROGRAM
            #pragma vertex FXVert
            #pragma fragment FXFragAdd
            #include "SS_FXCore.hlsl"
            ENDHLSL
        }
    }
}
