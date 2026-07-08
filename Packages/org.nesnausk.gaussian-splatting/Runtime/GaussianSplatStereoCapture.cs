// SPDX-License-Identifier: MIT

using System.IO;
using System.Text;
using UnityEngine;
using UnityEngine.XR;

namespace GaussianSplatting.Runtime
{
    public static class GaussianSplatStereoCapture
    {
        public static bool WriteStereoPair(Camera camera, string path, Color backgroundColor,
            Vector3 headOffset, Quaternion headRotation, out string message)
        {
            return WriteStereoDiagnostics(camera, null, path, backgroundColor, headOffset, headRotation, out message);
        }

        public static bool WriteStereoDiagnostics(Camera camera, GaussianSplatRenderer splat, string path,
            Color backgroundColor, Vector3 headOffset, Quaternion headRotation, out string message)
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
            EyePose leftPose = GetEyePose(camera, -ipd * 0.5f, headOffset, headRotation);
            EyePose rightPose = GetEyePose(camera, ipd * 0.5f, headOffset, headRotation);
            Texture2D left = RenderEyeCamera(camera, leftPose, width, height, backgroundColor);
            Texture2D right = RenderEyeCamera(camera, rightPose, width, height, backgroundColor);

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
            string basePath = Path.Combine(Path.GetDirectoryName(path) ?? ".",
                Path.GetFileNameWithoutExtension(path));
            WritePreviewImages(left, right, basePath);
            WriteFeatureMeasurements(camera, splat, basePath, width, height, leftPose, rightPose, ipd);
            message = $"Stereo diagnostics {width}x{height} per eye, IPD {ipd:F3}m, previews and feature measurements written.";

            Object.Destroy(left);
            Object.Destroy(right);
            Object.Destroy(pair);
            return true;
        }

        static void WritePreviewImages(Texture2D left, Texture2D right, string basePath)
        {
            int width = left.width;
            int height = left.height;
            Color32[] leftPixels = left.GetPixels32();
            Color32[] rightPixels = right.GetPixels32();

            WriteTexture(basePath + "_left.png", width, height, leftPixels);
            WriteTexture(basePath + "_right.png", width, height, rightPixels);
            WriteTexture(basePath + "_anaglyph_red_cyan.png", width, height,
                BuildAnaglyph(leftPixels, rightPixels));
            WriteTexture(basePath + "_overlay_50.png", width, height,
                BuildOverlay(leftPixels, rightPixels));
            WriteTexture(basePath + "_difference_heatmap.png", width, height,
                BuildDifferenceHeatmap(leftPixels, rightPixels));
            WriteWiggleGif(basePath + "_wiggle.gif", width, height, leftPixels, rightPixels);
        }

        static void WriteTexture(string path, int width, int height, Color32[] pixels)
        {
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            File.WriteAllBytes(path, texture.EncodeToPNG());
            Object.Destroy(texture);
        }

        static Color32[] BuildAnaglyph(Color32[] left, Color32[] right)
        {
            var pixels = new Color32[left.Length];
            for (int i = 0; i < pixels.Length; ++i)
            {
                byte l = Luma(left[i]);
                byte r = Luma(right[i]);
                pixels[i] = new Color32(l, r, r, 255);
            }

            return pixels;
        }

        static Color32[] BuildOverlay(Color32[] left, Color32[] right)
        {
            var pixels = new Color32[left.Length];
            for (int i = 0; i < pixels.Length; ++i)
            {
                pixels[i] = new Color32(
                    (byte)((left[i].r + right[i].r) >> 1),
                    (byte)((left[i].g + right[i].g) >> 1),
                    (byte)((left[i].b + right[i].b) >> 1),
                    255);
            }

            return pixels;
        }

        static Color32[] BuildDifferenceHeatmap(Color32[] left, Color32[] right)
        {
            var pixels = new Color32[left.Length];
            for (int i = 0; i < pixels.Length; ++i)
            {
                float t = Mathf.Clamp01(Mathf.Abs(Luma(left[i]) - Luma(right[i])) / 96.0f);
                byte r = (byte)Mathf.RoundToInt(255.0f * t);
                byte g = (byte)Mathf.RoundToInt(255.0f * (1.0f - Mathf.Abs(t * 2.0f - 1.0f)));
                byte b = (byte)Mathf.RoundToInt(255.0f * (1.0f - t));
                pixels[i] = new Color32(r, g, b, 255);
            }

            return pixels;
        }

