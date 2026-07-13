using System.Collections.Generic;
using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;

/// <summary>
/// Installs the optional peripheral target task after a scene has loaded.
/// The task is started and stopped with T, so existing scenes do not need to be edited.
/// </summary>
public static class PeripheralInspectionTaskRuntime
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void InstallBootstrap()
    {
        if (Object.FindObjectOfType<PeripheralInspectionTaskBootstrap>() != null)
            return;

        GameObject bootstrapObject = new GameObject("Peripheral Inspection Task Bootstrap");
        bootstrapObject.AddComponent<PeripheralInspectionTaskBootstrap>();
    }
}

public sealed class PeripheralInspectionTaskBootstrap : MonoBehaviour
{
    PeripheralInspectionTask activeTask;

    void Update()
    {
        if (Keyboard.current == null || !Keyboard.current.tKey.wasPressedThisFrame)
            return;

        if (activeTask != null)
        {
            Destroy(activeTask.gameObject);
            activeTask = null;
            return;
        }

        Camera taskCamera = Camera.main;
        if (taskCamera == null)
        {
            Debug.LogWarning("Peripheral inspection task: no camera tagged MainCamera was found.");
            return;
        }

        GameObject taskObject = new GameObject("Peripheral Inspection Task");
        activeTask = taskObject.AddComponent<PeripheralInspectionTask>();
        activeTask.Initialize(taskCamera, this);
    }

    internal void NotifyTaskEnded(PeripheralInspectionTask task)
    {
        if (activeTask == task)
            activeTask = null;
    }

    void OnGUI()
    {
        if (activeTask != null)
            return;

        const float width = 390.0f;
        const float height = 54.0f;
        Rect rect = new Rect(Screen.width - width - 18.0f, 18.0f, width, height);
        GUI.Box(rect, GUIContent.none);
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 8.0f, width - 28.0f, 20.0f),
            "Peripheral target inspection task");
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 28.0f, width - 28.0f, 20.0f),
            "Press T to start");
    }
}

public sealed class PeripheralInspectionTask : MonoBehaviour
{
    const float NormalFov = 60.0f;
    const float NormalFisheye = 0.0f;
    const float InspectionFov = 180.0f;
    const float InspectionFisheye = 0.72f;
    const float MarkerHudDepth = 2.0f;
    const float SelectionDistance = 12.0f;

    readonly List<PeripheralInspectionTarget> targets = new List<PeripheralInspectionTarget>();
    readonly Dictionary<GaussianSplatRenderer, Vector2> originalProjectionValues =
        new Dictionary<GaussianSplatRenderer, Vector2>();

    Camera taskCamera;
    CameraFovController fovController;
    PeripheralInspectionTaskBootstrap owner;
    Transform rightControllerRay;
    InputAction rightTriggerAction;
    InputAction lensToggleAction;

    float originalCameraFov;
    float originalControllerFov;
    int foundCount;
    int visibleCount;
    bool inspectionMode;
    bool initialized;
    bool completed;

    public void Initialize(Camera camera, PeripheralInspectionTaskBootstrap bootstrap)
    {
        taskCamera = camera;
        owner = bootstrap;
        fovController = camera.GetComponent<CameraFovController>();
        originalCameraFov = camera.fieldOfView;
        originalControllerFov = fovController != null
            ? fovController.verticalFieldOfView
            : camera.fieldOfView;

        rightTriggerAction = new InputAction(
            "Peripheral task select",
            InputActionType.Button,
            "<XRController>{RightHand}/triggerPressed");
        lensToggleAction = new InputAction(
            "Peripheral task lens toggle",
            InputActionType.Button,
            "<XRController>{LeftHand}/primaryButton");
        rightTriggerAction.Enable();
        lensToggleAction.Enable();

        CacheOriginalProjectionValues();
        ResolveRightControllerRay();
        SpawnTargets();
        SetInspectionMode(false);
        initialized = true;

        Debug.Log("Peripheral inspection task started. Tab toggles the lens; Space, left click, or the right trigger selects a marker; R resets; T ends the task.");
    }

