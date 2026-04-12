/*
 * Painterly Pointillist — URP opaque surface shader.
 *
 * Replaces the original Dither3D dithering pipeline with a multi-layer
 * pointillist composition that uses the same surface-stable Bayer fractal
 * textures but produces a painterly look with far fewer parameters.
 *
 * Compatible with Unity 6 / URP 17+.
 * Uses the existing DitherPatternProperty editor drawer for pattern selection.
 */

Shader "Dither 3D/URP/Painterly Pointillist"
{
    Properties
    {
        // ── Surface ──────────────────────────────────────────────
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo", 2D) = "white" {}
        _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Scale", Range(0,2)) = 1.0
        _EmissionMap ("Emission", 2D) = "black" {}
        _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        _Smoothness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0

        // ── Dot Placement ────────────────────────────────────────
        [Header(Dot Placement)]
        [DitherPatternProperty] _DitherMode ("Pattern", Int) = 3
        [HideInInspector] _DitherTex ("Dither 3D Texture", 3D) = "" {}
        [HideInInspector] _DitherRampTex ("Dither Ramp Texture", 2D) = "white" {}
        _DotScale ("Dot Scale", Range(2,10)) = 5.0
        _DotSharpness ("Dot Sharpness", Range(0.2,3.0)) = 1.2

        // ── Painterly Style ──────────────────────────────────────
        [Header(Painterly Style)]
        _Exposure ("Exposure", Range(0.2,3.0)) = 1.0
        _HueShift ("Accent Hue Shift", Range(0,0.2)) = 0.0
        _Chroma ("Chroma", Range(0.5,2.0)) = 1.2
        _ValueSpread ("Value Spread", Range(0,0.8)) = 0.3
        _WarmCool ("Shadow Warmth", Range(-0.5,0.5)) = 0.05
        _AccentAmount ("Accent Amount", Range(0,1)) = 0.15
        _CanvasColor ("Canvas Color", Color) = (0.95, 0.93, 0.88, 1)
        _CanvasShow ("Canvas Visibility", Range(0,1)) = 0.3
        _Impasto ("Impasto", Range(0,1)) = 0.25
    }

    SubShader
    {
        Tags
        {
            "RenderPipeline" = "UniversalPipeline"
            "RenderType"     = "Opaque"
            "Queue"          = "Geometry"
        }
        LOD 300

        // ═════════════════════════════════════════════════════════
        //  FORWARD LIT
        // ═════════════════════════════════════════════════════════
        Pass
        {
            Name "ForwardLit"
            Tags { "LightMode" = "UniversalForward" }

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   Vert
            #pragma fragment Frag

            #pragma multi_compile_fog
            #pragma multi_compile _ _MAIN_LIGHT_SHADOWS _MAIN_LIGHT_SHADOWS_CASCADE _MAIN_LIGHT_SHADOWS_SCREEN
            #pragma multi_compile _ _ADDITIONAL_LIGHTS_VERTEX _ADDITIONAL_LIGHTS
            #pragma multi_compile_fragment _ _ADDITIONAL_LIGHT_SHADOWS
            #pragma multi_compile_fragment _ _SHADOWS_SOFT

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"

            // ── Surface textures ─────────────────────────────────
            TEXTURE2D(_MainTex);      SAMPLER(sampler_MainTex);
            TEXTURE2D(_BumpMap);      SAMPLER(sampler_BumpMap);
            TEXTURE2D(_EmissionMap);  SAMPLER(sampler_EmissionMap);

            // ── SRP-Batcher constant buffer ──────────────────────
            // Every pass must declare the identical layout.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
                float4 _EmissionMap_ST;
                float4 _EmissionColor;
                float  _Smoothness;
                float  _Metallic;
                float  _BumpScale;
                float  _DitherMode;
                float  _DotScale;
                float  _DotSharpness;
                float  _Exposure;
                float  _HueShift;
                float  _Chroma;
                float  _ValueSpread;
                float  _WarmCool;
                float  _AccentAmount;
                float4 _CanvasColor;
                float  _CanvasShow;
                float  _Impasto;
            CBUFFER_END

            // Painterly core (textures + functions).
            #include "PainterlyCore.hlsl"

            // ── Vertex / fragment data ───────────────────────────
            struct Attributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct Varyings
            {
                float4 positionCS : SV_POSITION;
                float2 uvMain     : TEXCOORD0;
                float2 uvBump     : TEXCOORD1;
                float2 uvEmission : TEXCOORD2;
                float2 uvDither   : TEXCOORD3;
                float3 worldPos   : TEXCOORD4;
                float3 normalWS   : TEXCOORD5;
                float4 tangentWS  : TEXCOORD6;
                float  fogFactor  : TEXCOORD7;
            };

            Varyings Vert(Attributes input)
            {
                Varyings o;
                VertexPositionInputs posIn = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nIn  = GetVertexNormalInputs(input.normalOS, input.tangentOS);

                o.positionCS = posIn.positionCS;
                o.worldPos   = posIn.positionWS;
                o.normalWS   = nIn.normalWS;
                o.tangentWS  = float4(nIn.tangentWS, input.tangentOS.w * GetOddNegativeScale());

                o.uvMain     = TRANSFORM_TEX(input.uv, _MainTex);
                o.uvBump     = TRANSFORM_TEX(input.uv, _BumpMap);
                o.uvEmission = TRANSFORM_TEX(input.uv, _EmissionMap);
                o.uvDither   = input.uv;
                o.fogFactor  = ComputeFogFactor(posIn.positionCS.z);
                return o;
            }

            half3 SampleNormalWS(Varyings i)
            {
                half3 nTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uvBump),
                    _BumpScale);
                half3 T = normalize(i.tangentWS.xyz);
                half3 B = normalize(cross(i.normalWS, T) * i.tangentWS.w);
                half3 N = normalize(i.normalWS);
                return normalize(TransformTangentToWorld(nTS, half3x3(T, B, N)));
            }

            half4 Frag(Varyings i) : SV_Target
            {
                // ── Sample surface ───────────────────────────────
                half4 albedoSample = SAMPLE_TEXTURE2D(_MainTex, sampler_MainTex, i.uvMain) * _Color;
                half3 normalWS     = SampleNormalWS(i);
                half3 emission     = SAMPLE_TEXTURE2D(_EmissionMap, sampler_EmissionMap, i.uvEmission).rgb
                                   * _EmissionColor.rgb;

                // ── URP PBR lighting ─────────────────────────────
                half3  viewDir  = SafeNormalize(GetWorldSpaceViewDir(i.worldPos));
                float4 sCrd     = TransformWorldToShadowCoord(i.worldPos);
                Light  mainLight = GetMainLight(sCrd);

                half3 albedo = albedoSample.rgb;
                half  metal  = saturate(_Metallic);
                half  oneMinR = 1.0h - metal;
                half  rough  = 1.0h - saturate(_Smoothness);
                half  specPw = exp2(10.0h * (1.0h - rough) + 1.0h);
                half3 f0     = lerp(half3(0.04h, 0.04h, 0.04h), albedo, metal);

                // Ambient (spherical harmonics).
                half3 color = SampleSH(normalWS) * albedo * oneMinR;

                // Main directional light.
                half3 L     = normalize(mainLight.direction);
                half  NdotL = saturate(dot(normalWS, L));
                half3 H     = SafeNormalize(L + viewDir);
                half  NdotH = saturate(dot(normalWS, H));
                half  shadow = mainLight.distanceAttenuation * mainLight.shadowAttenuation;
                color += (albedo * oneMinR * NdotL
                        + f0 * pow(NdotH, specPw) * NdotL)
                       * mainLight.color * shadow;

                // Additional lights.
                #if defined(_ADDITIONAL_LIGHTS)
                uint lightCount = GetAdditionalLightsCount();
                for (uint li = 0u; li < lightCount; li++)
                {
                    Light aLight = GetAdditionalLight(li, i.worldPos);
                    half3 aL     = normalize(aLight.direction);
                    half  aNdotL = saturate(dot(normalWS, aL));
                    half3 aH     = SafeNormalize(aL + viewDir);
                    half  aNdotH = saturate(dot(normalWS, aH));
                    half  aSh    = aLight.distanceAttenuation * aLight.shadowAttenuation;
                    color += (albedo * oneMinR * aNdotL
                            + f0 * pow(aNdotH, specPw) * aNdotL)
                           * aLight.color * aSh;
                }
                #endif

                color += emission;

                // Reinhard tone-map into [0,1] before painterly processing.
                color = color / (1.0 + color);

                // ── Painterly composition ────────────────────────
                float2 dxUV = ddx(i.uvDither);
                float2 dyUV = ddy(i.uvDither);
                half3 result = PainterlyComposite(i.uvDither, dxUV, dyUV, color);

                // Fog on final result.
                result = MixFog(result, i.fogFactor);

                return half4(result, albedoSample.a);
            }
            ENDHLSL
        }

        // ═════════════════════════════════════════════════════════
        //  SHADOW CASTER
        // ═════════════════════════════════════════════════════════
        Pass
        {
            Name "ShadowCaster"
            Tags { "LightMode" = "ShadowCaster" }
            ZWrite On
            ZTest LEqual

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   ShadowVert
            #pragma fragment ShadowFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Shadows.hlsl"

            // Identical CBUFFER for SRP Batcher.
            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
                float4 _EmissionMap_ST;
                float4 _EmissionColor;
                float  _Smoothness;
                float  _Metallic;
                float  _BumpScale;
                float  _DitherMode;
                float  _DotScale;
                float  _DotSharpness;
                float  _Exposure;
                float  _HueShift;
                float  _Chroma;
                float  _ValueSpread;
                float  _WarmCool;
                float  _AccentAmount;
                float4 _CanvasColor;
                float  _CanvasShow;
                float  _Impasto;
            CBUFFER_END

            struct ShadowAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
            };

            struct ShadowVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            ShadowVaryings ShadowVert(ShadowAttributes input)
            {
                ShadowVaryings o;
                VertexPositionInputs posIn = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nIn  = GetVertexNormalInputs(input.normalOS);
                o.positionCS = TransformWorldToHClip(
                    ApplyShadowBias(posIn.positionWS, nIn.normalWS, _MainLightPosition.xyz));
                return o;
            }

            half4 ShadowFrag(ShadowVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // ═════════════════════════════════════════════════════════
        //  DEPTH ONLY
        // ═════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthOnly"
            Tags { "LightMode" = "DepthOnly" }
            ZWrite On
            ColorMask 0

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   DepthVert
            #pragma fragment DepthFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
                float4 _EmissionMap_ST;
                float4 _EmissionColor;
                float  _Smoothness;
                float  _Metallic;
                float  _BumpScale;
                float  _DitherMode;
                float  _DotScale;
                float  _DotSharpness;
                float  _Exposure;
                float  _HueShift;
                float  _Chroma;
                float  _ValueSpread;
                float  _WarmCool;
                float  _AccentAmount;
                float4 _CanvasColor;
                float  _CanvasShow;
                float  _Impasto;
            CBUFFER_END

            struct DepthAttributes
            {
                float4 positionOS : POSITION;
            };

            struct DepthVaryings
            {
                float4 positionCS : SV_POSITION;
            };

            DepthVaryings DepthVert(DepthAttributes input)
            {
                DepthVaryings o;
                o.positionCS = TransformObjectToHClip(input.positionOS.xyz);
                return o;
            }

            half4 DepthFrag(DepthVaryings input) : SV_Target { return 0; }
            ENDHLSL
        }

        // ═════════════════════════════════════════════════════════
        //  DEPTH NORMALS
        // ═════════════════════════════════════════════════════════
        Pass
        {
            Name "DepthNormals"
            Tags { "LightMode" = "DepthNormals" }
            ZWrite On

            HLSLPROGRAM
            #pragma target 3.5
            #pragma vertex   DNVert
            #pragma fragment DNFrag

            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            TEXTURE2D(_BumpMap);
            SAMPLER(sampler_BumpMap);

            CBUFFER_START(UnityPerMaterial)
                float4 _Color;
                float4 _MainTex_ST;
                float4 _BumpMap_ST;
                float4 _EmissionMap_ST;
                float4 _EmissionColor;
                float  _Smoothness;
                float  _Metallic;
                float  _BumpScale;
                float  _DitherMode;
                float  _DotScale;
                float  _DotSharpness;
                float  _Exposure;
                float  _HueShift;
                float  _Chroma;
                float  _ValueSpread;
                float  _WarmCool;
                float  _AccentAmount;
                float4 _CanvasColor;
                float  _CanvasShow;
                float  _Impasto;
            CBUFFER_END

            struct DNAttributes
            {
                float4 positionOS : POSITION;
                float3 normalOS   : NORMAL;
                float4 tangentOS  : TANGENT;
                float2 uv         : TEXCOORD0;
            };

            struct DNVaryings
            {
                float4 positionCS : SV_POSITION;
                float3 normalWS   : TEXCOORD0;
                float4 tangentWS  : TEXCOORD1;
                float2 uvBump     : TEXCOORD2;
            };

            DNVaryings DNVert(DNAttributes input)
            {
                DNVaryings o;
                VertexPositionInputs posIn = GetVertexPositionInputs(input.positionOS.xyz);
                VertexNormalInputs   nIn  = GetVertexNormalInputs(input.normalOS, input.tangentOS);
                o.positionCS = posIn.positionCS;
                o.normalWS   = nIn.normalWS;
                o.tangentWS  = float4(nIn.tangentWS, input.tangentOS.w * GetOddNegativeScale());
                o.uvBump     = TRANSFORM_TEX(input.uv, _BumpMap);
                return o;
            }

            half4 DNFrag(DNVaryings i) : SV_Target
            {
                half3 nTS = UnpackNormalScale(
                    SAMPLE_TEXTURE2D(_BumpMap, sampler_BumpMap, i.uvBump),
                    _BumpScale);
                half3 T = normalize(i.tangentWS.xyz);
                half3 B = normalize(cross(i.normalWS, T) * i.tangentWS.w);
                half3 N = normalize(TransformTangentToWorld(nTS,
                    half3x3(T, B, normalize(i.normalWS))));
                return half4(N * 0.5h + 0.5h, 1.0h);
            }
            ENDHLSL
        }
    }

    FallBack Off
}
