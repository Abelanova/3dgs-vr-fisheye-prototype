Shader "Hidden/Gaussian Splatting/High Quality Fisheye Composite"
{
    Properties { _MainTex ("Texture", 2D) = "black" {} }
    SubShader
    {
        Tags { "Queue"="Overlay" "RenderType"="Opaque" }
        Pass
        {
            ZWrite Off
            ZTest Always
            Cull Off

            HLSLPROGRAM
            #pragma vertex vert
            #pragma fragment frag
            #include "UnityCG.cginc"

            struct appdata { float4 vertex : POSITION; float2 uv : TEXCOORD0; };
            struct v2f { float4 vertex : SV_POSITION; float2 uv : TEXCOORD0; };

            sampler2D _FacePX, _FaceNX, _FacePY, _FaceNY, _FacePZ, _FaceNZ;
            float _FishEnabled;
            float4 _PerspectiveScale;
            float4 _FishParams; // x=k, y=1/k, z=projection scale x, w=projection scale y
            float _MaxTheta;
            float4x4 _CameraToWorld;

            v2f vert(appdata v)
            {
                v2f o;
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 SampleFaces(float3 d)
            {
                float3 a = abs(d);
                float2 uv;
                if (a.x >= a.y && a.x >= a.z)
                {
                    if (d.x >= 0)
                    {
                        uv = float2(-d.z, d.y) / a.x * 0.5 + 0.5;
                        return tex2D(_FacePX, uv);
                    }
                    uv = float2(d.z, d.y) / a.x * 0.5 + 0.5;
                    return tex2D(_FaceNX, uv);
                }
                if (a.y >= a.z)
                {
                    if (d.y >= 0)
                    {
                        uv = float2(d.x, -d.z) / a.y * 0.5 + 0.5;
                        return tex2D(_FacePY, uv);
                    }
                    uv = float2(d.x, d.z) / a.y * 0.5 + 0.5;
                    return tex2D(_FaceNY, uv);
                }
                if (d.z >= 0)
                {
                    uv = float2(d.x, d.y) / a.z * 0.5 + 0.5;
                    return tex2D(_FacePZ, uv);
                }
                uv = float2(-d.x, d.y) / a.z * 0.5 + 0.5;
                return tex2D(_FaceNZ, uv);
            }

            half4 frag(v2f i) : SV_Target
            {
                float2 ndc = i.uv * 2.0 - 1.0;
                if (_FishEnabled < 0.5)
                {
                    float3 perspectiveDirection = normalize(float3(
                        ndc.x / max(abs(_PerspectiveScale.x), 1e-6),
                        ndc.y / max(abs(_PerspectiveScale.y), 1e-6), 1.0));
                    float3 worldDirection = mul((float3x3)_CameraToWorld, perspectiveDirection);
                    return SampleFaces(worldDirection);
                }

                float2 p = float2(ndc.x / max(abs(_FishParams.z), 1e-6),
                                  ndc.y / max(abs(_FishParams.w), 1e-6));
                float r = length(p);
                float theta = _FishParams.x * atan(r * _FishParams.y);
                theta = min(theta, max(_MaxTheta - 0.001, 0.001));

                float sinTheta, cosTheta;
                sincos(theta, sinTheta, cosTheta);
                float2 radial = r > 1e-6 ? p / r : 0;
                float3 direction = float3(radial * sinTheta, cosTheta);
                float3 worldDirection = mul((float3x3)_CameraToWorld, direction);
                return SampleFaces(worldDirection);
            }
            ENDHLSL
        }
    }
}
