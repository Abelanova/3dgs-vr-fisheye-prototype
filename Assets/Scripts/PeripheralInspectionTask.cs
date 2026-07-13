using System.Collections.Generic;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.XR;

/// <summary>
/// Lightweight scene-inspection task used by the demo video and headset preview.
/// The task is injected at runtime and therefore does not modify the Gaussian asset
/// or require scene YAML changes. Press T in Play Mode to start/stop it.
/// </summary>
public static class PeripheralInspectionTaskRuntime
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallBootstrap()
    {
        if (Object.FindFirstObjectByType<PeripheralInspectionTaskBootstrap>() != null)
            return;

        var bootstrapObject = new GameObject("Peripheral Inspection Task Bootstrap");
        bootstrapObject.AddComponent<PeripheralInspectionTaskBootstrap>();
    }
}

public sealed class PeripheralInspectionTaskBootstrap : MonoBehaviour
{
    PeripheralInspectionTask activeTask;

    void Update()
    {
        if (!TogglePressed())
            return;

        if (activeTask != null)
        {
            Destroy(activeTask.gameObject);
            return;
        }

        Camera taskCamera = Camera.main;
        if (taskCamera == null)
        {
            Debug.LogWarning("Peripheral inspection task: no Main Camera is available yet.");
            return;
        }

        var taskObject = new GameObject("Peripheral Inspection Task");
        activeTask = taskObject.AddComponent<PeripheralInspectionTask>();
        activeTask.Initialize(taskCamera, this);
    }

    void OnGUI()
    {
        if (activeTask != null)
            return;

        const float width = 390.0f;
        const float height = 54.0f;
        var rect = new Rect(Screen.width - width - 18.0f, 18.0f, width, height);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 8.0f, width - 28.0f, 20.0f),
            "Peripheral target inspection task");
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 28.0f, width - 28.0f, 20.0f),
            "Press T to start (press T again to stop)");
    }

    internal void TaskEnded(PeripheralInspectionTask task)
    {
        if (activeTask == task)
            activeTask = null;
    }

    static bool TogglePressed()
    {
        bool keyboard = Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame;
        bool gamepad = Gamepad.current != null && Gamepad.current.startButton.wasPressedThisFrame;
        return keyboard || gamepad;
    }
}

public sealed class PeripheralInspectionTask : MonoBehaviour
{
    const float NormalFov = 60.0f;
    const float NormalFisheye = 0.0f;
    const float InspectionFov = 180.0f;
    const float InspectionFisheye = 0.72f;
    const float RayDistance = 12.0f;
    const float MarkerHudDepth = 2.0f;

    readonly List<PeripheralInspectionTarget> targets = new();
    readonly Dictionary<GaussianSplatRenderer, Vector2> originalSplatProjection = new();

    Camera taskCamera;
    CameraFovController fovController;
    GaussianSplatRenderer projectionRenderer;
    PeripheralInspectionTaskBootstrap owner;
    Transform rightControllerRay;
    float originalCameraFov;
    float originalControllerFov;
    int foundCount;
    int visibleCount;
    bool inspectionMode;
    bool completed;
    bool previousXrTrigger;
    bool initialized;

    public void Initialize(Camera camera, PeripheralInspectionTaskBootstrap bootstrap)
    {
        taskCamera = camera;
        owner = bootstrap;
        fovController = camera.GetComponent<CameraFovController>();
        originalCameraFov = camera.fieldOfView;
        originalControllerFov = fovController != null ? fovController.verticalFieldOfView : camera.fieldOfView;

        CacheSplatProjectionValues();
        ResolveRightControllerRay();
        SpawnTargets();
        SetInspectionMode(false);
        initialized = true;

        Debug.Log("Peripheral inspection task started. Tab toggles the inspection lens; Space, left click, or the right trigger selects a marker; R resets the task.");
    }

    void Update()
    {
        if (!initialized || taskCamera == null)
            return;

        CacheSplatProjectionValues();

        if (ProjectionTogglePressed())
            SetInspectionMode(!inspectionMode);

        if (ResetPressed())
            ResetTask();

        if (ExitPressed())
        {
            Destroy(gameObject);
            return;
        }

        Ray selectionRay = GetSelectionRay();
        UpdateTargetHover(selectionRay);

        if (SelectionPressed())
            TryActivateTarget(selectionRay);
    }