    void Update()
    {
        if (!initialized || taskCamera == null)
            return;

        if (Keyboard.current != null && Keyboard.current.tKey.wasPressedThisFrame)
        {
            Destroy(gameObject);
            return;
        }

        bool keyboardToggle = Keyboard.current != null && Keyboard.current.tabKey.wasPressedThisFrame;
        bool controllerToggle = lensToggleAction != null && lensToggleAction.WasPressedThisFrame();
        if (keyboardToggle || controllerToggle)
            SetInspectionMode(!inspectionMode);

        if (Keyboard.current != null && Keyboard.current.rKey.wasPressedThisFrame)
            ResetTask();

        Ray selectionRay = GetSelectionRay();
        UpdateTargetHover(selectionRay);

        bool keyboardSelect = Keyboard.current != null && Keyboard.current.spaceKey.wasPressedThisFrame;
        bool mouseSelect = Mouse.current != null && Mouse.current.leftButton.wasPressedThisFrame;
        bool controllerSelect = rightTriggerAction != null && rightTriggerAction.WasPressedThisFrame();
        if (keyboardSelect || mouseSelect || controllerSelect)
            TryActivateTarget(selectionRay);
    }

    void LateUpdate()
    {
        if (!initialized || taskCamera == null)
            return;

        visibleCount = 0;
        for (int i = 0; i < targets.Count; i++)
        {
            PeripheralInspectionTarget target = targets[i];
            if (target == null || target.IsActivated)
                continue;

            Vector2 viewport;
            bool visible = TryProjectTarget(target.AnchorWorldPosition, out viewport);
            if (!visible)
            {
                target.SetProjectedPose(Vector3.zero, Quaternion.identity, false);
                continue;
            }

            Vector3 markerPosition = taskCamera.ViewportToWorldPoint(
                new Vector3(viewport.x, viewport.y, MarkerHudDepth));
            target.SetProjectedPose(markerPosition, taskCamera.transform.rotation, true);
            visibleCount++;
        }
    }

    void OnDestroy()
    {
        if (rightTriggerAction != null)
        {
            rightTriggerAction.Disable();
            rightTriggerAction.Dispose();
        }

        if (lensToggleAction != null)
        {
            lensToggleAction.Disable();
            lensToggleAction.Dispose();
        }

        if (initialized)
            RestoreProjectionValues();

        if (owner != null)
            owner.NotifyTaskEnded(this);
    }

    void SpawnTargets()
    {
        Vector3 horizontalForward = Vector3.ProjectOnPlane(taskCamera.transform.forward, Vector3.up);
        if (horizontalForward.sqrMagnitude < 0.001f)
            horizontalForward = Vector3.forward;
        horizontalForward.Normalize();

        Quaternion baseRotation = Quaternion.LookRotation(horizontalForward, Vector3.up);
        CreateTarget(0, baseRotation, 8.0f, -2.0f, 3.0f,
            new Color(0.20f, 0.95f, 1.00f, 1.0f));
        CreateTarget(1, baseRotation, 55.0f, 8.0f, 3.7f,
            new Color(1.00f, 0.42f, 0.18f, 1.0f));
        CreateTarget(2, baseRotation, -62.0f, -10.0f, 4.2f,
            new Color(0.78f, 0.42f, 1.00f, 1.0f));
    }

