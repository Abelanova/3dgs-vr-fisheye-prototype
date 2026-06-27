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
            #pragma multi_compile _ STEREO_INSTANCING_ON STEREO_MULTIVIEW_ON
            #include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"

            struct appdata
            {
                uint vertexID : SV_VertexID;
                UNITY_VERTEX_INPUT_INSTANCE_ID
            };

            struct v2f
            {
                float4 vertex : SV_POSITION;
                float2 uv : TEXCOORD0;
                nointerpolation uint eyeIndex : TEXCOORD1;
                UNITY_VERTEX_INPUT_INSTANCE_ID
                UNITY_VERTEX_OUTPUT_STEREO
            };

            sampler2D _LeftPX, _LeftNX, _LeftPY, _LeftNY, _LeftPZ, _LeftNZ;
            sampler2D _RightPX, _RightNX, _RightPY, _RightNY, _RightPZ, _RightNZ;
            float _FishEnabled;
            float _MonoComposite;
            float _SwapEyes;
            float4 _LeftProjection;
            float4 _RightProjection;
            float4x4 _LeftInvProjection;
            float4x4 _RightInvProjection;
            float4 _FishParams;
            float _MaxTheta;
            float4x4 _LeftEyeToWorld;
            float4x4 _RightEyeToWorld;

            v2f vert(appdata v)
            {
                v2f o = (v2f)0;
                UNITY_SETUP_INSTANCE_ID(v);
                UNITY_TRANSFER_INSTANCE_ID(v, o);
                UNITY_INITIALIZE_VERTEX_OUTPUT_STEREO(o);
                o.vertex = GetFullScreenTriangleVertexPosition(v.vertexID);
                o.uv = GetFullScreenTriangleTexCoord(v.vertexID);
                o.eyeIndex = unity_StereoEyeIndex;
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

            float3 ProjectionRay(float2 ndc)
            {
                float4 view = mul(UNITY_MATRIX_I_P, float4(ndc, -1.0, 1.0));
                float safeW = abs(view.w) > 1e-6 ? view.w : (view.w < 0.0 ? -1e-6 : 1e-6);
                view.xyz /= safeW;
                return normalize(float3(view.x, view.y, -view.z));
            }

            half4 frag(v2f i) : SV_Target
            {
                UNITY_SETUP_INSTANCE_ID(i);
                UNITY_SETUP_STEREO_EYE_INDEX_POST_VERTEX(i);

                uint sampleEyeIndex = _MonoComposite > 0.5 ? 0 : unity_StereoEyeIndex;
                if (_SwapEyes > 0.5 && _MonoComposite < 0.5)
                    sampleEyeIndex = 1 - sampleEyeIndex;

                float2 ndc = i.uv * 2.0 - 1.0;
                float3 perspectiveRay = ProjectionRay(ndc);
                float3 direction;
                if (_FishEnabled < 0.5)
                {
                    direction = perspectiveRay;
                }
                else
                {
                    float2 currentProjectionScale = abs(float2(UNITY_MATRIX_P._m00, UNITY_MATRIX_P._m11));
                    float2 centeredNdc = perspectiveRay.xy / max(abs(perspectiveRay.z), 1e-6) * currentProjectionScale;
                    float2 p = float2(centeredNdc.x / max(abs(_FishParams.z), 1e-6),
                                      centeredNdc.y / max(abs(_FishParams.w), 1e-6));
                    float r = length(p);
                    float theta = _FishParams.x * atan(r * _FishParams.y);
                    if (theta > _MaxTheta - 0.01)
                        return half4(0, 0, 0, 1);

                    float sinTheta, cosTheta;
                    sincos(theta, sinTheta, cosTheta);
                    float2 radial = r > 1e-6 ? p / r : 0;
                    direction = float3(radial * sinTheta, cosTheta);
                }

                float3 worldDirection = mul((float3x3)UNITY_MATRIX_I_V, float3(direction.x, direction.y, -direction.z));

                return sampleEyeIndex == 0 ? SampleLeft(worldDirection) : SampleRight(worldDirection);
            }
            ENDHLSL
        }
    }
}
