Shader "Hidden/Gaussian Splatting/VR High Quality Fisheye Composite"
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
            #pragma multi_compile_instancing
            #include "UnityCG.cginc"

            struct appdata
            {
                float4 vertex : POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _LeftPX, _LeftNX, _LeftPY, _LeftNY, _LeftPZ, _LeftNZ;
            sampler2D _RightPX, _RightNX, _RightPY, _RightNY, _RightPZ, _RightNZ;
            float _FishEnabled;
            float4 _PerspectiveScale;
            float4 _FishParams;
            float _MaxTheta;
            float4x4 _LeftEyeToWorld;
            float4x4 _RightEyeToWorld;

            v2f vert(appdata v)
            {
                v2f o;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = UnityObjectToClipPos(v.vertex);
                o.uv = v.uv;
                return o;
            }

            float4 SampleLeft(float3 d)
            {
                float3 a = abs(d);
                float2 uv;
                if (a.x >= a.y && a.x >= a.z)
                {
                    if (d.x >= 0)
                    {
                        uv = float2(-d.z, d.y) / a.x * 0.5 + 0.5;
                        return tex2D(_LeftPX, uv);
                    }
                    uv = float2(d.z, d.y) / a.x * 0.5 + 0.5;
                    return tex2D(_LeftNX, uv);
                }
                if (a.y >= a.z)
                {
                    if (d.y >= 0)
                    {
                        uv = float2(d.x, -d.z) / a.y * 0.5 + 0.5;
                        return tex2D(_LeftPY, uv);
                    }
                    uv = float2(d.x, d.z) / a.y * 0.5 + 0.5;
                    return tex2D(_LeftNY, uv);
                }
                if (d.z >= 0)
                {
                    uv = float2(d.x, d.y) / a.z * 0.5 + 0.5;
                    return tex2D(_LeftPZ, uv);
                }
                uv = float2(-d.x, d.y) / a.z * 0.5 + 0.5;
                return tex2D(_LeftNZ, uv);
            }

            float4 SampleRight(float3 d)
            {
                float3 a = abs(d);
                float2 uv;
                if (a.x >= a.y && a.x >= a.z)
                {
                    if (d.x >= 0)
                    {
                        uv = float2(-d.z, d.y) / a.x * 0.5 + 0.5;
                        return tex2D(_RightPX, uv);
                    }
                    uv = float2(d.z, d.y) / a.x * 0.5 + 0.5;
                    return tex2D(_RightNX, uv);
                }
                if (a.y >= a.z)
                {
                    if (d.y >= 0)
                    {
                        uv = float2(d.x, -d.z) / a.y * 0.5 + 0.5;
                        return tex2D(_RightPY, uv);
                    }
                    uv = float2(d.x, d.z) / a.y * 0.5 + 0.5;
                    return tex2D(_RightNY, uv);
                }
                if (d.z >= 0)
                {
                    uv = float2(d.x, d.y) / a.z * 0.5 + 0.5;
                    return tex2D(_RightPZ, uv);
                }
                uv = float2(-d.x, d.y) / a.z * 0.5 + 0.5;
                return tex2D(_RightNZ, uv);
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                float2 ndc = i.uv * 2.0 - 1.0;
                float3 direction;
                if (_FishEnabled < 0.5)
                {
                    direction = normalize(float3(
                        ndc.x / max(abs(_PerspectiveScale.x), 1e-6),
                        ndc.y / max(abs(_PerspectiveScale.y), 1e-6), 1.0));
                }
                else
                {
                    float2 p = float2(ndc.x / max(abs(_FishParams.z), 1e-6),
                                      ndc.y / max(abs(_FishParams.w), 1e-6));
                    float r = length(p);
                    float theta = _FishParams.x * atan(r * _FishParams.y);
                    if (theta > _MaxTheta - 0.01)
                        return half4(0, 0, 0, 1);

                    float sinTheta, cosTheta;
                    sincos(theta, sinTheta, cosTheta);
                    float2 radial = r > 1e-6 ? p / r : 0;
                    direction = float3(radial * sinTheta, cosTheta);
                }

                if (unity_StereoEyeIndex == 0)
                {
                    float3 worldDirection = mul((float3x3)_LeftEyeToWorld, direction);
                    return SampleLeft(worldDirection);
                }

                float3 worldDirection = mul((float3x3)_RightEyeToWorld, direction);
                return SampleRight(worldDirection);
            }
            ENDHLSL
        }
    }
}
