using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.XR;

[DisallowMultipleComponent]
public sealed class DirectFisheyeVrDiagnostics : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField] bool showOverlay = true;
    [SerializeField] bool logWarnings = true;
    [SerializeField] float minExpectedIpdMeters = 0.01f;
    [SerializeField] float staticPoseWarningSeconds = 2.0f;
    [SerializeField] float stretchWarningRatio = 8.0f;
    [SerializeField] float highFovWarningDegrees = 150.0f;
    [SerializeField] float highFisheyeWarning = 0.85f;

    readonly GUIContent[] lines = new GUIContent[5];
    GUIStyle labelStyle;
    GUIStyle titleStyle;

    Vector3 previousPosePosition;
    Quaternion previousPoseRotation = Quaternion.identity;
    bool hasPreviousPose;
    float lastPoseChangeTime;
    float nextWarningTime;
    float currentIpdMeters;
    float stereoMatrixDelta;
    float poseStaticSeconds;
    float maxFisheyeStretchRatio = 1.0f;
    bool stereoEnabled;
    bool xrRunning;
    bool directFisheyeActive;

    void Awake()
    {
        ResolveTargets();
        lastPoseChangeTime = Time.unscaledTime;
    }

    void LateUpdate()
    {
        ResolveTargets();
        UpdatePoseStatus();
        UpdateStereoStatus();
        UpdateProjectionStatus();
        MaybeLogWarnings();
    }

    void OnGUI()
    {
        if (!showOverlay)
            return;

        EnsureGuiStyles();

        float width = Mathf.Min(460.0f, Screen.width - 24.0f);
        if (width <= 160.0f)
            return;

        GUILayout.BeginArea(new Rect(12.0f, 12.0f, width, 142.0f), GUI.skin.box);
        GUILayout.Label("Direct fisheye VR diagnostics", titleStyle);
        lines[0] = new GUIContent($"XR {(xrRunning ? "ON" : "OFF")}  Stereo {(stereoEnabled ? "ON" : "OFF")}  IPD {currentIpdMeters * 1000.0f:F1} mm");
        lines[1] = new GUIContent($"Eye matrix delta {stereoMatrixDelta:F4}  Pose static {poseStaticSeconds:F1}s");
        lines[2] = new GUIContent($"FOV {CurrentFov():F0}  Fisheye {CurrentFisheye():F2}  Direct {(directFisheyeActive ? "ON" : "OFF")}");
        lines[3] = new GUIContent($"Fisheye stretch probe {maxFisheyeStretchRatio:F1}x  Sort per eye in XR");
        lines[4] = new GUIContent("Move/turn the simulated HMD; IPD and pose values should change in Play Mode.");
        for (int i = 0; i < lines.Length; ++i)
            GUILayout.Label(lines[i], labelStyle);
        GUILayout.EndArea();
    }

    void ResolveTargets()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (targetSplat != null && targetSplat.isActiveAndEnabled && targetSplat.m_Asset != null)
            return;

        var renderers = Object.FindObjectsByType<GaussianSplatRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var renderer in renderers)
        {
            if (renderer.isActiveAndEnabled && renderer.m_Asset != null)
            {
                targetSplat = renderer;
                return;
            }
        }
    }

    void UpdatePoseStatus()
    {
        if (targetCamera == null)
            return;

        Vector3 position = targetCamera.transform.position;
        Quaternion rotation = targetCamera.transform.rotation;
        if (!hasPreviousPose ||
            Vector3.Distance(position, previousPosePosition) > 0.0001f ||
            Quaternion.Angle(rotation, previousPoseRotation) > 0.05f)
        {
            lastPoseChangeTime = Time.unscaledTime;
            previousPosePosition = position;
            previousPoseRotation = rotation;
            hasPreviousPose = true;
        }

        poseStaticSeconds = Time.unscaledTime - lastPoseChangeTime;
    }

    void UpdateStereoStatus()
    {
        xrRunning = XRSettings.enabled && XRSettings.isDeviceActive;
        stereoEnabled = targetCamera != null && targetCamera.stereoEnabled;
        currentIpdMeters = 0.0f;
        stereoMatrixDelta = 0.0f;

        if (targetCamera == null || !stereoEnabled)
            return;

        Matrix4x4 left = targetCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left);
        Matrix4x4 right = targetCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right);
        stereoMatrixDelta = MatrixDifference(left, right);

        Vector3 leftPos = EyePosition(left);
        Vector3 rightPos = EyePosition(right);
        if (IsFinite(leftPos) && IsFinite(rightPos))
            currentIpdMeters = Vector3.Distance(leftPos, rightPos);
    }

    void UpdateProjectionStatus()
    {
        directFisheyeActive = targetSplat != null && targetSplat.m_FisheyeStrength > 0.0001f;
        maxFisheyeStretchRatio = EstimateFisheyeStretchRatio();
    }

    void MaybeLogWarnings()
    {
        if (!Application.isPlaying || !logWarnings || Time.unscaledTime < nextWarningTime)
            return;

        nextWarningTime = Time.unscaledTime + 4.0f;

        if (XRSettings.enabled && !stereoEnabled)
            Debug.LogWarning("Direct fisheye VR validation: XR is enabled, but this camera is not rendering stereo.");

        if (stereoEnabled && currentIpdMeters < minExpectedIpdMeters)
            Debug.LogWarning("Direct fisheye VR validation: left/right eye matrices are not separated. Check Mock HMD/OpenXR stereo setup.");

        if (stereoEnabled && poseStaticSeconds > staticPoseWarningSeconds)
            Debug.LogWarning("Direct fisheye VR validation: camera pose has not changed recently. Move the simulated HMD to confirm head tracking reaches the render camera.");

        bool highProjection = CurrentFov() >= highFovWarningDegrees || CurrentFisheye() >= highFisheyeWarning;
        if (directFisheyeActive && highProjection && maxFisheyeStretchRatio >= stretchWarningRatio)
            Debug.LogWarning($"Direct fisheye VR validation: high FOV/fisheye stretch probe reached {maxFisheyeStretchRatio:F1}x. Inspect edge splats for elongated footprints.");
    }

    float EstimateFisheyeStretchRatio()
    {
        if (targetCamera == null || targetSplat == null || targetSplat.m_FisheyeStrength <= 0.0001f)
            return 1.0f;

        var (fisheyeParams, fisheyeParams2) = targetSplat.GetFisheyeShaderParams(targetCamera);
        if (fisheyeParams.x <= 0.0001f || fisheyeParams2.y <= 0.1f)
            return 1.0f;

        int width = XRSettings.eyeTextureWidth > 0 ? XRSettings.eyeTextureWidth : targetCamera.pixelWidth;
        int height = XRSettings.eyeTextureHeight > 0 ? XRSettings.eyeTextureHeight : targetCamera.pixelHeight;
        Vector2 screen = new(Mathf.Max(width, 1), Mathf.Max(height, 1));
        float maxTheta = Mathf.Min(fisheyeParams2.y - 0.02f, Mathf.PI - 0.05f);
        if (maxTheta <= 0.08f)
            return 1.0f;

        const float eps = 0.0025f;
        float maxRatio = 1.0f;
        for (int ti = 1; ti <= 8; ++ti)
        {
            float theta = Mathf.Lerp(0.05f, maxTheta * 0.95f, ti / 8.0f);
            for (int pi = 0; pi < 8; ++pi)
            {
                float phi = pi * Mathf.PI * 0.25f;
                if (!TryProjectFisheye(theta, phi, fisheyeParams, fisheyeParams2, screen, out Vector2 p) ||
                    !TryProjectFisheye(theta + eps, phi, fisheyeParams, fisheyeParams2, screen, out Vector2 pTheta) ||
                    !TryProjectFisheye(theta, phi + eps, fisheyeParams, fisheyeParams2, screen, out Vector2 pPhi))
                    continue;

                float dTheta = (pTheta - p).magnitude / eps;
                float dPhi = (pPhi - p).magnitude / Mathf.Max(eps * Mathf.Sin(theta), 0.0001f);
                float smaller = Mathf.Max(Mathf.Min(dTheta, dPhi), 0.0001f);
                maxRatio = Mathf.Max(maxRatio, Mathf.Max(dTheta, dPhi) / smaller);
            }
        }

        return maxRatio;
    }

    static bool TryProjectFisheye(float theta, float phi, Vector4 fisheyeParams, Vector4 fisheyeParams2,
        Vector2 screen, out Vector2 pixel)
    {
        pixel = screen * 0.5f;
        if (theta <= 0.0f || theta >= fisheyeParams2.y)
            return false;

        float k = fisheyeParams.y;
        float invK = fisheyeParams.z;
        float projMat00 = fisheyeParams.w;
        float projMat11 = fisheyeParams2.x;
        float gTheta = k * Mathf.Tan(theta * invK);
        float ndcX = projMat00 * gTheta * Mathf.Cos(phi);
        float ndcY = projMat11 * gTheta * Mathf.Sin(phi);

        pixel = new Vector2(
            (ndcX * 0.5f + 0.5f) * screen.x,
            (-ndcY * 0.5f + 0.5f) * screen.y);
        return IsFinite(pixel);
    }

    float CurrentFov() => targetSplat != null ? targetSplat.m_FisheyeFieldOfView :
        targetCamera != null ? targetCamera.fieldOfView : 0.0f;

    float CurrentFisheye() => targetSplat != null ? targetSplat.m_FisheyeStrength : 0.0f;

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

    static bool IsFinite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);

    static bool IsFinite(Vector2 value) => IsFinite(value.x) && IsFinite(value.y);

    static bool IsFinite(Vector3 value) => IsFinite(value.x) && IsFinite(value.y) && IsFinite(value.z);

    void EnsureGuiStyles()
    {
        if (labelStyle != null)
            return;

        labelStyle = new GUIStyle(GUI.skin.label)
        {
            fontSize = 12,
            normal = { textColor = new Color(0.88f, 0.93f, 0.96f, 0.95f) }
        };
        titleStyle = new GUIStyle(labelStyle)
        {
            fontSize = 13,
            fontStyle = FontStyle.Bold,
            normal = { textColor = Color.white }
        };
    }
}
