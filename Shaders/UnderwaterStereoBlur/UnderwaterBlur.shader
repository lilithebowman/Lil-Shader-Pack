// CozyCon/UnderwaterBlur
// Stereo-compatible screen-space blur used to wrap the player in a "bubble of
// blurriness" when they are underwater in VRChat.
//
// SETUP:
//   1. Create a sphere (Scale ~3-5m, or match your water volume size).
//   2. Apply this shader to its material.
//   3. Make sure the sphere's Mesh Renderer uses "Cull Front" so the inside
//      surface is visible only when the camera is inside the sphere.
//   4. Attach UnderwaterBlurController.cs to the water zone trigger object and
//      link the blur sphere's Renderer to control fade-in/out.
//
// STEREO NOTES:
//   Uses UNITY_DECLARE_SCREENSPACE_TEXTURE / UNITY_SAMPLE_SCREENSPACE_TEXTURE
//   and UNITY_VERTEX_OUTPUT_STEREO / UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX
//   for full single-pass stereo instancing compatibility (VRChat PC & Quest).

Shader "CozyCon/UnderwaterBlur"
{
    Properties
    {
        [Header(Blur Settings)]
        _BlurRadius           ("Blur Radius",              Range(0.001, 0.04)) = 0.010

        [Header(Water Appearance)]
        _WaterColor           ("Water Tint Color",         Color)              = (0.06, 0.28, 0.55, 0.50)
        _WaterFogDensity      ("Fog Blend Strength",       Range(0.0, 1.0))    = 0.25

        [Header(Wave Distortion)]
        _DistortStrength      ("Distortion Strength",      Range(0.0, 0.015))  = 0.004
        _DistortSpeed         ("Wave Animation Speed",     Float)              = 0.80
        _DistortScale         ("Wave Scale",               Float)              = 6.0
        _DistortOpacity       ("Distortion Opacity",       Range(0.0, 1.0))    = 1.0

        [Header(Chromatic Aberration)]
        _ChromaticStrength    ("Chromatic Shift",          Range(0.0, 0.012))  = 0.003

        [Header(Edge Vignette)]
        _VignetteBlurBoost    ("Edge Blur Boost",          Range(0.0, 5.0))    = 1.5

        [Header(Fade Control)]
        _EffectBlend          ("Effect Blend (0-1)",       Range(0.0, 1.0))    = 1.0

        [Header(Quest Safety)]
        _QuestBlurScale       ("Quest Blur Scale",          Range(0.0, 1.0))    = 0.55
        _QuestDistortScale    ("Quest Distort Scale",       Range(0.0, 1.0))    = 0.70
        _QuestChromaticScale  ("Quest Chromatic Scale",     Range(0.0, 1.0))    = 0.35
    }

    SubShader
    {
        // Render late so the entire opaque and transparent scene is already captured.
        Tags
        {
            "Queue"          = "Transparent+500"
            "RenderType"     = "Transparent"
            "IgnoreProjector"= "True"
        }

        // Named GrabPass: shared across all instances in one frame – one copy only.
        GrabPass { "_UnderwaterGrabTex" }

        Pass
        {
            Name "UNDERWATER_BLUR"

            // Cull Front  → inside surface faces the camera when the player is IN the sphere.
            Cull      Front
            ZWrite    Off
            ZTest     Always
            // Blend with whatever was in the framebuffer so _EffectBlend fades gracefully.
            Blend SrcAlpha OneMinusSrcAlpha

            CGPROGRAM
            #pragma vertex   vert
            #pragma fragment frag
            #pragma target   3.0

            // Required for single-pass stereo instancing (VRChat default).
            #pragma multi_compile_instancing

            #include "UnityCG.cginc"

            // ---------------------------------------------------------------
            // 13-tap normalised Gaussian kernel on a unit grid.
            //   Weight formula: w(x,y) = exp(-0.5 * (x*x + y*y))
            //   Normalised so all 13 weights sum to 1.
            //
            //   Centre  (0,0):          0.1838
            //   ±1 cardinal (×4):       0.1115 each
            //   ±1 diagonal (×4):       0.0677 each
            //   ±2 cardinal (×4):       0.0249 each
            //   Total: 0.1838 + 4(0.1115+0.0677+0.0249) = 1.0002 ≈ 1.0  ✓
            // ---------------------------------------------------------------
            static const float2 GAUSS_OFFSETS[13] =
            {
                float2( 0,  0),
                float2( 1,  0), float2(-1,  0), float2( 0,  1), float2( 0, -1),
                float2( 1,  1), float2(-1,  1), float2( 1, -1), float2(-1, -1),
                float2( 2,  0), float2(-2,  0), float2( 0,  2), float2( 0, -2)
            };

            static const float GAUSS_WEIGHTS[13] =
            {
                0.1838,
                0.1115, 0.1115, 0.1115, 0.1115,
                0.0677, 0.0677, 0.0677, 0.0677,
                0.0249, 0.0249, 0.0249, 0.0249
            };

            // ---------------------------------------------------------------
            // Structs
            // ---------------------------------------------------------------
            struct appdata
            {
                float4 vertex : POSITION;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 pos     : SV_POSITION;
                float4 grabPos : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            // ---------------------------------------------------------------
            // Uniforms
            // ---------------------------------------------------------------
            UNITY_DECLARE_SCREENSPACE_TEXTURE(_UnderwaterGrabTex);

            float  _BlurRadius;
            half4  _WaterColor;
            float  _WaterFogDensity;
            float  _DistortStrength;
            float  _DistortSpeed;
            float  _DistortScale;
            float  _ChromaticStrength;
            float  _VignetteBlurBoost;
            float  _EffectBlend;
            float  _DistortOpacity;
            float  _QuestBlurScale;
            float  _QuestDistortScale;
            float  _QuestChromaticScale;

            // ---------------------------------------------------------------
            // Vertex shader
            // ---------------------------------------------------------------
            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_OUTPUT(v2f, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

                o.pos     = UnityObjectToClipPos(v.vertex);
                o.grabPos = ComputeGrabScreenPos(o.pos);
                return o;
            }

            // ---------------------------------------------------------------
            // Fragment shader
            // ---------------------------------------------------------------
            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                // Perspective-divide to get [0,1] screen UV.
                float2 uv = i.grabPos.xy / i.grabPos.w;

                // ----------------------------------------------------------
                // Animated wave distortion – simulates light refracting
                // through a rippling water surface.
                // Two-layer sine/cosine for irregular, organic motion.
                // ----------------------------------------------------------
                float distortStrength = _DistortStrength * _DistortOpacity;
                float chromaStrength = _ChromaticStrength;
                float blurRadius = _BlurRadius;
#if defined(SHADER_API_MOBILE)
                distortStrength *= _QuestDistortScale;
                chromaStrength *= _QuestChromaticScale;
                blurRadius *= _QuestBlurScale;
#endif

                if (distortStrength > 0.00001)
                {
                    float t  = _Time.y * _DistortSpeed;
                    float dx =   sin(uv.y * _DistortScale       + t      ) * 0.6
                               + sin(uv.y * _DistortScale * 1.7 + t * 1.4) * 0.4;
                    float dy =   cos(uv.x * _DistortScale       + t * 0.9) * 0.6
                               + cos(uv.x * _DistortScale * 1.3 + t * 1.2) * 0.4;
                    uv += float2(dx, dy) * distortStrength;
                }
                uv = saturate(uv);

                // ----------------------------------------------------------
                // Edge vignette: blur grows stronger towards screen edges,
                // pushing the "underwater" sense of peripheral distortion.
                // ----------------------------------------------------------
                float2 centered  = uv * 2.0 - 1.0;
                float  vignette  = saturate(dot(centered, centered));
                float  blurStep  = blurRadius * (1.0 + vignette * _VignetteBlurBoost);

                // ----------------------------------------------------------
                // Desktop path: 13-tap Gaussian blur.
                // The R and B channels are sampled at slightly laterally-
                // shifted positions to produce chromatic aberration – the
                // same colour fringing you see when looking through water.
                // Total texture fetches: 13 (main) + 2 (CA) = 15.
                // ----------------------------------------------------------
                half4 blurred = (half4)0;
#if defined(SHADER_API_MOBILE)
                // Some Quest paths can provide an invalid/black grab texture.
                // If so, use a lightweight fallback overlay so the player still
                // gets an underwater cue without relying on grab-pass.
                half4 centerSample = UNITY_SAMPLE_SCREENSPACE_TEXTURE(_UnderwaterGrabTex, uv);
                if (centerSample.a < 0.001h && dot(centerSample.rgb, half3(1.0h, 1.0h, 1.0h)) < 0.001h)
                {
                    float wave = sin((uv.x + uv.y) * 24.0 + _Time.y * (_DistortSpeed * 2.0 + 0.5)) * 0.5 + 0.5;
                    half3 fallbackTint = _WaterColor.rgb * lerp(0.35h, 0.55h, (half)wave);
                    half fallbackAlpha = _EffectBlend * saturate(_WaterColor.a * _WaterFogDensity * 0.75h + (half)vignette * 0.15h);
                    return half4(fallbackTint, fallbackAlpha);
                }

                // Quest path: 5 taps + optional light chroma shift.
                blurred += centerSample * 0.40;
                blurred += UNITY_SAMPLE_SCREENSPACE_TEXTURE(_UnderwaterGrabTex, uv + float2( blurStep, 0.0)) * 0.15;
                blurred += UNITY_SAMPLE_SCREENSPACE_TEXTURE(_UnderwaterGrabTex, uv + float2(-blurStep, 0.0)) * 0.15;
                blurred += UNITY_SAMPLE_SCREENSPACE_TEXTURE(_UnderwaterGrabTex, uv + float2(0.0,  blurStep)) * 0.15;
                blurred += UNITY_SAMPLE_SCREENSPACE_TEXTURE(_UnderwaterGrabTex, uv + float2(0.0, -blurStep)) * 0.15;
#else
                [unroll]
                for (int k = 0; k < 13; k++)
                {
                    float2 offset = GAUSS_OFFSETS[k] * blurStep;
                    blurred += UNITY_SAMPLE_SCREENSPACE_TEXTURE(
                                    _UnderwaterGrabTex, uv + offset)
                               * GAUSS_WEIGHTS[k];
                }
#endif

                // Chromatic aberration: sample R/B at a shifted centre UV
                // (not per-tap to keep fetch count at 15 rather than 39).
                float2 caShift = float2(chromaStrength, chromaStrength * 0.3);
                blurred.r = lerp(
                    blurred.r,
                    UNITY_SAMPLE_SCREENSPACE_TEXTURE(_UnderwaterGrabTex, uv + caShift).r,
                    0.5);
                blurred.b = lerp(
                    blurred.b,
                    UNITY_SAMPLE_SCREENSPACE_TEXTURE(_UnderwaterGrabTex, uv - caShift).b,
                    0.5);

                // ----------------------------------------------------------
                // Water colour fog overlay.
                // _WaterColor.a × _WaterFogDensity controls how strongly the
                // tint colour bleeds into the blurred scene.
                // ----------------------------------------------------------
                half3 tinted = lerp(blurred.rgb,
                                    _WaterColor.rgb,
                                    _WaterColor.a * _WaterFogDensity);

                // _EffectBlend controls the overall opacity of this pass,
                // allowing UnderwaterBlurController to fade the effect in/out.
                // With Blend SrcAlpha OneMinusSrcAlpha the underlying scene
                // shows through when _EffectBlend < 1.
                return half4(tinted, _EffectBlend);
            }
            ENDCG
        }
    }

    // Fallback: nothing – hide gracefully if shader can't compile.
    FallBack Off
}
