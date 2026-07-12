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
    const string RuntimePath = "Packages/org.nesnausk.gaussian-splatting/Runtime/GaussianSplatRenderer.cs";

    const string HlslMarker = "CHATGPT_PEREYE_MATRIX_PATCH";
    const string ComputeMarker = "CHATGPT_PEREYE_CENTER_PATCH";
    const string SortMarker = "CHATGPT_PEREYE_SORT_PATCH";

    static ChatGPTPerEyeShaderPatcher() => EditorApplication.delayCall += Apply;

    [MenuItem("Tools/3DGS/Apply ChatGPT Per-Eye Patches")]
    public static void Apply()
    {
        try
        {
            bool changed = PatchHlsl() | PatchCompute() | PatchRuntimeSorting();
            if (changed)
            {
                AssetDatabase.Refresh();
                Debug.Log("Applied ChatGPT per-eye center, Jacobian, and eye-specific sorting patches.");
            }
            else
            {
                Debug.Log("ChatGPT per-eye patches are already present.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("ChatGPT per-eye patch failed: " + ex);
        }
    }

    [MenuItem("Tools/3DGS/Verify ChatGPT Per-Eye Patches")]
    public static void Verify()
    {
        try
        {
            bool hlslOk = File.Exists(HlslPath) && File.ReadAllText(HlslPath).Contains(HlslMarker);
            bool computeOk = File.Exists(ComputePath) && File.ReadAllText(ComputePath).Contains(ComputeMarker);
            bool sortOk = File.Exists(RuntimePath) && File.ReadAllText(RuntimePath).Contains(SortMarker);

            if (hlslOk && computeOk && sortOk)
            {
                Debug.Log("ChatGPT per-eye verification passed: matrix-aware center projection, per-eye Jacobian, and eye-specific sorting are installed.");
            }
            else
            {
                Debug.LogError($"ChatGPT per-eye verification failed. HLSL={hlslOk}, Compute={computeOk}, Sorting={sortOk}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("ChatGPT per-eye verification failed: " + ex);
        }
    }

    static bool PatchHlsl()
    {
        RequireFile(HlslPath);
        string s = File.ReadAllText(HlslPath);
        if (s.Contains(HlslMarker))
            return false;

        const string oldSignature =
            "float3 CalcCovariance2DFisheye(float3 worldPos, float3 cov3d0, float3 cov3d1, float4x4 matrixV,\n" +
            "    float4 screenParams, float4 fisheyeParams, float4 fisheyeParams2, out float aaFactor)";
        const string newSignature =
            "// " + HlslMarker + "\n" +
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
        RequireFile(ComputePath);
        string s = File.ReadAllText(ComputePath);
        if (s.Contains(ComputeMarker))
            return false;

        const string oldCenter =
            "            float2 ndc = float2(projMat00 * fisheyeS * centerViewPos.x, projMat11 * fisheyeS * centerViewPos.y);";
        const string newCenter =
            "            // " + ComputeMarker + ": route the nonlinear ray through the complete per-eye projection matrix.\n" +
            "            float2 warpedTangent = fisheyeS * centerViewPos.xy;\n" +
            "            float4 warpedClip = mul(_MatrixP, float4(warpedTangent, -1.0, 1.0));\n" +
            "            float safeW = abs(warpedClip.w) > 1e-6 ? warpedClip.w : (warpedClip.w >= 0.0 ? 1e-6 : -1e-6);\n" +
            "            float2 ndc = warpedClip.xy / safeW;";

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

    static bool PatchRuntimeSorting()
    {
        RequireFile(RuntimePath);
        string s = File.ReadAllText(RuntimePath);
        if (s.Contains(SortMarker))
            return false;

        const string oldDispatch =
            "                if (updateSortAndFrame && gs.m_FrameCounter % gs.m_SortNthFrame == 0)\n" +
            "                    gs.SortPoints(cmb, cam, matrix);\n" +
            "                if (updateSortAndFrame)\n" +
            "                    ++gs.m_FrameCounter;";
        const string newDispatch =
            "                // " + SortMarker + ": each stereo eye receives its own depth order.\n" +
            "                // The shared key buffer is safe because each eye is sorted and drawn sequentially.\n" +
            "                bool stereoEyeSort = stereoEye.HasValue;\n" +
            "                bool scheduledMonoSort = updateSortAndFrame && gs.m_FrameCounter % gs.m_SortNthFrame == 0;\n" +
            "                if (stereoEyeSort || scheduledMonoSort)\n" +
            "                    gs.SortPoints(cmb, cam, matrix, stereoEye);\n" +
            "                if (updateSortAndFrame)\n" +
            "                    ++gs.m_FrameCounter;";

        const string oldSignature =
            "        internal void SortPoints(CommandBuffer cmd, Camera cam, Matrix4x4 matrix)";
        const string newSignature =
            "        internal void SortPoints(CommandBuffer cmd, Camera cam, Matrix4x4 matrix,\n" +
            "            Camera.StereoscopicEye? stereoEye = null)";

        const string oldView =
            "            Matrix4x4 worldToCamMatrix = cam.worldToCameraMatrix;";
        const string newView =
            "            Matrix4x4 worldToCamMatrix = stereoEye.HasValue\n" +
            "                ? cam.GetStereoViewMatrix(stereoEye.Value)\n" +
            "                : cam.worldToCameraMatrix;";

        s = ReplaceRequired(s, oldDispatch, newDispatch, "stereo sort dispatch");
        s = ReplaceRequired(s, oldSignature, newSignature, "SortPoints signature");
        s = ReplaceRequired(s, oldView, newView, "eye-specific sorting view matrix");
        File.WriteAllText(RuntimePath, s);
        return true;
    }

    static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Required 3DGS package file was not found.", path);
    }

    static string ReplaceRequired(string source, string oldText, string newText, string label)
    {
        if (!source.Contains(oldText))
            throw new InvalidOperationException("Could not locate " + label + ". Package source may have changed or the patch is partially applied.");
        return source.Replace(oldText, newText);
    }
}
#endif
