using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public sealed class ProjectionControlPanel : MonoBehaviour
{
    [SerializeField] CameraFovController fovController;
    [SerializeField] GaussianSplatRenderer splat;
    [SerializeField] Slider fovSlider;
    [SerializeField] Slider fisheyeSlider;
    [SerializeField] Text fovValueText;
    [SerializeField] Text fisheyeValueText;

    bool applying;

    void Awake()
    {
        if (fovSlider != null)
            fovSlider.onValueChanged.AddListener(SetFov);

        if (fisheyeSlider != null)
            fisheyeSlider.onValueChanged.AddListener(SetFisheye);
    }

    void OnEnable()
    {
        RefreshFromTargets();
    }

    void Update()
    {
        if (applying)
            return;

        if (IsEditingSlider())
            RefreshLabels();
        else
            RefreshFromTargets();
    }

    void RefreshFromTargets()
    {
        ResolveSplatTarget();
        applying = true;

        if (fovSlider != null && fovController != null)
            fovSlider.SetValueWithoutNotify(fovController.verticalFieldOfView);

        if (fisheyeSlider != null && splat != null)
            fisheyeSlider.SetValueWithoutNotify(splat.m_FisheyeStrength);

        applying = false;
        RefreshLabels();
    }

    void SetFov(float value)
    {
        if (fovController != null)
            fovController.verticalFieldOfView = value;

        RefreshLabels();
    }

    void SetFisheye(float value)
    {
        ResolveSplatTarget();
        ApplyFisheyeToActiveSplats(Mathf.Clamp01(value));

        RefreshLabels();
    }

    void RefreshLabels()
    {
        ResolveSplatTarget();
        if (fovValueText != null && fovController != null)
            fovValueText.text = Mathf.RoundToInt(fovController.verticalFieldOfView).ToString();

        if (fisheyeValueText != null && splat != null)
            fisheyeValueText.text = splat.m_FisheyeStrength.ToString("0.00");
    }

    bool IsEditingSlider()
    {
        var selected = EventSystem.current != null ? EventSystem.current.currentSelectedGameObject : null;
        if (selected == null)
            return false;

        Transform selectedTransform = selected.transform;
        return IsWithinSlider(selectedTransform, fovSlider) || IsWithinSlider(selectedTransform, fisheyeSlider);
    }

    static bool IsWithinSlider(Transform selected, Slider slider) =>
        slider != null && selected != null &&
        (selected == slider.transform || selected.IsChildOf(slider.transform));

    void ResolveSplatTarget()
    {
        if (splat != null && splat.isActiveAndEnabled && splat.m_Asset != null)
            return;

        var renderers = Object.FindObjectsByType<GaussianSplatRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var renderer in renderers)
        {
            if (renderer.isActiveAndEnabled && renderer.m_Asset != null)
            {
                splat = renderer;
                return;
            }
        }
    }

    void ApplyFisheyeToActiveSplats(float value)
    {
        var renderers = Object.FindObjectsByType<GaussianSplatRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var renderer in renderers)
        {
            if (renderer.isActiveAndEnabled && renderer.m_Asset != null)
            {
                renderer.m_FisheyeStrength = value;
                splat = renderer;
            }
        }
    }
}
