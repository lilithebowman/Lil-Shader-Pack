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

half ORME_UVBoundaryFade(float2 uv, float4 rect, half fadeWidth)
{
	float2 rectMin = min(rect.xy, rect.zw);
	float2 rectMax = max(rect.xy, rect.zw);
	float2 distToEdge = min(uv - rectMin, rectMax - uv);
	float2 t = saturate(distToEdge / max(fadeWidth, 1e-5));
	return (half)min(smoothstep(0.0, 1.0, t.x), smoothstep(0.0, 1.0, t.y));
}

half SampleHeightMapClamped(float2 uv, float4 sampleRect, sampler2D parallaxMap, half invertHeightMap)
{
	float2 clampedUV = ORME_ClampUVToRect(uv, sampleRect);
	half height = tex2D(parallaxMap, clampedUV).r;
	return lerp(height, 1.0h - height, saturate(invertHeightMap));
}

half SampleHeightMap(float2 uv, float4 sampleRect, sampler2D parallaxMap, half invertHeightMap)
{
	float2 rectMin = min(sampleRect.xy, sampleRect.zw);
	float2 rectMax = max(sampleRect.xy, sampleRect.zw);
	half inside = step(rectMin.x, uv.x) * step(rectMin.y, uv.y)
				* step(uv.x, rectMax.x) * step(uv.y, rectMax.y);
	half height = tex2D(parallaxMap, clamp(uv, rectMin, rectMax)).r;
	return lerp(height, 1.0h - height, saturate(invertHeightMap)) * inside;
}

half SampleHeightMapSmooth(float2 uv, float4 sampleRect, sampler2D parallaxMap, half invertHeightMap, half smoothRadius)
{
	float r = smoothRadius;
	[branch]
	if (r < 1e-5)
		return SampleHeightMapClamped(uv, sampleRect, parallaxMap, invertHeightMap);
	half h = SampleHeightMapClamped(uv, sampleRect, parallaxMap, invertHeightMap);
	h += SampleHeightMapClamped(uv + float2( r,  0.0), sampleRect, parallaxMap, invertHeightMap);
	h += SampleHeightMapClamped(uv + float2(-r,  0.0), sampleRect, parallaxMap, invertHeightMap);
	h += SampleHeightMapClamped(uv + float2( 0.0,  r), sampleRect, parallaxMap, invertHeightMap);
	h += SampleHeightMapClamped(uv + float2( 0.0, -r), sampleRect, parallaxMap, invertHeightMap);
	return h * 0.2h;
}

