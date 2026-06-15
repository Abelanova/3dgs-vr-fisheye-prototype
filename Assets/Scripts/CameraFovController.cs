using UnityEngine;
using GaussianSplatting.Runtime;

[ExecuteAlways]
public sealed class CameraFovController : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField, Range(20.0f, 320.0f)] float verticalFov = 60.0f;
    [SerializeField, Range(20.0f, 170.0f)] float cameraFovLimit = 140.0f;

    public float verticalFieldOfView
    {
        get => verticalFov;
        set
        {
            verticalFov = Mathf.Clamp(value, 20.0f, 320.0f);
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

    void Apply()
    {
        if (targetCamera == null)
            targetCamera = GetComponent<Camera>();

        if (targetCamera != null)
            targetCamera.fieldOfView = Mathf.Min(verticalFov, cameraFovLimit);

        if (targetSplat != null)
            targetSplat.m_FisheyeFieldOfView = verticalFov;
    }
}
