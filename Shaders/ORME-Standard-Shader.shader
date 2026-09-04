// ORME-Standard-Shader.shader
//
// Implements a standard shader with support for ORME combined
// Occlusion, Roughness, Metallic, and Emission maps.

Shader "Lilithe/ORME-Standard-Shader"
{
    Properties
    {
        [Enum(Opaque,0,Cutout,1,Fade,2,Transparent,3)] _Mode ("Render Mode", Float) = 0
        [Enum(Back,2,Front,1,None,0)] _Cull ("Culling", Float) = 2
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        _DiffuseSaturation ("Diffuse Saturation", Range(0,2)) = 1.0
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,2)) = 1.0
        _ParallaxMap ("Height Map", 2D) = "black" {}
        [Toggle] _InvertHeightMap ("Invert Height Map", Float) = 0
        _ParallaxSampleRect ("Height Sample Rect (MinX,MinY,MaxX,MaxY)", Vector) = (0,0,1,1)
        _Parallax ("Height Strength", Range(0,0.1)) = 0.02
        _POMMinLayers ("POM Min Layers", Range(4,32)) = 10
        _POMMaxLayers ("POM Max Layers", Range(8,64)) = 28
        _POMSmoothRadius ("POM Smooth Kernel Radius", Range(0,0.02)) = 0.003
        _POMBoundaryFade ("POM UV Boundary Fade Width", Range(0,0.25)) = 0.05
        [Toggle] _UseSPOM ("Use SPOM (Silhouette POM)", Float) = 0
        [Toggle] _UseSilhouetteClipping ("SPOM UV Silhouette Clipping", Float) = 0
        [Toggle] _UseCurvedSilhouette ("SPOM Curved Silhouette", Float) = 1
        _HorizonSafeThreshold ("SPOM Horizon Safe Threshold", Range(0.01,1)) = 0.25
        _HorizonFalloffPower ("SPOM Horizon Falloff Power", Range(0.25,8)) = 2.0
        _HorizonClipStrength ("SPOM Horizon Clip Strength", Range(0,1)) = 0.4
        _HorizonHeightBias ("SPOM Horizon Height Bias", Range(-1,1)) = 0.0
        [Toggle] _UseORME ("Use ORME Map", Float) = 0
        _ORMEMap ("ORME (R=Occlusion G=Roughness B=Metallic A=Emission)", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        [Toggle(_USE_TRIPLANAR)] _UseTriplanar ("Use Triplanar Mapping", Float) = 0
        _TriplanarScale ("Triplanar Scale", Float) = 1.0
        _TriplanarBlendSharpness ("Triplanar Blend Sharpness", Range(1,8)) = 4.0
        _GrazingFadeThreshold ("Grazing Fade Threshold", Range(0,0.5)) = 0.15
        _Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
        _Alpha ("Alpha", Range(0,1)) = 1.0
        [HideInInspector] _SrcBlend ("__src", Float) = 1
        [HideInInspector] _DstBlend ("__dst", Float) = 0
        [HideInInspector] _ZWrite ("__zw", Float) = 1
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry" }
        Cull [_Cull]
        Blend [_SrcBlend] [_DstBlend]
        ZWrite [_ZWrite]
        LOD 200

        CGPROGRAM
        // Physically based Standard lighting model, and enable shadows on all light types
        #pragma surface surf Standard fullforwardshadows keepalpha
        #pragma shader_feature_local _USE_NORMALMAP
        #pragma shader_feature_local _USE_HEIGHTMAP
        #pragma shader_feature_local _USE_TRIPLANAR

        // Use shader model 3.0 target, to get nicer looking lighting
        #pragma target 3.0

        // Very old GLES targets can skip expensive/unsupported optional effects.
        #if defined(SHADER_API_GLES) && !defined(SHADER_API_GLES3)
            #define ORME_LOW_TIER_GLES 1
        #else
            #define ORME_LOW_TIER_GLES 0
        #endif

        // Quest-class Android VR devices: disable expensive POM and use a lightweight fallback.
        #if defined(UNITY_ANDROID) && (defined(UNITY_STEREO_INSTANCING_ENABLED) || defined(UNITY_STEREO_MULTIVIEW_ENABLED) || defined(UNITY_SINGLE_PASS_STEREO))
            #define ORME_DISABLE_POM 1
        #else
            #define ORME_DISABLE_POM 0
        #endif

        #if (ORME_LOW_TIER_GLES == 1) || (ORME_DISABLE_POM == 1)
            #define ORME_DISABLE_SPOM 1
        #else
            #define ORME_DISABLE_SPOM 0
        #endif

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _ParallaxMap;
        sampler2D _ORMEMap;
        float4 _MainTex_TexelSize;
        float4 _BumpMap_TexelSize;
        float4 _ParallaxMap_TexelSize;
        float4 _ORMEMap_TexelSize;

        struct Input
        {
            float2 uv_MainTex;
            float2 uv_BumpMap;
            float2 uv_ParallaxMap;
            float2 uv_ORMEMap;
            float3 viewDir;
            float3 worldPos;
            float3 worldNormal;
            INTERNAL_DATA
        };

        half _UseORME;
        half _BumpScale;
        half _Parallax;
        half _InvertHeightMap;
        float4 _ParallaxSampleRect;
        half _POMMinLayers;
        half _POMMaxLayers;
        half _POMSmoothRadius;
        half _POMBoundaryFade;
        half _UseSPOM;
        half _UseSilhouetteClipping;
        half _UseCurvedSilhouette;
        half _HorizonSafeThreshold;
        half _HorizonFalloffPower;
        half _HorizonClipStrength;
        half _HorizonHeightBias;
        half _OcclusionStrength;
        half _Glossiness;
        half _Metallic;
        half _DiffuseSaturation;
        fixed4 _Color;
        fixed4 _EmissionColor;
        half _TriplanarScale;
        half _TriplanarBlendSharpness;
        half _GrazingFadeThreshold;
        half _Mode;
        half _Cutoff;
        half _Alpha;
        #include "ORME-Standard-Shader-Helpers.cginc"

        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c;
            fixed4 orme;
            half parallaxVisibility = 1.0h;
            half parallaxClipEnabled = 0.0h;
            float3 worldViewDir = normalize(UnityWorldSpaceViewDir(IN.worldPos));

            #if defined(_USE_TRIPLANAR)
                // Triplanar mapping: project textures from all three world-space axes
                // and blend by surface normal, eliminating UV seams on complex geometry.
                     float2 triUVX;
                     float2 triUVY;
                     float2 triUVZ;
                     ORME_ComputeTriplanarUVs(IN.worldPos, _TriplanarScale, triUVX, triUVY, triUVZ);
                     float3 worldN = normalize(WorldNormalVector(IN, float3(0.0, 0.0, 1.0)));

                // Blend weights: sharper exponent = harder transitions between axes.
                     half3 triWeights = ORME_ComputeTriplanarWeights(worldN, _TriplanarBlendSharpness);

                #if defined(_USE_HEIGHTMAP) && (ORME_LOW_TIER_GLES == 0) && (ORME_DISABLE_POM == 0)
                {
                    // Per-axis POM keeps each projection in its own UV space, avoiding
                    // edge and corner artifacts from cross-axis offset reuse.
                    float4 triSampleRect = float4(0.0, 0.0, 1.0, 1.0);

                    float3 viewDirTSX = float3(-worldViewDir.z, -worldViewDir.y, abs(worldViewDir.x));
                    float3 viewDirTSY = float3(-worldViewDir.x, -worldViewDir.z, abs(worldViewDir.y));
                    float3 viewDirTSZ = float3(-worldViewDir.x, -worldViewDir.y, abs(worldViewDir.z));

                    // Fade parallax to zero at grazing angles per projection axis.
                    half grazingFadeThresh = max(_GrazingFadeThreshold, 1e-4h);
                    half grazeFadeX = smoothstep(0.0h, grazingFadeThresh, abs(worldViewDir.x));
                    half grazeFadeY = smoothstep(0.0h, grazingFadeThresh, abs(worldViewDir.y));
                    half grazeFadeZ = smoothstep(0.0h, grazingFadeThresh, abs(worldViewDir.z));

                    triUVX = ORME_WrapUVToSTRect(triUVX + ComputePOMOffset(triUVX, viewDirTSX, -_Parallax * grazeFadeX, triSampleRect, _POMMinLayers, _POMMaxLayers, _ParallaxMap, _InvertHeightMap), float2(1.0, 1.0), float2(0.0, 0.0));
                    triUVY = ORME_WrapUVToSTRect(triUVY + ComputePOMOffset(triUVY, viewDirTSY, -_Parallax * grazeFadeY, triSampleRect, _POMMinLayers, _POMMaxLayers, _ParallaxMap, _InvertHeightMap), float2(1.0, 1.0), float2(0.0, 0.0));
                    triUVZ = ORME_WrapUVToSTRect(triUVZ + ComputePOMOffset(triUVZ, viewDirTSZ, -_Parallax * grazeFadeZ, triSampleRect, _POMMinLayers, _POMMaxLayers, _ParallaxMap, _InvertHeightMap), float2(1.0, 1.0), float2(0.0, 0.0));
                }
                #endif

                // Albedo
                     fixed4 albedoSample = ORME_SampleWeighted4(_MainTex, triUVX, triUVY, triUVZ, triWeights);
                 half albedoLumaTri = dot(albedoSample.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                 albedoSample.rgb = lerp(albedoLumaTri.xxx, albedoSample.rgb, _DiffuseSaturation);
                 c = albedoSample * _Color;

                // ORME
                     orme = ORME_SampleWeighted4(_ORMEMap, triUVX, triUVY, triUVZ, triWeights);

                o.Albedo = c.rgb;

                #if defined(_USE_NORMALMAP) && (ORME_LOW_TIER_GLES == 0)
                    // Sample each axis projection and blend. xy components are
                    // scaled before blending so _BumpScale acts uniformly.
                    o.Normal = ORME_SampleWeightedNormal(_BumpMap, triUVX, triUVY, triUVZ, triWeights, _BumpScale);
                #endif

            #else
                // Standard UV-based path with optional parallax.
                float2 parallaxOffset = float2(0.0, 0.0);
                float4 sampleRect = saturate(_ParallaxSampleRect);
                half hasParallax = 0.0h;

                #if defined(_USE_HEIGHTMAP) && (ORME_LOW_TIER_GLES == 0)
                    half3 viewDirTS = normalize(IN.viewDir);
                    // Attenuate parallax height to zero at grazing angles (viewDirTS.z -> 0).
                    half grazeFade = smoothstep(0.0h, max(_GrazingFadeThreshold, 1e-4h), abs(viewDirTS.z));
                    // Attenuate parallax height to zero near UV rect boundaries.
                    half boundaryFade = ORME_UVBoundaryFade(IN.uv_ParallaxMap, sampleRect, _POMBoundaryFade);
                    half effectiveParallax = -_Parallax * grazeFade * boundaryFade;
                    hasParallax = 1.0h;
                    #if (ORME_DISABLE_SPOM == 0)
                        if (_UseSPOM > 0.5h)
                        {
                            half spomVisibility;
                            ComputeSPOMOffsetAndVisibility(
                                IN.uv_ParallaxMap,
                                viewDirTS,
                                IN.worldNormal,
                                worldViewDir,
                                effectiveParallax,
                                sampleRect,
                                _UseSilhouetteClipping,
                                _UseCurvedSilhouette,
                                _HorizonSafeThreshold,
                                _HorizonFalloffPower,
                                _HorizonClipStrength,
                                _HorizonHeightBias,
                                _POMSmoothRadius,
                                _POMBoundaryFade,
                                _POMMinLayers,
                                _POMMaxLayers,
                                _ParallaxMap,
                                _InvertHeightMap,
                                _ParallaxMap_TexelSize.xy,
                                parallaxOffset,
                                spomVisibility);
                            parallaxVisibility *= spomVisibility;
                            parallaxClipEnabled = 1.0h;
                        }
                        else
                        {
                            half heightSample = SampleHeightMap(IN.uv_ParallaxMap, sampleRect, _ParallaxMap, _InvertHeightMap);
                            parallaxOffset = ParallaxOffset(heightSample, effectiveParallax, float3(-viewDirTS.xy, viewDirTS.z));
                        }
                    #else
                        half heightSample = SampleHeightMap(IN.uv_ParallaxMap, sampleRect, _ParallaxMap, _InvertHeightMap);
                        parallaxOffset = ParallaxOffset(heightSample, effectiveParallax, float3(-viewDirTS.xy, viewDirTS.z));
                    #endif
                #endif

                // Wrap each map inside its own tiled/offset UV rectangle (atlas-safe wrapping).
                float2 uvMainBase   = ORME_WrapUVToSTRect(IN.uv_MainTex + parallaxOffset, float2(1.0, 1.0), float2(0.0, 0.0));
                float2 uvNormalBase = ORME_WrapUVToSTRect(IN.uv_BumpMap + parallaxOffset, float2(1.0, 1.0), float2(0.0, 0.0));
                float2 uvORMEBase   = ORME_WrapUVToSTRect(IN.uv_ORMEMap + parallaxOffset, float2(1.0, 1.0), float2(0.0, 0.0));

                if (hasParallax > 0.5h)
                {
                    // Only enforce atlas island clipping when an actual sub-rect is used.
                    if (ORME_IsRectFull01(sampleRect) < 0.5h)
                    {
                        parallaxVisibility *= ORME_IsUVInsideRectInset(uvMainBase, sampleRect, _MainTex_TexelSize.xy)
                            * ORME_IsUVInsideRectInset(uvORMEBase, sampleRect, _ORMEMap_TexelSize.xy);
                        parallaxClipEnabled = 1.0h;
                    }
                }

                float2 uvMain   = ORME_ClampUVToRectInset(uvMainBase, sampleRect, _MainTex_TexelSize.xy);
                float2 uvNormal = ORME_ClampUVToRectInset(uvNormalBase, sampleRect, _BumpMap_TexelSize.xy);
                float2 uvORME   = ORME_ClampUVToRectInset(uvORMEBase, sampleRect, _ORMEMap_TexelSize.xy);

                // Albedo comes from a texture tinted by color
                fixed4 albedoSample = tex2D(_MainTex, uvMain);
                half albedoLuma = dot(albedoSample.rgb, half3(0.2126h, 0.7152h, 0.0722h));
                albedoSample.rgb = lerp(albedoLuma.xxx, albedoSample.rgb, _DiffuseSaturation);
                c = albedoSample * _Color;
                orme = tex2D(_ORMEMap, uvORME);

                o.Albedo = c.rgb;

                #if defined(_USE_NORMALMAP) && (ORME_LOW_TIER_GLES == 0)
                    fixed3 normalTex = UnpackNormal(tex2D(_BumpMap, uvNormal));
                    normalTex.xy *= _BumpScale;
                    o.Normal = normalize(normalTex);
                #endif

            #endif // _USE_TRIPLANAR

            // ORME packing: R=Occlusion, G=Roughness, B=Metallic, A=Emission mask.
            half alphaOut;
            ORME_ApplyPackedSurface(c, orme, saturate(_UseORME), _OcclusionStrength, _Glossiness, _Metallic, _EmissionColor, _Alpha, o, alphaOut);
            half mode = floor(_Mode + 0.5h);
            half isTransparentMode = step(1.5h, mode);
            alphaOut = lerp(1.0h, alphaOut, isTransparentMode);
            o.Alpha = alphaOut;
            if (abs(mode - 1.0h) < 0.25h)
            {
                clip(alphaOut - _Cutoff);
            }
            if (parallaxClipEnabled > 0.5h)
            {
                clip(parallaxVisibility - 0.5h);
            }
        }
        ENDCG
    }
    CustomEditor "ORMEStandardShaderGUI"
    FallBack "Diffuse"
}