    void LateUpdate()
    {
        if (!initialized || taskCamera == null)
            return;

        visibleCount = 0;
        foreach (PeripheralInspectionTarget target in targets)
        {
            if (target == null || target.IsActivated)
                continue;

            bool visible = TryProjectTarget(target.AnchorWorldPosition, out Vector2 viewport);
            if (visible)
            {
                Vector3 markerPosition = taskCamera.ViewportToWorldPoint(
                    new Vector3(viewport.x, viewport.y, MarkerHudDepth));
                target.SetProjectedPose(markerPosition, taskCamera.transform.rotation, true);
                visibleCount++;
            }
            else
            {
                target.SetProjectedPose(Vector3.zero, Quaternion.identity, false);
            }
        }
    }

    void OnDestroy()
    {
        if (initialized)
            RestoreProjectionValues();

        owner?.TaskEnded(this);
    }

    void SpawnTargets()
    {
        Vector3 horizontalForward = Vector3.ProjectOnPlane(taskCamera.transform.forward, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.001f)
            horizontalForward = Vector3.forward;
        horizontalForward.Normalize();

        Quaternion baseRotation = Quaternion.LookRotation(horizontalForward, Vector3.up);

        // Anchors are fixed in the scene. Their marker graphics are projected onto
        // a comfortable HUD depth so the same task remains visible in desktop and
        // stereo VR even though ordinary Unity meshes do not use the splat shader.
        CreateTarget(0, baseRotation, yaw: 8.0f, pitch: -2.0f, distance: 3.0f,
            new Color(0.20f, 0.95f, 1.00f, 1.0f));
        CreateTarget(1, baseRotation, yaw: 55.0f, pitch: 8.0f, distance: 3.7f,
            new Color(1.00f, 0.42f, 0.18f, 1.0f));
        CreateTarget(2, baseRotation, yaw: -62.0f, pitch: -10.0f, distance: 4.2f,
            new Color(0.78f, 0.42f, 1.00f, 1.0f));
    }

    void CreateTarget(int index, Quaternion baseRotation, float yaw, float pitch, float distance, Color color)
    {
        Vector3 direction = baseRotation * Quaternion.Euler(-pitch, yaw, 0.0f) * Vector3.forward;
        Vector3 anchorPosition = taskCamera.transform.position + direction.normalized * distance;

        var targetObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        targetObject.name = $"Inspection Marker {index + 1}";
        targetObject.transform.SetParent(transform, true);
        targetObject.transform.localScale = Vector3.one * 0.13f;

        var marker = targetObject.AddComponent<PeripheralInspectionTarget>();
        marker.Initialize(index, color, targetObject.transform.localScale, anchorPosition);
        targets.Add(marker);

        var lightObject = new GameObject("Marker Glow");
        lightObject.transform.SetParent(targetObject.transform, false);
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.range = 0.75f;
        light.intensity = 1.2f;
        light.color = color;
    }

    internal void RegisterTarget(PeripheralInspectionTarget target)
    {
        if (target == null || target.IsActivated)
            return;

        target.Activate();
        foundCount++;
        completed = foundCount >= targets.Count;

        if (completed)
            Debug.Log("Peripheral inspection task complete: all markers found.");
    }

    bool TryProjectTarget(Vector3 anchorWorldPosition, out Vector2 viewport)
    {
        if (!inspectionMode)
        {
            Vector3 perspectiveViewport = taskCamera.WorldToViewportPoint(anchorWorldPosition);
            viewport = new Vector2(perspectiveViewport.x, perspectiveViewport.y);
            return perspectiveViewport.z > 0.0f && IsInsideViewport(viewport);
        }

        GaussianSplatRenderer renderer = ResolveProjectionRenderer();
        if (renderer == null)
        {
            viewport = default;
            return false;
        }

        var (fisheyeParams, fisheyeParams2) = renderer.GetFisheyeShaderParams(taskCamera);
        if (fisheyeParams.x <= 0.0001f)
        {
            viewport = default;
            return false;
        }

        Vector3 directionWorld = (anchorWorldPosition - taskCamera.transform.position).normalized;
        Vector3 directionCamera = taskCamera.transform.InverseTransformDirection(directionWorld);
        float radial = new Vector2(directionCamera.x, directionCamera.y).magnitude;
        float theta = Mathf.Atan2(radial, directionCamera.z);
        if (theta > fisheyeParams2.y - 0.01f)
        {
            viewport = default;
            return false;
        }

        float gTheta = fisheyeParams.y * Mathf.Tan(theta * fisheyeParams.z);
        float radialScale = radial > 0.0001f ? gTheta / radial : 0.0f;
        float ndcX = fisheyeParams.w * radialScale * directionCamera.x;
        float ndcY = fisheyeParams2.x * radialScale * directionCamera.y;
        viewport = new Vector2(ndcX * 0.5f + 0.5f, ndcY * 0.5f + 0.5f);
        return IsInsideViewport(viewport);
    }