        static byte Luma(Color32 c)
        {
            return (byte)((c.r * 54 + c.g * 183 + c.b * 19) >> 8);
        }

        static void WriteFeatureMeasurements(Camera camera, GaussianSplatRenderer splat, string basePath,
            int width, int height, EyePose leftPose, EyePose rightPose, float ipd)
        {
            FeatureProbe[] probes = BuildFeatureProbes(camera, splat, leftPose, rightPose);
            string csvPath = basePath + "_feature_disparity.csv";
            string txtPath = basePath + "_feature_disparity.txt";

            var csv = new StringBuilder();
            csv.AppendLine("label,world_x,world_y,world_z,left_visible,left_x,left_y,right_visible,right_x,right_y,delta_x,delta_y");

            var txt = new StringBuilder();
            txt.AppendLine("Stereo feature disparity");
            txt.AppendLine("Pixel origin: top-left of each per-eye image.");
            txt.AppendLine($"eyeWidth={width}");
            txt.AppendLine($"eyeHeight={height}");
            txt.AppendLine($"ipdMeters={ipd:F6}");
            txt.AppendLine();

            foreach (FeatureProbe probe in probes)
            {
                bool leftVisible = ProjectWorldPoint(camera, splat, probe.worldPosition, leftPose, width, height, out Vector2 leftPixel);
                bool rightVisible = ProjectWorldPoint(camera, splat, probe.worldPosition, rightPose, width, height, out Vector2 rightPixel);
                float dx = leftVisible && rightVisible ? leftPixel.x - rightPixel.x : float.NaN;
                float dy = leftVisible && rightVisible ? leftPixel.y - rightPixel.y : float.NaN;

                csv.Append(probe.label).Append(',')
                    .Append(Format(probe.worldPosition.x)).Append(',')
                    .Append(Format(probe.worldPosition.y)).Append(',')
                    .Append(Format(probe.worldPosition.z)).Append(',')
                    .Append(leftVisible ? "1" : "0").Append(',')
                    .Append(Format(leftPixel.x)).Append(',')
                    .Append(Format(leftPixel.y)).Append(',')
                    .Append(rightVisible ? "1" : "0").Append(',')
                    .Append(Format(rightPixel.x)).Append(',')
                    .Append(Format(rightPixel.y)).Append(',')
                    .Append(Format(dx)).Append(',')
                    .Append(Format(dy)).AppendLine();

                txt.Append(probe.label).Append(": ")
                    .Append("L(").Append(Format(leftPixel.x)).Append(", ").Append(Format(leftPixel.y)).Append(") ")
                    .Append("R(").Append(Format(rightPixel.x)).Append(", ").Append(Format(rightPixel.y)).Append(") ")
                    .Append("delta(").Append(Format(dx)).Append(", ").Append(Format(dy)).Append(") ")
                    .Append(leftVisible && rightVisible ? "visible" : "not fully visible")
                    .AppendLine();
            }

            File.WriteAllText(csvPath, csv.ToString());
            File.WriteAllText(txtPath, txt.ToString());
        }

        static FeatureProbe[] BuildFeatureProbes(Camera camera, GaussianSplatRenderer splat,
            EyePose leftPose, EyePose rightPose)
        {
            if (splat != null && splat.m_Asset != null)
            {
                Bounds bounds = BoundsFromSplat(splat);
                Vector3 center = bounds.center;
                Vector3 extents = bounds.extents;
                Vector3 headCenter = (leftPose.position + rightPose.position) * 0.5f;
                Vector3[] corners = BoundsCorners(bounds);
                Vector3 nearGround = ClosestLowestCorner(corners, headCenter);
                Vector3 farTarget = FarthestCorner(corners, headCenter);

                return new[]
                {
                    new FeatureProbe("center", center),
                    new FeatureProbe("left_edge", center - camera.transform.right * extents.x),
                    new FeatureProbe("right_edge", center + camera.transform.right * extents.x),
                    new FeatureProbe("near_ground", nearGround),
                    new FeatureProbe("far_target", farTarget)
                };
            }

            Transform tr = camera.transform;
            Vector3 origin = (leftPose.position + rightPose.position) * 0.5f;
            return new[]
            {
                new FeatureProbe("center", origin + tr.forward * 3.0f),
                new FeatureProbe("left_edge", origin + tr.forward * 3.0f - tr.right * 1.5f),
                new FeatureProbe("right_edge", origin + tr.forward * 3.0f + tr.right * 1.5f),
                new FeatureProbe("near_ground", origin + tr.forward * 1.5f - tr.up * 1.0f),
                new FeatureProbe("far_target", origin + tr.forward * 8.0f)
            };
        }

