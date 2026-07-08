using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.Rendering;

/// <summary>
/// Adds binocular disparity to the direct (non-composite) fisheye splat path.
///
/// The Gaussian centres and covariances remain cyclopean: both eyes use the same
/// projected ellipse. The splat vertex shader then applies a small, depth-based
/// horizontal disparity per eye. This deliberately avoids independently warping
/// the covariance for the left and right eyes, which tends to create two shapes
/// that cannot be fused.
/// </summary>
[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
[DefaultExecutionOrder(-1000)]
public sealed class BinocularFisheyeStereo : MonoBehaviour
{
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField, Min(0.1f)] float convergenceDistance = 2.0f;
    [SerializeField, Range(0.0f, 2.0f)] float stereoScale = 1.0f;
    [SerializeField, Range(0.005f, 0.15f)] float maximumPerEyeNdcShift = 0.06f;
    [SerializeField] bool enableBinocularFisheye = true;

    static readonly int BinocularParamsId = Shader.PropertyToID("_GSBinocularParams");
    static readonly int BinocularParams2Id = Shader.PropertyToID("_GSBinocularParams2");

    Camera xrCamera;

    void Awake() => xrCamera = GetComponent<Camera>();

    void OnEnable()
    {
        xrCamera = GetComponent<Camera>();
        RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
    }

    void OnDisable()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
        DisableGlobalStereo();
    }

    void OnDestroy()
    {
        RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
    }

    void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
    {
        if (camera != xrCamera)
            return;

        ResolveTarget();

        bool directFisheyeActive =
            enableBinocularFisheye &&
            xrCamera != null &&
            xrCamera.stereoEnabled &&
            targetSplat != null &&
            targetSplat.isActiveAndEnabled &&
            targetSplat.asset != null &&
            targetSplat.m_FisheyeStrength > 0.0001f;

        // VrHighQualityFisheye marks the output camera so the direct splat pass is
        // skipped. Do not apply the direct stereo correction to that composite path.
        if (xrCamera != null &&
            xrCamera.TryGetComponent<GaussianSplatProjectionCamera>(out var marker) &&
            marker.isActiveAndEnabled &&
            marker.role == GaussianSplatProjectionCameraRole.Output)
        {
            directFisheyeActive = false;
        }

        if (!directFisheyeActive)
        {
            DisableGlobalStereo();
            return;
        }

        float ipd = GetCurrentIpd();
        float nearPlane = Mathf.Max(xrCamera.nearClipPlane, 0.001f);
        float farPlane = Mathf.Max(xrCamera.farClipPlane, nearPlane + 0.001f);
        float convergence = Mathf.Max(convergenceDistance, nearPlane + 0.001f);

        Shader.SetGlobalVector(BinocularParamsId, new Vector4(
            1.0f,
            nearPlane,
            farPlane,
            SystemInfo.usesReversedZBuffer ? 1.0f : 0.0f));

        Shader.SetGlobalVector(BinocularParams2Id, new Vector4(
            ipd,
            convergence,
            stereoScale,
            maximumPerEyeNdcShift));
    }

    float GetCurrentIpd()
    {
        if (xrCamera == null || !xrCamera.stereoEnabled)
            return 0.0f;

        Matrix4x4 leftEyeToWorld =
            xrCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Left).inverse;
        Matrix4x4 rightEyeToWorld =
            xrCamera.GetStereoViewMatrix(Camera.StereoscopicEye.Right).inverse;

        Vector3 leftPosition = leftEyeToWorld.GetColumn(3);
        Vector3 rightPosition = rightEyeToWorld.GetColumn(3);
        float measuredIpd = Vector3.Distance(leftPosition, rightPosition);

        // Some editor XR simulators report coincident eye poses for a frame or two.
        // A conventional 64 mm fallback keeps the preview visibly stereoscopic.
        return measuredIpd > 0.001f ? measuredIpd : 0.064f;
    }

    void ResolveTarget()
    {
        if (targetSplat != null && targetSplat.isActiveAndEnabled && targetSplat.asset != null)
            return;

        foreach (var renderer in FindObjectsByType<GaussianSplatRenderer>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (renderer.isActiveAndEnabled && renderer.asset != null)
            {
                targetSplat = renderer;
                return;
            }
        }
    }

    static void DisableGlobalStereo()
    {
        Shader.SetGlobalVector(BinocularParamsId, Vector4.zero);
        Shader.SetGlobalVector(BinocularParams2Id, Vector4.zero);
    }
}

/// <summary>
/// Installs BinocularFisheyeStereo on XR-capable scene cameras without requiring
/// a scene YAML edit. The component remains visible and can be tuned in Inspector.
/// </summary>
public static class BinocularFisheyeStereoInstaller
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void Install()
    {
        foreach (Camera camera in Object.FindObjectsByType<Camera>(
                     FindObjectsInactive.Exclude, FindObjectsSortMode.None))
        {
            if (camera.stereoTargetEye == StereoTargetEyeMask.None)
                continue;
            if (camera.GetComponent<BinocularFisheyeStereo>() == null)
                camera.gameObject.AddComponent<BinocularFisheyeStereo>();
        }
    }
}