    void CreateTarget(
        int index,
        Quaternion baseRotation,
        float yaw,
        float pitch,
        float distance,
        Color color)
    {
        Vector3 direction = baseRotation * Quaternion.Euler(-pitch, yaw, 0.0f) * Vector3.forward;
        Vector3 anchorPosition = taskCamera.transform.position + direction.normalized * distance;

        GameObject targetObject = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        targetObject.name = "Inspection Marker " + (index + 1);
        targetObject.transform.SetParent(transform, true);
        targetObject.transform.localScale = Vector3.one * 0.13f;

        PeripheralInspectionTarget marker = targetObject.AddComponent<PeripheralInspectionTarget>();
        marker.Initialize(index, color, targetObject.transform.localScale, anchorPosition);
        targets.Add(marker);

        GameObject lightObject = new GameObject("Marker Glow");
        lightObject.transform.SetParent(targetObject.transform, false);
        Light markerLight = lightObject.AddComponent<Light>();
        markerLight.type = LightType.Point;
        markerLight.range = 0.75f;
        markerLight.intensity = 1.2f;
        markerLight.color = color;
    }

    bool TryProjectTarget(Vector3 anchorWorldPosition, out Vector2 viewport)
    {
        if (!inspectionMode)
        {
            Vector3 perspectiveViewport = taskCamera.WorldToViewportPoint(anchorWorldPosition);
            viewport = new Vector2(perspectiveViewport.x, perspectiveViewport.y);
            return perspectiveViewport.z > 0.0f && IsInsideViewport(viewport);
        }

        Vector3 directionWorld = (anchorWorldPosition - taskCamera.transform.position).normalized;
        Vector3 directionCamera = taskCamera.transform.InverseTransformDirection(directionWorld);

        float radial = Mathf.Sqrt(
            directionCamera.x * directionCamera.x +
            directionCamera.y * directionCamera.y);
        float theta = Mathf.Atan2(radial, directionCamera.z);
        float maxTheta = InspectionFov * Mathf.Deg2Rad * 0.5f;
        if (theta < 0.0f || theta > maxTheta)
        {
            viewport = Vector2.zero;
            return false;
        }

        Vector2 radialDirection = radial > 0.0001f
            ? new Vector2(directionCamera.x / radial, directionCamera.y / radial)
            : Vector2.zero;
        float normalizedRadius = theta / Mathf.Max(maxTheta, 0.0001f);

        viewport = new Vector2(
            0.5f + radialDirection.x * normalizedRadius * 0.46f,
            0.5f + radialDirection.y * normalizedRadius * 0.46f);
        return IsInsideViewport(viewport);
    }

    static bool IsInsideViewport(Vector2 viewport)
    {
        return viewport.x >= 0.04f && viewport.x <= 0.96f &&
               viewport.y >= 0.04f && viewport.y <= 0.96f;
    }

    Ray GetSelectionRay()
    {
        if (Mouse.current != null)
            return taskCamera.ScreenPointToRay(Mouse.current.position.ReadValue());

        if (rightControllerRay == null)
            ResolveRightControllerRay();

        if (rightControllerRay != null)
            return new Ray(rightControllerRay.position, rightControllerRay.forward);

        return new Ray(taskCamera.transform.position, taskCamera.transform.forward);
    }