        static Bounds BoundsFromSplat(GaussianSplatRenderer splat)
        {
            Vector3 min = splat.m_Asset.boundsMin;
            Vector3 max = splat.m_Asset.boundsMax;
            Bounds localBounds = default;
            localBounds.SetMinMax(min, max);
            Vector3[] corners = BoundsCorners(localBounds);
            Bounds worldBounds = new(splat.transform.TransformPoint(corners[0]), Vector3.zero);
            for (int i = 1; i < corners.Length; ++i)
                worldBounds.Encapsulate(splat.transform.TransformPoint(corners[i]));
            return worldBounds;
        }

        static Vector3[] BoundsCorners(Bounds bounds)
        {
            Vector3 min = bounds.min;
            Vector3 max = bounds.max;
            return new[]
            {
                new Vector3(min.x, min.y, min.z),
                new Vector3(max.x, min.y, min.z),
                new Vector3(min.x, max.y, min.z),
                new Vector3(max.x, max.y, min.z),
                new Vector3(min.x, min.y, max.z),
                new Vector3(max.x, min.y, max.z),
                new Vector3(min.x, max.y, max.z),
                new Vector3(max.x, max.y, max.z)
            };
        }

        static Vector3 ClosestLowestCorner(Vector3[] corners, Vector3 point)
        {
            float minY = corners[0].y;
            for (int i = 1; i < corners.Length; ++i)
                minY = Mathf.Min(minY, corners[i].y);

            Vector3 best = corners[0];
            float bestDistance = float.PositiveInfinity;
            foreach (Vector3 corner in corners)
            {
                if (Mathf.Abs(corner.y - minY) > 0.001f)
                    continue;
                float distance = (corner - point).sqrMagnitude;
                if (distance < bestDistance)
                {
                    bestDistance = distance;
                    best = corner;
                }
            }
            return best;
        }

        static Vector3 FarthestCorner(Vector3[] corners, Vector3 point)
        {
            Vector3 best = corners[0];
            float bestDistance = float.NegativeInfinity;
            foreach (Vector3 corner in corners)
            {
                float distance = (corner - point).sqrMagnitude;
                if (distance > bestDistance)
                {
                    bestDistance = distance;
                    best = corner;
                }
            }
            return best;
        }

        static bool ProjectWorldPoint(Camera camera, GaussianSplatRenderer splat, Vector3 worldPosition,
            EyePose eyePose, int width, int height, out Vector2 pixel)
        {
            pixel = Vector2.zero;
            Matrix4x4 view = Matrix4x4.TRS(eyePose.position, eyePose.rotation, Vector3.one).inverse;
            Vector3 viewPosition = view.MultiplyPoint(worldPosition);

            if (splat != null && splat.m_FisheyeStrength > 0.0001f)
            {
                var (fisheyeParams, fisheyeParams2) = CalcFisheyeParams(splat, camera, (float)width / height);
                float rxy = new Vector2(viewPosition.x, viewPosition.y).magnitude;
                float negZ = -viewPosition.z;
                float theta = Mathf.Atan2(rxy, negZ);
                if (theta > fisheyeParams2.y - 0.01f || viewPosition.sqrMagnitude < 0.0001f)
                    return false;

                float k = fisheyeParams.y;
                float invK = fisheyeParams.z;
                float gTheta = k * Mathf.Tan(theta * invK);
                float fisheyeScale = rxy > 1e-4f ? gTheta / rxy : negZ > 0.0f ? 1.0f / negZ : 0.0f;
                float clipX = fisheyeParams.w * fisheyeScale * viewPosition.x;
                float clipY = -fisheyeParams2.x * fisheyeScale * viewPosition.y;
                pixel = new Vector2(
                    (clipX * 0.5f + 0.5f) * width,
                    (-clipY * 0.5f + 0.5f) * height);
                return IsFinite(pixel) && pixel.x >= 0.0f && pixel.x <= width && pixel.y >= 0.0f && pixel.y <= height;
            }

            Matrix4x4 projection = Matrix4x4.Perspective(camera.fieldOfView, (float)width / height,
                camera.nearClipPlane, camera.farClipPlane);
            Vector4 clip = projection * new Vector4(viewPosition.x, viewPosition.y, viewPosition.z, 1.0f);
            if (clip.w <= 0.0001f)
                return false;

            Vector3 ndc = new(clip.x / clip.w, clip.y / clip.w, clip.z / clip.w);
            pixel = new Vector2(
                (ndc.x * 0.5f + 0.5f) * width,
                (-ndc.y * 0.5f + 0.5f) * height);
            return IsFinite(pixel) && ndc.z >= -1.0f && ndc.z <= 1.0f &&
                pixel.x >= 0.0f && pixel.x <= width && pixel.y >= 0.0f && pixel.y <= height;
        }

