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

    const string HlslMarkerV2 = "CHATGPT_PEREYE_JACOBIAN_V2";
    const string ComputeMarkerV2 = "CHATGPT_PEREYE_CENTER_V2";
    const string RuntimeSortMarkerV2 = "CHATGPT_EYE_CENTERED_SORT_V2";
    const string ComputeSortMarkerV2 = "CHATGPT_RADIAL_SORT_ORIGIN_V2";
    const string FootprintMarkerV1 = "CHATGPT_FISHEYE_FOOTPRINT_GUARD_V1";

    static ChatGPTPerEyeShaderPatcher() => EditorApplication.delayCall += Apply;

    [MenuItem("Tools/3DGS/Apply ChatGPT Per-Eye Patches")]
    public static void Apply()
    {
        try
        {
            bool changed = false;
            changed |= PatchHlslProjection();
            changed |= PatchComputeProjectionMarker();
            changed |= PatchRuntimeSorting();
            changed |= PatchComputeSorting();
            changed |= PatchComputeFootprintRegularization();

            if (changed)
            {
                AssetDatabase.Refresh(ImportAssetOptions.ForceUpdate);
                Debug.Log("Applied ChatGPT per-eye projection, eye-centered sorting, and fisheye footprint regularization patches.");
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
            bool hlslOk = Contains(HlslPath, HlslMarkerV2);
            bool computeOk = Contains(ComputePath, ComputeMarkerV2);
            bool runtimeSortOk = Contains(RuntimePath, RuntimeSortMarkerV2);
            bool computeSortOk = Contains(ComputePath, ComputeSortMarkerV2);
            bool footprintOk = Contains(ComputePath, FootprintMarkerV1);

            if (hlslOk && computeOk && runtimeSortOk && computeSortOk && footprintOk)
            {
                Debug.Log("ChatGPT per-eye verification passed: scale-consistent center projection, per-eye Jacobian, complete eye view matrices, eye-centered radial sorting, and fisheye footprint regularization are installed.");
            }
            else
            {
                Debug.LogError($"ChatGPT per-eye verification failed. HLSL={hlslOk}, Compute={computeOk}, RuntimeSorting={runtimeSortOk}, ComputeSorting={computeSortOk}, Footprint={footprintOk}");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("ChatGPT per-eye verification failed: " + ex);
        }
    }

    static bool PatchHlslProjection()
    {
        RequireFile(HlslPath);
        string s = ReadNormalized(HlslPath, out string newline);
        if (s.Contains(HlslMarkerV2))
            return false;

        const string oldCenterProjection =
            "    clipPos = mul(matrixP, float4(warpedTangent.x, warpedTangent.y, -1.0, 1.0));";

        const string newCenterProjection =
            "    // " + HlslMarkerV2 + ": preserve the virtual fisheye scale while using the complete per-eye matrix.\n" +
            "    float2 nativeProjectionScale = max(abs(float2(matrixP._m00, matrixP._m11)), 1e-6);\n" +
            "    float2 virtualProjectionScale = abs(float2(fisheyeParams.w, fisheyeParams2.x));\n" +
            "    float2 virtualToNativeScale = virtualProjectionScale / nativeProjectionScale;\n" +
            "    clipPos = mul(matrixP, float4(warpedTangent * virtualToNativeScale, -1.0, 1.0));";

        const string oldCovarianceDerivative =
            "    float4 dClipDu = float4(matrixP._m00, matrixP._m10, matrixP._m20, matrixP._m30);\n" +
            "    float4 dClipDv = float4(matrixP._m01, matrixP._m11, matrixP._m21, matrixP._m31);";

        const string newCovarianceDerivative =
            "    // Apply the same virtual-to-native input scaling used by the center map.\n" +
            "    float2 nativeProjectionScale = max(abs(float2(matrixP._m00, matrixP._m11)), 1e-6);\n" +
            "    float2 virtualProjectionScale = abs(float2(fisheyeParams.w, fisheyeParams2.x));\n" +
            "    float2 virtualToNativeScale = virtualProjectionScale / nativeProjectionScale;\n" +
            "    float4 dClipDu = float4(matrixP._m00, matrixP._m10, matrixP._m20, matrixP._m30) * virtualToNativeScale.x;\n" +
            "    float4 dClipDv = float4(matrixP._m01, matrixP._m11, matrixP._m21, matrixP._m31) * virtualToNativeScale.y;";

        s = ReplaceRequired(s, oldCenterProjection, newCenterProjection,
            "current helper-based fisheye center projection");
        s = ReplaceRequired(s, oldCovarianceDerivative, newCovarianceDerivative,
            "current helper-based fisheye covariance derivative");

        WriteNormalized(HlslPath, s, newline);
        return true;
    }

    static bool PatchComputeProjectionMarker()
    {
        RequireFile(ComputePath);
        string s = ReadNormalized(ComputePath, out string newline);
        if (s.Contains(ComputeMarkerV2))
            return false;

        const string oldCall =
            "        if (!ProjectFisheyeThroughMatrix(centerViewPos, covarianceProjection,\n" +
            "                _FisheyeParams, _FisheyeParams2, fisheyeClipPos))";

        const string newCall =
            "        // " + ComputeMarkerV2 + ": center and covariance share the same helper-based per-eye fisheye map.\n" +
            "        if (!ProjectFisheyeThroughMatrix(centerViewPos, covarianceProjection,\n" +
            "                _FisheyeParams, _FisheyeParams2, fisheyeClipPos))";

        s = ReplaceRequired(s, oldCall, newCall, "current helper-based fisheye center call");
        WriteNormalized(ComputePath, s, newline);
        return true;
    }

    static bool PatchRuntimeSorting()
    {
        RequireFile(RuntimePath);
        string s = ReadNormalized(RuntimePath, out string newline);
        if (s.Contains(RuntimeSortMarkerV2))
            return false;

        const string partialFlipBlock =
            "            Matrix4x4 worldToCamMatrix = stereoEye.HasValue\n" +
            "                ? cam.GetStereoViewMatrix(stereoEye.Value)\n" +
            "                : cam.worldToCameraMatrix;\n" +
            "            worldToCamMatrix.m20 *= -1;\n" +
            "            worldToCamMatrix.m21 *= -1;\n" +
            "            worldToCamMatrix.m22 *= -1;";

        const string completeEyeViewBlock =
            "            // " + RuntimeSortMarkerV2 + ": keep Unity's complete mono/per-eye view matrix intact.\n" +
            "            // The compute shader derives positive axial depth as -viewPos.z.\n" +
            "            Matrix4x4 worldToCamMatrix = stereoEye.HasValue\n" +
            "                ? cam.GetStereoViewMatrix(stereoEye.Value)\n" +
            "                : cam.worldToCameraMatrix;";

        s = ReplaceRequired(s, partialFlipBlock, completeEyeViewBlock,
            "partial Z-row sorting matrix flip");
        WriteNormalized(RuntimePath, s, newline);
        return true;
    }

    static bool PatchComputeSorting()
    {
        RequireFile(ComputePath);
        string s = ReadNormalized(ComputePath, out string newline);
        if (s.Contains(ComputeSortMarkerV2))
            return false;

        const string oldSortBlock =
            "    float3 pos = LoadSplatPos(origIdx);\n" +
            "    pos = mul(_MatrixMV, float4(pos.xyz, 1)).xyz;\n\n" +
            "    // Fisheye is non-linear: splats can overlap by angular distance instead of\n" +
            "    // a single camera-forward depth. Use eye-radial distance whenever fisheye\n" +
            "    // is active so sort order follows the projection mode.\n" +
            "    bool useRadialSort = _FisheyeParams.x > 0.0001;\n" +
            "    float sortDistance = useRadialSort ? length(pos) : pos.z;\n" +
            "    _SplatSortDistances[idx] = FloatToSortableUint(sortDistance);";

        const string newSortBlock =
            "    // " + ComputeSortMarkerV2 + ": evaluate both metrics from the complete mono/per-eye view matrix.\n" +
            "    float3 objectPos = LoadSplatPos(origIdx);\n" +
            "    float3 viewPos = mul(_MatrixMV, float4(objectPos, 1.0)).xyz;\n\n" +
            "    // Unity camera space looks along negative Z.\n" +
            "    float axialDepth = -viewPos.z;\n" +
            "    float radialDepth = length(viewPos);\n\n" +
            "    bool useRadialSort = _FisheyeParams.x > 0.0001;\n" +
            "    float sortDistance = useRadialSort ? radialDepth : axialDepth;\n" +
            "    _SplatSortDistances[idx] = FloatToSortableUint(sortDistance);";

        s = ReplaceRequired(s, oldSortBlock, newSortBlock, "current radial sorting block");
        WriteNormalized(ComputePath, s, newline);
        return true;
    }

    static bool PatchComputeFootprintRegularization()
    {
        RequireFile(ComputePath);
        string s = ReadNormalized(ComputePath, out string newline);
        if (s.Contains(FootprintMarkerV1))
            return false;

        const string oldAxisBlock =
            "    float vmin = min(1024.0, min(_VecScreenParams.x, _VecScreenParams.y));\n" +
            "    float axis1Length = min(sqrt(max(2.0 * lambda1, 0.0)), vmin * 0.5);\n" +
            "    float axis2Length = min(sqrt(max(2.0 * lambda2, 0.0)), vmin * 0.5);\n" +
            "    v1 = axis1Length * diagVec;\n" +
            "    v2 = axis2Length * float2(diagVec.y, -diagVec.x);";

        const string guardedAxisBlock =
            "    // " + FootprintMarkerV1 + ": a center Jacobian is only a local approximation.\n" +
            "    // At close range or near the fisheye boundary it can create screen-spanning streaks.\n" +
            "    // Smoothly limit footprint size and eccentricity only as fisheye strength increases.\n" +
            "    float vmin = min(1024.0, min(_VecScreenParams.x, _VecScreenParams.y));\n" +
            "    float defaultAxisLimit = vmin * 0.5;\n" +
            "    float axis1Length = min(sqrt(max(2.0 * lambda1, 0.0)), defaultAxisLimit);\n" +
            "    float axis2Length = min(sqrt(max(2.0 * lambda2, 0.0)), defaultAxisLimit);\n\n" +
            "    if (_FisheyeParams.x > 0.0001)\n" +
            "    {\n" +
            "        float fisheyeT = saturate(_FisheyeParams.x);\n" +
            "        float guardedAxisLimit = min(192.0, vmin * 0.18);\n" +
            "        float axisLimit = lerp(defaultAxisLimit, guardedAxisLimit, fisheyeT);\n" +
            "        axis1Length = min(axis1Length, axisLimit);\n" +
            "        axis2Length = min(axis2Length, axisLimit);\n\n" +
            "        // lambda1 is the major eigenvalue. Cap only the long axis so details are not blurred wider.\n" +
            "        float maxAxisRatio = lerp(64.0, 12.0, fisheyeT);\n" +
            "        axis1Length = min(axis1Length, max(axis2Length, 0.5) * maxAxisRatio);\n" +
            "    }\n\n" +
            "    v1 = axis1Length * diagVec;\n" +
            "    v2 = axis2Length * float2(diagVec.y, -diagVec.x);";

        s = ReplaceRequired(s, oldAxisBlock, guardedAxisBlock,
            "fisheye covariance axis decomposition block");
        WriteNormalized(ComputePath, s, newline);
        return true;
    }

    static string ReadNormalized(string path, out string newline)
    {
        string raw = File.ReadAllText(path);
        newline = raw.Contains("\r\n") ? "\r\n" : "\n";
        return raw.Replace("\r\n", "\n").Replace("\r", "\n");
    }

    static void WriteNormalized(string path, string normalizedText, string newline)
    {
        string output = newline == "\n" ? normalizedText : normalizedText.Replace("\n", newline);
        File.WriteAllText(path, output);
    }

    static bool Contains(string path, string marker) =>
        File.Exists(path) && File.ReadAllText(path).Contains(marker);

    static void RequireFile(string path)
    {
        if (!File.Exists(path))
            throw new FileNotFoundException("Required 3DGS package file was not found.", path);
    }

    static string ReplaceRequired(string source, string oldText, string newText, string label)
    {
        if (!source.Contains(oldText))
            throw new InvalidOperationException("Could not locate " + label + ". The package source does not match the expected current branch layout.");
        return source.Replace(oldText, newText);
    }
}
#endif
