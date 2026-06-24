// SPDX-License-Identifier: MIT
Shader "Hidden/Gaussian Splatting/Composite"
{
    SubShader
    {
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

CGPROGRAM
#pragma vertex vert
#pragma fragment frag
#pragma require compute
#pragma use_dxc
#include "UnityCG.cginc"

struct v2f
{
    float4 vertex : SV_POSITION;
};

v2f vert (uint vtxID : SV_VertexID)
{
    v2f o;
    float2 quadPos = float2(vtxID&1, (vtxID>>1)&1) * 4.0 - 1.0;
	o.vertex = float4(quadPos, 1, 1);
    return o;
}

Texture2D _GaussianSplatRT;

half4 frag (v2f i) : SV_Target
{
    half4 col = _GaussianSplatRT.Load(int3(i.vertex.xy, 0));
    return float4(GammaToLinearSpace(col.rgb/col.a),col.a);
}
ENDCG
        }

        // XR single-pass render targets are 2D texture arrays. The Gaussian
        // renderer fills each eye slice explicitly, so composite one selected
        // slice at a time without relying on stereo instance-ID rewriting.
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off
            Blend SrcAlpha OneMinusSrcAlpha

CGPROGRAM
#pragma vertex vertXR
#pragma fragment fragXR
#pragma require compute
#pragma use_dxc
#include "UnityCG.cginc"

struct v2fXR
{
    float4 vertex : SV_POSITION;
};

v2fXR vertXR(uint vtxID : SV_VertexID)
{
    v2fXR o;
    float2 quadPos = float2(vtxID & 1, (vtxID >> 1) & 1) * 4.0 - 1.0;
    o.vertex = float4(quadPos, 1, 1);
    return o;
}

Texture2DArray _GaussianSplatRT;
uint _GaussianEyeSlice;

half4 fragXR(v2fXR i) : SV_Target
{
    half4 col = _GaussianSplatRT.Load(int4(i.vertex.xy, _GaussianEyeSlice, 0));
    half safeAlpha = max(col.a, 1.0h / 65535.0h);
    return half4(GammaToLinearSpace(col.rgb / safeAlpha), col.a);
}
ENDCG
        }
    }
}
