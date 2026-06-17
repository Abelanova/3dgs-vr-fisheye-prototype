using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;

public sealed class XRSimulatorShiftSpeedBoost : MonoBehaviour
{
    [SerializeField] XRInteractionSimulator simulator;
    [SerializeField, Min(1.0f)] float shiftMultiplier = 4.0f;

    float normalXSpeed;
    float normalYSpeed;
    float normalZSpeed;
    bool capturedNormalSpeeds;
    static XRSimulatorShiftSpeedBoost instance;

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void CreateRuntimeInstance()
    {
        if (instance != null)
            return;

        var go = new GameObject("XR Simulator Shift Speed Boost");
        go.hideFlags = HideFlags.DontSave;
        instance = go.AddComponent<XRSimulatorShiftSpeedBoost>();
    }

    void OnDisable()
    {
        RestoreNormalSpeeds();
        if (instance == this)
            instance = null;
    }

    void Update()
    {
        if (!ResolveSimulator())
            return;

        CaptureNormalSpeeds();

        var keyboard = Keyboard.current;
        bool fast = keyboard != null && (keyboard.leftShiftKey.isPressed || keyboard.rightShiftKey.isPressed);
        float multiplier = fast ? shiftMultiplier : 1.0f;

        simulator.translateXSpeed = normalXSpeed * multiplier;
        simulator.translateYSpeed = normalYSpeed * multiplier;
        simulator.translateZSpeed = normalZSpeed * multiplier;
    }

    bool ResolveSimulator()
    {
        if (simulator != null)
            return true;

        simulator = Object.FindAnyObjectByType<XRInteractionSimulator>();
        capturedNormalSpeeds = false;
        return simulator != null;
    }

    void CaptureNormalSpeeds()
    {
        if (capturedNormalSpeeds)
            return;

        normalXSpeed = simulator.translateXSpeed;
        normalYSpeed = simulator.translateYSpeed;
        normalZSpeed = simulator.translateZSpeed;
        capturedNormalSpeeds = true;
    }

    void RestoreNormalSpeeds()
    {
        if (simulator == null || !capturedNormalSpeeds)
            return;

        simulator.translateXSpeed = normalXSpeed;
        simulator.translateYSpeed = normalYSpeed;
        simulator.translateZSpeed = normalZSpeed;
    }
}
