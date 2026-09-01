Shader "Lilithe/Lil Standard"
{
	Properties
	{
		[Enum(Opaque,0,Cutout,1,Transparent,3,Fade,4)] _BlendMode ("Blend Mode", Float) = 0
		_Cutoff ("Alpha Cutoff", Range(0,1)) = 0.5
		_Color ("Color", Color) = (1,1,1,1)
		_MainTex ("Albedo (RGB)", 2D) = "white" {}
		_Glossiness ("Smoothness", Range(0,1)) = 0.5
		_Metallic ("Metallic", Range(0,1)) = 0.0
		[Toggle] _EnableNormalMap ("Enable Normal Map", Float) = 0
		_BumpMap ("Normal Map", 2D) = "bump" {}
		[Toggle] _EnableTriplanar ("Enable Triplanar Mapping", Float) = 0
		_TriplanarBlendSharpness ("Triplanar Blend Sharpness", Range(1,10)) = 4.0
	}

	SubShader
	{
	Tags { "RenderType"="Opaque" }
	LOD 200
	// Allow material to control blend and zwrite
	Blend [_SrcBlend] [_DstBlend]
	ZWrite [_ZWrite]
	CGPROGRAM
	#pragma surface surf Standard fullforwardshadows
		#pragma target 3.0
		sampler2D _MainTex;
		sampler2D _BumpMap;
		struct Input
		{
			float2 uv_MainTex;
			float _BlendMode;
			float2 uv_BumpMap;
			float3 worldPos;
			float3 worldNormal;
			INTERNAL_DATA
		};
		half _Glossiness;
		half _Metallic;
		fixed4 _Color;
		float _EnableSSR;
		float _TriplanarBlendSharpness;
		float _EnableTriplanar;
		float _EnableNormalMap;
		float _BlendMode;
		float _Cutoff;
		UNITY_INSTANCING_BUFFER_START(Props)
			// put more per-instance properties here
		UNITY_INSTANCING_BUFFER_END(Props)
		void surf (Input IN, inout SurfaceOutputStandard o)
		{
			fixed4 c;
			if (_EnableTriplanar > 0.5)
			{
				float sharpness = _TriplanarBlendSharpness;
				float3 safeNormal = IN.worldNormal;
				if (length(safeNormal) < 1e-5) safeNormal = float3(0,0,1);
				float3 blendWeights = pow(abs(safeNormal), sharpness);
				blendWeights /= max(blendWeights.x + blendWeights.y + blendWeights.z, 1e-5);
				float2 xUV = IN.worldPos.yz;
				float2 yUV = IN.worldPos.xz;
				float2 zUV = IN.worldPos.xy;
				fixed4 xAlbedo = tex2D(_MainTex, xUV);
				fixed4 yAlbedo = tex2D(_MainTex, yUV);
				fixed4 zAlbedo = tex2D(_MainTex, zUV);
				c = xAlbedo * blendWeights.x + yAlbedo * blendWeights.y + zAlbedo * blendWeights.z;
				c *= _Color;
			}
			else
			{
				c = tex2D (_MainTex, IN.uv_MainTex) * _Color;
			}
			o.Albedo = c.rgb;
			o.Metallic = _Metallic;
			o.Smoothness = _Glossiness;
			// Blend/cutout logic based on _BlendMode
			if (_BlendMode == 0) // Opaque
			{
				o.Alpha = 1.0;
			}
			else if (_BlendMode == 1) // Cutout
			{
				// Use alpha from color or texture
				float alpha = c.a;
				clip(alpha - _Cutoff);
				o.Alpha = 1.0;
			}
			else if (_BlendMode == 3) // Transparent
			{
				// Use alpha from color or texture
				o.Alpha = c.a;
			}
			else if (_BlendMode == 4) // Fade
			{
				// Use alpha from color or texture, but don't clip
				o.Alpha = c.a;
			}
			
			// SSR logic: sample screen space reflection if enabled
			#ifdef UNITY_SAMPLE_SCREENSPACE_REFLECTION
			o.Emission += UNITY_SAMPLE_SCREENSPACE_REFLECTION(IN.worldPos, o.Normal);
			#endif
			// Normal map support (toggle)
			if (_EnableNormalMap > 0.5)
			{
				float2 xUV = IN.worldPos.yz;
				float2 yUV = IN.worldPos.xz;
				float2 zUV = IN.worldPos.xy;
				float3 xNormal = UnpackNormal(tex2D(_BumpMap, xUV));
				float3 yNormal = UnpackNormal(tex2D(_BumpMap, yUV));
				float3 zNormal = UnpackNormal(tex2D(_BumpMap, zUV));
				float sharpness = _TriplanarBlendSharpness;
				float3 safeNormal = IN.worldNormal;
				if (length(safeNormal) < 1e-5) safeNormal = float3(0,0,1);
				float3 blendWeights = pow(abs(safeNormal), sharpness);
				blendWeights /= max(blendWeights.x + blendWeights.y + blendWeights.z, 1e-5);
				float3 blendedNormal = xNormal * blendWeights.x + yNormal * blendWeights.y + zNormal * blendWeights.z;
				o.Normal = normalize(blendedNormal);
			}
			else
			{
				o.Normal = UnpackNormal(tex2D(_BumpMap, IN.uv_BumpMap));
			}
		}
		ENDCG
	}
	FallBack "Diffuse"
}