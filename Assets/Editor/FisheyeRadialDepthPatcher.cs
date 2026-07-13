#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Applies a minimal depth-only fix to the embedded Gaussian splat compute shader.
/// Fisheye rays can extend beyond the forward hemisphere, where -viewPos.z becomes
/// zero or negative. Using that axial value for depth collapses side/back-facing
/// splats onto the near plane and produces a circular occlusion boundary in point mode.
/// </summary>
[InitializeOnLoad]
public static class FisheyeRadialDepthPatcher
{
    private const string ComputePath =
        "Packages/org.nesnausk.gaussian-splatting/Shaders/SplatUtilities.compute";

    private const string Marker = "CHATGPT_FISHEYE_RADIAL_DEPTH_V1";

    private const string OldDepthLine =
        "            float depthNdc = saturate((negZ - nearPlane) / max(farPlane - nearPlane, 1e-6));";

    private const string NewDepthBlock =
        "            // " + Marker + ": fisheye rays may extend beyond the forward hemisphere.\n" +
        "            // Axial depth (-viewPos.z) becomes zero or negative at side/back directions,\n" +
        "            // collapsing those splats onto the near plane. Use spherical camera distance instead.\n" +
        "            float radialDepth = length(centerViewPos);\n" +
        "            float depthNdc = saturate((radialDepth - nearPlane) / max(farPlane - nearPlane, 1e-6));";

    static FisheyeRadialDepthPatcher()
    {
        EditorApplication.delayCall += Apply;
    }

    [MenuItem("Tools/3DGS/Apply Fisheye Radial Depth Fix")]
    public static void Apply()
    {
        try
        {
            if (!File.Exists(ComputePath))
            {
                Debug.LogError($"Fisheye radial-depth fix failed: file not found: {ComputePath}");
                return;
            }

            string raw = File.ReadAllText(ComputePath);
            if (raw.Contains(Marker))
                return;

            string newline = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n");

            if (!normalized.Contains(OldDepthLine, StringComparison.Ordinal))
            {
                Debug.LogError(
                    "Fisheye radial-depth fix failed: the expected axial-depth line was not found. " +
                    "The package source may differ from the target branch.");
                return;
            }

            normalized = normalized.Replace(OldDepthLine, NewDepthBlock);
            string output = newline == "\n" ? normalized : normalized.Replace("\n", newline);
            File.WriteAllText(ComputePath, output);

            AssetDatabase.ImportAsset(ComputePath, ImportAssetOptions.ForceUpdate);
            Debug.Log(
                "Applied fisheye radial-depth fix. Fisheye center depth now uses length(centerViewPos) " +
                "instead of -centerViewPos.z; projection, covariance, sorting, and culling were not changed.");
        }
        catch (Exception ex)
        {
            Debug.LogError("Fisheye radial-depth fix failed: " + ex);
        }
    }

    [MenuItem("Tools/3DGS/Verify Fisheye Radial Depth Fix")]
    public static void Verify()
    {
        if (!File.Exists(ComputePath))
        {
            Debug.LogError($"Verification failed: file not found: {ComputePath}");
            return;
        }

        string source = File.ReadAllText(ComputePath);
        bool markerPresent = source.Contains(Marker, StringComparison.Ordinal);
        bool radialDepthPresent = source.Contains(
            "float radialDepth = length(centerViewPos);", StringComparison.Ordinal);
        bool oldAxialDepthPresent = source.Contains(OldDepthLine, StringComparison.Ordinal);

        if (markerPresent && radialDepthPresent && !oldAxialDepthPresent)
        {
            Debug.Log(
                "Fisheye radial-depth verification passed: the compute shader uses radial depth for fisheye centers.");
        }
        else
        {
            Debug.LogError(
                $"Fisheye radial-depth verification failed. Marker={markerPresent}, " +
                $"RadialDepth={radialDepthPresent}, OldAxialDepth={oldAxialDepthPresent}");
        }
    }
}
#endif
