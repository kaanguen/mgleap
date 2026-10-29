Shader "OxygenPickups/BlueBubble"
{
    Properties
    {
        _BaseColor ("Blue", Color) = (.015,.075,.8,1)
        _RimColor ("Rim", Color) = (.12,.55,1,1)
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "RenderPipeline"="UniversalPipeline" }
        Pass
        {
            Tags { "LightMode"="SRPDefaultUnlit" }
            HLSLPROGRAM
            #pragma vertex Vert
            #pragma fragment Frag
            #pragma multi_compile_instancing
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            CBUFFER_START(UnityPerMaterial)
            half4 _BaseColor;
            half4 _RimColor;
            CBUFFER_END
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };
            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float3 positionWS : TEXCOORD1;
                UNITY_VERTEX_OUTPUT_STEREO
            };
            Varyings Vert(Attributes input)
            {
                Varyings output = (Varyings)0;
                UNITY_SETUP_INSTANCE_ID(input);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(output);
                output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
                output.positionCS = TransformWorldToHClip(output.positionWS);
                output.normalWS = TransformObjectToWorldNormal(input.normalOS);
                return output;
            }
            half4 Frag(Varyings input) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(input);
                half3 n = normalize(input.normalWS);
                half3 view = GetWorldSpaceNormalizeViewDir(input.positionWS);
                half rim = pow(1 - saturate(dot(n, view)), 2.8);
                half3 nView = normalize(TransformWorldToViewDir(n));
                half3 viewDir = normalize(TransformWorldToViewDir(view));
                half3 lightDir = normalize(half3(-.4,.8,1));
                half light = saturate(dot(nView, lightDir));
                half gloss = pow(saturate(dot(reflect(-lightDir, nView), viewDir)), 48);
                return half4(_BaseColor.rgb * (.7 + light * .6) + _RimColor.rgb * rim * 1.1 + gloss * .65, 1);
            }
            ENDHLSL
        }
    }
}
