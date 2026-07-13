#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;

[InitializeOnLoad]
public static class RevertFisheyeBoundaryRegression
{
    const string ComputePath = "Packages/org.nesnausk.gaussian-splatting/Shaders/SplatUtilities.compute";
    const string RestoredMarker = "CHATGPT_FISHEYE_REGRESSION_ROLLBACK_V1";

    static RevertFisheyeBoundaryRegression()
    {
        // Run one editor-delay later than the existing patcher, so the final on-disk
        // shader state is the previously working radial-sort / V1-footprint version.
        EditorApplication.delayCall += () => EditorApplication.delayCall += Apply;
    }

    [MenuItem("Tools/3DGS/Revert Regressive Boundary Patch")]
    public static void Apply()
    {
        try
        {
            if (!File.Exists(ComputePath))
                throw new FileNotFoundException("Gaussian splat compute shader was not found.", ComputePath);

            string raw = File.ReadAllText(ComputePath);
            string newline = raw.Contains("\r\n") ? "\r\n" : "\n";
            string s = raw.Replace("\r\n", "\n").Replace("\r", "\n");
            bool changed = false;

            const string unstableSort =
                "    // CHATGPT_STABLE_AXIAL_SORT_V3: preserve stable front-to-back order in the forward hemisphere.\n" +
                "    // Pure radial sorting changes many pairwise orders as soon as fisheye is enabled,\n" +
                "    // which turns large edge footprints into a translucent smear.\n" +
                "    float3 objectPos = LoadSplatPos(origIdx);\n" +
                "    float3 viewPos = mul(_MatrixMV, float4(objectPos, 1.0)).xyz;\n" +
                "    float axialDepth = -viewPos.z;\n" +
                "    float radialDepth = length(viewPos);\n" +
                "    float sortDistance = axialDepth >= 0.0 ? axialDepth : radialDepth;\n" +
                "    _SplatSortDistances[idx] = FloatToSortableUint(sortDistance);";

            const string restoredSort =
                "    // CHATGPT_RADIAL_SORT_ORIGIN_V2: evaluate both metrics from the complete mono/per-eye view matrix.\n" +
                "    float3 objectPos = LoadSplatPos(origIdx);\n" +
                "    float3 viewPos = mul(_MatrixMV, float4(objectPos, 1.0)).xyz;\n\n" +
                "    // Unity camera space looks along negative Z.\n" +
                "    float axialDepth = -viewPos.z;\n" +
                "    float radialDepth = length(viewPos);\n\n" +
                "    bool useRadialSort = _FisheyeParams.x > 0.0001;\n" +
                "    float sortDistance = useRadialSort ? radialDepth : axialDepth;\n" +
                "    _SplatSortDistances[idx] = FloatToSortableUint(sortDistance);";

            changed |= ReplaceIfPresent(ref s, unstableSort, restoredSort);

            string[] v2GuardVariants =
            {
                BuildV2Guard("48.0", "0.05", "24.0", "6.0"),
                BuildV2Guard("96.0", "0.10", "32.0", "8.0")
            };

            const string restoredGuard =
                "    // CHATGPT_FISHEYE_FOOTPRINT_GUARD_V1: a center Jacobian is only a local approximation.\n" +
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

            foreach (string candidate in v2GuardVariants)
                changed |= ReplaceIfPresent(ref s, candidate, restoredGuard);

            const string regressiveCull =
                "        // CHATGPT_FISHEYE_FOOTPRINT_CULL_V1: use the projected footprint for both perspective and fisheye.\n" +
                "        // This rejects giant off-screen ellipses while retaining splats whose bounded footprint crosses the edge.\n" +
                "        float lMax = 4.0 * max(length(view.axis1), length(view.axis2));\n" +
                "        bool belowMinimumSize = lMax < _MinPixelSize;\n" +
                "        float2 cullScale = centerClipPos.ww / _VecScreenParams.xy;\n" +
                "        bool outsideView = any(abs(centerClipPos.xy) - lMax * cullScale > centerClipPos.ww * 1.05);\n" +
                "        if (belowMinimumSize || outsideView)";

            const string restoredCull =
                "        // Match PlayCanvas' footprint-aware x/y frustum test for ordinary\n" +
                "        // perspective. Fisheye stays unculled here because the footprint warp is\n" +
                "        // non-linear and can remove visible splats while moving.\n" +
                "        float lMax = 4.0 * max(length(view.axis1), length(view.axis2));\n" +
                "        bool belowMinimumSize = lMax < _MinPixelSize;\n" +
                "        float2 cullScale = centerClipPos.ww / _VecScreenParams.xy;\n" +
                "        bool outsidePerspectiveView = _FisheyeParams.x <= 0.0001 &&\n" +
                "            any(abs(centerClipPos.xy) - lMax * cullScale > centerClipPos.ww);\n" +
                "        if (belowMinimumSize || outsidePerspectiveView)";

            changed |= ReplaceIfPresent(ref s, regressiveCull, restoredCull);

            if (!s.Contains(RestoredMarker))
            {
                s = s.Replace("#include \"UnityCG.cginc\"", "#include \"UnityCG.cginc\"\n// " + RestoredMarker);
                changed = true;
            }

            if (changed)
            {
                string output = newline == "\n" ? s : s.Replace("\n", newline);
                File.WriteAllText(ComputePath, output);
                AssetDatabase.ImportAsset(ComputePath, ImportAssetOptions.ForceUpdate);
                Debug.Log("Reverted the regressive fisheye boundary experiment and restored the previous radial sorting, V1 footprint guard, and fisheye culling behavior.");
            }
        }
        catch (Exception ex)
        {
            Debug.LogError("Failed to revert the fisheye boundary regression: " + ex);
        }
    }

