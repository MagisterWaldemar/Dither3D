/*
 * Copyright (c) 2025 Rune Skovbo Johansen
 *
 * This Source Code Form is subject to the terms of the Mozilla Public
 * License, v. 2.0. If a copy of the MPL was not distributed with this
 * file, You can obtain one at https://mozilla.org/MPL/2.0/.
 */

Shader "Dither 3D/URP/Opaque"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _EmissionMap ("Emission", 2D) = "white" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0

        [Header(Dither Input Brightness)]
        _InputExposure ("Exposure", Range(0,5)) = 1
        _InputOffset ("Offset", Range(-1,1)) = 0

        [Header(Dither Settings)]
        [DitherPatternProperty] _DitherMode ("Pattern", Int) = 3
        [HideInInspector] _DitherTex ("Dither 3D Texture", 3D) = "" {}
        [HideInInspector] _DitherRampTex ("Dither Ramp Texture", 2D) = "white" {}
        _Scale ("Dot Scale", Range(2,10)) = 5.0
        _SizeVariability ("Dot Size Variability", Range(0,1)) = 0
        _Contrast ("Dot Contrast", Range(0,2)) = 1
        _StretchSmoothness ("Stretch Smoothness", Range(0,2)) = 1
        [Space]
        [Header(Blue Noise Fractal (Optional))]
        [Enum(Bayer,0,BlueNoiseFractal,1)] _DitherPatternSource ("Pattern Source", Float) = 0
        _BlueNoiseRankTex ("Blue Noise Rank Texture", 2D) = "gray" {}
        _BlueNoisePhaseTex ("Blue Noise Phase Texture (Optional)", 2D) = "black" {}
        _BlueNoisePhaseSpeed ("Blue Noise Phase Speed", Range(0,1)) = 0.15
        _BlueNoiseHysteresis ("Blue Noise Hysteresis", Range(0,1)) = 0.8
        _BlueNoiseMinDot ("Blue Noise Min Dot", Range(0,1)) = 0.12
        [Space]
        [Header(Pointillism (Optional))]
        [Toggle] _PointillismEnable ("Enable Pointillism", Float) = 0
        _PointillismDirectionality ("Stroke Directionality", Range(0,1)) = 0.5
        _PointillismStrokeLength ("Stroke Length", Range(0,1)) = 0.4
        _PointillismColorSteps ("Color Steps", Range(2,32)) = 8
        [Enum(UV,0,AltUVHook,1,ObjectSpace,2,TriplanarObjectSpace,3)] _PointillismCoordSource ("Pointillism Coord Source", Float) = 0
        _PointillismObjectScale ("Pointillism Object Scale", Range(0.1,32)) = 1
        _PointillismTriplanarSharpness ("Pointillism Triplanar Sharpness", Range(1,8)) = 4
        _PointillismClampMinColor ("Clamp Min Color", Color) = (0,0,0,0)
        _PointillismClampMaxColor ("Clamp Max Color", Color) = (1,1,1,1)
        _PointillismLUTTex ("Pointillism LUT (Optional)", 2D) = "white" {}
        _PointillismLUTBlend ("Pointillism LUT Blend", Range(0,1)) = 0
    }

    SubShader
    {
        Tags { "RenderPipeline"="UniversalPipeline" "RenderType"="Opaque" "Queue"="Geometry" }
        LOD 300

        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode"="UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #pragma multi_compile __ DITHERCOL_GRAYSCALE DITHERCOL_RGB DITHERCOL_CMYK
            #pragma multi_compile __ INVERSE_DOTS
            #pragma multi_compile __ RADIAL_COMPENSATION
            #pragma multi_compile __ QUANTIZE_LAYERS
            #pragma multi_compile __ DEBUG_FRACTAL

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
            #include "Dither3DInclude.cginc"

            TEXTURE2D(_MainTex);
            SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);
            TEXTURE2D(_EmissionMap);
            SAMPLER(sampler_EmissionMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _EmissionColor;
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
                float4 _EmissionMap_ST;
                float _Glossiness;
                float _Metallic;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uvMain : TEXCOORD0;
                float2 uvBump : TEXCOORD1;
                float2 uvEmission : TEXCOORD2;
                float2 uvDither : TEXCOORD3;
                float3 worldPos : TEXCOORD4;
                float3 normalWS : TEXCOORD5;
                float4 tangentWS : TEXCOORD6;
                float4 screenPos : TEXCOORD7;
                float fogFactor : TEXCOORD8;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                output.positionCS = positionInputs.positionCS;
                output.worldPos = positionInputs.positionWS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());

                output.uvMain = TRANSFORM_TEX(input.uv, _MainTex);
                output.uvBump = TRANSFORM_TEX(input.uv, _BumpMap);
                output.uvEmission = TRANSFORM_TEX(input.uv, _EmissionMap);
                output.uvDither = input.uv;
                output.screenPos = ComputeScreenPos(positionInputs.positionCS);
                output.fogFactor = ComputeFogFactor(positionInputs.positionCS.z);
                return output;
            }

            half3 SampleNormalWS(Varyings input)
            {
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uvBump), 1.0);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS = normalize(cross(input.normalWS, tangentWS) * input.tangentWS.w);
                half3 normalWS = TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalize(input.normalWS)));
                return normalize(normalWS);
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half4 albedoSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, input.uvMain) * _Color;
                half3 normalWS = SampleNormalWS(input);
                half3 emission = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, input.uvEmission).rgb * _EmissionColor.rgb;

                half3 viewDirWS = SafeNormalize(GetWorldSpaceViewDir(input.worldPos));
                float4 shadowCoord = TransformWorldToShadowCoord(input.worldPos);
                Light mainLight = GetMainLight(shadowCoord);

                half3 albedo = albedoSample.rgb;
                half metallic = saturate(_Metallic);
                half smoothness = saturate(_Glossiness);
                half perceptualRoughness = 1.0h - smoothness;
                half specPower = exp2(10.0h * (1.0h - perceptualRoughness) + 1.0h);
                half3 f0 = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metallic);
                half oneMinusReflectivity = 1.0h - metallic;

                half3 color = SampleSH(normalWS) * albedo * oneMinusReflectivity;

                half3 lightDir = normalize(mainLight.direction);
                half ndotl = saturate(dot(normalWS, lightDir));
                half3 halfDir = SafeNormalize(lightDir + viewDirWS);
                half ndoth = saturate(dot(normalWS, halfDir));
                half3 diffuse = albedo * oneMinusReflectivity * ndotl;
                half3 specular = f0 * pow(ndoth, specPower) * ndotl;
                color += (diffuse + specular) * mainLight.color * (mainLight.distanceAttenuation * mainLight.shadowAttenuation);

                #if defined(_ADDITIONAL_LIGHTS)
                uint additionalLightsCount = GetAdditionalLightsCount();
                for (uint i = 0u; i < additionalLightsCount; i++)
                {
                    Light light = GetAdditionalLight(i, input.worldPos);
                    half3 addLightDir = normalize(light.direction);
                    half addNdotL = saturate(dot(normalWS, addLightDir));
                    half3 addHalfDir = SafeNormalize(addLightDir + viewDirWS);
                    half addNdotH = saturate(dot(normalWS, addHalfDir));
                    half3 addDiffuse = albedo * oneMinusReflectivity * addNdotL;
                    half3 addSpecular = f0 * pow(addNdotH, specPower) * addNdotL;
                    color += (addDiffuse + addSpecular) * light.color * (light.distanceAttenuation * light.shadowAttenuation);
                }
                #endif

                color += emission;
                color = MixFog(color, input.fogFactor);

                fixed4 finalColor = fixed4(color, albedoSample.a);
                return GetDither3DColorWorld(input.uvDither, input.uvMain, input.worldPos, normalWS, input.screenPos, finalColor);
            }
            ENDHLSL
        }

        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode"="ShadowCaster" }
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS);
                float3 positionWS = ApplyShadowBias(positionInputs.positionWS, normalInputs.normalWS, _MainLightPosition.xyz);
                output.positionCS = TransformWorldToHClip(positionWS);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode"="DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct Attributes
            {
                float4 positionOS : POSITION;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                output.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                return 0;
            }
            ENDHLSL
        }

        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode"="DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex Vert
            #pragma fragment Frag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _BumpMap_ST;
            CBUFFER_END

            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS : NORMAL;
                float4 tangentOS : TANGENT;
                float2 uv : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS : TEXCOORD0;
                float4 tangentWS : TEXCOORD1;
                float2 uvBump : TEXCOORD2;
            };

            Varyings Vert(Attributes input)
            {
                Varyings output;
                VertexPositionInputs positionInputs = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs normalInputs = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                output.positionCS = positionInputs.positionCS;
                output.normalWS = normalInputs.normalWS;
                output.tangentWS = float4(normalInputs.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                output.uvBump = TRANSFORM_TEX(input.uv, _BumpMap);
                return output;
            }

            half4 Frag(Varyings input) : SV_Target
            {
                half3 normalTS = UnpackNormalScale(SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, input.uvBump), 1.0);
                half3 tangentWS = normalize(input.tangentWS.xyz);
                half3 bitangentWS = normalize(cross(input.normalWS, tangentWS) * input.tangentWS.w);
                half3 normalWS = normalize(TransformTangentToWorld(normalTS, half3x3(tangentWS, bitangentWS, normalize(input.normalWS))));
                return half4(normalWS * 0.5h + 0.5h, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
