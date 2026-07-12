#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class ChatGPTPerEyeShaderPatcher
{
    const string ComputePath = "Packages/org.nesnausk.gaussian-splatting/Shaders/SplatUtilities.compute";
    const string HlslPath = "Packages/org.nesnausk.gaussian-splatting/Shaders/GaussianSplatting.hlsl";
    const string Marker = "CHATGPT_PEREYE_MATRIX_PATCH";

    static ChatGPTPerEyeShaderPatcher() => EditorApplication.delayCall += Apply;

    [MenuItem("Tools/3DGS/Apply ChatGPT Per-Eye Jacobian Patch")]
    public static void Apply()
    {
        try
        {
            bool changed = PatchHlsl() | PatchCompute();
            if (changed)
            {
                AssetDatabase.Refresh();
                Debug.Log("Applied ChatGPT per-eye matrix/Jacobian shader patch.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("ChatGPT per-eye shader patch failed: " + ex);
        }
    }

    static bool PatchHlsl()
    {
        string s = File.ReadAllText(HlslPath);
        if (s.Contains(Marker)) return false;

        const string oldSignature =
            "float3 CalcCovariance2DFisheye(float3 worldPos, float3 cov3d0, float3 cov3d1, float4x4 matrixV,\n" +
            "    float4 screenParams, float4 fisheyeParams, float4 fisheyeParams2, out float aaFactor)";
        const string newSignature =
            "// " + Marker + "\n" +
            "float3 CalcCovariance2DFisheye(float3 worldPos, float3 cov3d0, float3 cov3d1, float4x4 matrixV,\n" +
            "    float4x4 matrixP, float4 screenParams, float4 fisheyeParams, float4 fisheyeParams2, out float aaFactor)";

        const string oldFocal = "    float focal = screenParams.x * projMat00 * 0.5;";
        const string newFocal =
            "    float focalX = screenParams.x * abs(matrixP._m00) * 0.5;\n" +
            "    float focalY = screenParams.y * abs(matrixP._m11) * 0.5;";

        const string oldJ =
            "        focal * (fisheyeS + kCoeff * viewPos.x * viewPos.x),\n" +
            "        focal * kCoeff * viewPos.x * viewPos.y,\n" +
            "        focal * gPrime * viewPos.x / d2,\n\n" +
            "        focal * kCoeff * viewPos.x * viewPos.y,\n" +
            "        focal * (fisheyeS + kCoeff * viewPos.y * viewPos.y),\n" +
            "        focal * gPrime * viewPos.y / d2,";
        const string newJ =
            "        focalX * (fisheyeS + kCoeff * viewPos.x * viewPos.x),\n" +
            "        focalX * kCoeff * viewPos.x * viewPos.y,\n" +
            "        focalX * gPrime * viewPos.x / d2,\n\n" +
            "        focalY * kCoeff * viewPos.x * viewPos.y,\n" +
            "        focalY * (fisheyeS + kCoeff * viewPos.y * viewPos.y),\n" +
            "        focalY * gPrime * viewPos.y / d2,";

        s = ReplaceRequired(s, oldSignature, newSignature, "fisheye covariance signature");
        s = ReplaceRequired(s, oldFocal, newFocal, "fisheye focal scaling");
        s = ReplaceRequired(s, oldJ, newJ, "fisheye Jacobian rows");
        File.WriteAllText(HlslPath, s);
        return true;
    }

    static bool PatchCompute()
    {
        string s = File.ReadAllText(ComputePath);
        if (s.Contains(Marker)) return false;

        const string oldCenter =
            "            float2 ndc = float2(projMat00 * fisheyeS * centerViewPos.x, projMat11 * fisheyeS * centerViewPos.y);";
        const string newCenter =
            "            // " + Marker + ": route the nonlinear ray through the complete per-eye projection matrix.\n" +
            "            float2 warpedTangent = fisheyeS * centerViewPos.xy;\n" +
            "            float4 warpedClip = mul(_MatrixP, float4(warpedTangent, -1.0, 1.0));\n" +
            "            float2 ndc = warpedClip.xy / max(abs(warpedClip.w), 1e-6);";

        const string oldCall =
            "            cov2d = CalcCovariance2DFisheye(splat.pos, cov3d0, cov3d1, _MatrixMV,\n" +
            "                _VecScreenParams, _FisheyeParams, _FisheyeParams2, aaFactor);";
        const string newCall =
            "            cov2d = CalcCovariance2DFisheye(splat.pos, cov3d0, cov3d1, _MatrixMV,\n" +
            "                covarianceProjection, _VecScreenParams, _FisheyeParams, _FisheyeParams2, aaFactor);";

        s = ReplaceRequired(s, oldCenter, newCenter, "per-eye center projection");
        s = ReplaceRequired(s, oldCall, newCall, "matrix-aware covariance call");
        File.WriteAllText(ComputePath, s);
        return true;
    }

    static string ReplaceRequired(string source, string oldText, string newText, string label)
    {
        if (!source.Contains(oldText))
            throw new InvalidOperationException("Could not locate " + label + ". Package source may have changed.");
        return source.Replace(oldText, newText);
    }
}
#endif