float2 ComputePOMOffset(
	float2 uv,
	float3 viewDirTS,
	half heightScale,
	float4 sampleRect,
	float minLayers,
	float maxLayers,
	sampler2D parallaxMap,
	half invertHeightMap)
{
	viewDirTS = normalize(viewDirTS);

	float ndotv = saturate(abs(viewDirTS.z));
	float layerCount = lerp(maxLayers, minLayers, ndotv);
	float layerDepth = rcp(layerCount);

	float2 rayStep = (-viewDirTS.xy / max(0.05, abs(viewDirTS.z))) * heightScale;
	float2 deltaUV = rayStep * layerDepth;

	float2 currentUV = uv;
	float jitter = Hash12(uv * 4096.0);
	float currentLayerDepth = jitter * layerDepth;
	currentUV -= deltaUV * jitter;
	float currentHeight = SampleHeightMap(currentUV, sampleRect, parallaxMap, invertHeightMap);

	[loop]
	for (int step = 0; step < 64; ++step)
	{
		if (step >= (int)layerCount || currentLayerDepth >= currentHeight)
			break;

		currentUV -= deltaUV;
		currentLayerDepth += layerDepth;
		currentHeight = SampleHeightMap(currentUV, sampleRect, parallaxMap, invertHeightMap);
	}

	float2 prevUV = currentUV + deltaUV;
	float prevLayerDepth = currentLayerDepth - layerDepth;
	float prevHeight = SampleHeightMap(prevUV, sampleRect, parallaxMap, invertHeightMap);

	float2 aboveUV = prevUV;
	float aboveLayerDepth = prevLayerDepth;
	float aboveHeight = prevHeight;

	float2 belowUV = currentUV;
	float belowLayerDepth = currentLayerDepth;
	float belowHeight = currentHeight;

	[unroll]
	for (int refine = 0; refine < 3; ++refine)
	{
		float2 midUV = (aboveUV + belowUV) * 0.5;
		float midLayerDepth = (aboveLayerDepth + belowLayerDepth) * 0.5;
		float midHeight = SampleHeightMap(midUV, sampleRect, parallaxMap, invertHeightMap);

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
	half useSilhouetteClipping,
	half useCurvedSilhouette,
	half horizonSafeThreshold,
	half horizonFalloffPower,
	half horizonClipStrength,
	half horizonHeightBias,
	half pomSmoothRadius,
	float minLayers,
	float maxLayers,
	sampler2D parallaxMap,
	half invertHeightMap,
	float2 parallaxMapTexelSize,
	out float2 parallaxOffset,
	out half silhouetteVisibility)
{
	float2 hitUV = uv + ComputePOMOffset(uv, viewDirTS, heightScale, sampleRect, minLayers, maxLayers, parallaxMap, invertHeightMap);
	parallaxOffset = hitUV - uv;
	silhouetteVisibility = 1.0h;

	if (useSilhouetteClipping > 0.5h)
	{
		silhouetteVisibility *= ORME_IsUVInsideRectInset(hitUV, sampleRect, parallaxMapTexelSize);
	}

	if (useCurvedSilhouette > 0.5h)
	{
		float ndotv = abs(dot(normalize(worldNormal), normalize(worldViewDir)));
		float t = saturate(1.0 - ndotv / max(horizonSafeThreshold, 1e-3h));
		float horizonFactor = pow(t, max(horizonFalloffPower, 1e-3h));
		float heightThreshold = saturate(horizonFactor * saturate(horizonClipStrength));
		float surfaceHeight = SampleHeightMapSmooth(hitUV, sampleRect, parallaxMap, invertHeightMap, pomSmoothRadius) - horizonHeightBias;
		float smoothEdge = max(pomSmoothRadius * 8.0, 1e-4);
		silhouetteVisibility *= smoothstep(heightThreshold - smoothEdge, heightThreshold + smoothEdge, surfaceHeight);
	}
}

void ORME_ComputeTriplanarUVs(float3 worldPos, half triplanarScale, out float2 uvX, out float2 uvY, out float2 uvZ)
{
	float3 triPos = worldPos * max(triplanarScale, 1e-4h);
	uvX = frac(triPos.zy);
	uvY = frac(triPos.xz);
	uvZ = frac(triPos.xy);
}

half3 ORME_ComputeTriplanarWeights(float3 worldNormal, half blendSharpness)
{
	half3 weights = max(pow(abs(normalize(worldNormal)), blendSharpness), 1e-4h);
	return weights / (weights.x + weights.y + weights.z);
}

fixed4 ORME_SampleWeighted4(sampler2D textureMap, float2 uvX, float2 uvY, float2 uvZ, half3 weights)
{
	return tex2D(textureMap, uvX) * weights.x
		+ tex2D(textureMap, uvY) * weights.y
		+ tex2D(textureMap, uvZ) * weights.z;
}

half3 ORME_SampleWeightedNormal(sampler2D bumpMap, float2 uvX, float2 uvY, float2 uvZ, half3 weights, half bumpScale)
{
	half3 normalX = UnpackNormal(tex2D(bumpMap, uvX));
	half3 normalY = UnpackNormal(tex2D(bumpMap, uvY));
	half3 normalZ = UnpackNormal(tex2D(bumpMap, uvZ));
	normalX.xy *= bumpScale;
	normalY.xy *= bumpScale;
	normalZ.xy *= bumpScale;
	return normalize(normalX * weights.x + normalY * weights.y + normalZ * weights.z);
}

void ORME_ApplyPackedSurface(
	fixed4 albedoSample,
	fixed4 ormeSample,
	half useORME,
	half occlusionStrength,
	half glossiness,
	half metallic,
	fixed4 emissionColor,
	half alphaMultiplier,
	inout SurfaceOutputStandard o,
	out half alphaOut)
{
	half mapOcclusion = lerp(1.0h, ormeSample.r, occlusionStrength);
	half mapSmoothness = 1.0h - saturate(ormeSample.g);
	half mapMetallic = saturate(ormeSample.b);

	o.Metallic = lerp(metallic, mapMetallic * metallic, useORME);
	o.Smoothness = lerp(glossiness, mapSmoothness * glossiness, useORME);
	o.Occlusion = lerp(1.0h, mapOcclusion, useORME);

	half emissionMask = ormeSample.a * useORME;
	half emissionIntensity = max(emissionColor.r, max(emissionColor.g, emissionColor.b));
	half3 emissionTint = lerp(half3(1.0h, 1.0h, 1.0h), saturate(emissionColor.rgb / max(emissionIntensity, 1e-4h)), 0.25h);
	o.Emission = albedoSample.rgb * emissionMask * emissionIntensity * emissionTint;

	alphaOut = saturate(albedoSample.a * alphaMultiplier);
}