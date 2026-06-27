using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.Rendering.Universal;

[DisallowMultipleComponent]
[RequireComponent(typeof(Camera))]
public sealed class VrHighQualityFisheye : MonoBehaviour
{
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField, Range(256, 1024)] int faceResolution = 512;
    [SerializeField, Range(1, 3)] int facePairsPerFrame = 1;
    [SerializeField, Min(0.005f)] float positionRefreshDistance = 0.02f;
    [SerializeField] bool highQualityEnabled;
    [SerializeField] bool monoComposite = true;
    [SerializeField] bool swapEyes;
    [SerializeField, Range(0.0f, 1.0f)] float stereoSeparationScale = 1.0f;

    const int FacesPerEye = 6;
    const int EyeCount = 2;

    static readonly string[] FaceTextureNames =
    {
        "_LeftPX", "_LeftNX", "_LeftPY", "_LeftNY", "_LeftPZ", "_LeftNZ",
        "_RightPX", "_RightNX", "_RightPY", "_RightNY", "_RightPZ", "_RightNZ"
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
    RenderTexture[] faceTextureBuffers;
    Material outputMaterial;
    int allocatedResolution;
    int activeBuffer;
    int nextFacePair;
    int initialCaptureFrame = -1;
    int swapReadyFrame = -1;
    bool hasValidCapture;
    bool updateCycleActive;
    bool swapPending;
    bool outputCameraIsIsolated;
    int originalOutputCullingMask;
    Vector3 activeLeftPosition;
    Vector3 activeRightPosition;
    Vector3 pendingLeftPosition;
    Vector3 pendingRightPosition;

    bool WantsHighQuality =>
        highQualityEnabled && targetSplat != null && targetSplat.isActiveAndEnabled &&
        targetSplat.asset != null && targetSplat.m_FisheyeStrength > 0.0001f;

    void Awake() => outputCamera = GetComponent<Camera>();

    void OnEnable()
    {
        outputCamera = GetComponent<Camera>();
        if (!Application.isPlaying)
            return;

        EnsureResources();
        Application.onBeforeRender += UpdateCompositeEyeRotations;
    }

    void LateUpdate()
    {
        if (!Application.isPlaying)
            return;

        ResolveTarget();
        EnsureResources();
#if ENABLE_LEGACY_INPUT_MANAGER
        UpdateDiagnosticShortcuts();
#endif
        bool active = WantsHighQuality && outputMaterial != null;

        DisableCaptureCameras();

        if (active)
        {
            UpdateProjectionMaterial();
            UpdateCompositeEyeRotations();
            ScheduleCaptureWork();
        }

        bool compositeReady = active && hasValidCapture;
        SetOutputCameraIsolation(compositeReady);

        if (outputMarker != null)
        {
            outputMarker.enabled = compositeReady;
            outputMarker.compositeActive = compositeReady;
            outputMarker.compositeMaterial = compositeReady ? outputMaterial : null;
        }

        if (!active)
            return;
    }

    void UpdateCompositeEyeRotations()
    {
        if (!Application.isPlaying || !WantsHighQuality || outputMaterial == null)
            return;

        GetEyePose(Camera.StereoscopicEye.Left, out _, out Quaternion leftRotation);
        GetEyePose(Camera.StereoscopicEye.Right, out _, out Quaternion rightRotation);
        outputMaterial.SetMatrix("_LeftEyeToWorld", Matrix4x4.Rotate(leftRotation));
        outputMaterial.SetMatrix("_RightEyeToWorld", Matrix4x4.Rotate(rightRotation));
    }

    void ScheduleCaptureWork()
    {
        GetEyePose(Camera.StereoscopicEye.Left, out Vector3 leftPosition, out _);
        GetEyePose(Camera.StereoscopicEye.Right, out Vector3 rightPosition, out _);
        if (UseMonoComposite)
        {
            leftPosition = outputCamera.transform.position;
            rightPosition = leftPosition;
        }
        else if (stereoSeparationScale < 0.999f)
        {
            Vector3 centerPosition = outputCamera.transform.position;
            leftPosition = Vector3.Lerp(centerPosition, leftPosition, stereoSeparationScale);
            rightPosition = Vector3.Lerp(centerPosition, rightPosition, stereoSeparationScale);
        }

        if (!hasValidCapture)
        {
            if (initialCaptureFrame < 0)
            {
                pendingLeftPosition = leftPosition;
                pendingRightPosition = rightPosition;
                ScheduleAllFaces(activeBuffer, pendingLeftPosition, pendingRightPosition);
                initialCaptureFrame = Time.frameCount;
                return;
            }

            if (Time.frameCount <= initialCaptureFrame)
                return;

            hasValidCapture = true;
            activeLeftPosition = pendingLeftPosition;
            activeRightPosition = pendingRightPosition;
            CopyActiveToInactiveBuffer();
        }

        if (swapPending)
        {
            if (Time.frameCount < swapReadyFrame)
                return;

            activeBuffer = 1 - activeBuffer;
            activeLeftPosition = pendingLeftPosition;
            activeRightPosition = pendingRightPosition;
            BindActiveTextures();
            swapPending = false;
            swapReadyFrame = -1;
            updateCycleActive = false;
        }

        float thresholdSq = positionRefreshDistance * positionRefreshDistance;
        bool positionChanged =
            (leftPosition - activeLeftPosition).sqrMagnitude >= thresholdSq ||
            (rightPosition - activeRightPosition).sqrMagnitude >= thresholdSq;
        if (!updateCycleActive && positionChanged)
        {
            pendingLeftPosition = leftPosition;
            pendingRightPosition = rightPosition;
            nextFacePair = 0;
            updateCycleActive = true;
        }

        if (!updateCycleActive)
            return;

        int pairCount = Mathf.Clamp(facePairsPerFrame, 1, 3);
        int stagingBuffer = 1 - activeBuffer;
        for (int i = 0; i < pairCount && nextFacePair < FacesPerEye; ++i)
        {
            ScheduleFace(0, nextFacePair, stagingBuffer, pendingLeftPosition);
            if (!UseMonoComposite)
                ScheduleFace(1, nextFacePair, stagingBuffer, pendingRightPosition);
            ++nextFacePair;
        }

        if (nextFacePair >= FacesPerEye && !swapPending)
        {
            swapPending = true;
            // Camera work scheduled during LateUpdate is submitted later in the
            // frame by URP. Keep displaying the old complete buffer until one
            // additional frame has passed, so a late camera submission can never
            // expose an unfinished (black) staging texture.
            swapReadyFrame = Time.frameCount + 2;
        }
    }

    void SetOutputCameraIsolation(bool isolate)
    {
        if (outputCamera == null)
            return;

        if (!isolate)
        {
            RestoreOutputCameraIsolation();
            return;
        }

        if (!outputCameraIsIsolated)
        {
            originalOutputCullingMask = outputCamera.cullingMask;
            outputCameraIsIsolated = true;
        }

        outputCamera.cullingMask = 0;
    }

    void RestoreOutputCameraIsolation()
    {
        if (outputCamera == null || !outputCameraIsIsolated)
            return;

        outputCamera.cullingMask = originalOutputCullingMask;
        outputCameraIsIsolated = false;
    }

    void ScheduleAllFaces(int buffer, Vector3 leftPosition, Vector3 rightPosition)
    {
        for (int face = 0; face < FacesPerEye; ++face)
        {
            ScheduleFace(0, face, buffer, leftPosition);
            if (!UseMonoComposite)
                ScheduleFace(1, face, buffer, rightPosition);
        }
    }

    void ScheduleFace(int eye, int face, int buffer, Vector3 position)
    {
        int index = eye * FacesPerEye + face;
        Camera capture = faceCameras[index];
        capture.targetTexture = FaceTexture(buffer, index);
        capture.transform.SetPositionAndRotation(position, FaceRotations[face]);
        capture.nearClipPlane = outputCamera.nearClipPlane;
        capture.farClipPlane = outputCamera.farClipPlane;
        capture.depth = outputCamera.depth - 20.0f + index;
        capture.enabled = true;
    }

    void DisableCaptureCameras()
    {
        if (faceCameras == null)
            return;
        foreach (Camera capture in faceCameras)
            if (capture != null)
                capture.enabled = false;
    }

    RenderTexture FaceTexture(int buffer, int faceIndex) =>
        faceTextureBuffers[buffer * EyeCount * FacesPerEye + faceIndex];

    void BindActiveTextures()
    {
        if (outputMaterial == null || faceTextureBuffers == null)
            return;
        for (int i = 0; i < EyeCount * FacesPerEye; ++i)
            outputMaterial.SetTexture(FaceTextureNames[i], FaceTexture(activeBuffer, i));
    }

    void CopyActiveToInactiveBuffer()
    {
        int inactiveBuffer = 1 - activeBuffer;
        for (int i = 0; i < EyeCount * FacesPerEye; ++i)
            Graphics.CopyTexture(FaceTexture(activeBuffer, i), FaceTexture(inactiveBuffer, i));
    }

    void GetEyePose(Camera.StereoscopicEye eye, out Vector3 position, out Quaternion rotation)
    {
        if (!outputCamera.stereoEnabled)
        {
            position = outputCamera.transform.position;
            rotation = outputCamera.transform.rotation;
            return;
        }

        Matrix4x4 eyeToWorld = outputCamera.GetStereoViewMatrix(eye).inverse;
        position = eyeToWorld.GetColumn(3);
        Vector3 forward = eyeToWorld.MultiplyVector(Vector3.back).normalized;
        Vector3 up = eyeToWorld.MultiplyVector(Vector3.up).normalized;
        rotation = Quaternion.LookRotation(forward, up);
    }

    void UpdateProjectionMaterial()
    {
        var (fisheyeParams, fisheyeParams2) = targetSplat.GetFisheyeShaderParams(outputCamera);
        bool fisheyeEnabled = fisheyeParams.x > 0.0001f;
        Matrix4x4 leftProjectionMatrix = GetProjectionMatrix(Camera.StereoscopicEye.Left);
        Matrix4x4 rightProjectionMatrix = GetProjectionMatrix(Camera.StereoscopicEye.Right);
        Vector4 leftProjection = ProjectionParams(leftProjectionMatrix);
        Vector4 rightProjection = ProjectionParams(rightProjectionMatrix);

        outputMaterial.SetFloat("_FishEnabled", fisheyeEnabled ? 1.0f : 0.0f);
        outputMaterial.SetFloat("_MonoComposite", UseMonoComposite ? 1.0f : 0.0f);
        outputMaterial.SetFloat("_SwapEyes", swapEyes ? 1.0f : 0.0f);
        outputMaterial.SetVector("_LeftProjection", leftProjection);
        outputMaterial.SetVector("_RightProjection", rightProjection);
        outputMaterial.SetMatrix("_LeftInvProjection", leftProjectionMatrix.inverse);
        outputMaterial.SetMatrix("_RightInvProjection", rightProjectionMatrix.inverse);
        outputMaterial.SetVector("_FishParams", new Vector4(
            fisheyeParams.y, fisheyeParams.z, fisheyeParams.w, fisheyeParams2.x));
        outputMaterial.SetFloat("_MaxTheta", fisheyeParams2.y);
    }

    Matrix4x4 GetProjectionMatrix(Camera.StereoscopicEye eye)
    {
        return outputCamera.stereoEnabled
            ? outputCamera.GetStereoProjectionMatrix(eye)
            : outputCamera.projectionMatrix;
    }

    static Vector4 ProjectionParams(Matrix4x4 projection)
    {
        return new Vector4(projection.m00, projection.m11, projection.m02, projection.m12);
    }

#if ENABLE_LEGACY_INPUT_MANAGER
    void UpdateDiagnosticShortcuts()
    {
        if (Input.GetKeyDown(KeyCode.M))
        {
            monoComposite = !monoComposite;
            InvalidateCapture();
            Debug.Log($"VR fisheye mono composite: {monoComposite}");
        }

        if (Input.GetKeyDown(KeyCode.X))
        {
            swapEyes = !swapEyes;
            Debug.Log($"VR fisheye swap eyes: {swapEyes}");
        }

        if (Input.GetKeyDown(KeyCode.Comma))
        {
            stereoSeparationScale = Mathf.Max(0.0f, stereoSeparationScale - 0.25f);
            InvalidateCapture();
            Debug.Log($"VR fisheye stereo separation: {stereoSeparationScale:0.00}");
        }

        if (Input.GetKeyDown(KeyCode.Period))
        {
            stereoSeparationScale = Mathf.Min(1.0f, stereoSeparationScale + 0.25f);
            InvalidateCapture();
            Debug.Log($"VR fisheye stereo separation: {stereoSeparationScale:0.00}");
        }
    }
#endif

    void InvalidateCapture()
    {
        hasValidCapture = false;
        updateCycleActive = false;
        swapPending = false;
        swapReadyFrame = -1;
        initialCaptureFrame = -1;
        nextFacePair = 0;
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
        int resolution = Mathf.Clamp(faceResolution, 256, 1024);
        if (faceCameras != null && allocatedResolution == resolution)
            return;

        ReleaseResources(false);
        allocatedResolution = resolution;
        outputCamera = GetComponent<Camera>();

        outputMarker = GetComponent<GaussianSplatProjectionCamera>();
        if (outputMarker == null)
            outputMarker = gameObject.AddComponent<GaussianSplatProjectionCamera>();
        outputMarker.role = GaussianSplatProjectionCameraRole.Output;

        int faceCount = EyeCount * FacesPerEye;
        faceCameras = new Camera[faceCount];
        faceTextureBuffers = new RenderTexture[faceCount * 2];
        for (int buffer = 0; buffer < 2; ++buffer)
        {
            for (int eye = 0; eye < EyeCount; ++eye)
            {
                for (int face = 0; face < FacesPerEye; ++face)
                {
                    int faceIndex = eye * FacesPerEye + face;
                    int textureIndex = buffer * faceCount + faceIndex;
                    string eyeName = eye == 0 ? "Left" : "Right";
                    var texture = new RenderTexture(resolution, resolution, 24,
                        RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear)
                    {
                        name = $"VR HQ Fisheye Buffer {buffer} {eyeName} Face {face}",
                        hideFlags = HideFlags.HideAndDontSave,
                        useMipMap = false,
                        autoGenerateMips = false,
                        antiAliasing = 1
                    };
                    texture.Create();
                    faceTextureBuffers[textureIndex] = texture;
                }
            }
        }

        for (int eye = 0; eye < EyeCount; ++eye)
        {
            for (int face = 0; face < FacesPerEye; ++face)
            {
                int index = eye * FacesPerEye + face;
                string eyeName = eye == 0 ? "Left" : "Right";
                var cameraObject = new GameObject($"VR HQ Fisheye {eyeName} Capture {face}")
                {
                    hideFlags = HideFlags.HideAndDontSave
                };
                Camera capture = cameraObject.AddComponent<Camera>();
                capture.CopyFrom(outputCamera);
                capture.targetTexture = FaceTexture(0, index);
                capture.fieldOfView = 90.0f;
                capture.aspect = 1.0f;
                capture.rect = new Rect(0, 0, 1, 1);
                capture.clearFlags = CameraClearFlags.SolidColor;
                capture.backgroundColor = Color.black;
                capture.cullingMask = 0;
                capture.allowMSAA = false;
                capture.allowHDR = false;
                capture.stereoTargetEye = StereoTargetEyeMask.None;
                cameraObject.AddComponent<GaussianSplatProjectionCamera>().role =
                    GaussianSplatProjectionCameraRole.Capture;
                UniversalAdditionalCameraData cameraData =
                    capture.GetUniversalAdditionalCameraData();
                cameraData.allowXRRendering = false;
                cameraData.renderPostProcessing = false;
                cameraData.renderType = CameraRenderType.Base;
                faceCameras[index] = capture;
            }
        }

        Shader shader = Resources.Load<Shader>("VrHighQualityFisheyeComposite");
        if (shader != null)
        {
            outputMaterial = new Material(shader)
            {
                name = "VR HQ Fisheye Composite",
                hideFlags = HideFlags.HideAndDontSave
            };
            BindActiveTextures();
        }

        hasValidCapture = false;
        updateCycleActive = false;
        swapPending = false;
        swapReadyFrame = -1;
        initialCaptureFrame = -1;
        nextFacePair = 0;
    }

    bool UseMonoComposite => monoComposite || outputCamera == null || !outputCamera.stereoEnabled;

    void OnDisable()
    {
        Application.onBeforeRender -= UpdateCompositeEyeRotations;
        ReleaseResources(true);
    }

    void OnDestroy()
    {
        Application.onBeforeRender -= UpdateCompositeEyeRotations;
        ReleaseResources(true);
    }

    void ReleaseResources(bool disableOutputMarker)
    {
        RestoreOutputCameraIsolation();

        if (outputMarker != null)
        {
            outputMarker.compositeActive = false;
            outputMarker.compositeMaterial = null;
        }

        if (disableOutputMarker && outputMarker != null)
            outputMarker.enabled = false;

        if (faceCameras != null)
        {
            foreach (Camera capture in faceCameras)
                if (capture != null)
                    Destroy(capture.gameObject);
        }

        if (faceTextureBuffers != null)
        {
            foreach (RenderTexture texture in faceTextureBuffers)
            {
                if (texture == null)
                    continue;
                texture.Release();
                Destroy(texture);
            }
        }

        if (outputMaterial != null)
            Destroy(outputMaterial);

        faceCameras = null;
        faceTextureBuffers = null;
        outputMaterial = null;
        allocatedResolution = 0;
        activeBuffer = 0;
        hasValidCapture = false;
        updateCycleActive = false;
        swapPending = false;
        swapReadyFrame = -1;
        initialCaptureFrame = -1;
    }
}
