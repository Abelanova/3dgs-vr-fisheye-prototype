using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class DesktopHighQualityFisheye : MonoBehaviour
{
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField, Range(256, 2048)] int faceResolution = 1024;
    [SerializeField, Min(0.005f)] float positionRefreshDistance = 0.02f;
    [SerializeField, Range(60.0f, 130.0f)] float directPerspectiveFovLimit = 100.0f;
    [SerializeField] bool highQualityEnabled = true;

    static readonly string[] FaceTextureNames =
    {
        "_FacePX", "_FaceNX", "_FacePY", "_FaceNY", "_FacePZ", "_FaceNZ"
    };

    static readonly Quaternion[] FaceRotations =
    {
        Quaternion.LookRotation(Vector3.right, Vector3.up),
        Quaternion.LookRotation(Vector3.left, Vector3.up),
        Quaternion.LookRotation(Vector3.up, Vector3.back),
        Quaternion.LookRotation(Vector3.down, Vector3.forward),
        Quaternion.LookRotation(Vector3.forward, Vector3.up),
        Quaternion.LookRotation(Vector3.back, Vector3.up)
    };

    Camera outputCamera;
    GaussianSplatProjectionCamera outputMarker;
    Camera[] faceCameras;
    RenderTexture[] faceTextures;
    Canvas outputCanvas;
    RawImage outputImage;
    Material outputMaterial;
    int allocatedResolution;
    bool hasValidCapture;
    bool cubemapActive;
    Vector3 capturedPosition;

    bool WantsHighQuality =>
        highQualityEnabled && targetSplat != null && targetSplat.isActiveAndEnabled &&
        targetSplat.asset != null;

    void Awake() => outputCamera = GetComponent<Camera>();

    void OnEnable()
    {
        outputCamera = GetComponent<Camera>();
        if (Application.isPlaying)
            EnsureResources();
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        ResolveTarget();
        EnsureResources();
        bool resourcesReady = WantsHighQuality && outputMaterial != null;
        var (fisheyeParams, fisheyeParams2) = resourcesReady
            ? targetSplat.GetFisheyeShaderParams(outputCamera)
            : (Vector4.zero, Vector4.zero);
        bool fisheyeEnabled = fisheyeParams.x > 0.0001f;
        if (!resourcesReady)
            cubemapActive = false;
        else if (fisheyeEnabled)
            cubemapActive = true;
        else
        {
            // Hysteresis prevents rapid path switching while the FOV slider sits
            // around the boundary. Both paths use the same perspective geometry.
            float threshold = directPerspectiveFovLimit + (cubemapActive ? -5.0f : 5.0f);
            cubemapActive = outputCamera.fieldOfView > threshold;
        }
        bool active = resourcesReady && cubemapActive;

        outputMarker.enabled = active;
        outputCanvas.gameObject.SetActive(active);
        bool needsCapture = active && (!hasValidCapture ||
            (outputCamera.transform.position - capturedPosition).sqrMagnitude >=
            positionRefreshDistance * positionRefreshDistance);
        for (int i = 0; i < faceCameras.Length; ++i)
        {
            Camera faceCamera = faceCameras[i];
            faceCamera.enabled = needsCapture;
            if (!needsCapture)
                continue;

            faceCamera.transform.SetPositionAndRotation(outputCamera.transform.position,
                FaceRotations[i]);
            faceCamera.nearClipPlane = outputCamera.nearClipPlane;
            faceCamera.farClipPlane = outputCamera.farClipPlane;
            faceCamera.depth = outputCamera.depth - 10.0f + i;
        }

        if (needsCapture)
        {
            capturedPosition = outputCamera.transform.position;
            hasValidCapture = true;
        }

        if (!active)
            return;

        float perspectiveP11 = 1.0f / Mathf.Tan(outputCamera.fieldOfView * Mathf.Deg2Rad * 0.5f);
        float perspectiveP00 = perspectiveP11 / Mathf.Max(outputCamera.aspect, 0.0001f);
        outputMaterial.SetFloat("_FishEnabled", fisheyeEnabled ? 1.0f : 0.0f);
        outputMaterial.SetVector("_PerspectiveScale", new Vector4(perspectiveP00, perspectiveP11, 0, 0));
        outputMaterial.SetVector("_FishParams", new Vector4(
            fisheyeParams.y, fisheyeParams.z, fisheyeParams.w, fisheyeParams2.x));
        outputMaterial.SetFloat("_MaxTheta", fisheyeParams2.y);
        outputMaterial.SetMatrix("_CameraToWorld",
            Matrix4x4.Rotate(outputCamera.transform.rotation));
        UpdateOutputRect();
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

    void EnsureResources()
    {
        int resolution = Mathf.Clamp(faceResolution, 256, 2048);
        if (faceCameras != null && allocatedResolution == resolution)
            return;

        ReleaseResources();
        allocatedResolution = resolution;
        outputCamera = GetComponent<Camera>();

        outputMarker = GetComponent<GaussianSplatProjectionCamera>();
        if (outputMarker == null)
            outputMarker = gameObject.AddComponent<GaussianSplatProjectionCamera>();
        outputMarker.role = GaussianSplatProjectionCameraRole.Output;

        faceCameras = new Camera[6];
        faceTextures = new RenderTexture[6];
        for (int i = 0; i < 6; ++i)
        {
            var texture = new RenderTexture(resolution, resolution, 24, RenderTextureFormat.ARGBHalf,
                RenderTextureReadWrite.Linear)
            {
                name = $"HQ Fisheye Face {i}", hideFlags = HideFlags.HideAndDontSave,
                useMipMap = false, autoGenerateMips = false, antiAliasing = 1
            };
            texture.Create();
            faceTextures[i] = texture;

            var cameraObject = new GameObject($"HQ Fisheye Capture {i}")
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            var camera = cameraObject.AddComponent<Camera>();
            camera.CopyFrom(outputCamera);
            camera.targetTexture = texture;
            camera.fieldOfView = 90.0f;
            camera.aspect = 1.0f;
            camera.rect = new Rect(0, 0, 1, 1);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = Color.black;
            camera.cullingMask = 0;
            camera.allowMSAA = false;
            camera.allowHDR = true;
            camera.stereoTargetEye = StereoTargetEyeMask.None;
            camera.enabled = false;
            cameraObject.AddComponent<GaussianSplatProjectionCamera>().role =
                GaussianSplatProjectionCameraRole.Capture;
            var cameraData = camera.GetUniversalAdditionalCameraData();
            cameraData.renderPostProcessing = false;
            cameraData.renderType = CameraRenderType.Base;
            faceCameras[i] = camera;
        }

        Shader shader = Resources.Load<Shader>("HighQualityFisheyeComposite");
        if (shader != null)
        {
            outputMaterial = new Material(shader)
            {
                name = "HQ Fisheye Composite", hideFlags = HideFlags.HideAndDontSave
            };
            for (int i = 0; i < 6; ++i)
                outputMaterial.SetTexture(FaceTextureNames[i], faceTextures[i]);
        }

        var canvasObject = new GameObject("HQ Fisheye Output")
        {
            hideFlags = HideFlags.HideAndDontSave
        };
        outputCanvas = canvasObject.AddComponent<Canvas>();
        outputCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        outputCanvas.sortingOrder = -1000;
        var imageObject = new GameObject("Image") { hideFlags = HideFlags.HideAndDontSave };
        imageObject.transform.SetParent(canvasObject.transform, false);
        outputImage = imageObject.AddComponent<RawImage>();
        outputImage.raycastTarget = false;
        outputImage.material = outputMaterial;
        outputImage.texture = faceTextures[4];
        UpdateOutputRect();
        hasValidCapture = false;
    }

    void UpdateOutputRect()
    {
        if (outputImage == null)
            return;
        Rect rect = outputCamera.rect;
        RectTransform rt = outputImage.rectTransform;
        rt.anchorMin = new Vector2(rect.xMin, rect.yMin);
        rt.anchorMax = new Vector2(rect.xMax, rect.yMax);
        rt.offsetMin = Vector2.zero;
        rt.offsetMax = Vector2.zero;
    }

    void OnDisable() => ReleaseResources();
    void OnDestroy() => ReleaseResources();

    void ReleaseResources()
    {
        if (outputMarker != null)
            outputMarker.enabled = false;
        if (faceCameras != null)
            foreach (Camera camera in faceCameras)
                if (camera != null)
                    Destroy(camera.gameObject);
        if (faceTextures != null)
            foreach (RenderTexture texture in faceTextures)
                if (texture != null)
                {
                    texture.Release();
                    Destroy(texture);
                }
        if (outputCanvas != null)
            Destroy(outputCanvas.gameObject);
        if (outputMaterial != null)
            Destroy(outputMaterial);

        faceCameras = null;
        faceTextures = null;
        outputCanvas = null;
        outputImage = null;
        outputMaterial = null;
        allocatedResolution = 0;
        hasValidCapture = false;
        cubemapActive = false;
    }
}