    [MenuItem("Tools/3DGS/Verify Boundary Regression Revert")]
    public static void Verify()
    {
        string s = File.Exists(ComputePath) ? File.ReadAllText(ComputePath) : string.Empty;
        bool ok = s.Contains("CHATGPT_RADIAL_SORT_ORIGIN_V2") &&
                  s.Contains("CHATGPT_FISHEYE_FOOTPRINT_GUARD_V1") &&
                  !s.Contains("CHATGPT_STABLE_AXIAL_SORT_V3") &&
                  !s.Contains("CHATGPT_FISHEYE_FOOTPRINT_GUARD_V2") &&
                  !s.Contains("CHATGPT_FISHEYE_FOOTPRINT_CULL_V1");

        if (ok)
            Debug.Log("Boundary regression revert verified: the previous rendering behavior is restored.");
        else
            Debug.LogError("Boundary regression revert verification failed. Run Tools > 3DGS > Revert Regressive Boundary Patch once more.");
    }

    static string BuildV2Guard(string pixelCap, string viewportFraction, string ratioStart, string ratioEnd)
    {
        return
            "    // CHATGPT_FISHEYE_FOOTPRINT_GUARD_V2: the center Jacobian is only a local linearization.\n" +
            "    // Near the fisheye boundary its major eigenvalue can diverge, creating radial streaks.\n" +
            "    // Tighten the footprint limit before the singular region while preserving normal projection.\n" +
            "    float vmin = min(1024.0, min(_VecScreenParams.x, _VecScreenParams.y));\n" +
            "    float defaultAxisLimit = vmin * 0.5;\n" +
            "    float axis1Length = min(sqrt(max(2.0 * lambda1, 0.0)), defaultAxisLimit);\n" +
            "    float axis2Length = min(sqrt(max(2.0 * lambda2, 0.0)), defaultAxisLimit);\n\n" +
            "    if (_FisheyeParams.x > 0.0001)\n" +
            "    {\n" +
            "        float fisheyeT = saturate(_FisheyeParams.x);\n" +
            "        float guardWeight = saturate(fisheyeT * 2.0);\n" +
            "        guardWeight = guardWeight * guardWeight * (3.0 - 2.0 * guardWeight);\n" +
            "        float guardedAxisLimit = min(" + pixelCap + ", vmin * " + viewportFraction + ");\n" +
            "        float axisLimit = lerp(defaultAxisLimit, guardedAxisLimit, guardWeight);\n" +
            "        axis1Length = min(axis1Length, axisLimit);\n" +
            "        axis2Length = min(axis2Length, axisLimit);\n\n" +
            "        // lambda1 is the major eigenvalue. Limit eccentricity before it becomes a screen-spanning ray.\n" +
            "        float maxAxisRatio = lerp(" + ratioStart + ", " + ratioEnd + ", guardWeight);\n" +
            "        axis1Length = min(axis1Length, max(axis2Length, 0.75) * maxAxisRatio);\n" +
            "    }\n\n" +
            "    v1 = axis1Length * diagVec;\n" +
            "    v2 = axis2Length * float2(diagVec.y, -diagVec.x);";
    }

    static bool ReplaceIfPresent(ref string source, string oldText, string newText)
    {
        if (!source.Contains(oldText))
            return false;
        source = source.Replace(oldText, newText);
        return true;
    }
}
#endif
