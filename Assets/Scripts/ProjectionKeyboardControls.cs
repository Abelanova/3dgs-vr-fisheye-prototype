using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

public sealed class ProjectionKeyboardControls : MonoBehaviour
{
    [SerializeField] CameraFovController fovController;
    [SerializeField] GaussianSplatRenderer splat;
    [SerializeField] float fovStepPerSecond = 35.0f;
    [SerializeField] float fisheyeStepPerSecond = 0.35f;
    [SerializeField] float defaultFov = 60.0f;
    [SerializeField] float defaultFisheye = 0.0f;

    void Update()
    {
        if (Keyboard.current == null)
            return;

        float dt = Time.unscaledDeltaTime;

        if (splat != null)
        {
            float fisheyeDelta = 0.0f;
            if (Keyboard.current.commaKey.isPressed)
                fisheyeDelta -= 1.0f;
            if (Keyboard.current.periodKey.isPressed)
                fisheyeDelta += 1.0f;

            if (Mathf.Abs(fisheyeDelta) > 0.0f)
                splat.m_FisheyeStrength = Mathf.Clamp01(splat.m_FisheyeStrength + fisheyeDelta * fisheyeStepPerSecond * dt);
        }

        if (fovController != null)
        {
            float fovDelta = 0.0f;
            if (Keyboard.current.minusKey.isPressed)
                fovDelta -= 1.0f;
            if (Keyboard.current.equalsKey.isPressed)
                fovDelta += 1.0f;

            if (Mathf.Abs(fovDelta) > 0.0f)
                fovController.verticalFieldOfView += fovDelta * fovStepPerSecond * dt;
        }

        if (Keyboard.current.backspaceKey.wasPressedThisFrame)
        {
            if (splat != null)
                splat.m_FisheyeStrength = defaultFisheye;

            if (fovController != null)
                fovController.verticalFieldOfView = defaultFov;
        }
    }
}
