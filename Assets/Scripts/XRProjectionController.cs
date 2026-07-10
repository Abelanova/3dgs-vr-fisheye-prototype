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
    [SerializeField] float verticalSpeed = 0.8f;
    [SerializeField] float deadZone = 0.18f;
    [SerializeField] float fovStepPerSecond = 35.0f;
    [SerializeField] float fisheyeStepPerSecond = 0.35f;
    [SerializeField] float defaultFov = 120.0f;
    [SerializeField, Range(0.0f, 1.0f)] float defaultFisheye = 0.20f;
    [SerializeField] bool applyDefaultsOnStart = true;

    readonly List<InputDevice> rightHandDevices = new();
    readonly List<InputDevice> leftHandDevices = new();
    InputDevice rightHand;
    InputDevice leftHand;
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
        float dt = Time.unscaledDeltaTime;
        if (TryGetRightHand(out InputDevice rightDevice))
        {
            ApplyThumbstickMovement(rightDevice, dt);
            ApplyVerticalMovement(rightDevice, dt);
        }

        if (TryGetLeftHand(out InputDevice leftDevice))
        {
            ApplyProjectionThumbstick(leftDevice, dt);
            if (ReadButton(leftDevice, CommonUsages.primary2DAxisClick))
                ApplyDefaults();
        }
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

    void ApplyVerticalMovement(InputDevice device, float dt)
    {
        float vertical = 0.0f;
        if (ReadButton(device, CommonUsages.secondaryButton))
            vertical -= 1.0f;
        if (ReadButton(device, CommonUsages.primaryButton))
            vertical += 1.0f;

        if (Mathf.Abs(vertical) <= 0.0f)
            return;

        Transform target = rigRoot != null ? rigRoot : transform;
        target.position += Vector3.up * (vertical * verticalSpeed * dt);
    }

    void ApplyProjectionThumbstick(InputDevice device, float dt)
    {
        if (!device.TryGetFeatureValue(CommonUsages.primary2DAxis, out Vector2 axis))
            return;

        float fisheyeInput = Mathf.Abs(axis.x) >= deadZone ? axis.x : 0.0f;
        float fovInput = Mathf.Abs(axis.y) >= deadZone ? axis.y : 0.0f;

        if (Mathf.Abs(fisheyeInput) > 0.0f)
            ApplyFisheyeToActiveSplats(CurrentFisheye() +
                fisheyeInput * fisheyeStepPerSecond * dt);

        if (fovController != null && Mathf.Abs(fovInput) > 0.0f)
            fovController.verticalFieldOfView += fovInput * fovStepPerSecond * dt;
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

    bool TryGetLeftHand(out InputDevice device)
    {
        if (leftHand.isValid)
        {
            device = leftHand;
            return true;
        }

        leftHandDevices.Clear();
        InputDevices.GetDevicesWithCharacteristics(
            InputDeviceCharacteristics.Left | InputDeviceCharacteristics.Controller,
            leftHandDevices);

        if (leftHandDevices.Count > 0)
        {
            leftHand = leftHandDevices[0];
            device = leftHand;
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
