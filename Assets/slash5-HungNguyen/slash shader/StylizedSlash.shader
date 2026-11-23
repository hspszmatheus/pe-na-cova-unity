Shader "Stylized/SlashVFX"
{
    Properties
    {
        [Header(Colors and Cel Shading)]
        [Toggle] _EnableToonSteps ("Toon / Cel-Shaded Mode", Float) = 1.0
        [HDR] _ColorMain ("Core / Highlight Color", Color) = (3.44, 3.44, 3.44, 1.0)
        _ToneColor ("Mid-Tone Color", Color) = (0.8, 0.8, 0.8, 1.0)
        _BorderColor ("Outline / Border Color", Color) = (0.0, 0.0, 0.0, 1.0)
        
        [Header(Cel Bands Thickness)]
        _BorderThickness ("Outline Thickness", Range(0.01, 0.4)) = 0.10
        _ToneThickness ("Mid-Tone Thickness", Range(0.01, 0.4)) = 0.15
        _Softness ("Edge Softness (0 for sharp Toon)", Range(0.001, 0.1)) = 0.005

        [Header(Animation and Fade)]
        _SlashOpacity ("Slash Opacity", Range(0.0, 1.0)) = 1.0
        _Progress ("Slash Progress (0 to 1)", Range(0.0, 1.0)) = 0.5
        _ArcWidth ("Arc Width", Range(0.1, 1.0)) = 0.6
        
        [Header(Textures)]
        _LeftRightTex ("Left Right Fade (U)", 2D) = "white" {}
        _TopBotTex ("Top Bot Fade (V)", 2D) = "white" {}
        _DistortTex ("Distortion / Noise", 2D) = "white" {}
        _DistortStrength ("Distort Strength", Range(0.0, 0.2)) = 0.035
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
            Name "ForwardLit"
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
                float4 _ColorMain;
                float4 _ToneColor;
                float4 _BorderColor;
                float4 _LeftRightTex_ST;
                float4 _TopBotTex_ST;
                float4 _DistortTex_ST;
                float _EnableToonSteps;
                float _BorderThickness;
                float _ToneThickness;
                float _Softness;
                float _SlashOpacity;
                float _Progress;
                float _ArcWidth;
                float _DistortStrength;
            CBUFFER_END

            TEXTURE2D(_LeftRightTex);
            SAMPLER(sampler_LeftRightTex);

            TEXTURE2D(_TopBotTex);
            SAMPLER(sampler_TopBotTex);

            TEXTURE2D(_DistortTex);
            SAMPLER(sampler_DistortTex);

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

                // Noise distortion
                float2 noiseUV = TRANSFORM_TEX(uv, _DistortTex) + float2(_Progress * 0.4, 0);
                float noise = SAMPLE_TEXTURE2D(_DistortTex, sampler_DistortTex, noiseUV).r;
                uv += (noise - 0.5) * _DistortStrength;

                // Arc sweep based on Progress (inverted for right-to-left scythe swing)
                float arcCoord = 1.0 - uv.y;
                float head = _Progress * (1.0 + _ArcWidth);
                float tail = head - _ArcWidth;
                
                float leadAlpha = smoothstep(head, head - _Softness, arcCoord);
                float trailAlpha = smoothstep(tail, tail + _Softness, arcCoord);
                float arcAlpha = saturate(leadAlpha * trailAlpha);

                // Sample texture profiles
                float4 lr = SAMPLE_TEXTURE2D(_LeftRightTex, sampler_LeftRightTex, TRANSFORM_TEX(uv, _LeftRightTex));
                float4 tb = SAMPLE_TEXTURE2D(_TopBotTex, sampler_TopBotTex, TRANSFORM_TEX(uv, _TopBotTex));

                float radialFade = tb.r * lr.r;
                float combinedMask = arcAlpha * radialFade * _SlashOpacity;

                clip(combinedMask - 0.02);

                if (_EnableToonSteps > 0.5)
                {
                    // Sharp cel-shaded toon bands (Anime / Cel-shaded look)
                    float borderMask = smoothstep(0.04, 0.04 + _Softness, combinedMask);
                    float toneMask = smoothstep(0.04 + _BorderThickness, 0.04 + _BorderThickness + _Softness, combinedMask);
                    float coreMask = smoothstep(0.04 + _BorderThickness + _ToneThickness, 0.04 + _BorderThickness + _ToneThickness + _Softness, combinedMask);

                    float3 finalRgb = _BorderColor.rgb;
                    finalRgb = lerp(finalRgb, _ToneColor.rgb, toneMask);
                    finalRgb = lerp(finalRgb, _ColorMain.rgb, coreMask);

                    float finalAlpha = borderMask * _SlashOpacity;
                    return float4(finalRgb, saturate(finalAlpha));
                }
                else
                {
                    // Smooth gradient mode
                    float borderMask = smoothstep(0.04, 0.04 + _Softness, combinedMask);
                    float coreMask = smoothstep(0.04 + _BorderThickness, 0.04 + _BorderThickness + _Softness, combinedMask);

                    float3 finalRgb = lerp(_BorderColor.rgb, _ColorMain.rgb, coreMask);
                    float finalAlpha = borderMask * _SlashOpacity;
                    return float4(finalRgb, saturate(finalAlpha));
                }
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
