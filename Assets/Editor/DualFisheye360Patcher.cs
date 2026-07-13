#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Installs the experimental direct dual-fisheye include into the embedded
/// Gaussian splat compute shader. The include activates automatically only for
/// near-360-degree fisheye cones (maxTheta > 3 radians), preserving all existing
/// single-fisheye, covariance, sorting, stereo and culling paths at lower FOVs.
/// </summary>
[InitializeOnLoad]
public static class DualFisheye360Patcher
{
    private const string ComputePath =
        "Packages/org.nesnausk.gaussian-splatting/Shaders/SplatUtilities.compute";

    private const string IncludeLine = "#include \"DualFisheye360.hlsl\"";
    private const string AnchorLine = "#include \"GaussianSplatting.hlsl\"";

    static DualFisheye360Patcher()
    {
        EditorApplication.delayCall += Apply;
    }

    [MenuItem("Tools/3DGS/Apply Dual Fisheye 360")]
    public static void Apply()
    {
        try
        {
            if (!File.Exists(ComputePath))
            {
                Debug.LogError($"Dual-fisheye patch failed: file not found: {ComputePath}");
                return;
            }

            string raw = File.ReadAllText(ComputePath);
            if (raw.Contains(IncludeLine, StringComparison.Ordinal))
                return;

            string newline = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n");

            if (!normalized.Contains(AnchorLine, StringComparison.Ordinal))
            {
                Debug.LogError(
                    "Dual-fisheye patch failed: GaussianSplatting.hlsl include anchor was not found.");
                return;
            }

            normalized = normalized.Replace(
                AnchorLine,
                AnchorLine + "\n" + IncludeLine);

            string output = newline == "\n" ? normalized : normalized.Replace("\n", newline);
            File.WriteAllText(ComputePath, output);
            AssetDatabase.ImportAsset(ComputePath, ImportAssetOptions.ForceUpdate);

            Debug.Log(
                "Applied experimental dual-fisheye 360 projection. " +
                "Near-360 fisheye views now map the front hemisphere to the left disc " +
                "and the back hemisphere to the right disc in one direct covariance-aware pass.");
        }
        catch (Exception ex)
        {
            Debug.LogError("Dual-fisheye patch failed: " + ex);
        }
    }

    [MenuItem("Tools/3DGS/Verify Dual Fisheye 360")]
    public static void Verify()
    {
        if (!File.Exists(ComputePath))
        {
            Debug.LogError($"Dual-fisheye verification failed: file not found: {ComputePath}");
            return;
        }

        string source = File.ReadAllText(ComputePath);
        bool includePresent = source.Contains(IncludeLine, StringComparison.Ordinal);
        bool helperPresent = File.Exists(
            "Packages/org.nesnausk.gaussian-splatting/Shaders/DualFisheye360.hlsl");

        if (includePresent && helperPresent)
        {
            Debug.Log(
                "Dual-fisheye verification passed: the compute shader includes DualFisheye360.hlsl.");
        }
        else
        {
            Debug.LogError(
                $"Dual-fisheye verification failed. Include={includePresent}, HelperFile={helperPresent}");
        }
    }
}
#endif
