Shader "Toon/OverwatchAllySilhouette"
{
    Properties
    {
        [Header(Overwatch Ally Solid Color)]
        _SilhouetteColor ("Silhouette Solid Color", Color) = (0.20, 0.72, 0.96, 0.88)
    }

    SubShader
    {
        Tags 
        { 
            "RenderType" = "Transparent" 
            "Queue" = "Transparent+300" 
            "RenderPipeline" = "UniversalPipeline" 
            "IgnoreProjector" = "True"
        }

        Pass
        {
            Name "OverwatchSolidSilhouette"
            Tags { "LightMode" = "UniversalForward" }

            Cull Back
            ZWrite Off
            ZTest Greater
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _SilhouetteColor;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);

                // Aplica um leve viés de profundidade para garantir estabilidade absoluta:
                // O clone fica ligeiramente mais próximo da câmera que a malha normal do personagem,
                // de modo que o ZTest Greater NUNCA passe contra a própria malha (eliminando qualquer z-fighting),
                // mas passe com folga atrás de qualquer parede ou obstáculo no cenário.
                #if UNITY_REVERSED_Z
                    output.positionCS.z += 0.0005;
                #else
                    output.positionCS.z -= 0.0005;
                #endif

                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                return _SilhouetteColor;
            }
            ENDHLSL
        }
    }
}
