using System.Collections;
using System;
using System.IO;
using System.Reflection;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.XR;
using UnityEngine.XR.OpenXR.NativeTypes;

#if UNITY_EDITOR
using UnityEditor;
#endif

[DisallowMultipleComponent]
public sealed class DirectFisheyeVrCaptureDriver : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField] CameraFovController fovController;
    [SerializeField] string outputDirectory = "Recordings/DirectFisheyeVrValidation";

    const float IpdMeters = 0.064f;
    readonly WaitForEndOfFrame waitForEndOfFrame = new();
    Vector3 baseHeadPosition;
    Quaternion baseHeadRotation;

    void Awake()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();
        if (targetSplat == null)
            targetSplat = UnityEngine.Object.FindFirstObjectByType<GaussianSplatRenderer>();
        if (fovController == null)
            fovController = targetCamera != null ? targetCamera.GetComponent<CameraFovController>() : null;
    }

    IEnumerator Start()
    {
        string root = ResolveOutputDirectory();
        Directory.CreateDirectory(root);
        yield return null;
        yield return null;

        ApplyMockHeadPose(Vector3.zero, Quaternion.identity, 120.0f);
        yield return null;
        if (!Application.isBatchMode)
            yield return waitForEndOfFrame;

        baseHeadPosition = targetCamera.transform.position;
        baseHeadRotation = targetCamera.transform.rotation;

        yield return CaptureCase("dynamic_scale035_center", 120.0f, 0.20f,
            Vector3.zero, Quaternion.identity);
        yield return CaptureCase("dynamic_scale035_yaw_left10", 120.0f, 0.20f,
            Vector3.zero, Quaternion.Euler(0.0f, -10.0f, 0.0f));
        yield return CaptureCase("dynamic_scale035_yaw_right10", 120.0f, 0.20f,
            Vector3.zero, Quaternion.Euler(0.0f, 10.0f, 0.0f));
        yield return CaptureCase("dynamic_scale035_translate_left05m", 120.0f, 0.20f,
            Vector3.left * 0.05f, Quaternion.identity);
        yield return CaptureCase("dynamic_scale035_translate_right05m", 120.0f, 0.20f,
            Vector3.right * 0.05f, Quaternion.identity);

#if UNITY_EDITOR
        Debug.Log("Direct fisheye VR capture run complete.");
        EditorApplication.Exit(0);
#endif
    }

    IEnumerator CaptureCase(string caseName, float fov, float fisheye, Vector3 headOffset, Quaternion headRotation)
    {
        ApplyProjection(fov, fisheye);
        ApplyMockHeadPose(headOffset, headRotation, fov);

        yield return null;
        if (!Application.isBatchMode)
            yield return waitForEndOfFrame;

        string path = Path.Combine(ResolveOutputDirectory(), caseName + ".png");
        ScreenCapture.CaptureScreenshot(path);
        string stereoPath = Path.Combine(ResolveOutputDirectory(), caseName + "_stereo_pair.png");
        Vector3 diagnosticPosition = baseHeadPosition + baseHeadRotation * headOffset;
        Quaternion diagnosticRotation = baseHeadRotation * headRotation;
        if (GaussianSplatStereoCapture.WriteStereoDiagnosticsAtPose(targetCamera, targetSplat, stereoPath,
                new Color(0.74f, 0.52f, 0.40f, 1.0f), diagnosticPosition, diagnosticRotation,
                out string stereoMessage))
        {
            Debug.Log($"Direct fisheye VR stereo diagnostics wrote {stereoPath}: {stereoMessage}");
        }
        else
        {
            Debug.LogWarning($"Direct fisheye VR stereo diagnostics failed for {caseName}: {stereoMessage}");
        }

        WriteMetrics(caseName, fov, fisheye, headOffset, headRotation);
        Debug.Log($"Direct fisheye VR capture wrote {path}");

        yield return null;
        if (!Application.isBatchMode)
            yield return waitForEndOfFrame;
    }

    void ApplyProjection(float fov, float fisheye)
    {
        if (fovController != null)
            fovController.verticalFieldOfView = fov;

        if (targetSplat == null)
            return;

        targetSplat.m_SortNthFrame = 1;
        targetSplat.m_FisheyeFieldOfView = fov;
        targetSplat.m_FisheyeStrength = Mathf.Clamp01(fisheye);
        targetSplat.m_StereoIpdMeters = IpdMeters;
        targetSplat.m_StereoConvergenceDistance = 2.0f;
        targetSplat.m_StereoScale = 0.35f;
        targetSplat.m_StereoRadialCompression = 2.0f;
        targetSplat.m_StereoMaxShift = 0.004f;
    }

    static void ApplyMockHeadPose(Vector3 headOffset, Quaternion headRotation, float fov)
    {
        Type mockRuntime = FindMockRuntimeType();
        if (mockRuntime == null)
        {
            Debug.LogWarning("OpenXR MockRuntime type was not found; capture will use the active XR runtime pose.");
            return;
        }

        Vector4 openXrFov = ToOpenXrFov(fov);
        var flags = XrSpaceLocationFlags.OrientationValid | XrSpaceLocationFlags.PositionValid |
            XrSpaceLocationFlags.OrientationTracked | XrSpaceLocationFlags.PositionTracked;
        var viewFlags = XrViewStateFlags.OrientationValid | XrViewStateFlags.PositionValid |
            XrViewStateFlags.OrientationTracked | XrViewStateFlags.PositionTracked;

        InvokeMock(mockRuntime, "SetSpace", XrReferenceSpaceType.Local, headOffset, headRotation, flags);
        InvokeMock(mockRuntime, "SetViewState", XrViewConfigurationType.PrimaryStereo, viewFlags);
        InvokeMock(mockRuntime, "SetViewPose", XrViewConfigurationType.PrimaryStereo, 0,
            headOffset + headRotation * Vector3.left * (IpdMeters * 0.5f), headRotation, openXrFov);
        InvokeMock(mockRuntime, "SetViewPose", XrViewConfigurationType.PrimaryStereo, 1,
            headOffset + headRotation * Vector3.right * (IpdMeters * 0.5f), headRotation, openXrFov);
    }

    static Type FindMockRuntimeType()
    {
        foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            Type type = assembly.GetType("UnityEngine.XR.OpenXR.Features.Mock.MockRuntime");
            if (type != null)
                return type;
        }

        return null;
    }

    static void InvokeMock(Type mockRuntime, string methodName, params object[] args)
    {
        MethodInfo method = null;
        foreach (MethodInfo candidate in mockRuntime.GetMethods(BindingFlags.Public | BindingFlags.Static))
        {
            if (candidate.Name != methodName || candidate.GetParameters().Length != args.Length)
                continue;

            method = candidate;
            break;
        }

        if (method == null)
        {
            Debug.LogWarning($"OpenXR MockRuntime method {methodName} was not found.");
            return;
        }

        method.Invoke(null, args);
    }

    static Vector4 ToOpenXrFov(float verticalFovDegrees)
    {
        float half = Mathf.Clamp(verticalFovDegrees, 20.0f, 170.0f) * Mathf.Deg2Rad * 0.5f;
        return new Vector4(-half, half, half, -half);
    }

    void WriteMetrics(string caseName, float fov, float fisheye, Vector3 headOffset, Quaternion headRotation)
    {
        bool stereoEnabled = targetCamera != null && targetCamera.stereoEnabled;
        float ipd = 0.0f;
        float eyeMatrixDelta = 0.0f;
        if (stereoEnabled)
        {
            Matrix4x4 left = targetCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 right = targetCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
            ipd = Vector3.Distance(EyePosition(left), EyePosition(right));
            eyeMatrixDelta = MatrixDifference(left, right);
        }

        string path = Path.Combine(ResolveOutputDirectory(), caseName + ".txt");
        File.WriteAllText(path,
            $"case={caseName}\n" +
            $"xrEnabled={XRSettings.enabled}\n" +
            $"xrDeviceActive={XRSettings.isDeviceActive}\n" +
            $"stereoEnabled={stereoEnabled}\n" +
            $"ipdMeters={ipd:F5}\n" +
            $"eyeMatrixDelta={eyeMatrixDelta:F5}\n" +
            $"fov={fov:F1}\n" +
            $"fisheye={fisheye:F2}\n" +
            "stereoSaturation=soft\n" +
            $"stereoScale={(targetSplat != null ? targetSplat.m_StereoScale : 0.0f):F3}\n" +
            $"stereoRadialCompression={(targetSplat != null ? targetSplat.m_StereoRadialCompression : 0.0f):F3}\n" +
            $"stereoMaxShift={(targetSplat != null ? targetSplat.m_StereoMaxShift : 0.0f):F3}\n" +
            $"stereoConvergenceMeters={(targetSplat != null ? targetSplat.m_StereoConvergenceDistance : 0.0f):F3}\n" +
            $"headOffset={headOffset.x:F3},{headOffset.y:F3},{headOffset.z:F3}\n" +
            $"headRotationEuler={headRotation.eulerAngles.x:F2},{headRotation.eulerAngles.y:F2},{headRotation.eulerAngles.z:F2}\n" +
            $"diagnosticPosition={baseHeadPosition.x + (baseHeadRotation * headOffset).x:F3}," +
            $"{baseHeadPosition.y + (baseHeadRotation * headOffset).y:F3}," +
            $"{baseHeadPosition.z + (baseHeadRotation * headOffset).z:F3}\n");
    }

    string ResolveOutputDirectory()
    {
        if (Path.IsPathRooted(outputDirectory))
            return outputDirectory;

        string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, ".."));
        return Path.Combine(projectRoot, outputDirectory);
    }

    static Vector3 EyePosition(Matrix4x4 viewMatrix)
    {
        Matrix4x4 inverse = viewMatrix.inverse;
        Vector4 column = inverse.GetColumn(3);
        return new Vector3(column.x, column.y, column.z);
    }

    static float MatrixDifference(Matrix4x4 a, Matrix4x4 b)
    {
        float diff = 0.0f;
        for (int i = 0; i < 16; ++i)
            diff = Mathf.Max(diff, Mathf.Abs(a[i] - b[i]));
        return diff;
    }
}
