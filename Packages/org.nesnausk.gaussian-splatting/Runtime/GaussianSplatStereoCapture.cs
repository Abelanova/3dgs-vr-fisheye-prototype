// SPDX-License-Identifier: MIT

using System.IO;
using UnityEngine;
using UnityEngine.XR;

namespace GaussianSplatting.Runtime
{
    public static class GaussianSplatStereoCapture
    {
        public static bool WriteStereoPair(Camera camera, string path, Color backgroundColor,
            Vector3 headOffset, Quaternion headRotation, out string message)
        {
            message = string.Empty;
            if (camera == null)
            {
                message = "Camera is missing.";
                return false;
            }

            int width = XRSettings.eyeTextureWidth > 0 ? XRSettings.eyeTextureWidth : Mathf.Max(camera.pixelWidth, 512);
            int height = XRSettings.eyeTextureHeight > 0 ? XRSettings.eyeTextureHeight : Mathf.Max(camera.pixelHeight, 512);
            if (width <= 0 || height <= 0)
            {
                message = "Invalid eye texture dimensions.";
                return false;
            }

            float ipd = GetIpd(camera);
            Texture2D left = RenderEyeCamera(camera, -ipd * 0.5f, width, height, backgroundColor,
                headOffset, headRotation);
            Texture2D right = RenderEyeCamera(camera, ipd * 0.5f, width, height, backgroundColor,
                headOffset, headRotation);

            if (left == null || right == null)
            {
                Object.Destroy(left);
                Object.Destroy(right);
                message = "Eye render failed.";
                return false;
            }

            var pair = new Texture2D(width * 2, height, TextureFormat.RGBA32, false, false)
            {
                name = "Gaussian Splat Stereo Pair"
            };
            pair.SetPixels(0, 0, width, height, left.GetPixels());
            pair.SetPixels(width, 0, width, height, right.GetPixels());
            pair.Apply(false, false);

            Directory.CreateDirectory(Path.GetDirectoryName(path) ?? ".");
            File.WriteAllBytes(path, pair.EncodeToPNG());
            message = $"Stereo pair {width}x{height} per eye, IPD {ipd:F3}m.";

            Object.Destroy(left);
            Object.Destroy(right);
            Object.Destroy(pair);
            return true;
        }

        static float GetIpd(Camera camera)
        {
            const float fallbackIpd = 0.064f;
            if (!camera.stereoEnabled)
                return fallbackIpd;

            Matrix4x4 left = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 right = camera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
            float measuredIpd = Vector3.Distance(EyePosition(left), EyePosition(right));
            return measuredIpd > 0.001f && measuredIpd < 0.2f ? measuredIpd : fallbackIpd;
        }

        static Texture2D RenderEyeCamera(Camera source, float localXOffset, int width, int height,
            Color backgroundColor, Vector3 headOffset, Quaternion headRotation)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            target.name = "Gaussian Splat Stereo Eye";

            var go = new GameObject($"Gaussian Splat Stereo Eye {localXOffset:F3}");
            var eyeCamera = go.AddComponent<Camera>();
            try
            {
                eyeCamera.CopyFrom(source);
                eyeCamera.enabled = false;
                eyeCamera.stereoTargetEye = StereoTargetEyeMask.None;
                eyeCamera.targetTexture = target;
                eyeCamera.backgroundColor = backgroundColor;
                eyeCamera.aspect = (float)width / height;
                Quaternion poseRotation = source.transform.rotation * headRotation;
                Vector3 posePosition = source.transform.position + source.transform.rotation * headOffset;
                eyeCamera.transform.SetPositionAndRotation(
                    posePosition + poseRotation * new Vector3(localXOffset, 0.0f, 0.0f),
                    poseRotation);
                eyeCamera.Render();

                var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false)
                {
                    name = "Gaussian Splat Stereo Eye"
                };
                RenderTexture previous = RenderTexture.active;
                RenderTexture.active = target;
                texture.ReadPixels(new Rect(0, 0, width, height), 0, 0, false);
                texture.Apply(false, false);
                RenderTexture.active = previous;
                return texture;
            }
            finally
            {
                Object.Destroy(go);
                RenderTexture.ReleaseTemporary(target);
            }
        }

        static Vector3 EyePosition(Matrix4x4 viewMatrix)
        {
            Matrix4x4 inverse = viewMatrix.inverse;
            Vector4 column = inverse.GetColumn(3);
            return new Vector3(column.x, column.y, column.z);
        }
    }
}
