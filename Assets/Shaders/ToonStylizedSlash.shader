Shader "Toon/ToonStylizedSlash"
{
    Properties
    {
        [Header(Colors and Cel Shading)]
        [HDR] _ColorCore ("Core Highlight Color", Color) = (3.5, 3.2, 1.2, 1.0)
        _ColorTone ("Mid-Tone Color", Color) = (1.0, 0.45, 0.05, 1.0)
        _ColorBorder ("Outline / Border Color", Color) = (0.0, 0.0, 0.0, 1.0)
        
        [Header(Bands Thickness)]
        _BorderThickness ("Outline Thickness", Range(0.01, 0.35)) = 0.08
        _ToneThickness ("Mid-Tone Thickness", Range(0.01, 0.35)) = 0.16
        _Softness ("Edge Softness (0 for sharp Toon)", Range(0.001, 0.05)) = 0.005
        _Cutoff ("Alpha Cutoff", Range(0.01, 0.5)) = 0.04
        
        [Header(Textures)]
        _SlashMask ("Slash Mask (Main)", 2D) = "white" {}
        _MaskTex ("Gradient / Sweep Mask", 2D) = "white" {}
        _TipFadeTex ("Tip Fade (U)", 2D) = "white" {}
        _DissolveTex ("Dissolve / Noise Tex", 2D) = "white" {}
        _DistortStrength ("Distort Strength", Range(0.0, 0.2)) = 0.012
        
        [Header(Settings)]
        _UseParticleColor ("Use Particle Color Tint", Range(0.0, 1.0)) = 1.0
        _Emission ("Core Emission Boost", Float) = 1.0
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent" 
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector" = "True"
        }

        Blend SrcAlpha OneMinusSrcAlpha
        Cull Off
        ZWrite Off
        ZTest LEqual

        Pass
        {
            Name "UniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv : TEXCOORD0;
                float4 color : COLOR;
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _ColorCore;
                float4 _ColorTone;
                float4 _ColorBorder;
                float4 _SlashMask_ST;
                float4 _MaskTex_ST;
                float4 _TipFadeTex_ST;
                float4 _DissolveTex_ST;
                float _BorderThickness;
                float _ToneThickness;
                float _Softness;
                float _Cutoff;
                float _DistortStrength;
                float _UseParticleColor;
                float _Emission;
            CBUFFER_END

            TEXTURE2D(_SlashMask);
            SAMPLER(sampler_SlashMask);

            TEXTURE2D(_MaskTex);
            SAMPLER(sampler_MaskTex);

            TEXTURE2D(_TipFadeTex);
            SAMPLER(sampler_TipFadeTex);

            TEXTURE2D(_DissolveTex);
            SAMPLER(sampler_DissolveTex);

            Varyings vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                output.color = input.color;
                return output;
            }

            float4 frag(Varyings input) : SV_Target
            {
                float2 uv = input.uv;

                // Subtle noise distortion on UVs for stylized organic ink edge
                float2 noiseUV = TRANSFORM_TEX(uv, _DissolveTex);
                float noise = SAMPLE_TEXTURE2D(_DissolveTex, sampler_DissolveTex, noiseUV).r;
                float2 distortedUV = uv + (noise - 0.5) * _DistortStrength;

                // Sample primary slash shape
                float2 maskUV = TRANSFORM_TEX(distortedUV, _SlashMask);
                float mask = SAMPLE_TEXTURE2D(_SlashMask, sampler_SlashMask, maskUV).r;

                // Sample sweep / arc gradient mask
                float2 gradUV = TRANSFORM_TEX(distortedUV, _MaskTex);
                float grad = SAMPLE_TEXTURE2D(_MaskTex, sampler_MaskTex, gradUV).r;

                // Sample tip fade (tapers ends to sharp cartoon crescent points)
                float2 tipUV = TRANSFORM_TEX(distortedUV, _TipFadeTex);
                float tip = SAMPLE_TEXTURE2D(_TipFadeTex, sampler_TipFadeTex, tipUV).r;

                // Combine mask and particle alpha
                float particleAlpha = saturate(input.color.a);
                float combined = mask * grad * tip * particleAlpha;

                // Cutoff transparent pixels outside the slash
                clip(combined - _Cutoff);

                // Sharp cel-shaded toon bands with solid black cartoon outline
                float borderMask = smoothstep(_Cutoff, _Cutoff + _Softness, combined);
                float toneMask   = smoothstep(_Cutoff + _BorderThickness, _Cutoff + _BorderThickness + _Softness, combined);
                float coreMask   = smoothstep(_Cutoff + _BorderThickness + _ToneThickness, _Cutoff + _BorderThickness + _ToneThickness + _Softness, combined);

                // Particle color tint
                float3 particleTint = lerp(float3(1, 1, 1), input.color.rgb, _UseParticleColor);

                // Cel Shading color composition:
                // Outer ring: Solid Black Outline (_ColorBorder)
                // Mid ring:   Stylized Toon Midtone (_ColorTone)
                // Inner core: Luminous Toon Highlight (_ColorCore * _Emission)
                float3 finalRgb = _ColorBorder.rgb;
                finalRgb = lerp(finalRgb, _ColorTone.rgb * particleTint, toneMask);
                finalRgb = lerp(finalRgb, _ColorCore.rgb * particleTint * _Emission, coreMask);

                float finalAlpha = borderMask;
                return float4(finalRgb, saturate(finalAlpha));
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
