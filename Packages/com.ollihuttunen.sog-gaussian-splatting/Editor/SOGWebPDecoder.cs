using System;
using UnityEngine;

namespace GaussianSplatting.SOG.Editor
{
    internal static class SOGWebPDecoder
    {
        public static Color32[] Decode(string filename, byte[] imageData)
        {
            try
            {
                return LibWebPDecoder.Decode(filename, imageData);
            }
            catch (Exception libWebPException)
            {
                try
                {
                    Debug.LogWarning(
                        $"[SOG] libwebp failed for '{filename}' ({libWebPException.GetType().Name}: {libWebPException.Message}). " +
                        "Falling back to Unity Editor FreeImage decoder.");
                    return FreeImageWebP.Decode(filename, imageData);
                }
                catch (Exception freeImageException)
                {
                    throw new InvalidOperationException(
                        $"Could not decode WebP '{filename}'. libwebp failed with " +
                        $"{libWebPException.GetType().Name}: {libWebPException.Message}; FreeImage failed with " +
                        $"{freeImageException.GetType().Name}: {freeImageException.Message}", freeImageException);
                }
            }
        }
    }
}
