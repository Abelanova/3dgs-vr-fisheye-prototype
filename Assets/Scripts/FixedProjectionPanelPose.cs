using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.UI;

public sealed class FixedProjectionPanelPose : MonoBehaviour
{
    const float HudDistance = 0.55f;
    const float HudVerticalOffset = -0.12f;
    const float HudScale = 0.00043f;
    const float ReferenceFov = 60.0f;

    [SerializeField] Camera targetCamera;

    Canvas panelCanvas;
    RectTransform panelRect;
    bool configured;
    Material uiAlwaysOnTopMaterial;

    void OnEnable()
    {
        configured = false;
    }

    void LateUpdate()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        if (targetCamera == null)
            return;

        if (!configured)
            ConfigureWorldSpaceHud();

        float currentHalfFov = targetCamera.fieldOfView * Mathf.Deg2Rad * 0.5f;
        float referenceHalfFov = ReferenceFov * Mathf.Deg2Rad * 0.5f;
        float fovScale = Mathf.Tan(currentHalfFov) / Mathf.Max(Mathf.Tan(referenceHalfFov), 0.0001f);

        transform.position = targetCamera.transform.TransformPoint(new Vector3(0.0f, HudVerticalOffset * fovScale, HudDistance));
        transform.rotation = targetCamera.transform.rotation;
        transform.localScale = Vector3.one * (HudScale * fovScale);
    }

    void ConfigureWorldSpaceHud()
    {
        if (panelCanvas == null)
            panelCanvas = GetComponent<Canvas>();

        if (panelCanvas == null)
            return;

        panelCanvas.renderMode = RenderMode.WorldSpace;
        panelCanvas.worldCamera = targetCamera;
        panelCanvas.overrideSorting = true;
        panelCanvas.sortingOrder = short.MaxValue;

        if (panelRect == null)
            panelRect = transform as RectTransform;

        if (panelRect != null)
        {
            panelRect.sizeDelta = new Vector2(440.0f, 178.0f);
            panelRect.pivot = new Vector2(0.5f, 0.5f);
        }

        ApplyAlwaysOnTopMaterial();

        configured = true;
    }

    void ApplyAlwaysOnTopMaterial()
    {
        if (uiAlwaysOnTopMaterial == null)
        {
            var shader = Shader.Find("UI/Default");
            if (shader == null)
                return;

            uiAlwaysOnTopMaterial = new Material(shader)
            {
                hideFlags = HideFlags.HideAndDontSave
            };
            uiAlwaysOnTopMaterial.SetInt("unity_GUIZTestMode", (int)CompareFunction.Always);
        }

        var graphics = GetComponentsInChildren<Graphic>(true);
        foreach (var graphic in graphics)
            graphic.material = uiAlwaysOnTopMaterial;
    }
}
