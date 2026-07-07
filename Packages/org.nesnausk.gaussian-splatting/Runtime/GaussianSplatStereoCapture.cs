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
            string basePath = Path.Combine(Path.GetDirectoryName(path) ?? ".",
                Path.GetFileNameWithoutExtension(path));
            WritePreviewImages(left, right, basePath);
            message = $"Stereo pair {width}x{height} per eye, IPD {ipd:F3}m, previews written.";

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

            WriteTexture(basePath + "_anaglyph.png", width, height,
                BuildAnaglyph(leftPixels, rightPixels));
            WriteTexture(basePath + "_overlay_ghost.png", width, height,
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