    static bool IsInsideViewport(Vector2 viewport) =>
        viewport.x >= 0.04f && viewport.x <= 0.96f &&
        viewport.y >= 0.04f && viewport.y <= 0.96f;

    GaussianSplatRenderer ResolveProjectionRenderer()
    {
        if (projectionRenderer != null && projectionRenderer.isActiveAndEnabled)
            return projectionRenderer;

        GaussianSplatRenderer[] renderers = Object.FindObjectsByType<GaussianSplatRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (GaussianSplatRenderer renderer in renderers)
        {
            if (!renderer.isActiveAndEnabled)
                continue;

            projectionRenderer = renderer;
            return projectionRenderer;
        }

        return null;
    }

    void TryActivateTarget(Ray ray)
    {
        if (!Physics.Raycast(ray, out RaycastHit hit, RayDistance, ~0, QueryTriggerInteraction.Ignore))
            return;

        var target = hit.collider.GetComponent<PeripheralInspectionTarget>();
        if (target != null)
            RegisterTarget(target);
    }

    void UpdateTargetHover(Ray ray)
    {
        PeripheralInspectionTarget hovered = null;
        if (Physics.Raycast(ray, out RaycastHit hit, RayDistance, ~0, QueryTriggerInteraction.Ignore))
            hovered = hit.collider.GetComponent<PeripheralInspectionTarget>();

        foreach (PeripheralInspectionTarget target in targets)
        {
            if (target != null && !target.IsActivated && target.IsProjectionVisible)
                target.SetHovered(target == hovered);
        }
    }

    Ray GetSelectionRay()
    {
        if (Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame)
            return taskCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (rightControllerRay == null)
            ResolveRightControllerRay();

        if (rightControllerRay != null)
            return new Ray(rightControllerRay.position, rightControllerRay.forward);

        return new Ray(taskCamera.transform.position, taskCamera.transform.forward);
    }

    void ResolveRightControllerRay()
    {
        Transform[] transforms = Object.FindObjectsByType<Transform>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (Transform candidate in transforms)
        {
            string lowerName = candidate.name.ToLowerInvariant();
            if (lowerName.Contains("right") &&
                (lowerName.Contains("controller") || lowerName.Contains("ray")))
            {
                rightControllerRay = candidate;
                return;
            }
        }
    }

    void SetInspectionMode(bool enabled)
    {
        inspectionMode = enabled;
        float fov = enabled ? InspectionFov : NormalFov;
        float fisheye = enabled ? InspectionFisheye : NormalFisheye;

        if (fovController != null)
            fovController.verticalFieldOfView = fov;
        else if (taskCamera != null)
            taskCamera.fieldOfView = Mathf.Clamp(fov, 20.0f, 140.0f);

        GaussianSplatRenderer[] renderers = Object.FindObjectsByType<GaussianSplatRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (GaussianSplatRenderer renderer in renderers)
        {
            RememberOriginalProjection(renderer);
            renderer.m_FisheyeFieldOfView = fov;
            renderer.m_FisheyeStrength = fisheye;
        }
    }

