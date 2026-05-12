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
        [Toggle(_USE_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 1
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,2)) = 1.0
        [Toggle(_USE_HEIGHTMAP)] _UseHeightMap ("Use Height Map", Float) = 1
        _ParallaxMap ("Height Map", 2D) = "black" {}
        [Toggle] _InvertHeightMap ("Invert Height Map", Float) = 1
        _ParallaxSampleRect ("Height Sample Rect (MinX,MinY,MaxX,MaxY)", Vector) = (0,0,1,1)
        _Parallax ("Height Strength", Range(0,0.1)) = 0.02
        _POMMinLayers ("POM Min Layers", Range(4,32)) = 10
        _POMMaxLayers ("POM Max Layers", Range(8,64)) = 28
        [Toggle] _UseSPOM ("Use SPOM (Silhouette POM)", Float) = 1
        [Toggle] _UseSilhouetteClipping ("SPOM UV Silhouette Clipping", Float) = 0
        [Toggle] _UseCurvedSilhouette ("SPOM Curved Silhouette", Float) = 1
        _HorizonSafeThreshold ("SPOM Horizon Safe Threshold", Range(0.01,1)) = 0.25
        _HorizonFalloffPower ("SPOM Horizon Falloff Power", Range(0.25,8)) = 2.0
        _HorizonClipStrength ("SPOM Horizon Clip Strength", Range(0,1)) = 0.4
        _HorizonHeightBias ("SPOM Horizon Height Bias", Range(-1,1)) = 0.0
        [Toggle] _UseORME ("Use ORME Map", Float) = 1
        _ORMEMap ("ORME (R=Occlusion G=Roughness B=Metallic A=Emission)", 2D) = "white" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        [Toggle(_USE_TRIPLANAR)] _UseTriplanar ("Use Triplanar Mapping", Float) = 0
        _TriplanarScale ("Triplanar Scale", Float) = 1.0
        _TriplanarBlendSharpness ("Triplanar Blend Sharpness", Range(1,8)) = 4.0
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
        #pragma surface surf Standard fullforwardshadows
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
        fixed4 _Color;
        fixed4 _EmissionColor;
        half _TriplanarScale;
        half _TriplanarBlendSharpness;
        half _Mode;
        half _Cutoff;
        half _Alpha;

        // Add instancing support for this shader. You need to check 'Enable Instancing' on materials that use the shader.
        // See https://docs.unity3d.com/Manual/GPUInstancing.html for more information about instancing.
        // #pragma instancing_options assumeuniformscaling
        UNITY_INSTANCING_BUFFER_START(Props)
            // put more per-instance properties here
        UNITY_INSTANCING_BUFFER_END(Props)

        float Hash12(float2 p)
        {
            float3 p3 = frac(float3(p.xyx) * 0.1031);
            p3 += dot(p3, p3.yzx + 33.33);
            return frac((p3.x + p3.y) * p3.z);
        }

        float2 ORME_WrapUVToSTRect(float2 uv, float2 scale, float2 offset)
        {
            float2 signScale = float2(scale.x < 0.0 ? -1.0 : 1.0, scale.y < 0.0 ? -1.0 : 1.0);
            float2 safeScale = max(abs(scale), 1e-5.xx) * signScale;
            float2 localUV = frac((uv - offset) / safeScale);
            return localUV * safeScale + offset;
        }

        float2 ORME_ClampUVToRect(float2 uv, float4 rect)
        {
            float2 rectMin = min(rect.xy, rect.zw);
            float2 rectMax = max(rect.xy, rect.zw);
            return clamp(uv, rectMin, rectMax);
        }

        float2 ORME_ClampUVToRectInset(float2 uv, float4 rect, float2 texelSize)
        {
            float2 rectMin = min(rect.xy, rect.zw);
            float2 rectMax = max(rect.xy, rect.zw);
            float2 inset = abs(texelSize) * 0.5;
            rectMin = min(rectMin + inset, rectMax);
            rectMax = max(rectMax - inset, rectMin);
            return clamp(uv, rectMin, rectMax);
        }

        half ORME_IsUVInsideRectInset(float2 uv, float4 rect, float2 texelSize)
        {
            float2 rectMin = min(rect.xy, rect.zw);
            float2 rectMax = max(rect.xy, rect.zw);
            float2 inset = abs(texelSize) * 0.5;
            rectMin = min(rectMin + inset, rectMax);
            rectMax = max(rectMax - inset, rectMin);

            return step(rectMin.x, uv.x)
                * step(rectMin.y, uv.y)
                * step(uv.x, rectMax.x)
                * step(uv.y, rectMax.y);
        }

            half ORME_IsRectFull01(float4 rect)
            {
                const half eps = 1e-4h;
                return step(abs(rect.x - 0.0h), eps)
                * step(abs(rect.y - 0.0h), eps)
                * step(abs(rect.z - 1.0h), eps)
                * step(abs(rect.w - 1.0h), eps);
            }

        half SampleHeightMap(float2 uv, float4 sampleRect)
        {
            float2 clampedUV = ORME_ClampUVToRect(uv, sampleRect);
            half height = tex2D(_ParallaxMap, clampedUV).r;
            return lerp(height, 1.0h - height, saturate(_InvertHeightMap));
        }

        // POM UV ray marching in tangent space. Uses per-eye view direction, so it remains stable in stereo.
        float2 ComputePOMOffset(float2 uv, float3 viewDirTS, half heightScale, float4 sampleRect)
        {
            viewDirTS = normalize(viewDirTS);

            // Increase layer count at grazing angles for better silhouette depth.
            float ndotv = saturate(abs(viewDirTS.z));
            float minLayers = min(_POMMinLayers, _POMMaxLayers);
            float maxLayers = max(_POMMinLayers, _POMMaxLayers);
            float layerCount = lerp(maxLayers, minLayers, ndotv);
            float layerDepth = rcp(layerCount);

            float2 rayStep = (-viewDirTS.xy / max(0.05, abs(viewDirTS.z))) * heightScale;
            float2 deltaUV = rayStep * layerDepth;

            float2 currentUV = uv;
            // Jitter the starting depth to break up visible marching bands.
            float jitter = Hash12(uv * 4096.0);
            float currentLayerDepth = jitter * layerDepth;
            currentUV -= deltaUV * jitter;
            float currentHeight = SampleHeightMap(currentUV, sampleRect);

            [loop]
            for (int step = 0; step < 64; ++step)
            {
                if (step >= (int)layerCount || currentLayerDepth >= currentHeight)
                    break;

                currentUV -= deltaUV;
                currentLayerDepth += layerDepth;
                currentHeight = SampleHeightMap(currentUV, sampleRect);
            }

            float2 prevUV = currentUV + deltaUV;
            float prevLayerDepth = currentLayerDepth - layerDepth;
            float prevHeight = SampleHeightMap(prevUV, sampleRect);

            float2 aboveUV = prevUV;
            float aboveLayerDepth = prevLayerDepth;
            float aboveHeight = prevHeight;

            float2 belowUV = currentUV;
            float belowLayerDepth = currentLayerDepth;
            float belowHeight = currentHeight;

            // Refine the intersection to smooth stair-stepping artifacts.
            [unroll]
            for (int refine = 0; refine < 3; ++refine)
            {
                float2 midUV = (aboveUV + belowUV) * 0.5;
                float midLayerDepth = (aboveLayerDepth + belowLayerDepth) * 0.5;
                float midHeight = SampleHeightMap(midUV, sampleRect);

                if (midLayerDepth < midHeight)
                {
                    aboveUV = midUV;
                    aboveLayerDepth = midLayerDepth;
                    aboveHeight = midHeight;
                }
                else
                {
                    belowUV = midUV;
                    belowLayerDepth = midLayerDepth;
                    belowHeight = midHeight;
                }
            }

            float afterDepth = belowHeight - belowLayerDepth;
            float beforeDepth = aboveHeight - aboveLayerDepth;
            float weight = saturate(afterDepth / max(afterDepth - beforeDepth, 1e-5));
            float2 hitUV = lerp(belowUV, aboveUV, weight);

            return hitUV - uv;
        }

        void ComputeSPOMOffsetAndVisibility(
            float2 uv,
            float3 viewDirTS,
            float3 worldNormal,
            float3 worldViewDir,
            half heightScale,
            float4 sampleRect,
            out float2 parallaxOffset,
            out half silhouetteVisibility)
        {
            float2 hitUV = uv + ComputePOMOffset(uv, viewDirTS, heightScale, sampleRect);
            parallaxOffset = hitUV - uv;
            silhouetteVisibility = 1.0h;

            if (_UseSilhouetteClipping > 0.5h)
            {
                silhouetteVisibility *= ORME_IsUVInsideRectInset(hitUV, sampleRect, _ParallaxMap_TexelSize.xy);
            }

            if (_UseCurvedSilhouette > 0.5h)
            {
                float ndotv = abs(dot(normalize(worldNormal), normalize(worldViewDir)));
                float t = saturate(1.0 - ndotv / max(_HorizonSafeThreshold, 1e-3h));
                float horizonFactor = pow(t, max(_HorizonFalloffPower, 1e-3h));
                float heightThreshold = saturate(horizonFactor * saturate(_HorizonClipStrength));
                float surfaceHeight = SampleHeightMap(hitUV, sampleRect) - _HorizonHeightBias;

                if (surfaceHeight < heightThreshold)
                {
                    silhouetteVisibility = 0.0h;
                }
            }
        }

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
                     float3 triPos = IN.worldPos * max(_TriplanarScale, 1e-4h);
                     float2 triUVX = frac(triPos.zy);
                     float2 triUVY = frac(triPos.xz);
                     float2 triUVZ = frac(triPos.xy);
                     float3 worldN = normalize(WorldNormalVector(IN, float3(0.0, 0.0, 1.0)));

                // Blend weights: sharper exponent = harder transitions between axes.
                     half3 triWeights = max(pow(abs(worldN), _TriplanarBlendSharpness), 1e-4h);
                 triWeights /= (triWeights.x + triWeights.y + triWeights.z);

                #if defined(_USE_HEIGHTMAP) && (ORME_LOW_TIER_GLES == 0) && (ORME_DISABLE_POM == 0)
                {
                    // Per-axis POM keeps each projection in its own UV space, avoiding
                    // edge and corner artifacts from cross-axis offset reuse.
                    float4 triSampleRect = float4(0.0, 0.0, 1.0, 1.0);

                    float3 viewDirTSX = float3(-worldViewDir.z, -worldViewDir.y, abs(worldViewDir.x));
                    float3 viewDirTSY = float3(-worldViewDir.x, -worldViewDir.z, abs(worldViewDir.y));
                    float3 viewDirTSZ = float3(-worldViewDir.x, -worldViewDir.y, abs(worldViewDir.z));

                    triUVX += ComputePOMOffset(triUVX, viewDirTSX, _Parallax, triSampleRect);
                    triUVY += ComputePOMOffset(triUVY, viewDirTSY, _Parallax, triSampleRect);
                    triUVZ += ComputePOMOffset(triUVZ, viewDirTSZ, _Parallax, triSampleRect);
                }
                #endif

                // Albedo
                     c  = tex2D(_MainTex, triUVX) * triWeights.x
                         + tex2D(_MainTex, triUVY) * triWeights.y
                         + tex2D(_MainTex, triUVZ) * triWeights.z;
                c *= _Color;

                // ORME
                     orme = tex2D(_ORMEMap, triUVX) * triWeights.x
                            + tex2D(_ORMEMap, triUVY) * triWeights.y
                            + tex2D(_ORMEMap, triUVZ) * triWeights.z;

                o.Albedo = c.rgb;

                #if defined(_USE_NORMALMAP) && (ORME_LOW_TIER_GLES == 0)
                    // Sample each axis projection and blend. xy components are
                    // scaled before blending so _BumpScale acts uniformly.
                    half3 nX = UnpackNormal(tex2D(_BumpMap, triUVX));
                    half3 nY = UnpackNormal(tex2D(_BumpMap, triUVY));
                    half3 nZ = UnpackNormal(tex2D(_BumpMap, triUVZ));
                    nX.xy *= _BumpScale;
                    nY.xy *= _BumpScale;
                    nZ.xy *= _BumpScale;
                    o.Normal = normalize(nX * triWeights.x + nY * triWeights.y + nZ * triWeights.z);
                #endif

            #else
                // Standard UV-based path with optional parallax.
                float2 parallaxOffset = float2(0.0, 0.0);
                float4 sampleRect = saturate(_ParallaxSampleRect);
                half hasParallax = 0.0h;

                #if defined(_USE_HEIGHTMAP) && (ORME_LOW_TIER_GLES == 0)
                    half3 viewDirTS = normalize(IN.viewDir);
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
                                _Parallax,
                                sampleRect,
                                parallaxOffset,
                                spomVisibility);
                            parallaxVisibility *= spomVisibility;
                            parallaxClipEnabled = 1.0h;
                        }
                        else
                        {
                            half heightSample = SampleHeightMap(IN.uv_ParallaxMap, sampleRect);
                            parallaxOffset = ParallaxOffset(heightSample, _Parallax, float3(-viewDirTS.xy, viewDirTS.z));
                        }
                    #else
                        half heightSample = SampleHeightMap(IN.uv_ParallaxMap, sampleRect);
                        parallaxOffset = ParallaxOffset(heightSample, _Parallax, float3(-viewDirTS.xy, viewDirTS.z));
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
                c    = tex2D(_MainTex,  uvMain) * _Color;
                orme = tex2D(_ORMEMap, uvORME);

                o.Albedo = c.rgb;

                #if defined(_USE_NORMALMAP) && (ORME_LOW_TIER_GLES == 0)
                    fixed3 normalTex = UnpackNormal(tex2D(_BumpMap, uvNormal));
                    normalTex.xy *= _BumpScale;
                    o.Normal = normalize(normalTex);
                #endif

            #endif // _USE_TRIPLANAR

            // ORME packing: R=Occlusion, G=Roughness, B=Metallic, A=Emission mask.
            half useORME       = saturate(_UseORME);
            half mapOcclusion  = lerp(1.0h, orme.r, _OcclusionStrength);
            half mapSmoothness = 1.0h - saturate(orme.g);
            half mapMetallic   = saturate(orme.b);

            o.Metallic   = lerp(_Metallic,   mapMetallic   * _Metallic,   useORME);
            o.Smoothness = lerp(_Glossiness, mapSmoothness * _Glossiness, useORME);
            o.Occlusion  = lerp(1.0h,        mapOcclusion,                useORME);
            // Keep emission map color/contrast, using emissive color mainly as intensity.
            half emissionMask = orme.a * useORME;
            half emissionIntensity = max(_EmissionColor.r, max(_EmissionColor.g, _EmissionColor.b));
            half3 emissionTint = lerp(half3(1.0h, 1.0h, 1.0h), saturate(_EmissionColor.rgb / max(emissionIntensity, 1e-4h)), 0.25h);
            o.Emission   = c.rgb * emissionMask * emissionIntensity * emissionTint;
            half mode = floor(_Mode + 0.5h);
            half isTransparentMode = step(1.5h, mode);
            half alphaOut = saturate(c.a * _Alpha);
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
