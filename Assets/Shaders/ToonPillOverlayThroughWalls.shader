Shader "Toon/PillOverlayThroughWalls"
{
    Properties
    {
        _BaseColor ("Badge Background Color", Color) = (0.02, 0.05, 0.08, 0.60)
        _BorderColor ("Border Glow Color", Color) = (0.20, 0.72, 0.96, 0.45)
        _BorderThickness ("Border Thickness", Range(0.0, 0.2)) = 0.04
    }

    SubShader
    {
        Tags 
        { 
            "Queue"="Transparent+490" 
            "IgnoreProjector"="True" 
            "RenderType"="Transparent" 
            "RenderPipeline"="UniversalPipeline"
        }

        Pass
        {
            Name "PillOverlayThroughWalls"
            Tags { "LightMode" = "UniversalForward" }

            Cull Off
            ZWrite Off
            ZTest Always
            Blend SrcAlpha OneMinusSrcAlpha

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uv         : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            CBUFFER_START(UnityPerMaterial)
                float4 _BaseColor;
                float4 _BorderColor;
                float _BorderThickness;
            CBUFFER_END

            Varyings vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_TRANSFER_INSTANCE_ID(input, output);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);

                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                output.uv = input.uv;
                return output;
            }

            half4 frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);

                float2 uv = input.uv;
                float edgeX = min(uv.x, 1.0 - uv.x);
                float edgeY = min(uv.y, 1.0 - uv.y);
                float distEdge = min(edgeX, edgeY);

                half4 col = _BaseColor;
                if (distEdge < _BorderThickness)
                {
                    float factor = 1.0 - (distEdge / _BorderThickness);
                    col = lerp(_BaseColor, _BorderColor, factor * 0.75);
                }

                return col;
            }
            ENDHLSL
        }
    }
}
