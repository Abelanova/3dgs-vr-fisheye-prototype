using UnityEngine;
using GaussianSplatting.Runtime;

[ExecuteAlways]
public sealed class CameraFovController : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField, Range(20.0f, 360.0f)] float verticalFov = 60.0f;
    [SerializeField, Range(20.0f, 170.0f)] float cameraFovLimit = 140.0f;
    [SerializeField, Tooltip("Keep the XR runtime's native per-eye projection matrices. The virtual FOV still controls the fisheye mapping on the splat renderer.")]
    bool preserveNativeXrProjection = true;
#if UNITY_EDITOR
    [SerializeField] bool squareEditorGameView = true;
#endif

    public float verticalFieldOfView
    {
        get => verticalFov;
        set
        {
            verticalFov = Mathf.Clamp(value, 20.0f, 360.0f);
            Apply();
        }
    }

    void Reset()
    {
        targetCamera = GetComponent<Camera>();
        if (targetCamera != null)
            verticalFov = targetCamera.fieldOfView;
    }

    void OnValidate()
    {
        Apply();
    }

    void Awake()
    {
        Apply();
    }

    void Update()
    {
        Apply();
    }

    void OnDisable()
    {
#if UNITY_EDITOR
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (targetCamera != null)
            targetCamera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
#endif
    }

    void Apply()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (targetCamera != null)
        {
            bool xrOwnsProjection = preserveNativeXrProjection && targetCamera.stereoEnabled;
            if (!xrOwnsProjection)
                targetCamera.fieldOfView = Mathf.Min(verticalFov, cameraFovLimit);

#if UNITY_EDITOR
            // The square viewport is only a desktop/editor preview aid. Do not
            // crop an XR eye texture, because that changes the effective
            // principal point seen by the compositor.
            if (!targetCamera.stereoEnabled)
                ApplySquareEditorViewport(targetCamera);
            else
                targetCamera.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
#endif
        }

        // Always forward the requested virtual FOV to the splat renderer. In XR
        // this controls the nonlinear camera model without overwriting the
        // runtime-provided left/right projection matrices.
        ApplyToActiveSplats(verticalFov);
    }

#if UNITY_EDITOR
    void ApplySquareEditorViewport(Camera cam)
    {
        if (!squareEditorGameView)
        {
            cam.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            return;
        }

        float width = Screen.width;
        float height = Screen.height;
        if (width <= 0.0f || height <= 0.0f)
        {
            cam.rect = new Rect(0.0f, 0.0f, 1.0f, 1.0f);
            return;
        }

        float aspect = width / height;
        if (aspect > 1.0f)
        {
            float viewportWidth = 1.0f / aspect;
            cam.rect = new Rect((1.0f - viewportWidth) * 0.5f, 0.0f, viewportWidth, 1.0f);
        }
        else
        {
            float viewportHeight = aspect;
            cam.rect = new Rect(0.0f, (1.0f - viewportHeight) * 0.5f, 1.0f, viewportHeight);
        }
    }
#endif

    void ResolveSplatTarget()
    {
        if (targetSplat != null && targetSplat.isActiveAndEnabled && targetSplat.m_Asset != null)
            return;

        var renderers = Object.FindObjectsByType<GaussianSplatRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var renderer in renderers)
        {
            if (renderer.isActiveAndEnabled && renderer.m_Asset != null)
            {
                targetSplat = renderer;
                return;
            }
        }
    }

    void ApplyToActiveSplats(float fov)
    {
        var renderers = Object.FindObjectsByType<GaussianSplatRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var renderer in renderers)
        {
            if (renderer.isActiveAndEnabled && renderer.m_Asset != null)
            {
                renderer.m_FisheyeFieldOfView = fov;
                targetSplat = renderer;
            }
        }
    }
}
