using System.Collections.Generic;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.XR;

public sealed class XRProjectionController : MonoBehaviour
{
    [SerializeField] CameraFovController fovController;
    [SerializeField] GaussianSplatRenderer splat;
    [SerializeField] Transform rigRoot;
    [SerializeField] Camera xrCamera;
    [SerializeField] float moveSpeed = 0.8f;
    [SerializeField] float deadZone = 0.18f;
    [SerializeField] float fovStepPerSecond = 35.0f;
    [SerializeField] float fisheyeStepPerSecond = 0.35f;
    [SerializeField] float defaultFov = 120.0f;
    [SerializeField, Range(0.0f, 1.0f)] float defaultFisheye = 0.45f;
    [SerializeField] bool applyDefaultsOnStart = true;

    readonly List<InputDevice> rightHandDevices = new();
    InputDevice rightHand;
    bool defaultsApplied;

    void Awake()
    {
        ResolveTargets();
    }

    void Start()
    {
        if (applyDefaultsOnStart)
            ApplyDefaults();
    }

    void Update()
    {
        ResolveTargets();
        if (!TryGetRightHand(out InputDevice device))
            return;

        float dt = Time.unscaledDeltaTime;
        ApplyThumbstickMovement(device, dt);
        ApplyProjectionButtons(device, dt);

        if (ReadButton(device, CommonUsages.primary2DAxisClick))
            ApplyDefaults();
    }

    void ApplyDefaults()
    {
        defaultsApplied = true;

        if (fovController != null)
            fovController.verticalFieldOfView = defaultFov;

        ApplyFisheyeToActiveSplats(defaultFisheye);
    }

    void ApplyThumbstickMovement(InputDevice device, float dt)
    {
        if (!device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis) ||
            axis.sqrMagnitude < deadZone * deadZone)
            return;

        Transform reference = xrCamera != null ? xrCamera.transform : transform;
        Vector3 forward = Vector3.ProjectOnPlane(reference.forward, Vector3.up).normalized;
        Vector3 right = Vector3.ProjectOnPlane(reference.right, Vector3.up).normalized;
        if (forward.sqrMagnitude < 0.0001f)
            forward = transform.forward;
        if (right.sqrMagnitude < 0.0001f)
            right = transform.right;

        Transform target = rigRoot != null ? rigRoot : transform;
        Vector3 delta = (forward * axis.y + right * axis.x) * (moveSpeed * dt);
        target.position += delta;
    }

    void ApplyProjectionButtons(InputDevice device, float dt)
    {
        float fisheyeDelta = 0.0f;
        if (ReadButton(device, CommonUsages.secondaryButton))
            fisheyeDelta -= 1.0f;
        if (ReadButton(device, CommonUsages.primaryButton))
            fisheyeDelta += 1.0f;

        if (Mathf.Abs(fisheyeDelta) > 0.0f)
            ApplyFisheyeToActiveSplats(CurrentFisheye() + fisheyeDelta * fisheyeStepPerSecond * dt);

        if (fovController == null)
            return;

        float fovDelta = 0.0f;
        if (ReadButton(device, CommonUsages.gripButton))
            fovDelta -= 1.0f;
        if (ReadButton(device, CommonUsages.triggerButton))
            fovDelta += 1.0f;

        if (Mathf.Abs(fovDelta) > 0.0f)
            fovController.verticalFieldOfView += fovDelta * fovStepPerSecond * dt;
    }

    static bool ReadButton(InputDevice device, InputFeatureUsage<bool> usage) =>
        device.TryGetFeatureValue(usage, out bool pressed) && pressed;

    bool TryGetRightHand(out InputDevice device)
    {
        if (rightHand.isValid)
        {
            device = rightHand;
            return true;
        }

        rightHandDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Right | InputDeviceCharacteristics.Controller,
            rightHandDevices);

        if (rightHandDevices.Count > 0)
        {
            rightHand = rightHandDevices[0];
            device = rightHand;
            return true;
        }

        device = default;
        return false;
    }

    float CurrentFisheye()
    {
        ResolveSplatTarget();
        return splat != null ? splat.m_FisheyeStrength : defaultFisheye;
    }

    void ApplyFisheyeToActiveSplats(float value)
    {
        float clamped = Mathf.Clamp01(value);
        var renderers = Object.FindObjectsByType<GaussianSplatRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var renderer in renderers)
        {
            if (renderer.isActiveAndEnabled && renderer.m_Asset != null)
            {
                renderer.m_FisheyeStrength = clamped;
                splat = renderer;
            }
        }
    }

    void ResolveTargets()
    {
        if (xrCamera == null)
            xrCamera = GetComponent<Camera>();
        if (rigRoot == null)
        {
            var current = transform;
            while (current.parent != null)
                current = current.parent;
            rigRoot = current;
        }
        ResolveSplatTarget();

        if (!defaultsApplied && applyDefaultsOnStart && Application.isPlaying)
            ApplyDefaults();
    }

    void ResolveSplatTarget()
    {
        if (splat != null && splat.isActiveAndEnabled && splat.m_Asset != null)
            return;

        var renderers = Object.FindObjectsByType<GaussianSplatRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (var renderer in renderers)
        {
            if (renderer.isActiveAndEnabled && renderer.m_Asset != null)
            {
                splat = renderer;
                return;
            }
        }
    }
}
