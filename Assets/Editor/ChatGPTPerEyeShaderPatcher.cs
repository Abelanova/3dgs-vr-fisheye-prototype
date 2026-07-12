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

    const string HlslMarkerV1 = "CHATGPT_PEREYE_MATRIX_PATCH";
    const string HlslMarkerV2 = "CHATGPT_PEREYE_JACOBIAN_V2";
    const string ComputeMarkerV1 = "CHATGPT_PEREYE_CENTER_PATCH";
    const string ComputeMarkerV2 = "CHATGPT_PEREYE_CENTER_V2";
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
            bool hlslOk = File.Exists(HlslPath) && File.ReadAllText(HlslPath).Contains(HlslMarkerV2);
            bool computeOk = File.Exists(ComputePath) && File.ReadAllText(ComputePath).Contains(ComputeMarkerV2);
            bool sortOk = File.Exists(RuntimePath) && File.ReadAllText(RuntimePath).Contains(SortMarker);

            if (hlslOk && computeOk && sortOk)
            {
                Debug.Log("ChatGPT per-eye verification passed: scale-consistent center projection, per-eye Jacobian, and eye-specific sorting are installed.");
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
        if (s.Contains(HlslMarkerV2))
            return false;

        const string originalSignature =
            "float3 CalcCovariance2DFisheye(float3 worldPos, float3 cov3d0, float3 cov3d1, float4x4 matrixV,\n" +
            "    float4 screenParams, float4 fisheyeParams, float4 fisheyeParams2, out float aaFactor)";
        const string patchedSignature =
            "// " + HlslMarkerV1 + "\n" +
            "// " + HlslMarkerV2 + ": the Jacobian uses the same virtual fisheye x/y scales as the center map.\n" +
            "float3 CalcCovariance2DFisheye(float3 worldPos, float3 cov3d0, float3 cov3d1, float4x4 matrixV,\n" +
            "    float4x4 matrixP, float4 screenParams, float4 fisheyeParams, float4 fisheyeParams2, out float aaFactor)";

        const string v1Signature =
            "// " + HlslMarkerV1 + "\n" +
            "float3 CalcCovariance2DFisheye(float3 worldPos, float3 cov3d0, float3 cov3d1, float4x4 matrixV,\n" +
            "    float4x4 matrixP, float4 screenParams, float4 fisheyeParams, float4 fisheyeParams2, out float aaFactor)";

        const string originalFocal = "    float focal = screenParams.x * projMat00 * 0.5;";
        const string v1Focal =
            "    float focalX = screenParams.x * abs(matrixP._m00) * 0.5;\n" +
            "    float focalY = screenParams.y * abs(matrixP._m11) * 0.5;";
        const string v2Focal =
            "    // Principal-point offsets do not affect the covariance derivative.\n" +
            "    // Use the exact virtual fisheye scales used by the center projection.\n" +
            "    float focalX = screenParams.x * abs(fisheyeParams.w) * 0.5;\n" +
            "    float focalY = screenParams.y * abs(fisheyeParams2.x) * 0.5;";

        const string originalJ =
            "        focal * (fisheyeS + kCoeff * viewPos.x * viewPos.x),\n" +
            "        focal * kCoeff * viewPos.x * viewPos.y,\n" +
            "        focal * gPrime * viewPos.x / d2,\n\n" +
            "        focal * kCoeff * viewPos.x * viewPos.y,\n" +
            "        focal * (fisheyeS + kCoeff * viewPos.y * viewPos.y),\n" +
            "        focal * gPrime * viewPos.y / d2,";
        const string xyJ =
            "        focalX * (fisheyeS + kCoeff * viewPos.x * viewPos.x),\n" +
            "        focalX * kCoeff * viewPos.x * viewPos.y,\n" +
            "        focalX * gPrime * viewPos.x / d2,\n\n" +
            "        focalY * kCoeff * viewPos.x * viewPos.y,\n" +
            "        focalY * (fisheyeS + kCoeff * viewPos.y * viewPos.y),\n" +
            "        focalY * gPrime * viewPos.y / d2,";

        if (s.Contains(HlslMarkerV1))
        {
            s = ReplaceRequired(s, v1Signature, patchedSignature, "v1 fisheye covariance signature");
            s = ReplaceRequired(s, v1Focal, v2Focal, "v1 fisheye focal scaling");
        }
        else
        {
            s = ReplaceRequired(s, originalSignature, patchedSignature, "fisheye covariance signature");
            s = ReplaceRequired(s, originalFocal, v2Focal, "fisheye focal scaling");
            s = ReplaceRequired(s, originalJ, xyJ, "fisheye Jacobian rows");
        }

        File.WriteAllText(HlslPath, s);
        return true;
    }

    static bool PatchCompute()
    {
        RequireFile(ComputePath);
        string s = File.ReadAllText(ComputePath);
        if (s.Contains(ComputeMarkerV2))
            return false;

        const string originalCenter =
            "            float2 ndc = float2(projMat00 * fisheyeS * centerViewPos.x, projMat11 * fisheyeS * centerViewPos.y);";

        const string v1CenterA =
            "            // " + ComputeMarkerV1 + ": route the nonlinear ray through the complete per-eye projection matrix.\n" +
            "            float2 warpedTangent = fisheyeS * centerViewPos.xy;\n" +
            "            float4 warpedClip = mul(_MatrixP, float4(warpedTangent, -1.0, 1.0));\n" +
            "            float2 ndc = warpedClip.xy / max(abs(warpedClip.w), 1e-6);";

        const string v1CenterB =
            "            // " + ComputeMarkerV1 + ": route the nonlinear ray through the complete per-eye projection matrix.\n" +
            "            float2 warpedTangent = fisheyeS * centerViewPos.xy;\n" +
            "            float4 warpedClip = mul(_MatrixP, float4(warpedTangent, -1.0, 1.0));\n" +
            "            float safeW = abs(warpedClip.w) > 1e-6 ? warpedClip.w : (warpedClip.w >= 0.0 ? 1e-6 : -1e-6);\n" +
            "            float2 ndc = warpedClip.xy / safeW;";

        const string v2Center =
            "            // " + ComputeMarkerV1 + "\n" +
            "            // " + ComputeMarkerV2 + ": preserve the requested virtual fisheye scale while\n" +
            "            // inheriting the per-eye principal point and GPU projection-axis signs.\n" +
            "            float2 warpedTangent = fisheyeS * centerViewPos.xy;\n" +
            "            float2 nativeProjectionScale = max(abs(float2(_MatrixP._m00, _MatrixP._m11)), 1e-6);\n" +
            "            float2 virtualToNativeScale = float2(projMat00, projMat11) / nativeProjectionScale;\n" +
            "            float4 warpedClip = mul(_MatrixP, float4(warpedTangent * virtualToNativeScale, -1.0, 1.0));\n" +
            "            float safeW = abs(warpedClip.w) > 1e-6 ? warpedClip.w : (warpedClip.w >= 0.0 ? 1e-6 : -1e-6);\n" +
            "            float2 ndc = warpedClip.xy / safeW;";

        if (s.Contains(ComputeMarkerV1))
        {
            if (s.Contains(v1CenterB))
                s = s.Replace(v1CenterB, v2Center);
            else if (s.Contains(v1CenterA))
                s = s.Replace(v1CenterA, v2Center);
            else
                throw new InvalidOperationException("Could not upgrade the v1 per-eye center projection block.");
        }
        else
        {
            s = ReplaceRequired(s, originalCenter, v2Center, "per-eye center projection");
        }

        const string originalCall =
            "            cov2d = CalcCovariance2DFisheye(splat.pos, cov3d0, cov3d1, _MatrixMV,\n" +
            "                _VecScreenParams, _FisheyeParams, _FisheyeParams2, aaFactor);";
        const string matrixCall =
            "            cov2d = CalcCovariance2DFisheye(splat.pos, cov3d0, cov3d1, _MatrixMV,\n" +
            "                covarianceProjection, _VecScreenParams, _FisheyeParams, _FisheyeParams2, aaFactor);";
        if (s.Contains(originalCall))
            s = s.Replace(originalCall, matrixCall);
        else if (!s.Contains(matrixCall))
            throw new InvalidOperationException("Could not locate the matrix-aware fisheye covariance call.");

        const string flippedCenter = "            centerClipPos = float4(ndc.x, -ndc.y, depthNdc, 1.0);";
        const string gpuConsistentCenter =
            "            // The GPU projection matrix already carries the render-target Y convention.\n" +
            "            centerClipPos = float4(ndc.x, ndc.y, depthNdc, 1.0);";
        s = ReplaceRequired(s, flippedCenter, gpuConsistentCenter, "GPU-consistent fisheye center Y");

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
