// SPDX-License-Identifier: MIT
Shader "Gaussian Splatting/Render Splats"
{
    SubShader
    {
        Tags { "RenderType"="Transparent" "Queue"="Transparent" }

        Pass
        {
            ZWrite Off
            Blend OneMinusDstAlpha One
            Cull Off

CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute
#pragma use_dxc
#pragma multi_compile_instancing

#include "UnityCG.cginc"
#include "GaussianSplatting.hlsl"

StructuredBuffer<uint> _OrderBuffer;

// x: enabled, y: near plane, z: far plane, w: reversed-Z flag
float4 _GSBinocularParams;
// x: IPD in metres, y: convergence distance, z: stereo scale,
// w: maximum per-eye NDC shift
float4 _GSBinocularParams2;

struct appdata
{
    uint vertexID : SV_VertexID;
    UNITY_VERTEX_INPUT_INSTANCE_ID
};

struct v2f
{
    half4 col : COLOR0;
    float2 pos : TEXCOORD0;
    float4 vertex : SV_POSITION;
    UNITY_VERTEX_OUTPUT_STEREO
};

StructuredBuffer<SplatViewData> _SplatViewData;
ByteAddressBuffer _SplatSelectedBits;
uint _SplatBitsValid;

float ReconstructCyclopeanDepth(float clipDepth)
{
    float depth01 = saturate(clipDepth);
    if (_GSBinocularParams.w > 0.5)
        depth01 = 1.0 - depth01;

    return lerp(_GSBinocularParams.y, _GSBinocularParams.z, depth01);
}

float2 GetPerEyeProjectionCenter()
{
    // For an asymmetric HMD projection, a point on the optical axis lands at
    // (-P02, -P12) in NDC. UNITY_MATRIX_P resolves to the current eye in
    // multipass and single-pass-instanced stereo variants.
    return float2(-UNITY_MATRIX_P._m02, -UNITY_MATRIX_P._m12);
}

v2f vert(appdata v)
{
    UNITY_SETUP_INSTANCE_ID(v);

    v2f o = (v2f)0;
    UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);

    // In single-pass instanced stereo Unity encodes the eye in SV_InstanceID.
    // unity_InstanceID is the decoded splat instance, so both eyes read the
    // same cyclopean SplatViewData instead of indexing unrelated splats.
    uint drawInstance = unity_InstanceID;
    uint instID = _OrderBuffer[drawInstance];
    SplatViewData view = _SplatViewData[instID];
    float4 centerClipPos = view.pos;
    bool behindCam = centerClipPos.w <= 0;

    if (behindCam)
    {
        o.vertex = asfloat(0x7fc00000); // NaN discards the primitive
    }
    else
    {
        o.col.r = f16tof32(view.color.x >> 16);
        o.col.g = f16tof32(view.color.x);
        o.col.b = f16tof32(view.color.y >> 16);
        o.col.a = f16tof32(view.color.y);

        if (_GSBinocularParams.x > 0.5)
        {
            // The nonlinear fisheye projection and covariance are computed once
            // in a cyclopean camera. Both eyes therefore receive exactly the same
            // shape. Stereo is introduced only as horizontal disparity derived
            // from the splat's linear depth.
            float depth = max(ReconstructCyclopeanDepth(centerClipPos.z),
                              _GSBinocularParams.y + 1e-4);
            float convergence = max(_GSBinocularParams2.y,
                                    _GSBinocularParams.y + 1e-4);
            float disparity = 0.5 * _GSBinocularParams2.x * _GSBinocularParams2.z *
                              (rcp(depth) - rcp(convergence));
            disparity = clamp(disparity,
                              -abs(_GSBinocularParams2.w),
                               abs(_GSBinocularParams2.w));

            float eyeSign = unity_StereoEyeIndex == 0 ? 1.0 : -1.0;
            centerClipPos.x += eyeSign * disparity * centerClipPos.w;

            // Preserve the runtime-provided asymmetric optical centre for each
            // eye. The direct fisheye compute path otherwise assumes NDC (0, 0)
            // for both eyes, which creates additional fusion errors.
            centerClipPos.xy += GetPerEyeProjectionCenter() * centerClipPos.w;
        }

        uint idx = v.vertexID;
        float2 quadPos = float2(idx & 1, (idx >> 1) & 1) * 2.0 - 1.0;
        quadPos *= 2;

        o.pos = quadPos;

        float2 deltaScreenPos =
            (quadPos.x * view.axis1 + quadPos.y * view.axis2) * 2 / _ScreenParams.xy;
        o.vertex = centerClipPos;
        o.vertex.xy += deltaScreenPos * centerClipPos.w;

        // is this splat selected?
        if (_SplatBitsValid)
        {
            uint wordIdx = instID / 32;
            uint bitIdx = instID & 31;
            uint selVal = _SplatSelectedBits.Load(wordIdx * 4);
            if (selVal & (1 << bitIdx))
                o.col.a = -1;
        }
    }

    FlipProjectionIfBackbuffer(o.vertex);
    return o;
}

half4 frag(v2f i) : SV_Target
{
    UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

    float power = -dot(i.pos, i.pos);
    half alpha = exp(power);
    if (i.col.a >= 0)
    {
        alpha = saturate(alpha * i.col.a);
    }
    else
    {
        // "selected" splat: magenta outline, increase opacity, magenta tint
        half3 selectedColor = half3(1, 0, 1);
        if (alpha > 7.0 / 255.0)
        {
            if (alpha < 10.0 / 255.0)
            {
                alpha = 1;
                i.col.rgb = selectedColor;
            }
            alpha = saturate(alpha + 0.3);
        }
        i.col.rgb = lerp(i.col.rgb, selectedColor, 0.5);
    }

    if (alpha < 1.0 / 255.0)
        discard;

    return half4(i.col.rgb * alpha, alpha);
}
ENDCG
        }
    }
}
