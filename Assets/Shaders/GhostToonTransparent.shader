Shader "Toon/GhostToonTransparent"
{
    Properties
    {
        [Header(Base Textures)]
        _MainTex ("Texture Sample", 2D) = "white" {}
        _TextureSample ("Texture Sample (Alt)", 2D) = "white" {}
        _BaseMap ("Base Map", 2D) = "white" {}
        
        [Header(Colors and Alpha)]
        _BaseColor ("Base Color Tint", Color) = (0.9, 0.95, 1.0, 1.0)
        _CharacterAlpha ("Ghost Transparency", Range(0.1, 1.0)) = 0.40
        
        [Header(Toon Shading)]
        _RampThreshold ("Cel Shadow Threshold", Range(0.0, 1.0)) = 0.40
        _RampSmooth ("Cel Shadow Smoothness", Range(0.001, 0.2)) = 0.05
        _ShadowIntensity ("Shadow Multiplier", Range(0.0, 1.0)) = 0.60
        
        [Header(Ghost Toon Rim Glow)]
        _RimColor ("Ghost Rim Color", Color) = (0.45, 0.85, 1.0, 1.0)
        _RimPower ("Ghost Rim Power", Range(0.5, 6.0)) = 2.5
        _RimIntensity ("Ghost Rim Intensity", Range(0.0, 3.0)) = 1.2
        _RimThreshold ("Toon Rim Step", Range(0.1, 0.9)) = 0.45
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent+100" 
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "GhostUniversalForward"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 positionWS : TEXCOORD0;
                float3 normalWS : TEXCOORD1;
                float2 uv : TEXCOORD2;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_TextureSample);
            SAMPLER(sampler_TextureSample);
            TEXTURE2D(_BaseMap);
            SAMPLER(sampler_BaseMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _RimColor;
                float _CharacterAlpha;
                float _RampThreshold;
                float _RampSmooth;
                float _ShadowIntensity;
                float _RimPower;
                float _RimIntensity;
                float _RimThreshold;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float3 normalWS = normalize(input.normalWS);
                float3 viewDirWS = SafeNormalize(GetCameraPositionWS() - input.positionWS);

                // Amostra de textura albedo
                float4 texColor = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uv);
                if (texColor.r < 0.001 && texColor.g < 0.001 && texColor.b < 0.001 && texColor.a < 0.001)
                {
                    texColor = SAMPLE_TEXTURE2D(_TextureSample, sampler_TextureSample, input.uv);
                }

                // Cel-Shading Toon com degrau suave
                Light mainLight = GetMainLight();
                float NdotL = saturate(dot(normalWS, mainLight.direction));
                float toonLight = smoothstep(_RampThreshold - _RampSmooth, _RampThreshold + _RampSmooth, NdotL);
                float shadowFactor = lerp(_ShadowIntensity, 1.0, toonLight);

                // Iluminação difusa Toon e ambiente
                float3 ambient = SampleSH(normalWS) * 0.40;
                float3 diffuseLight = (mainLight.color * shadowFactor) + ambient;

                // Contorno / Rim Toon espectral (efeito cartoon nos limites da silhueta)
                float NdotV = 1.0 - saturate(dot(normalWS, viewDirWS));
                float toonRimFactor = smoothstep(_RimThreshold - 0.08, _RimThreshold + 0.08, NdotV);
                float rimPowerFactor = pow(NdotV, _RimPower);
                float rimFinal = saturate(toonRimFactor * 0.75 + rimPowerFactor * 0.45) * _RimIntensity;
                float3 rimEffect = _RimColor.rgb * rimFinal;

                // Cor Toon final
                float3 albedo = texColor.rgb * _BaseColor.rgb;
                float3 finalRgb = albedo * diffuseLight + rimEffect;

                // Detecção de detalhes escuros faciais (olhos e boca do fantasma)
                float darkDetail = 1.0 - saturate((texColor.r + texColor.g + texColor.b) / 1.5);
                
                // Transparência equilibrada: corpo visivelmente translúcido (meio transparente),
                // com os olhos mais sólidos e a borda toon nítida
                float baseAlpha = lerp(_CharacterAlpha, 0.85, darkDetail);
                float finalAlpha = saturate(baseAlpha + toonRimFactor * 0.25);

                return half4(finalRgb, finalAlpha);
            }
            ENDHLSL
        }
    }
    FallBack "Hidden/Universal Render Pipeline/FallbackError"
}