        static (Vector4, Vector4) CalcFisheyeParams(GaussianSplatRenderer splat, Camera camera, float aspect)
        {
            if (camera.TryGetComponent<GaussianSplatProjectionCamera>(out var projectionCamera) &&
                projectionCamera.isActiveAndEnabled &&
                projectionCamera.role == GaussianSplatProjectionCameraRole.Capture)
                return (Vector4.zero, Vector4.zero);

            float t = Mathf.Clamp01(splat.m_FisheyeStrength);
            float verticalFov = Mathf.Clamp(splat.m_FisheyeFieldOfView > 0.0f
                ? splat.m_FisheyeFieldOfView
                : camera.fieldOfView, 20.0f, 359.9f);
            if (t <= 0.0f || camera.orthographic)
                return (Vector4.zero, Vector4.zero);

            float halfVerticalFov = verticalFov * Mathf.Deg2Rad * 0.5f;
            float safeAspect = Mathf.Max(aspect, 0.0001f);
            float p11 = 1.0f / Mathf.Tan(halfVerticalFov);
            float p00 = p11 / safeAspect;
            float halfFovX = Mathf.Atan2(1.0f, p00);
            float halfFovY = Mathf.Atan2(1.0f, p11);

            float kMin = verticalFov / 180.0f + 0.15f;
            float kStart = Mathf.Max(1.0f, verticalFov / 180.0f + 0.05f);
            float k = kStart * Mathf.Pow(kMin / kStart, t);
            float invK = 1.0f / k;
            float cornerScale = 1.0f + (Mathf.Sqrt(2.0f) - 1.0f) * t;
            float maxTheta = Mathf.Min(k * Mathf.PI * 0.5f, 3.13f);

            float effHalfFovX = Mathf.Min(halfFovX, maxTheta - 0.01f);
            float projMat00 = cornerScale / (k * Mathf.Tan(effHalfFovX * invK));
            float effHalfFovY = Mathf.Min(halfFovY, maxTheta - 0.01f);
            float projMat11 = cornerScale / (k * Mathf.Tan(effHalfFovY * invK));

            return (new Vector4(t, k, invK, projMat00), new Vector4(projMat11, maxTheta, 0, 0));
        }

        static string Format(float value)
        {
            return float.IsNaN(value) || float.IsInfinity(value) ? "nan" : value.ToString("F3",
                System.Globalization.CultureInfo.InvariantCulture);
        }

        static void WriteWiggleGif(string path, int width, int height, Color32[] left, Color32[] right)
        {
            using var stream = new FileStream(path, FileMode.Create, FileAccess.Write);
            WriteAscii(stream, "GIF89a");
            WriteUInt16(stream, width);
            WriteUInt16(stream, height);
            stream.WriteByte(0xF7);
            stream.WriteByte(0);
            stream.WriteByte(0);
            WriteRgb332Palette(stream);
            WriteLoopExtension(stream);
            WriteGifFrame(stream, width, height, left, 28);
            WriteGifFrame(stream, width, height, right, 28);
            stream.WriteByte(0x3B);
        }

        static void WriteGifFrame(Stream stream, int width, int height, Color32[] pixels, int delayCentiseconds)
        {
            stream.WriteByte(0x21);
            stream.WriteByte(0xF9);
            stream.WriteByte(4);
            stream.WriteByte(0);
            WriteUInt16(stream, delayCentiseconds);
            stream.WriteByte(0);
            stream.WriteByte(0);

            stream.WriteByte(0x2C);
            WriteUInt16(stream, 0);
            WriteUInt16(stream, 0);
            WriteUInt16(stream, width);
            WriteUInt16(stream, height);
            stream.WriteByte(0);
            stream.WriteByte(8);

            byte[] data = EncodeUncompressedGifLzw(width, height, pixels);
            for (int offset = 0; offset < data.Length;)
            {
                int count = Mathf.Min(255, data.Length - offset);
                stream.WriteByte((byte)count);
                stream.Write(data, offset, count);
                offset += count;
            }

            stream.WriteByte(0);
        }

