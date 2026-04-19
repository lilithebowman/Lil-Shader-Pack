// ORME-Standard-Shader.shader
//
// Implements a standard shader with support for ORME combined
// Occlusion, Roughness, Metallic, and Emission maps.

Shader "Lilithe/ORME-Standard-Shader"
{
    Properties
    {
        _Color ("Color", Color) = (1,1,1,1)
        _MainTex ("Albedo (RGB)", 2D) = "white" {}
        [Toggle(_USE_NORMALMAP)] _UseNormalMap ("Use Normal Map", Float) = 1
        [Normal] _BumpMap ("Normal Map", 2D) = "bump" {}
        _BumpScale ("Normal Strength", Range(0,2)) = 1.0
        [Toggle(_USE_HEIGHTMAP)] _UseHeightMap ("Use Height Map", Float) = 1
        _ParallaxMap ("Height Map", 2D) = "black" {}
        [Toggle] _InvertHeightMap ("Invert Height Map", Float) = 1
        _Parallax ("Height Strength", Range(0,0.1)) = 0.02
        _POMMinLayers ("POM Min Layers", Range(4,32)) = 10
        _POMMaxLayers ("POM Max Layers", Range(8,64)) = 28
        [Toggle] _UseORME ("Use ORME Map", Float) = 1
        _ORMEMap ("ORME (R=Occlusion G=Roughness B=Metallic A=Emission)", 2D) = "black" {}
        _OcclusionStrength ("Occlusion Strength", Range(0,1)) = 1.0
        _Glossiness ("Smoothness", Range(0,1)) = 0.5
        _Metallic ("Metallic", Range(0,1)) = 0.0
        [HDR] _EmissionColor ("Emission Color", Color) = (0,0,0,0)
        [Toggle(_USE_TRIPLANAR)] _UseTriplanar ("Use Triplanar Mapping", Float) = 0
        _TriplanarScale ("Triplanar Scale", Float) = 1.0
        _TriplanarBlendSharpness ("Triplanar Blend Sharpness", Range(1,8)) = 4.0
    }
    SubShader
    {
        Tags { "RenderType"="Opaque" }
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

        sampler2D _MainTex;
        sampler2D _BumpMap;
        sampler2D _ParallaxMap;
        sampler2D _ORMEMap;

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
        half _POMMinLayers;
        half _POMMaxLayers;
        half _OcclusionStrength;
        half _Glossiness;
        half _Metallic;
        fixed4 _Color;
        fixed4 _EmissionColor;
        half _TriplanarScale;
        half _TriplanarBlendSharpness;

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

        half SampleHeightMap(float2 uv)
        {
            half height = tex2D(_ParallaxMap, frac(uv)).r;
            return lerp(height, 1.0h - height, saturate(_InvertHeightMap));
        }

        // POM UV ray marching in tangent space. Uses per-eye view direction, so it remains stable in stereo.
        float2 ComputePOMOffset(float2 uv, float3 viewDirTS, half heightScale)
        {
            viewDirTS = normalize(viewDirTS);

            // Increase layer count at grazing angles for better silhouette depth.
            float ndotv = saturate(abs(viewDirTS.z));
            float minLayers = min(_POMMinLayers, _POMMaxLayers);
            float maxLayers = max(_POMMinLayers, _POMMaxLayers);
            float layerCount = lerp(maxLayers, minLayers, ndotv);
            float layerDepth = rcp(layerCount);

            float2 rayStep = (viewDirTS.xy / max(0.05, abs(viewDirTS.z))) * heightScale;
            float2 deltaUV = rayStep * layerDepth;

            float2 currentUV = uv;
            // Jitter the starting depth to break up visible marching bands.
            float jitter = Hash12(uv * 4096.0);
            float currentLayerDepth = jitter * layerDepth;
            currentUV -= deltaUV * jitter;
            float currentHeight = SampleHeightMap(currentUV);

            [loop]
            for (int step = 0; step < 64; ++step)
            {
                if (step >= (int)layerCount || currentLayerDepth >= currentHeight)
                    break;

                currentUV -= deltaUV;
                currentLayerDepth += layerDepth;
                currentHeight = SampleHeightMap(currentUV);
            }

            float2 prevUV = currentUV + deltaUV;
            float prevLayerDepth = currentLayerDepth - layerDepth;
            float prevHeight = SampleHeightMap(prevUV);

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
                float midHeight = SampleHeightMap(midUV);

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

        void surf (Input IN, inout SurfaceOutputStandard o)
        {
            fixed4 c;
            fixed4 orme;

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

                #if defined(_USE_HEIGHTMAP) && (ORME_LOW_TIER_GLES == 0)
                    half3 viewDirTS = normalize(IN.viewDir);
                    #if (ORME_DISABLE_POM == 0)
                        parallaxOffset = ComputePOMOffset(frac(IN.uv_ParallaxMap), viewDirTS, _Parallax);
                    #else
                        half heightSample = SampleHeightMap(IN.uv_ParallaxMap);
                        parallaxOffset = ParallaxOffset(heightSample, _Parallax, viewDirTS);
                    #endif
                #endif

                // Wrap after parallax so UVs crossing 1.0 continue from 0.0.
                float2 uvMain   = frac(IN.uv_MainTex   + parallaxOffset);
                float2 uvNormal = frac(IN.uv_BumpMap   + parallaxOffset);
                float2 uvORME   = frac(IN.uv_ORMEMap   + parallaxOffset);

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
            o.Emission   = _EmissionColor.rgb * (orme.a * useORME);
            o.Alpha      = c.a;
        }
        ENDCG
    }
    FallBack "Diffuse"
}
