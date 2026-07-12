using System;
using System.Globalization;
using System.IO;
using System.Text;
using GaussianSplatting.Runtime;
using UnityEngine;

/// <summary>
/// Offline/Editor diagnostics for the experimental per-eye nonlinear projection.
/// It can use OpenXR-provided eye matrices when available, or a parallel mock
/// stereo rig when no headset is connected. The CSV compares the branch's
/// legacy symmetric fisheye mapping with the proposed mapping that routes the
/// warped ray through each eye's complete projection matrix.
/// </summary>
[ExecuteAlways]
public sealed class StereoProjectionDiagnostics : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField] bool preferRuntimeStereoMatrices = true;
    [SerializeField, Range(0.0f, 0.12f)] float mockIpdMeters = 0.064f;
    [SerializeField, Min(64)] int mockEyeWidth = 1440;
    [SerializeField, Min(64)] int mockEyeHeight = 1584;
    [SerializeField] bool includePeripheralSamples = true;

    static readonly float[] Depths = { 0.5f, 1.0f, 2.0f, 5.0f, 10.0f };

    static readonly SampleDirection[] CoreDirections =
    {
        new("center", 0.0f, 0.0f),
        new("left_20", -20.0f, 0.0f),
        new("right_20", 20.0f, 0.0f),
        new("up_15", 0.0f, 15.0f),
        new("down_15", 0.0f, -15.0f),
    };

    static readonly SampleDirection[] PeripheralDirections =
    {
        new("left_45", -45.0f, 0.0f),
        new("right_45", 45.0f, 0.0f),
        new("upper_left", -35.0f, 25.0f),
        new("upper_right", 35.0f, 25.0f),
        new("lower_left", -35.0f, -25.0f),
        new("lower_right", 35.0f, -25.0f),
    };

    readonly struct SampleDirection
    {
        public readonly string Name;
        public readonly float YawDegrees;
        public readonly float PitchDegrees;

        public SampleDirection(string name, float yawDegrees, float pitchDegrees)
        {
            Name = name;
            YawDegrees = yawDegrees;
            PitchDegrees = pitchDegrees;
        }
    }

    readonly struct EyeMatrices
    {
        public readonly Matrix4x4 View;
        public readonly Matrix4x4 GpuProjection;
        public readonly Vector3 WorldPosition;

        public EyeMatrices(Matrix4x4 view, Matrix4x4 gpuProjection)
        {
            View = view;
            GpuProjection = gpuProjection;
            WorldPosition = view.inverse.GetColumn(3);
        }
    }

    readonly struct FishParameters
    {
        public readonly bool Enabled;
        public readonly float K;
        public readonly float InvK;
        public readonly float ScaleX;
        public readonly float ScaleY;
        public readonly float MaxTheta;

        public FishParameters(bool enabled, float k, float invK, float scaleX, float scaleY, float maxTheta)
        {
            Enabled = enabled;
            K = k;
            InvK = invK;
            ScaleX = scaleX;
            ScaleY = scaleY;
            MaxTheta = maxTheta;
        }
    }

    void Reset()
    {
        targetCamera = GetComponent<Camera>();
        ResolveSplat();
    }

    [ContextMenu("Log Stereo Projection Diagnostics")]
    public void LogDiagnostics()
    {
        string csv = BuildCsv(out string summary);
        Debug.Log(summary + "\n" + csv, this);
    }

    [ContextMenu("Export Stereo Projection Diagnostics CSV")]
    public void ExportCsv()
    {
        string csv = BuildCsv(out string summary);
        string path = Path.Combine(Application.persistentDataPath, "stereo_projection_diagnostics.csv");
        File.WriteAllText(path, csv, Encoding.UTF8);
        Debug.Log(summary + $"\nStereo diagnostics written to: {path}", this);
    }

    string BuildCsv(out string summary)
    {
        ResolveTargets();
        if (targetCamera == null)
        {
            summary = "Stereo diagnostics failed: no Camera was found.";
            return string.Empty;
        }

        GetEyeMatrices(out EyeMatrices left, out EyeMatrices right, out bool usingRuntimeStereo);
        float ipd = Vector3.Distance(left.WorldPosition, right.WorldPosition);
        int width = usingRuntimeStereo && targetCamera.pixelWidth > 0 ? targetCamera.pixelWidth : mockEyeWidth;
        int height = usingRuntimeStereo && targetCamera.pixelHeight > 0 ? targetCamera.pixelHeight : mockEyeHeight;
        float aspect = width / Mathf.Max((float)height, 1.0f);
        FishParameters fish = CalculateFishParameters(aspect);

        var csv = new StringBuilder(8192);
        csv.AppendLine("projection,id,depth_m,yaw_deg,pitch_deg,left_ndc_x,left_ndc_y,right_ndc_x,right_ndc_y,dx_px,dy_px,left_valid,right_valid");

        float maxLegacyVertical = 0.0f;
        float maxPerEyeVertical = 0.0f;
        int invalidLegacy = 0;
        int invalidPerEye = 0;

        AppendSamples(CoreDirections, left, right, fish, width, height, csv,
            ref maxLegacyVertical, ref maxPerEyeVertical, ref invalidLegacy, ref invalidPerEye);
        if (includePeripheralSamples)
        {
            AppendSamples(PeripheralDirections, left, right, fish, width, height, csv,
                ref maxLegacyVertical, ref maxPerEyeVertical, ref invalidLegacy, ref invalidPerEye);
        }

        string matrixSource = usingRuntimeStereo ? "runtime XR eye matrices" : "parallel mock stereo matrices";
        summary = string.Format(CultureInfo.InvariantCulture,
            "Stereo diagnostics ({0}): IPD={1:F4} m, eye resolution={2}x{3}, fisheye={4}, virtual FOV={5:F1} deg. " +
            "Maximum |dy|: legacy={6:F2}px, per-eye-matrix={7:F2}px. Invalid samples: legacy={8}, per-eye-matrix={9}.",
            matrixSource, ipd, width, height, fish.Enabled, CurrentVirtualFov(),
            maxLegacyVertical, maxPerEyeVertical, invalidLegacy, invalidPerEye);
        return csv.ToString();
    }

    void AppendSamples(SampleDirection[] directions, EyeMatrices left, EyeMatrices right,
        FishParameters fish, int width, int height, StringBuilder csv,
        ref float maxLegacyVertical, ref float maxPerEyeVertical,
        ref int invalidLegacy, ref int invalidPerEye)
    {
        foreach (SampleDirection direction in directions)
        {
            foreach (float depth in Depths)
            {
                Vector3 worldPoint = BuildWorldPoint(direction, depth);

                Vector2 legacyLeft = ProjectLegacy(worldPoint, left, fish, out bool legacyLeftValid);
                Vector2 legacyRight = ProjectLegacy(worldPoint, right, fish, out bool legacyRightValid);
                AppendRow(csv, "legacy_symmetric", direction, depth, legacyLeft, legacyRight,
                    legacyLeftValid, legacyRightValid, width, height, ref maxLegacyVertical);
                if (!legacyLeftValid || !legacyRightValid)
                    ++invalidLegacy;

                Vector2 perEyeLeft = ProjectThroughEyeMatrix(worldPoint, left, fish, out bool perEyeLeftValid);
                Vector2 perEyeRight = ProjectThroughEyeMatrix(worldPoint, right, fish, out bool perEyeRightValid);
                AppendRow(csv, "per_eye_matrix", direction, depth, perEyeLeft, perEyeRight,
                    perEyeLeftValid, perEyeRightValid, width, height, ref maxPerEyeVertical);
                if (!perEyeLeftValid || !perEyeRightValid)
                    ++invalidPerEye;
            }
        }
    }

    static void AppendRow(StringBuilder csv, string projection, SampleDirection direction, float depth,
        Vector2 left, Vector2 right, bool leftValid, bool rightValid, int width, int height,
        ref float maxVerticalDisparity)
    {
        float dx = (left.x - right.x) * 0.5f * width;
        float dy = (left.y - right.y) * 0.5f * height;
        if (leftValid && rightValid)
            maxVerticalDisparity = Mathf.Max(maxVerticalDisparity, Mathf.Abs(dy));

        csv.Append(projection).Append(',')
            .Append(direction.Name).Append(',')
            .Append(F(depth)).Append(',')
            .Append(F(direction.YawDegrees)).Append(',')
            .Append(F(direction.PitchDegrees)).Append(',')
            .Append(F(left.x)).Append(',').Append(F(left.y)).Append(',')
            .Append(F(right.x)).Append(',').Append(F(right.y)).Append(',')
            .Append(F(dx)).Append(',').Append(F(dy)).Append(',')
            .Append(leftValid ? '1' : '0').Append(',')
            .Append(rightValid ? '1' : '0').AppendLine();
    }

    Vector3 BuildWorldPoint(SampleDirection direction, float depth)
    {
        float x = Mathf.Tan(direction.YawDegrees * Mathf.Deg2Rad);
        float y = Mathf.Tan(direction.PitchDegrees * Mathf.Deg2Rad);
        Vector3 localDirection = new Vector3(x, y, 1.0f).normalized;
        return targetCamera.transform.TransformPoint(localDirection * depth);
    }

    Vector2 ProjectLegacy(Vector3 worldPoint, EyeMatrices eye, FishParameters fish, out bool valid)
    {
        Vector3 p = eye.View.MultiplyPoint3x4(worldPoint);
        if (!fish.Enabled)
            return ProjectPerspective(p, eye.GpuProjection, out valid);

        if (!TryWarpDirection(p, fish, out Vector2 warpedTangent))
        {
            valid = false;
            return Vector2.zero;
        }

        valid = true;
        // Matches the playcanvas-direct-fisheye branch: eye-specific view pose,
        // but shared symmetric fisheye intrinsics and a manually flipped NDC Y.
        return new Vector2(fish.ScaleX * warpedTangent.x, -fish.ScaleY * warpedTangent.y);
    }

    Vector2 ProjectThroughEyeMatrix(Vector3 worldPoint, EyeMatrices eye, FishParameters fish, out bool valid)
    {
        Vector3 p = eye.View.MultiplyPoint3x4(worldPoint);
        if (!fish.Enabled)
            return ProjectPerspective(p, eye.GpuProjection, out valid);

        if (!TryWarpDirection(p, fish, out Vector2 warpedTangent))
        {
            valid = false;
            return Vector2.zero;
        }

        // The nonlinear camera produces a virtual tangent-space ray. Routing
        // that ray through the complete per-eye matrix preserves asymmetric
        // frusta and the eye-specific principal-point offset.
        Vector4 clip = eye.GpuProjection * new Vector4(warpedTangent.x, warpedTangent.y, -1.0f, 1.0f);
        valid = Mathf.Abs(clip.w) > 1e-6f;
        return valid ? new Vector2(clip.x / clip.w, clip.y / clip.w) : Vector2.zero;
    }

    static Vector2 ProjectPerspective(Vector3 viewPoint, Matrix4x4 projection, out bool valid)
    {
        Vector4 clip = projection * new Vector4(viewPoint.x, viewPoint.y, viewPoint.z, 1.0f);
        valid = Mathf.Abs(clip.w) > 1e-6f;
        return valid ? new Vector2(clip.x / clip.w, clip.y / clip.w) : Vector2.zero;
    }

    static bool TryWarpDirection(Vector3 viewPoint, FishParameters fish, out Vector2 warpedTangent)
    {
        float rxy = new Vector2(viewPoint.x, viewPoint.y).magnitude;
        float negZ = -viewPoint.z;
        float theta = Mathf.Atan2(rxy, negZ);
        if (theta > fish.MaxTheta - 0.01f || viewPoint.sqrMagnitude < 1e-8f)
        {
            warpedTangent = Vector2.zero;
            return false;
        }

        float gTheta = fish.K * Mathf.Tan(theta * fish.InvK);
        warpedTangent = rxy > 1e-5f
            ? new Vector2(viewPoint.x, viewPoint.y) * (gTheta / rxy)
            : Vector2.zero;
        return true;
    }

    void GetEyeMatrices(out EyeMatrices left, out EyeMatrices right, out bool usingRuntimeStereo)
    {
        usingRuntimeStereo = preferRuntimeStereoMatrices && targetCamera.stereoEnabled;
        if (usingRuntimeStereo)
        {
            Matrix4x4 leftView = targetCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 rightView = targetCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
            Matrix4x4 leftProjection = GL.GetGPUProjectionMatrix(
                targetCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left), true);
            Matrix4x4 rightProjection = GL.GetGPUProjectionMatrix(
                targetCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right), true);
            left = new EyeMatrices(leftView, leftProjection);
            right = new EyeMatrices(rightView, rightProjection);
            return;
        }

        Matrix4x4 centerView = targetCamera.worldToCameraMatrix;
        float halfIpd = mockIpdMeters * 0.5f;
        // Parallel cameras: translating the left eye negatively in world space
        // is equivalent to adding +halfIPD in camera/view space.
        Matrix4x4 leftViewMock = Matrix4x4.Translate(new Vector3(halfIpd, 0.0f, 0.0f)) * centerView;
        Matrix4x4 rightViewMock = Matrix4x4.Translate(new Vector3(-halfIpd, 0.0f, 0.0f)) * centerView;
        Matrix4x4 projection = GL.GetGPUProjectionMatrix(targetCamera.projectionMatrix, true);
        left = new EyeMatrices(leftViewMock, projection);
        right = new EyeMatrices(rightViewMock, projection);
    }

    FishParameters CalculateFishParameters(float aspect)
    {
        float strength = targetSplat != null ? Mathf.Clamp01(targetSplat.m_FisheyeStrength) : 0.0f;
        float verticalFov = CurrentVirtualFov();
        if (strength <= 0.0001f)
            return new FishParameters(false, 1.0f, 1.0f, 1.0f, 1.0f, Mathf.PI * 0.5f);

        float halfVerticalFov = verticalFov * Mathf.Deg2Rad * 0.5f;
        float p11 = 1.0f / Mathf.Tan(halfVerticalFov);
        float p00 = p11 / Mathf.Max(aspect, 0.0001f);
        float halfFovX = Mathf.Atan2(1.0f, p00);
        float halfFovY = Mathf.Atan2(1.0f, p11);
        float kMin = verticalFov / 180.0f + 0.15f;
        float kStart = Mathf.Max(1.0f, verticalFov / 180.0f + 0.05f);
        float k = kStart * Mathf.Pow(kMin / kStart, strength);
        float invK = 1.0f / k;
        float cornerScale = 1.0f + (Mathf.Sqrt(2.0f) - 1.0f) * strength;
        float maxTheta = Mathf.Min(k * Mathf.PI * 0.5f, 3.13f);
        float effectiveHalfFovX = Mathf.Min(halfFovX, maxTheta - 0.01f);
        float effectiveHalfFovY = Mathf.Min(halfFovY, maxTheta - 0.01f);
        float scaleX = cornerScale / (k * Mathf.Tan(effectiveHalfFovX * invK));
        float scaleY = cornerScale / (k * Mathf.Tan(effectiveHalfFovY * invK));
        return new FishParameters(true, k, invK, scaleX, scaleY, maxTheta);
    }

    float CurrentVirtualFov()
    {
        if (targetSplat != null && targetSplat.m_FisheyeFieldOfView > 0.0f)
            return Mathf.Clamp(targetSplat.m_FisheyeFieldOfView, 20.0f, 359.9f);
        return targetCamera != null ? targetCamera.fieldOfView : 60.0f;
    }

    void ResolveTargets()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();
        ResolveSplat();
    }

    void ResolveSplat()
    {
        if (targetSplat != null && targetSplat.isActiveAndEnabled && targetSplat.m_Asset != null)
            return;

        GaussianSplatRenderer[] renderers = UnityEngine.Object.FindObjectsByType<GaussianSplatRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (GaussianSplatRenderer renderer in renderers)
        {
            if (renderer.isActiveAndEnabled && renderer.m_Asset != null)
            {
                targetSplat = renderer;
                return;
            }
        }
    }

    static string F(float value) => value.ToString("G9", CultureInfo.InvariantCulture);
}