        static byte[] EncodeUncompressedGifLzw(int width, int height, Color32[] pixels)
        {
            using var packed = new MemoryStream();
            int bitBuffer = 0;
            int bitCount = 0;

            void WriteCode(int code)
            {
                bitBuffer |= code << bitCount;
                bitCount += 9;
                while (bitCount >= 8)
                {
                    packed.WriteByte((byte)(bitBuffer & 0xFF));
                    bitBuffer >>= 8;
                    bitCount -= 8;
                }
            }

            WriteCode(256);
            int codesSinceClear = 0;
            for (int y = height - 1; y >= 0; --y)
            {
                int row = y * width;
                for (int x = 0; x < width; ++x)
                {
                    if (codesSinceClear >= 250)
                    {
                        WriteCode(256);
                        codesSinceClear = 0;
                    }

                    WriteCode(ToRgb332Index(pixels[row + x]));
                    ++codesSinceClear;
                }
            }

            WriteCode(257);
            if (bitCount > 0)
                packed.WriteByte((byte)(bitBuffer & 0xFF));
            return packed.ToArray();
        }

        static byte ToRgb332Index(Color32 c)
        {
            return (byte)((c.r & 0xE0) | ((c.g & 0xE0) >> 3) | (c.b >> 6));
        }

        static void WriteRgb332Palette(Stream stream)
        {
            for (int i = 0; i < 256; ++i)
            {
                int r = (i >> 5) & 0x07;
                int g = (i >> 2) & 0x07;
                int b = i & 0x03;
                stream.WriteByte((byte)(r * 255 / 7));
                stream.WriteByte((byte)(g * 255 / 7));
                stream.WriteByte((byte)(b * 255 / 3));
            }
        }

        static void WriteLoopExtension(Stream stream)
        {
            stream.WriteByte(0x21);
            stream.WriteByte(0xFF);
            stream.WriteByte(11);
            WriteAscii(stream, "NETSCAPE2.0");
            stream.WriteByte(3);
            stream.WriteByte(1);
            WriteUInt16(stream, 0);
            stream.WriteByte(0);
        }

        static void WriteAscii(Stream stream, string value)
        {
            byte[] bytes = Encoding.ASCII.GetBytes(value);
            stream.Write(bytes, 0, bytes.Length);
        }

        static void WriteUInt16(Stream stream, int value)
        {
            stream.WriteByte((byte)(value & 0xFF));
            stream.WriteByte((byte)((value >> 8) & 0xFF));
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

        static Texture2D RenderEyeCamera(Camera source, EyePose eyePose, int width, int height,
            Color backgroundColor)
        {
            RenderTexture target = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32,
                RenderTextureReadWrite.sRGB);
            target.name = "Gaussian Splat Stereo Eye";

            var go = new GameObject("Gaussian Splat Stereo Eye");
            var eyeCamera = go.AddComponent<Camera>();
            try
            {
                eyeCamera.CopyFrom(source);
                eyeCamera.enabled = false;
                eyeCamera.stereoTargetEye = StereoTargetEyeMask.None;
                eyeCamera.targetTexture = target;
                eyeCamera.backgroundColor = backgroundColor;
                eyeCamera.aspect = (float)width / height;
                eyeCamera.transform.SetPositionAndRotation(eyePose.position, eyePose.rotation);
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

        static EyePose GetEyePose(Camera source, float localXOffset, Vector3 headOffset, Quaternion headRotation)
        {
            Quaternion poseRotation = source.transform.rotation * headRotation;
            Vector3 posePosition = source.transform.position + source.transform.rotation * headOffset;
            return new EyePose(posePosition + poseRotation * new Vector3(localXOffset, 0.0f, 0.0f), poseRotation);
        }

        static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

        static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);

        readonly struct EyePose
        {
            public readonly Vector3 position;
            public readonly Quaternion rotation;

            public EyePose(Vector3 position, Quaternion rotation)
            {
                this.position = position;
                this.rotation = rotation;
            }
        }

        readonly struct FeatureProbe
        {
            public readonly string label;
            public readonly Vector3 worldPosition;

            public FeatureProbe(string label, Vector3 worldPosition)
            {
                this.label = label;
                this.worldPosition = worldPosition;
            }
        }
    }
}
