using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ProjectionKeyboardControls : MonoBehaviour
{
    [SerializeField] CameraFovController fovController;
    [SerializeField] GaussianSplatRenderer splat;
    [SerializeField] float fovStepPerSecond = 70.0f;
    [SerializeField] float fisheyeStepPerSecond = 0.35f;
    [SerializeField] float defaultFov = 60.0f;
    [SerializeField] float defaultFisheye = 0.0f;
    [SerializeField] bool enableLeftStickProjection = true;
    [SerializeField, Range(0.0f, 0.95f)] float leftStickDeadzone = 0.2f;

    InputAction leftStickProjectionAction;

    void OnEnable()
    {
        EnsureLeftStickProjectionAction();
        leftStickProjectionAction.Enable();
    }

    void OnDisable()
    {
        leftStickProjectionAction?.Disable();
    }

    void OnDestroy()
    {
        leftStickProjectionAction?.Dispose();
        leftStickProjectionAction = null;
    }

    void Update()
    {
        ResolveSplatTarget();
        float dt = Time.unscaledDeltaTime;
        var keyboard = Keyboard.current;
        Vector2 projectionStick = ReadProjectionStick();

        if (splat != null)
        {
            float fisheyeDelta = projectionStick.x;
            if (keyboard != null && keyboard.commaKey.isPressed)
                fisheyeDelta -= 1.0f;
            if (keyboard != null && keyboard.periodKey.isPressed)
                fisheyeDelta += 1.0f;

            if (Mathf.Abs(fisheyeDelta) > 0.0f)
                splat.m_FisheyeStrength = Mathf.Clamp01(splat.m_FisheyeStrength + fisheyeDelta * fisheyeStepPerSecond * dt);
        }

        if (fovController != null)
        {
            float fovDelta = projectionStick.y;
            if (keyboard != null && keyboard.minusKey.isPressed)
                fovDelta -= 1.0f;
            if (keyboard != null && keyboard.equalsKey.isPressed)
                fovDelta += 1.0f;

            if (Mathf.Abs(fovDelta) > 0.0f)
                fovController.verticalFieldOfView += fovDelta * fovStepPerSecond * dt;
        }

        if (keyboard != null && keyboard.backspaceKey.wasPressedThisFrame)
        {
            if (splat != null)
                splat.m_FisheyeStrength = defaultFisheye;

            if (fovController != null)
                fovController.verticalFieldOfView = defaultFov;
        }
    }

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

    void EnsureLeftStickProjectionAction()
    {
        if (leftStickProjectionAction != null)
            return;

        leftStickProjectionAction = new InputAction("Left Stick Projection", InputActionType.Value, expectedControlType: "Vector2");
        leftStickProjectionAction.AddBinding("<XRController>{LeftHand}/{Primary2DAxis}");
        leftStickProjectionAction.AddBinding("<XRController>{LeftHand}/primary2DAxis");
    }

    Vector2 ReadProjectionStick()
    {
        if (!enableLeftStickProjection || leftStickProjectionAction == null)
            return Vector2.zero;

        Vector2 value = leftStickProjectionAction.ReadValue<Vector2>();
        value.x = ApplyDeadzone(value.x);
        value.y = ApplyDeadzone(value.y);
        return value;
    }

    float ApplyDeadzone(float value)
    {
        return Mathf.Abs(value) >= leftStickDeadzone ? value : 0.0f;
    }
}