    void CacheSplatProjectionValues()
    {
        GaussianSplatRenderer[] renderers = Object.FindObjectsByType<GaussianSplatRenderer>(
            FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        foreach (GaussianSplatRenderer renderer in renderers)
            RememberOriginalProjection(renderer);
    }

    void RememberOriginalProjection(GaussianSplatRenderer renderer)
    {
        if (renderer != null && !originalSplatProjection.ContainsKey(renderer))
            originalSplatProjection.Add(renderer,
                new Vector2(renderer.m_FisheyeFieldOfView, renderer.m_FisheyeStrength));
    }

    void RestoreProjectionValues()
    {
        if (fovController != null)
            fovController.verticalFieldOfView = originalControllerFov;
        else if (taskCamera != null)
            taskCamera.fieldOfView = originalCameraFov;

        foreach (var pair in originalSplatProjection)
        {
            if (pair.Key == null)
                continue;

            pair.Key.m_FisheyeFieldOfView = pair.Value.x;
            pair.Key.m_FisheyeStrength = pair.Value.y;
        }
    }

    void ResetTask()
    {
        foundCount = 0;
        completed = false;
        foreach (PeripheralInspectionTarget target in targets)
        {
            if (target != null)
                target.ResetTarget();
        }
        SetInspectionMode(false);
    }

    bool SelectionPressed()
    {
        bool keyboard = Keyboard.current != null &&
                        (Keyboard.current.spaceKey.wasPressedThisFrame ||
                         Keyboard.current.enterKey.wasPressedThisFrame);
        bool mouse = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool gamepad = Gamepad.current != null && Gamepad.current.rightTrigger.wasPressedThisFrame;

        bool xrTrigger = false;
        UnityEngine.XR.InputDevice device = InputDevices.GetDeviceAtXRNode(XRNode.RightHand);
        if (device.isValid && device.TryGetFeatureValue(CommonUsages.triggerButton, out bool triggerPressed))
        {
            xrTrigger = triggerPressed && !previousXrTrigger;
            previousXrTrigger = triggerPressed;
        }
        else
        {
            previousXrTrigger = false;
        }

        return keyboard || mouse || gamepad || xrTrigger;
    }

    static bool ProjectionTogglePressed()
    {
        bool keyboard = Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
        bool gamepad = Gamepad.current != null && Gamepad.current.leftShoulder.wasPressedThisFrame;
        return keyboard || gamepad;
    }

    static bool ResetPressed() =>
        Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame;

    static bool ExitPressed() =>
        Keyboard.current != null && Keyboard.current.escapeKey.wasPressedThisFrame;

    void OnGUI()
    {
        const float width = 390.0f;
        float height = completed ? 178.0f : 158.0f;
        var rect = new Rect(Screen.width - width - 18.0f, 18.0f, width, height);
        GUI.Box(rect, GUIContent.none);

        string mode = inspectionMode ? "Fisheye inspection" : "Perspective baseline";
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 8.0f, width - 28.0f, 22.0f),
            "Peripheral Target Search");
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 34.0f, width - 28.0f, 20.0f),
            $"Mode: {mode}");
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 56.0f, width - 28.0f, 20.0f),
            $"Visible now: {visibleCount} / {targets.Count - foundCount}");
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 78.0f, width - 28.0f, 20.0f),
            $"Targets found: {foundCount} / {targets.Count}");
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 104.0f, width - 28.0f, 20.0f),
            "Tab / left shoulder: toggle lens");
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 124.0f, width - 28.0f, 20.0f),
            "Aim + Space / click / right trigger: activate");

        if (completed)
            GUI.Label(new Rect(rect.x + 14.0f, rect.y + 148.0f, width - 28.0f, 20.0f),
                "Inspection complete — press R to reset");
    }
}

public sealed class PeripheralInspectionTarget : MonoBehaviour
{
    Renderer targetRenderer;
    Material runtimeMaterial;
    Vector3 baseScale;

    public Vector3 AnchorWorldPosition { get; private set; }
    public bool IsActivated { get; private set; }
    public bool IsProjectionVisible { get; private set; }

    public void Initialize(int index, Color color, Vector3 initialScale, Vector3 anchorWorldPosition)
    {
        AnchorWorldPosition = anchorWorldPosition;
        baseScale = initialScale;
        targetRenderer = GetComponent<Renderer>();

        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Unlit/Color");
        if (shader == null)
            shader = Shader.Find("Standard");

        if (shader != null)
        {
            runtimeMaterial = new Material(shader) { name = $"Inspection Marker {index + 1} Material" };
            ApplyMaterialColor(runtimeMaterial, color);
            if (targetRenderer != null)
                targetRenderer.sharedMaterial = runtimeMaterial;
        }
    }

    public void SetProjectedPose(Vector3 position, Quaternion rotation, bool visible)
    {
        IsProjectionVisible = visible;
        if (IsActivated)
        {
            if (gameObject.activeSelf)
                gameObject.SetActive(false);
            return;
        }

        if (gameObject.activeSelf != visible)
            gameObject.SetActive(visible);

        if (!visible)
            return;

        transform.SetPositionAndRotation(position, rotation);
    }

    public void SetHovered(bool hovered)
    {
        if (IsActivated || !IsProjectionVisible)
            return;

        transform.localScale = baseScale * (hovered ? 1.32f : 1.0f);
    }

    public void Activate()
    {
        if (IsActivated)
            return;

        IsActivated = true;
        IsProjectionVisible = false;
        gameObject.SetActive(false);
    }

    public void ResetTarget()
    {
        IsActivated = false;
        IsProjectionVisible = false;
        transform.localScale = baseScale;
    }

    void OnDestroy()
    {
        if (runtimeMaterial != null)
            Destroy(runtimeMaterial);
    }

    static void ApplyMaterialColor(Material material, Color color)
    {
        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2.2f);
        }
    }
}
