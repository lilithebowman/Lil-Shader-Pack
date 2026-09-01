Shader "Hidden/Lilithe/UVUnwrap"
{
    SubShader
    {
        Tags { "RenderType"="Opaque" "Queue"="Geometry+1" }
        Cull Off
        ZWrite Off
        ZTest Always

        // PASS 0 — UV -> World Position
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_pos
            #pragma fragment frag_pos
            #include "UnityCG.cginc"

            struct appv {
                float4 vertex : POSITION;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f_pos {
                float4 pos : SV_POSITION;
                float3 worldPos : TEXCOORD0;
            };

            v2f_pos vert_pos(appv v)
            {
                v2f_pos o;
                // Map UV (0..1) to clip space (-1..1) using LoadOrtho / DrawMeshNow approach
                float2 uv = v.texcoord.xy;
                o.pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);
                // world position (use Unity macro for safety)
                o.worldPos = mul(unity_ObjectToWorld, v.vertex).xyz;
                return o;
            }

            float4 frag_pos(v2f_pos i) : SV_Target
            {
                // Write world position directly (float range may be large; saved as RGBAFloat)
                return float4(i.worldPos, 1.0);
            }
            ENDCG
        }

        // PASS 1 — UV -> World Normal (remapped to 0..1)
        Pass
        {
            CGPROGRAM
            #pragma vertex vert_nrm
            #pragma fragment frag_nrm
            #include "UnityCG.cginc"

            struct appn {
                float4 vertex : POSITION;
                float3 normal : NORMAL;
                float2 texcoord : TEXCOORD0;
            };

            struct v2f_nrm {
                float4 pos : SV_POSITION;
                float3 worldNormal : TEXCOORD0;
            };

            v2f_nrm vert_nrm(appn v)
            {
                v2f_nrm o;
                float2 uv = v.texcoord.xy;
                o.pos = float4(uv * 2.0 - 1.0, 0.0, 1.0);

                // Transform normal to world space robustly and normalize
                float3 worldN = normalize(mul((float3x3)unity_ObjectToWorld, v.normal));
                o.worldNormal = worldN;
                return o;
            }

            float4 frag_nrm(v2f_nrm i) : SV_Target
            {
                // Remap from [-1,1] to [0,1] so it saves as visible color
                float3 n = normalize(i.worldNormal);
                float3 encoded = n * 0.5 + 0.5;
                return float4(encoded, 1.0);
            }
            ENDCG
        }
    }
}
