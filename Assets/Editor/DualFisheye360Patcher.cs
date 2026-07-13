#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Removes the abandoned single-pass dual-fisheye experiment from the embedded
/// Gaussian splat compute shader. The previous experiment only split the same
/// radial full-sphere structure into two lenses and did not solve the original
/// 360-degree projection artifact.
/// </summary>
[InitializeOnLoad]
public static class DualFisheye360Rollback
{
    private const string ComputePath =
        "Packages/org.nesnausk.gaussian-splatting/Shaders/SplatUtilities.compute";

    private const string IncludeLine = "#include \"DualFisheye360.hlsl\"";

    static DualFisheye360Rollback()
    {
        EditorApplication.delayCall += Apply;
    }

    [MenuItem("Tools/3DGS/Revert Dual Fisheye Experiment")]
    public static void Apply()
    {
        try
        {
            if (!File.Exists(ComputePath))
            {
                Debug.LogError($"Dual-fisheye rollback failed: file not found: {ComputePath}");
                return;
            }

            string raw = File.ReadAllText(ComputePath);
            string newline = raw.Contains("\r\n", StringComparison.Ordinal) ? "\r\n" : "\n";
            string normalized = raw.Replace("\r\n", "\n").Replace("\r", "\n");

            bool changed = false;
            string[] lines = normalized.Split('\n');
            using var writer = new StringWriter();
            for (int i = 0; i < lines.Length; i++)
            {
                if (lines[i].Trim() == IncludeLine)
                {
                    changed = true;
                    continue;
                }

                writer.Write(lines[i]);
                if (i < lines.Length - 1)
                    writer.Write('\n');
            }

            if (!changed)
            {
                Debug.Log("Dual-fisheye rollback: no active include was found; the compute shader is already clean.");
                return;
            }

            string output = writer.ToString();
            if (newline == "\r\n")
                output = output.Replace("\n", "\r\n");

            File.WriteAllText(ComputePath, output);
            AssetDatabase.ImportAsset(ComputePath, ImportAssetOptions.ForceUpdate);

            Debug.Log(
                "Reverted the experimental dual-fisheye shader path. " +
                "The renderer is back to the prior single-fisheye implementation.");
        }
        catch (Exception ex)
        {
            Debug.LogError("Dual-fisheye rollback failed: " + ex);
        }
    }

    [MenuItem("Tools/3DGS/Verify Dual Fisheye Revert")]
    public static void Verify()
    {
        if (!File.Exists(ComputePath))
        {
            Debug.LogError($"Dual-fisheye rollback verification failed: file not found: {ComputePath}");
            return;
        }

        string source = File.ReadAllText(ComputePath);
        bool includePresent = source.Contains(IncludeLine, StringComparison.Ordinal);

        if (!includePresent)
            Debug.Log("Dual-fisheye rollback verification passed: no dual-fisheye include remains.");
        else
            Debug.LogError("Dual-fisheye rollback verification failed: the include is still present.");
    }
}
#endif
