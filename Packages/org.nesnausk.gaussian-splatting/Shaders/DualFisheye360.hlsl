// SPDX-License-Identifier: MIT
#ifndef GAUSSIAN_DUAL_FISHEYE_360_INCLUDED
#define GAUSSIAN_DUAL_FISHEYE_360_INCLUDED

// Experimental direct dual-fisheye projection for near-360-degree views.
// Version 2 deliberately uses two separated, pixel-circular, equidistant
// 180-degree lenses. This avoids the previous capsule / oval appearance that
// resulted when two generalized-fisheye discs touched in the middle.

static const float DUAL_FISHEYE_HALF_PI = 1.5707963267948966;
static const float DUAL_FISHEYE_ENABLE_THETA = 3.0;
static const float DUAL_FISHEYE_CENTER_X = 0.52;
static const float DUAL_FISHEYE_MAX_RADIUS_X = 0.46;
static const float DUAL_FISHEYE_MAX_RADIUS_Y = 0.92;

bool IsDualFisheye360(float4 fisheyeParams2)
{
    return fisheyeParams2.y > DUAL_FISHEYE_ENABLE_THETA;
}

float3 DualFisheyeLensView(float3 viewPos, out bool backHemisphere)
{
    backHemisphere = viewPos.z > 0.0;
    // The rear lens is the same 180-degree model after a Y-axis half turn.
    return backHemisphere ? float3(-viewPos.x, viewPos.y, -viewPos.z) : viewPos;
}

float2 DualFisheyeDiscScale(float4 screenParams)
{
    float aspect = max(screenParams.x / max(screenParams.y, 1.0), 1e-5);

    // Preserve circles in pixel space:
    // radiusX * viewportWidth == radiusY * viewportHeight.
    // Leave a visible gap between the front and rear discs so their union can
    // never be mistaken for one oval / capsule.
    float radiusX = min(DUAL_FISHEYE_MAX_RADIUS_X,
                        DUAL_FISHEYE_MAX_RADIUS_Y / aspect);
    float radiusY = radiusX * aspect;
    return float2(radiusX, radiusY);
}

bool IsFisheyeCenterValidDualAware(float3 viewPos, float4 fisheyeParams2)
{
    if (IsDualFisheye360(fisheyeParams2))
        return dot(viewPos, viewPos) >= 0.0001;

    return IsFisheyeCenterValid(viewPos, fisheyeParams2);
}

float2 ProjectFisheyeCenterDualAware(
    float3 viewPos,
    float4 fisheyeParams,
    float4 fisheyeParams2,
    float4 screenParams)
{
    if (!IsDualFisheye360(fisheyeParams2))
        return ProjectFisheyeCenter(viewPos, fisheyeParams, fisheyeParams2);

    bool backHemisphere;
    float3 lensPos = DualFisheyeLensView(viewPos, backHemisphere);

    float rxy = length(lensPos.xy);
    float negZ = -lensPos.z;
    float theta = min(atan2(rxy, negZ), DUAL_FISHEYE_HALF_PI);

    // Equidistant 180-degree lens: theta = 0 at the disc center and theta =
    // pi/2 at the circumference. This is monotonic and has no generalized-tan
    // singularity inside either hemisphere.
    float normalizedRadius = theta / DUAL_FISHEYE_HALF_PI;
    float2 direction = rxy > 1e-5 ? lensPos.xy / rxy : float2(0.0, 0.0);
    float2 localDisc = normalizedRadius * float2(direction.x, -direction.y);
    float2 discScale = DualFisheyeDiscScale(screenParams);
    float discCenterX = backHemisphere ? DUAL_FISHEYE_CENTER_X : -DUAL_FISHEYE_CENTER_X;

    return float2(discCenterX + localDisc.x * discScale.x,
                  localDisc.y * discScale.y);
}