    void ResolveRightControllerRay()
    {
        Transform[] transforms = Object.FindObjectsOfType<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            Transform candidate = transforms[i];
            string lowerName = candidate.name.ToLowerInvariant();
            if (lowerName.Contains("right") &&
                (lowerName.Contains("controller") || lowerName.Contains("ray")))
            {
                rightControllerRay = candidate;
                return;
            }
        }
    }

    void TryActivateTarget(Ray ray)
    {
        RaycastHit hit;
        if (!Physics.Raycast(ray, out hit, SelectionDistance, ~0, QueryTriggerInteraction.Ignore))
            return;

        PeripheralInspectionTarget target = hit.collider.GetComponent<PeripheralInspectionTarget>();
        if (target == null || target.IsActivated)
            return;

        target.Activate();
        foundCount++;
        completed = foundCount >= targets.Count;
        if (completed)
            Debug.Log("Peripheral inspection task complete: all markers found.");
    }

    void UpdateTargetHover(Ray ray)
    {
        PeripheralInspectionTarget hovered = null;
        RaycastHit hit;
        if (Physics.Raycast(ray, out hit, SelectionDistance, ~0, QueryTriggerInteraction.Ignore))
            hovered = hit.collider.GetComponent<PeripheralInspectionTarget>();

        for (int i = 0; i < targets.Count; i++)
        {
            PeripheralInspectionTarget target = targets[i];
            if (target != null && !target.IsActivated && target.IsProjectionVisible)
                target.SetHovered(target == hovered);
        }
    }

    void SetInspectionMode(bool enabled)
    {
        inspectionMode = enabled;
        float fov = enabled ? InspectionFov : NormalFov;
        float fisheye = enabled ? InspectionFisheye : NormalFisheye;

        if (fovController != null)
            fovController.verticalFieldOfView = fov;
        else
            taskCamera.fieldOfView = Mathf.Clamp(fov, 20.0f, 140.0f);

        GaussianSplatRenderer[] renderers = Object.FindObjectsOfType<GaussianSplatRenderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            GaussianSplatRenderer renderer = renderers[i];
            RememberOriginalProjection(renderer);
            renderer.m_FisheyeFieldOfView = fov;
            renderer.m_FisheyeStrength = fisheye;
        }
    }

    void CacheOriginalProjectionValues()
    {
        GaussianSplatRenderer[] renderers = Object.FindObjectsOfType<GaussianSplatRenderer>();
        for (int i = 0; i < renderers.Length; i++)
            RememberOriginalProjection(renderers[i]);
    }

    void RememberOriginalProjection(GaussianSplatRenderer renderer)
    {
        if (renderer == null || originalProjectionValues.ContainsKey(renderer))
            return;

        originalProjectionValues.Add(
            renderer,
            new Vector2(renderer.m_FisheyeFieldOfView, renderer.m_FisheyeStrength));
    }

    void RestoreProjectionValues()
    {
        if (fovController != null)
            fovController.verticalFieldOfView = originalControllerFov;
        else if (taskCamera != null)
            taskCamera.fieldOfView = originalCameraFov;

        foreach (KeyValuePair<GaussianSplatRenderer, Vector2> pair in originalProjectionValues)
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
        for (int i = 0; i < targets.Count; i++)
        {
            if (targets[i] != null)
                targets[i].ResetTarget();
        }
        SetInspectionMode(false);
    }

    void OnGUI()
    {
        const float width = 390.0f;
        float height = completed ? 178.0f : 158.0f;
        Rect rect = new Rect(Screen.width - width - 18.0f, 18.0f, width, height);
        GUI.Box(rect, GUIContent.none);

        string mode = inspectionMode ? "Fisheye inspection" : "Perspective baseline";
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 8.0f, width - 28.0f, 22.0f),
            "Peripheral Target Search");
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 34.0f, width - 28.0f, 20.0f),
            "Mode: " + mode);
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 56.0f, width - 28.0f, 20.0f),
            "Visible now: " + visibleCount + " / " + (targets.Count - foundCount));
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 78.0f, width - 28.0f, 20.0f),
            "Targets found: " + foundCount + " / " + targets.Count);
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 104.0f, width - 28.0f, 20.0f),
            "Tab / left primary: toggle lens");
        GUI.Label(new Rect(rect.x + 14.0f, rect.y + 124.0f, width - 28.0f, 20.0f),
            "Aim + Space / click / right trigger: activate");

        if (completed)
            GUI.Label(new Rect(rect.x + 14.0f, rect.y + 148.0f, width - 28.0f, 20.0f),
                "Inspection complete - press R to reset");
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

        if (shader == null)
            return;

        runtimeMaterial = new Material(shader);
        runtimeMaterial.name = "Inspection Marker " + (index + 1) + " Material";
        ApplyMaterialColor(runtimeMaterial, color);
        if (targetRenderer != null)
            targetRenderer.sharedMaterial = runtimeMaterial;
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

        transform.position = position;
        transform.rotation = rotation;
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
