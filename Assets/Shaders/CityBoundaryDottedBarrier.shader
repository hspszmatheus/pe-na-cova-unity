Shader "Custom/CityBoundaryDottedBarrier"
{
    Properties
    {
        [Header(Colors)]
        [HDR] _DotColor ("Dot Color", Color) = (0.75, 0.75, 0.82, 1.0)
        _BaseColor ("Backdrop Color", Color) = (0.06, 0.06, 0.08, 0.06)
        [HDR] _BorderGlowColor ("Border Glow Color", Color) = (0.68, 0.68, 0.74, 0.35)

        [Header(Fine Dot Grid Pattern)]
        _DotSpacing ("Dot Spacing Meters", Float) = 0.45
        _DotRadius ("Dot Radius", Range(0.03, 0.35)) = 0.10
        _DotSmoothness ("Dot Smoothness", Range(0.01, 0.10)) = 0.03

        [Header(Proximity and Animation)]
        _ProximityMinDist ("Full Alpha Distance", Float) = 3.5
        _ProximityMaxDist ("Start Fade Distance", Float) = 14.0
        _AlphaMultiplier ("Per Segment Alpha", Range(0.0, 1.0)) = 1.0
        _PulseSpeed ("Subtle Pulse Speed", Float) = 1.8
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
            Name "CityBoundaryBarrierPass"
            Tags { "LightMode" = "UniversalForward" }

            Blend SrcAlpha OneMinusSrcAlpha
            ZWrite Off
            ZTest LEqual
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #pragma multi_compile_instancing
            #pragma target 3.5

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                half4 _DotColor;
                half4 _BaseColor;
                half4 _BorderGlowColor;
                float _DotSpacing;
                float _DotRadius;
                float _DotSmoothness;
                float _ProximityMinDist;
                float _ProximityMaxDist;
                float _AlphaMultiplier;
                float _PulseSpeed;
            CBUFFER_END

            // Posicao 3D global do jogador local (atualizada por CityBoundaryVisualController)
            float4 _BoundaryLocalPlayerPos;

            struct Attributes
            {
                float4 positionOS : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct Varyings
            {
                float4 positionHCS : SV_POSITION;
                float2 uvInMeters : TEXCOORD0;
                float3 worldPos : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            Varyings vert(Attributes IN)
            {
                Varyings OUT = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_TRANSFER_INSTANCE_ID(IN, OUT);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(OUT);

                OUT.worldPos = TransformObjectToWorld(IN.positionOS.xyz);
                OUT.positionHCS = TransformWorldToHClip(OUT.worldPos);
                OUT.uvInMeters = IN.uv;
                return OUT;
            }

            half4 frag(Varyings IN) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(IN);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(IN);

                // 1. Proximidade 3D esferica real com o jogador local (chao ou voo no ceu)
                float distToPlayer = distance(IN.worldPos.xyz, _BoundaryLocalPlayerPos.xyz);
                float proxRange = max(0.01, _ProximityMaxDist - _ProximityMinDist);
                float proxAlpha = 1.0 - saturate((distToPlayer - _ProximityMinDist) / proxRange);

                float combinedAlpha = proxAlpha * _AlphaMultiplier;
                if (combinedAlpha <= 0.005)
                {
                    discard;
                }

                // 2. Grade de bolinhas finas e uniformes mapeadas em metros mundiais
                float spacing = max(0.05, _DotSpacing);
                float2 grid = frac(IN.uvInMeters / spacing) - 0.5;
                float cellDist = length(grid);
                float dotMask = 1.0 - smoothstep(_DotRadius - _DotSmoothness, _DotRadius + _DotSmoothness, cellDist);

                // 3. Fade esferico suave na borda do raio de proximidade
                float sphericalFade = smoothstep(0.0, 0.25, proxAlpha);

                // 4. Pulsacao sutil
                float pulse = 0.92 + 0.08 * sin(_Time.y * _PulseSpeed + (IN.worldPos.x + IN.worldPos.z) * 0.5);

                // 5. Composicao de cor cinza prateado padrao de task
                half4 col = lerp(_BaseColor, _DotColor * pulse, dotMask);
                col.rgb += _BorderGlowColor.rgb * (dotMask * 0.35);

                float finalAlpha = (col.a + dotMask * 0.95) * combinedAlpha * sphericalFade;
                col.a = saturate(finalAlpha);

                return col;
            }
            ENDHLSL
        }
    }
}