float3 CalcCovariance2DFisheyeDualAware(
    float3 worldPos,
    float3 cov3d0,
    float3 cov3d1,
    float4x4 matrixV,
    float4 screenParams,
    float4 fisheyeParams,
    float4 fisheyeParams2,
    out float aaFactor)
{
    if (!IsDualFisheye360(fisheyeParams2))
        return CalcCovariance2DFisheye(
            worldPos, cov3d0, cov3d1, matrixV,
            screenParams, fisheyeParams, fisheyeParams2, aaFactor);

    float3 viewPos = mul(matrixV, float4(worldPos, 1.0)).xyz;
    if (dot(viewPos, viewPos) < 0.0001)
    {
        aaFactor = 0.0;
        return 0.0;
    }

    bool backHemisphere;
    float3 lensPos = DualFisheyeLensView(viewPos, backHemisphere);

    float3x3 W = (float3x3)matrixV;
    if (backHemisphere)
    {
        float3x3 backRotation = float3x3(
            -1.0, 0.0,  0.0,
             0.0, 1.0,  0.0,
             0.0, 0.0, -1.0);
        W = mul(backRotation, W);
    }

    float rxy = length(lensPos.xy);
    float negZ = -lensPos.z;
    float theta = min(atan2(rxy, negZ), DUAL_FISHEYE_HALF_PI);

    // For the equidistant model g(theta) = theta:
    // g'(theta) = 1 and g(theta) / r = theta / r.
    float gPrime = 1.0;
    float gTheta = theta;
    float fisheyeS = rxy > 1e-4 ? gTheta / rxy : rcp(max(negZ, 1e-5));

    float2 discScale = DualFisheyeDiscScale(screenParams);
    float focalX = screenParams.x * 0.5 * discScale.x / DUAL_FISHEYE_HALF_PI;
    float focalY = screenParams.y * 0.5 * discScale.y / DUAL_FISHEYE_HALF_PI;

    float d2 = max(dot(lensPos, lensPos), 1e-8);
    float r2 = max(rxy * rxy, 1e-8);
    float kCoeff = rxy > 1e-4 ? (gPrime * negZ / d2 - fisheyeS) / r2 : 0.0;

    float3x3 J = float3x3(
        focalX * (fisheyeS + kCoeff * lensPos.x * lensPos.x),
        focalX * kCoeff * lensPos.x * lensPos.y,
        focalX * gPrime * lensPos.x / d2,

        focalY * kCoeff * lensPos.x * lensPos.y,
        focalY * (fisheyeS + kCoeff * lensPos.y * lensPos.y),
        focalY * gPrime * lensPos.y / d2,

        0.0, 0.0, 0.0
    );

    float3x3 T = mul(J, W);
    float3x3 V = float3x3(
        cov3d0.x, cov3d0.y, cov3d0.z,
        cov3d0.y, cov3d1.x, cov3d1.y,
        cov3d0.z, cov3d1.y, cov3d1.z
    );
    float3x3 cov = mul(T, mul(V, transpose(T)));

    float detOrig = cov._m00 * cov._m11 - cov._m01 * cov._m01;
    cov._m00 += 0.3;
    cov._m11 += 0.3;
    float detBlur = cov._m00 * cov._m11 - cov._m01 * cov._m01;
    aaFactor = sqrt(max(detOrig / max(detBlur, 1e-12), 0.0));

    return float3(cov._m00, cov._m01, cov._m11);
}

// Calls below this include in SplatUtilities.compute become dual-aware while
// ordinary and sub-360 fisheye rendering keep using the original functions.
#define IsFisheyeCenterValid(viewPos, fisheyeParams2) \
    IsFisheyeCenterValidDualAware(viewPos, fisheyeParams2)

#define ProjectFisheyeCenter(viewPos, fisheyeParams, fisheyeParams2) \
    ProjectFisheyeCenterDualAware(viewPos, fisheyeParams, fisheyeParams2, _VecScreenParams)

#define CalcCovariance2DFisheye(worldPos, cov3d0, cov3d1, matrixV, screenParams, fisheyeParams, fisheyeParams2, aaFactor) \
    CalcCovariance2DFisheyeDualAware(worldPos, cov3d0, cov3d1, matrixV, screenParams, fisheyeParams, fisheyeParams2, aaFactor)

#endif
