using GaussianSplatting.Runtime;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

[DisallowMultipleComponent]
public sealed class DesktopPeripheralSearchTask : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] CameraFovController fovController;
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField] DesktopHighQualityFisheye highQualityFisheye;
    [SerializeField] bool initializeNarrowView = true;
    [SerializeField, Range(20.0f, 140.0f)] float initialFov = 60.0f;
    [SerializeField, Range(0.0f, 1.0f)] float initialFisheye = 0.0f;
    [SerializeField, Range(-130.0f, 130.0f)] float targetAzimuthDegrees = 78.0f;
    [SerializeField, Range(-45.0f, 45.0f)] float targetElevationDegrees = -4.0f;
    [SerializeField, Min(1.0f)] float targetDistance = 5.5f;
    [SerializeField, Min(0.05f)] float targetRadius = 0.28f;
    [SerializeField] int targetLayer = 31;
    [SerializeField] LayerMask raycastMask = ~0;
    [SerializeField, Range(70.0f, 180.0f)] float wideViewHintFov = 115.0f;
    [SerializeField, Range(0.05f, 1.0f)] float wideViewHintFisheye = 0.28f;
    [SerializeField] bool autoAddTargetLayerToCubemap = true;

    DesktopPeripheralTarget[] targets;
    DesktopRedMouseRay mouseRay;
    DesktopPeripheralGuideArrow guideArrow;
    Canvas taskCanvas;
    Text titleText;
    Text statusText;
    Text detailText;
    int selectedCount;
    bool selected;

    public void Configure(Camera camera, CameraFovController fov, GaussianSplatRenderer splat, DesktopHighQualityFisheye fisheye)
    {
        targetCamera = camera;
        fovController = fov;
        targetSplat = splat;
        highQualityFisheye = fisheye;
    }

    void Start()
    {
        ResolveReferences();

        if (initializeNarrowView)
        {
            if (fovController != null)
                fovController.verticalFieldOfView = initialFov;
            else if (targetCamera != null)
                targetCamera.fieldOfView = initialFov;

            if (targetSplat != null)
                targetSplat.m_FisheyeStrength = initialFisheye;
        }

        CreateTargets();
        ConfigureCubemapCapture();
        CreateMouseRay();
        CreateGuideArrow();
        CreateTaskUi();
        RefreshUi();
    }

    void Update()
    {
        if (targetCamera == null)
            ResolveReferences();

        RefreshUi();
    }

    void OnDestroy()
    {
        if (targets != null)
        {
            foreach (DesktopPeripheralTarget target in targets)
            {
                if (target != null)
                    Destroy(target.gameObject);
            }
        }

        if (taskCanvas != null)
            Destroy(taskCanvas.gameObject);
    }

    public void NotifyTargetSelected(DesktopPeripheralTarget selectedTarget)
    {
        if (selectedTarget == null || selectedTarget.isSelected || !ContainsTarget(selectedTarget))
            return;

        selectedTarget.SetActivated();
        selectedCount = CountSelectedTargets();
        selected = targets != null && selectedCount >= targets.Length;
        RequestCaptureRefresh();
        RefreshUi();
    }

    public DesktopPeripheralTarget GetGuideTarget()
    {
        if (targets == null)
            return null;

        foreach (DesktopPeripheralTarget taskTarget in targets)
        {
            if (taskTarget != null && !taskTarget.isSelected)
                return taskTarget;
        }

        return null;
    }

    public void RequestCaptureRefresh()
    {
        if (highQualityFisheye != null)
            highQualityFisheye.InvalidateCapture();
    }

    void ResolveReferences()
    {
        if (targetCamera == null)
            targetCamera = Camera.main != null ? Camera.main : FindFirst<Camera>();

        if (targetCamera != null)
        {
            if (fovController == null)
                fovController = targetCamera.GetComponent<CameraFovController>();

            if (highQualityFisheye == null)
                highQualityFisheye = targetCamera.GetComponent<DesktopHighQualityFisheye>();
        }

        if (targetSplat == null)
        {
            foreach (var renderer in FindObjectsByType<GaussianSplatRenderer>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (renderer.isActiveAndEnabled)
                {
                    targetSplat = renderer;
                    break;
                }
            }
        }

        if (highQualityFisheye == null)
            highQualityFisheye = FindFirst<DesktopHighQualityFisheye>();
    }

    void CreateTargets()
    {
        if (targets != null || targetCamera == null)
            return;

        var targetSpecs = new[]
        {
            new TargetSpec(targetAzimuthDegrees, targetElevationDegrees, targetDistance, 1.0f, new Color(0.1f, 0.85f, 1.0f, 1.0f)),
            new TargetSpec(-70.0f, 11.0f, targetDistance * 0.94f, 0.92f, new Color(1.0f, 0.32f, 0.92f, 1.0f)),
            new TargetSpec(118.0f, -13.0f, targetDistance * 1.08f, 1.08f, new Color(0.46f, 1.0f, 0.30f, 1.0f))
        };

        targets = new DesktopPeripheralTarget[targetSpecs.Length];
        for (int i = 0; i < targetSpecs.Length; ++i)
        {
            TargetSpec spec = targetSpecs[i];
            Vector3 direction = targetCamera.transform.rotation *
                (Quaternion.Euler(spec.elevationDegrees, spec.azimuthDegrees, 0.0f) * Vector3.forward);
            Vector3 position = targetCamera.transform.position + direction.normalized * spec.distance;

            var targetObject = new GameObject($"Peripheral Beacon Target {i + 1}")
            {
                layer = Mathf.Clamp(targetLayer, 0, 31)
            };
            targetObject.transform.position = position;
            var taskTarget = targetObject.AddComponent<DesktopPeripheralTarget>();
            taskTarget.Build(this, targetRadius * spec.radiusScale, spec.color, i + 1);
            targets[i] = taskTarget;
        }
    }

    void ConfigureCubemapCapture()
    {
        if (!autoAddTargetLayerToCubemap || highQualityFisheye == null || targets == null)
            return;

        foreach (DesktopPeripheralTarget taskTarget in targets)
        {
            if (taskTarget != null)
                highQualityFisheye.AddAdditionalCaptureLayer(taskTarget.gameObject.layer);
        }
    }

    void CreateMouseRay()
    {
        if (mouseRay != null)
            return;

        mouseRay = gameObject.GetComponent<DesktopRedMouseRay>();
        if (mouseRay == null)
            mouseRay = gameObject.AddComponent<DesktopRedMouseRay>();

        mouseRay.Configure(targetCamera, this, targetSplat, raycastMask);
    }

    void CreateGuideArrow()
    {
        if (guideArrow != null)
            return;

        guideArrow = gameObject.GetComponent<DesktopPeripheralGuideArrow>();
        if (guideArrow == null)
            guideArrow = gameObject.AddComponent<DesktopPeripheralGuideArrow>();

        guideArrow.Configure(targetCamera, this);
    }

    void CreateTaskUi()
    {
        if (taskCanvas != null)
            return;

        var canvasObject = new GameObject("Peripheral Search Task UI");
        taskCanvas = canvasObject.AddComponent<Canvas>();
        taskCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        taskCanvas.sortingOrder = 30;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1.0f;

        var panelObject = new GameObject("Panel");
        panelObject.transform.SetParent(canvasObject.transform, false);
        var panel = panelObject.AddComponent<Image>();
        panel.color = new Color(0.015f, 0.02f, 0.028f, 0.68f);

        var panelRect = panelObject.GetComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.0f, 1.0f);
        panelRect.anchorMax = new Vector2(0.0f, 1.0f);
        panelRect.pivot = new Vector2(0.0f, 1.0f);
        panelRect.anchoredPosition = new Vector2(20.0f, -20.0f);
        panelRect.sizeDelta = new Vector2(520.0f, 132.0f);

        titleText = CreateText("Title", panelObject.transform, 18, FontStyle.Bold, new Color(0.83f, 0.96f, 1.0f, 1.0f));
        SetRect(titleText.rectTransform, new Vector2(18.0f, -12.0f), new Vector2(484.0f, 24.0f));

        statusText = CreateText("Status", panelObject.transform, 15, FontStyle.Bold, new Color(0.96f, 0.98f, 1.0f, 0.96f));
        SetRect(statusText.rectTransform, new Vector2(18.0f, -45.0f), new Vector2(484.0f, 26.0f));

        detailText = CreateText("Detail", panelObject.transform, 13, FontStyle.Normal, new Color(0.78f, 0.84f, 0.88f, 0.94f));
        SetRect(detailText.rectTransform, new Vector2(18.0f, -78.0f), new Vector2(484.0f, 40.0f));
    }

    Text CreateText(string name, Transform parent, int size, FontStyle style, Color color)
    {
        var textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        var text = textObject.AddComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        text.fontSize = size;
        text.fontStyle = style;
        text.alignment = TextAnchor.MiddleLeft;
        text.color = color;
        text.raycastTarget = false;
        return text;
    }

    static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size)
    {
        rect.anchorMin = new Vector2(0.0f, 1.0f);
        rect.anchorMax = new Vector2(0.0f, 1.0f);
        rect.pivot = new Vector2(0.0f, 1.0f);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    void RefreshUi()
    {
        if (titleText == null || statusText == null || detailText == null)
            return;

        titleText.text = "Peripheral Search Task";

        if (selected)
        {
            statusText.text = "All targets activated";
            detailText.text = "Demo endpoint: three off-axis objects were found with wide-FOV/fisheye context and selected with the red ray.";
            return;
        }

        float fov = fovController != null ? fovController.verticalFieldOfView :
            (targetCamera != null ? targetCamera.fieldOfView : initialFov);
        float fisheye = targetSplat != null ? targetSplat.m_FisheyeStrength : 0.0f;
        bool wideViewReady = fov >= wideViewHintFov || fisheye >= wideViewHintFisheye;

        if (!wideViewReady)
        {
            statusText.text = "Find the hidden peripheral beacons";
            detailText.text = "They start outside the narrow view. Press '=' to widen FOV or '.' to increase fisheye, then follow the guide arrow.";
            return;
        }

        DesktopPeripheralTarget hoveredTarget = GetHoveredTarget();
        if (hoveredTarget != null)
        {
            statusText.text = $"Ray locked on target {hoveredTarget.targetIndex}";
            detailText.text = "Press left mouse button, Space, or Enter to activate this beacon.";
            return;
        }

        int total = targets != null ? targets.Length : 3;
        statusText.text = $"{selectedCount}/{total} beacons activated";
        detailText.text = "The red rays follow the cursor. Use the longer right-hand ray to select the next glowing target.";
    }

    bool ContainsTarget(DesktopPeripheralTarget candidate)
    {
        if (targets == null)
            return false;

        foreach (DesktopPeripheralTarget taskTarget in targets)
        {
            if (taskTarget == candidate)
                return true;
        }

        return false;
    }

    int CountSelectedTargets()
    {
        if (targets == null)
            return 0;

        int count = 0;
        foreach (DesktopPeripheralTarget taskTarget in targets)
        {
            if (taskTarget != null && taskTarget.isSelected)
                ++count;
        }

        return count;
    }

    DesktopPeripheralTarget GetHoveredTarget()
    {
        if (targets == null)
            return null;

        foreach (DesktopPeripheralTarget taskTarget in targets)
        {
            if (taskTarget != null && taskTarget.isHovered)
                return taskTarget;
        }

        return null;
    }

    readonly struct TargetSpec
    {
        public readonly float azimuthDegrees;
        public readonly float elevationDegrees;
        public readonly float distance;
        public readonly float radiusScale;
        public readonly Color color;

        public TargetSpec(float azimuthDegrees, float elevationDegrees, float distance, float radiusScale, Color color)
        {
            this.azimuthDegrees = azimuthDegrees;
            this.elevationDegrees = elevationDegrees;
            this.distance = distance;
            this.radiusScale = radiusScale;
            this.color = color;
        }
    }

    static T FindFirst<T>() where T : Object
    {
        var objects = FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return objects.Length > 0 ? objects[0] : null;
    }
}

public sealed class DesktopRedMouseRay : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] GaussianSplatRenderer targetSplat;
    [SerializeField] LayerMask raycastMask = ~0;
    [SerializeField, Min(0.5f)] float rayLength = 18.0f;
    [SerializeField] Vector2 rightRayOriginViewport = new(0.70f, 0.10f);
    [SerializeField] Vector2 leftRayOriginViewport = new(0.30f, 0.10f);
    [SerializeField, Min(20.0f)] float leftRayMaxLength = 165.0f;
    [SerializeField, Min(1.0f)] float rightNearWidth = 12.0f;
    [SerializeField, Min(1.0f)] float rightFarWidth = 4.2f;
    [SerializeField, Min(1.0f)] float leftNearWidth = 8.0f;
    [SerializeField, Min(1.0f)] float leftFarWidth = 2.8f;

    DesktopPeripheralSearchTask owner;
    DesktopPeripheralTarget hoveredTarget;
    Canvas overlayCanvas;
    RawImage[] rightRayGlowSegments;
    RawImage[] rightRayCoreSegments;
    RawImage[] leftRayGlowSegments;
    RawImage[] leftRayCoreSegments;
    readonly RaycastHit[] hitBuffer = new RaycastHit[24];
    readonly Color idleRayNearColor = new(1.0f, 0.02f, 0.01f, 0.92f);
    readonly Color idleRayFarColor = new(1.0f, 0.02f, 0.01f, 0.38f);
    readonly Color hoverRayNearColor = new(1.0f, 0.20f, 0.04f, 1.0f);
    readonly Color hoverRayFarColor = new(1.0f, 0.16f, 0.02f, 0.58f);

    public void Configure(Camera camera, DesktopPeripheralSearchTask taskOwner,
        GaussianSplatRenderer splat, LayerMask mask)
    {
        targetCamera = camera;
        owner = taskOwner;
        targetSplat = splat;
        raycastMask = mask;
    }

    void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        EnsureVisuals();
    }

    void Update()
    {
        if (targetCamera == null)
        {
            targetCamera = Camera.main;
            if (targetCamera == null)
                return;
        }

        EnsureVisuals();
        UpdateRay();
    }

    void OnDestroy()
    {
        if (overlayCanvas != null)
            Destroy(overlayCanvas.gameObject);
    }

    void UpdateRay()
    {
        Vector2 mousePosition = GetClampedMousePosition();
        if (!TryBuildMouseRay(mousePosition, out Ray ray))
            return;

        int hitCount = Physics.RaycastNonAlloc(ray, hitBuffer, rayLength, raycastMask, QueryTriggerInteraction.Collide);
        var newHoveredTarget = FindClosestTargetHit(hitCount);
        if (newHoveredTarget != hoveredTarget)
        {
            if (hoveredTarget != null)
                hoveredTarget.SetHover(false);

            hoveredTarget = newHoveredTarget;

            if (hoveredTarget != null)
                hoveredTarget.SetHover(true);
        }

        bool hoveringTarget = hoveredTarget != null;
        SetOverlayRay(mousePosition, hoveringTarget);

        if (hoveringTarget && WasSelectPressed())
            hoveredTarget.Select(owner);
    }

    DesktopPeripheralTarget FindClosestTargetHit(int hitCount)
    {
        DesktopPeripheralTarget closestTarget = null;
        float closestDistance = float.PositiveInfinity;
        for (int i = 0; i < hitCount; ++i)
        {
            Collider hitCollider = hitBuffer[i].collider;
            if (hitCollider == null || hitBuffer[i].distance >= closestDistance)
                continue;

            var hitTarget = hitCollider.GetComponentInParent<DesktopPeripheralTarget>();
            if (hitTarget == null)
                continue;

            closestTarget = hitTarget;
            closestDistance = hitBuffer[i].distance;
        }

        return closestTarget;
    }

    bool TryBuildMouseRay(Vector2 screenPosition, out Ray ray)
    {
        ray = default;
        if (targetCamera == null)
            return false;

        if (targetSplat == null)
            targetSplat = FindFirst<GaussianSplatRenderer>();

        if (targetSplat != null && TryBuildFisheyeRay(screenPosition, out ray))
            return true;

        ray = targetCamera.ScreenPointToRay(screenPosition);
        return true;
    }

    Vector2 GetClampedMousePosition()
    {
        Rect rect = targetCamera.pixelRect;
        Vector2 position = new(rect.center.x, rect.center.y);
        var mouse = Mouse.current;
        if (mouse != null)
            position = mouse.position.ReadValue();

        position.x = Mathf.Clamp(position.x, rect.xMin + 1.0f, rect.xMax - 1.0f);
        position.y = Mathf.Clamp(position.y, rect.yMin + 1.0f, rect.yMax - 1.0f);
        return position;
    }

    bool TryBuildFisheyeRay(Vector2 screenPosition, out Ray ray)
    {
        ray = default;
        var (fisheyeParams, fisheyeParams2) = targetSplat.GetFisheyeShaderParams(targetCamera);
        if (fisheyeParams.x <= 0.0001f || targetCamera.orthographic)
            return false;

        Rect rect = targetCamera.pixelRect;
        if (rect.width <= 1.0f || rect.height <= 1.0f)
            return false;

        Vector2 uv = new(
            Mathf.InverseLerp(rect.xMin, rect.xMax, screenPosition.x),
            Mathf.InverseLerp(rect.yMin, rect.yMax, screenPosition.y));
        Vector2 ndc = uv * 2.0f - Vector2.one;

        float k = fisheyeParams.y;
        float invK = fisheyeParams.z;
        float projX = fisheyeParams.w;
        float projY = fisheyeParams2.x;
        float maxTheta = fisheyeParams2.y;
        if (Mathf.Abs(k) < 1e-6f || Mathf.Abs(invK) < 1e-6f ||
            Mathf.Abs(projX) < 1e-6f || Mathf.Abs(projY) < 1e-6f)
            return false;

        Vector2 p = new(ndc.x / projX, ndc.y / projY);
        float r = p.magnitude;
        float theta = k * Mathf.Atan(r * invK);
        theta = Mathf.Min(theta, Mathf.Max(0.0f, maxTheta - 0.012f));

        Vector2 radial = r > 1e-6f ? p / r : Vector2.zero;
        float sinTheta = Mathf.Sin(theta);
        float cosTheta = Mathf.Cos(theta);
        Vector3 localDirection = new Vector3(radial.x * sinTheta, radial.y * sinTheta, cosTheta).normalized;
        Vector3 worldDirection = targetCamera.transform.TransformDirection(localDirection);
        ray = new Ray(targetCamera.transform.position, worldDirection);
        return true;
    }

    bool WasSelectPressed()
    {
        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
            return true;

        var keyboard = Keyboard.current;
        return keyboard != null &&
            (keyboard.spaceKey.wasPressedThisFrame || keyboard.enterKey.wasPressedThisFrame || keyboard.numpadEnterKey.wasPressedThisFrame);
    }

    void EnsureVisuals()
    {
        if (overlayCanvas == null || rightRayCoreSegments == null || rightRayGlowSegments == null ||
            leftRayCoreSegments == null || leftRayGlowSegments == null)
            CreateOverlayRay();
    }

    void CreateOverlayRay()
    {
        var canvasObject = new GameObject("Red Ray Overlay");
        overlayCanvas = canvasObject.AddComponent<Canvas>();
        overlayCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        overlayCanvas.sortingOrder = 2000;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1.0f;

        leftRayGlowSegments = CreateRaySegments("Left Ray Glow", overlayCanvas.transform, 6);
        leftRayCoreSegments = CreateRaySegments("Left Ray Core", overlayCanvas.transform, 6);
        rightRayGlowSegments = CreateRaySegments("Right Ray Glow", overlayCanvas.transform, 12);
        rightRayCoreSegments = CreateRaySegments("Right Ray Core", overlayCanvas.transform, 12);

        SetOverlayRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f), false);
    }

    RawImage[] CreateRaySegments(string groupName, Transform parent, int segmentCount)
    {
        var groupObject = new GameObject(groupName);
        groupObject.transform.SetParent(parent, false);
        var groupRect = groupObject.AddComponent<RectTransform>();
        groupRect.anchorMin = Vector2.zero;
        groupRect.anchorMax = Vector2.zero;
        groupRect.pivot = Vector2.zero;
        groupRect.anchoredPosition = Vector2.zero;
        groupRect.sizeDelta = new Vector2(Screen.width, Screen.height);
        var segments = new RawImage[segmentCount];
        for (int i = 0; i < segments.Length; ++i)
        {
            var segmentObject = new GameObject($"Segment {i + 1}");
            segmentObject.transform.SetParent(groupObject.transform, false);
            var image = segmentObject.AddComponent<RawImage>();
            image.texture = Texture2D.whiteTexture;
            image.raycastTarget = false;
            var rect = image.rectTransform;
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.zero;
            rect.pivot = new Vector2(0.0f, 0.5f);
            segments[i] = image;
        }

        return segments;
    }

    void SetOverlayRay(Vector2 targetScreenPosition, bool hoveringTarget)
    {
        if (rightRayCoreSegments == null || rightRayGlowSegments == null ||
            leftRayCoreSegments == null || leftRayGlowSegments == null)
            return;

        Vector2 rightOrigin = new(Screen.width * rightRayOriginViewport.x, Screen.height * rightRayOriginViewport.y);
        Vector2 leftOrigin = new(Screen.width * leftRayOriginViewport.x, Screen.height * leftRayOriginViewport.y);
        Vector2 leftDelta = targetScreenPosition - leftOrigin;
        Vector2 leftDirection = leftDelta.sqrMagnitude > 0.0001f ? leftDelta.normalized : Vector2.right;
        float leftLength = Mathf.Min(leftRayMaxLength, Mathf.Max(92.0f, leftDelta.magnitude * 0.24f));
        Vector2 leftEnd = leftOrigin + leftDirection * leftLength;

        Color nearColor = hoveringTarget ? hoverRayNearColor : idleRayNearColor;
        Color farColor = hoveringTarget ? hoverRayFarColor : idleRayFarColor;
        Color leftNearColor = new(nearColor.r, nearColor.g, nearColor.b, nearColor.a * 0.56f);
        Color leftFarColor = new(farColor.r, farColor.g, farColor.b, farColor.a * 0.46f);

        SetSegmentedRay(rightRayGlowSegments, rightOrigin, targetScreenPosition, rightNearWidth * 2.55f, rightFarWidth * 2.4f,
            WithAlpha(nearColor, 0.22f), WithAlpha(farColor, 0.08f));
        SetSegmentedRay(rightRayCoreSegments, rightOrigin, targetScreenPosition, rightNearWidth, rightFarWidth, nearColor, farColor);
        SetSegmentedRay(leftRayGlowSegments, leftOrigin, leftEnd, leftNearWidth * 2.25f, leftFarWidth * 2.1f,
            WithAlpha(leftNearColor, 0.14f), WithAlpha(leftFarColor, 0.05f));
        SetSegmentedRay(leftRayCoreSegments, leftOrigin, leftEnd, leftNearWidth, leftFarWidth, leftNearColor, leftFarColor);
    }

    void SetSegmentedRay(RawImage[] segments, Vector2 start, Vector2 end, float nearWidth, float farWidth,
        Color nearColor, Color farColor)
    {
        Vector2 delta = end - start;
        float length = delta.magnitude;
        if (length <= 2.0f)
        {
            foreach (RawImage segment in segments)
            {
                if (segment != null)
                    segment.enabled = false;
            }

            return;
        }

        float angle = Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg;
        for (int i = 0; i < segments.Length; ++i)
        {
            RawImage segment = segments[i];
            if (segment == null)
                continue;

            float t0 = (float)i / segments.Length;
            float t1 = (float)(i + 1) / segments.Length;
            float tm = (t0 + t1) * 0.5f;
            Vector2 segmentStart = Vector2.Lerp(start, end, t0);
            Vector2 segmentEnd = Vector2.Lerp(start, end, t1);
            float segmentLength = (segmentEnd - segmentStart).magnitude + 1.5f;
            float width = Mathf.Lerp(nearWidth, farWidth, tm);
            segment.enabled = true;
            segment.color = Color.Lerp(nearColor, farColor, tm);
            var rect = segment.rectTransform;
            rect.anchoredPosition = segmentStart;
            rect.sizeDelta = new Vector2(segmentLength, width);
            rect.localEulerAngles = new Vector3(0.0f, 0.0f, angle);
        }
    }

    static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    static T FindFirst<T>() where T : Object
    {
        var objects = Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return objects.Length > 0 ? objects[0] : null;
    }
}

public sealed class DesktopPeripheralGuideArrow : MonoBehaviour
{
    [SerializeField] Camera targetCamera;
    [SerializeField] Vector2 edgePadding = new(128.0f, 112.0f);
    [SerializeField, Range(0.2f, 0.48f)] float edgeRadius = 0.36f;

    DesktopPeripheralSearchTask owner;
    Canvas guideCanvas;
    CanvasGroup canvasGroup;
    RectTransform indicatorRoot;
    Texture2D ringTexture;
    Texture2D wedgeTexture;
    Texture2D dotTexture;

    public void Configure(Camera camera, DesktopPeripheralSearchTask taskOwner)
    {
        targetCamera = camera;
        owner = taskOwner;
    }

    void Start()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        EnsureVisuals();
    }

    void Update()
    {
        if (targetCamera == null)
            targetCamera = Camera.main;

        EnsureVisuals();
        UpdateIndicator();
    }

    void OnDestroy()
    {
        if (guideCanvas != null)
            Destroy(guideCanvas.gameObject);

        if (ringTexture != null)
            Destroy(ringTexture);
        if (wedgeTexture != null)
            Destroy(wedgeTexture);
        if (dotTexture != null)
            Destroy(dotTexture);
    }

    void EnsureVisuals()
    {
        if (guideCanvas != null && indicatorRoot != null)
            return;

        var canvasObject = new GameObject("Peripheral Spatial Guide Overlay");
        guideCanvas = canvasObject.AddComponent<Canvas>();
        guideCanvas.renderMode = RenderMode.ScreenSpaceOverlay;
        guideCanvas.sortingOrder = 2150;
        canvasGroup = canvasObject.AddComponent<CanvasGroup>();
        canvasGroup.interactable = false;
        canvasGroup.blocksRaycasts = false;

        var scaler = canvasObject.AddComponent<CanvasScaler>();
        scaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        scaler.scaleFactor = 1.0f;

        var rootObject = new GameObject("Spatial Guide Indicator");
        rootObject.transform.SetParent(guideCanvas.transform, false);
        indicatorRoot = rootObject.AddComponent<RectTransform>();
        indicatorRoot.anchorMin = Vector2.zero;
        indicatorRoot.anchorMax = Vector2.zero;
        indicatorRoot.pivot = new Vector2(0.5f, 0.5f);
        indicatorRoot.sizeDelta = new Vector2(132.0f, 132.0f);

        ringTexture = CreateRingTexture(128, 0.42f, 0.025f, 0.18f);
        wedgeTexture = CreateWedgeTexture(128, 92);
        dotTexture = CreateDiscTexture(48);

        CreateSpatialGraphic("Guide Ambient Halo", indicatorRoot, ringTexture, new Vector2(132.0f, 132.0f),
            Vector2.zero, new Color(0.12f, 0.82f, 1.0f, 0.13f));
        CreateSpatialGraphic("Guide Depth Ring", indicatorRoot, ringTexture, new Vector2(88.0f, 88.0f),
            new Vector2(-12.0f, 0.0f), new Color(0.82f, 0.98f, 1.0f, 0.46f));
        CreateSpatialGraphic("Guide Inner Ring", indicatorRoot, ringTexture, new Vector2(54.0f, 54.0f),
            new Vector2(-16.0f, 0.0f), new Color(0.30f, 0.92f, 1.0f, 0.32f));
        CreateSpatialGraphic("Guide Wedge Chromatic Edge", indicatorRoot, wedgeTexture, new Vector2(102.0f, 72.0f),
            new Vector2(27.0f, -2.5f), new Color(0.84f, 0.26f, 1.0f, 0.18f));
        CreateSpatialGraphic("Guide Wedge Glow", indicatorRoot, wedgeTexture, new Vector2(116.0f, 84.0f),
            new Vector2(30.0f, 0.0f), new Color(0.08f, 0.72f, 1.0f, 0.18f));
        CreateSpatialGraphic("Guide Wedge Core", indicatorRoot, wedgeTexture, new Vector2(88.0f, 58.0f),
            new Vector2(28.0f, 0.0f), new Color(0.86f, 0.98f, 1.0f, 0.78f));
        CreateSpatialGraphic("Guide Core Dot", indicatorRoot, dotTexture, new Vector2(16.0f, 16.0f),
            new Vector2(-16.0f, 0.0f), new Color(0.92f, 1.0f, 1.0f, 0.86f));
    }

    void CreateSpatialGraphic(string graphicName, Transform parent, Texture texture, Vector2 size,
        Vector2 offset, Color graphicColor)
    {
        var graphicObject = new GameObject(graphicName);
        graphicObject.transform.SetParent(parent, false);
        var rect = graphicObject.AddComponent<RectTransform>();
        rect.anchorMin = new Vector2(0.5f, 0.5f);
        rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.pivot = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = offset;
        rect.sizeDelta = size;

        var graphic = graphicObject.AddComponent<RawImage>();
        graphic.texture = texture;
        graphic.raycastTarget = false;
        graphic.color = graphicColor;
    }

    void UpdateIndicator()
    {
        if (guideCanvas == null || indicatorRoot == null || owner == null || targetCamera == null)
            return;

        DesktopPeripheralTarget target = owner.GetGuideTarget();
        bool showIndicator = target != null;
        indicatorRoot.gameObject.SetActive(showIndicator);
        if (!showIndicator)
            return;

        Vector2 direction = GetTargetDirection(target.transform.position);
        Vector2 center = new(Screen.width * 0.5f, Screen.height * 0.5f);
        float radius = Mathf.Min(Screen.width, Screen.height) * edgeRadius;
        float drift = Mathf.Sin(Time.unscaledTime * 2.3f) * 5.5f;
        Vector2 position = center + direction * (radius + drift);
        position.x = Mathf.Clamp(position.x, edgePadding.x, Screen.width - edgePadding.x);
        position.y = Mathf.Clamp(position.y, edgePadding.y, Screen.height - edgePadding.y);

        indicatorRoot.anchoredPosition = position;
        indicatorRoot.localEulerAngles = new Vector3(0.0f, 0.0f, Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg);
        float pulse = 1.0f + Mathf.Sin(Time.unscaledTime * 3.7f) * 0.025f;
        indicatorRoot.localScale = Vector3.one * pulse;
        if (canvasGroup != null)
            canvasGroup.alpha = 0.72f + Mathf.Sin(Time.unscaledTime * 2.7f) * 0.08f;
    }

    Vector2 GetTargetDirection(Vector3 targetPosition)
    {
        Vector3 local = targetCamera.transform.InverseTransformPoint(targetPosition);
        Vector2 direction = new(local.x, local.y);
        if (direction.sqrMagnitude < 0.0001f)
        {
            Vector3 screen = targetCamera.WorldToScreenPoint(targetPosition);
            direction = new Vector2(screen.x - Screen.width * 0.5f, screen.y - Screen.height * 0.5f);
        }

        if (direction.sqrMagnitude < 0.0001f)
            direction = Vector2.right;

        return direction.normalized;
    }

    static Texture2D CreateRingTexture(int size, float radius01, float lineWidth01, float glowWidth01)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Spatial Guide Ring"
        };

        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        float radius = size * radius01;
        float lineWidth = size * lineWidth01;
        float glowWidth = size * glowWidth01;
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float lineAlpha = Mathf.Clamp01(1.0f - Mathf.Abs(d - radius) / Mathf.Max(0.001f, lineWidth));
                float glowAlpha = Mathf.Clamp01(1.0f - Mathf.Abs(d - radius) / Mathf.Max(0.001f, glowWidth)) * 0.38f;
                float alpha = Mathf.Max(lineAlpha, glowAlpha);
                pixels[y * size + x] = new Color(1.0f, 1.0f, 1.0f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static Texture2D CreateWedgeTexture(int width, int height)
    {
        var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
        {
            name = "Spatial Guide Wedge"
        };

        var pixels = new Color32[width * height];
        Vector2 leftTop = new(width * 0.20f, height * 0.18f);
        Vector2 tip = new(width * 0.82f, height * 0.50f);
        Vector2 leftBottom = new(width * 0.20f, height * 0.82f);
        Vector2 stemA = new(width * 0.08f, height * 0.50f);
        Vector2 stemB = new(width * 0.46f, height * 0.50f);
        float lineWidth = Mathf.Max(2.5f, height * 0.045f);
        float glowWidth = lineWidth * 3.2f;

        for (int y = 0; y < height; ++y)
        {
            for (int x = 0; x < width; ++x)
            {
                Vector2 p = new(x, y);
                float d = Mathf.Min(
                    DistanceToSegment(p, leftTop, tip),
                    DistanceToSegment(p, tip, leftBottom));
                d = Mathf.Min(d, DistanceToSegment(p, stemA, stemB));
                float lineAlpha = Mathf.Clamp01(1.0f - d / lineWidth);
                float glowAlpha = Mathf.Clamp01(1.0f - d / glowWidth) * 0.36f;
                float alpha = Mathf.Max(lineAlpha, glowAlpha);
                pixels[y * width + x] = new Color(1.0f, 1.0f, 1.0f, alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static Texture2D CreateDiscTexture(int size)
    {
        var texture = new Texture2D(size, size, TextureFormat.RGBA32, false)
        {
            name = "Spatial Guide Core"
        };
        var pixels = new Color32[size * size];
        float center = (size - 1) * 0.5f;
        for (int y = 0; y < size; ++y)
        {
            for (int x = 0; x < size; ++x)
            {
                float d = Vector2.Distance(new Vector2(x, y), new Vector2(center, center));
                float alpha = Mathf.Clamp01(1.0f - d / center);
                pixels[y * size + x] = new Color(1.0f, 1.0f, 1.0f, alpha * alpha);
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply(false, true);
        return texture;
    }

    static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
    {
        Vector2 ab = b - a;
        float t = Vector2.Dot(p - a, ab) / Mathf.Max(0.0001f, Vector2.Dot(ab, ab));
        t = Mathf.Clamp01(t);
        return Vector2.Distance(p, a + ab * t);
    }
}

public sealed class DesktopPeripheralTarget : MonoBehaviour
{
    public bool isHovered { get; private set; }
    public bool isSelected { get; private set; }
    public int targetIndex { get; private set; }

    DesktopPeripheralSearchTask owner;
    Transform orbitRoot;
    Transform innerGlowShell;
    Transform outerGlowShell;
    Material coreMaterial;
    Material ringMaterial;
    Material satelliteMaterial;
    Material innerGlowMaterial;
    Material outerGlowMaterial;
    Color idleColor = new(0.1f, 0.85f, 1.0f, 1.0f);
    Color hoverColor = new(1.0f, 0.82f, 0.18f, 1.0f);
    Color selectedColor = new(0.35f, 1.0f, 0.45f, 1.0f);
    Color activeColor;
    float radius;

    public void Build(DesktopPeripheralSearchTask taskOwner, float targetRadius, Color baseColor, int index)
    {
        owner = taskOwner;
        targetIndex = index;
        radius = Mathf.Max(0.05f, targetRadius);
        idleColor = baseColor;
        activeColor = idleColor;

        coreMaterial = CreateLitMaterial(idleColor, true);
        ringMaterial = CreateGlowMaterial(idleColor, 0.9f);
        satelliteMaterial = CreateLitMaterial(new Color(0.88f, 0.96f, 1.0f, 1.0f), true);
        innerGlowMaterial = CreateGlowMaterial(idleColor, 0.24f);
        outerGlowMaterial = CreateGlowMaterial(idleColor, 0.10f);

        var interactionCollider = gameObject.AddComponent<SphereCollider>();
        interactionCollider.radius = radius * 2.85f;
        interactionCollider.isTrigger = true;

        var core = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        core.name = "Glow Core";
        core.transform.SetParent(transform, false);
        core.transform.localScale = Vector3.one * (radius * 2.0f);
        core.GetComponent<Renderer>().sharedMaterial = coreMaterial;
        Destroy(core.GetComponent<Collider>());

        innerGlowShell = CreateGlowShell("Inner Volumetric Glow", radius * 3.0f, innerGlowMaterial);
        outerGlowShell = CreateGlowShell("Outer Pulsing Glow", radius * 4.25f, outerGlowMaterial);

        orbitRoot = new GameObject("Orbiting Detail").transform;
        orbitRoot.SetParent(transform, false);

        CreateRing("XY Ring", Quaternion.identity);
        CreateRing("XZ Ring", Quaternion.Euler(90.0f, 0.0f, 0.0f));
        CreateRing("Tilt Ring", Quaternion.Euler(35.0f, 18.0f, 0.0f));
        CreateSatellites();

        var light = gameObject.AddComponent<Light>();
        light.type = LightType.Point;
        light.color = idleColor;
        light.intensity = 1.5f;
        light.range = 2.0f;

        SetLayerRecursively(transform, gameObject.layer);
    }

    void Update()
    {
        float pulse01 = 0.5f + Mathf.Sin(Time.unscaledTime * 4.8f) * 0.5f;
        float slowPulse = 0.5f + Mathf.Sin(Time.unscaledTime * 2.1f + 1.3f) * 0.5f;

        if (orbitRoot != null)
            orbitRoot.Rotate(0.0f, 58.0f * Time.unscaledDeltaTime, 34.0f * Time.unscaledDeltaTime, Space.Self);

        float selectedScale = isSelected ? 1.12f : 1.0f;
        transform.localScale = Vector3.one * selectedScale;

        if (innerGlowShell != null)
            innerGlowShell.localScale = Vector3.one * radius * Mathf.Lerp(2.75f, 3.25f, pulse01);
        if (outerGlowShell != null)
            outerGlowShell.localScale = Vector3.one * radius * Mathf.Lerp(3.85f, 4.75f, slowPulse);

        float innerAlpha = isSelected ? 0.34f : isHovered ? 0.30f : Mathf.Lerp(0.16f, 0.29f, pulse01);
        float outerAlpha = isSelected ? 0.20f : isHovered ? 0.16f : Mathf.Lerp(0.06f, 0.13f, slowPulse);
        SetMaterialColor(innerGlowMaterial, WithAlpha(activeColor, innerAlpha), true);
        SetMaterialColor(outerGlowMaterial, WithAlpha(activeColor, outerAlpha), true);

        var light = GetComponent<Light>();
        if (light != null)
        {
            light.color = activeColor;
            light.intensity = (isSelected ? 3.3f : isHovered ? 2.5f : 1.45f) + pulse01 * 0.55f;
        }
    }

    void OnDestroy()
    {
        if (coreMaterial != null)
            Destroy(coreMaterial);
        if (ringMaterial != null)
            Destroy(ringMaterial);
        if (satelliteMaterial != null)
            Destroy(satelliteMaterial);
        if (innerGlowMaterial != null)
            Destroy(innerGlowMaterial);
        if (outerGlowMaterial != null)
            Destroy(outerGlowMaterial);
    }

    public void SetHover(bool hovered)
    {
        if (isSelected)
            return;

        isHovered = hovered;
        ApplyColor(hovered ? hoverColor : idleColor);
        owner?.RequestCaptureRefresh();
    }

    public void Select(DesktopPeripheralSearchTask taskOwner)
    {
        if (taskOwner != null)
            owner = taskOwner;

        owner?.NotifyTargetSelected(this);
    }

    public void SetActivated()
    {
        isSelected = true;
        isHovered = false;
        ApplyColor(selectedColor);
        owner?.RequestCaptureRefresh();
    }

    void CreateRing(string ringName, Quaternion localRotation)
    {
        var ringObject = new GameObject(ringName);
        ringObject.transform.SetParent(transform, false);
        ringObject.transform.localRotation = localRotation;
        var ring = ringObject.AddComponent<LineRenderer>();
        ring.useWorldSpace = false;
        ring.loop = true;
        ring.positionCount = 96;
        ring.widthMultiplier = radius * 0.035f;
        ring.numCapVertices = 4;
        ring.material = ringMaterial;

        float ringRadius = radius * 1.65f;
        for (int i = 0; i < ring.positionCount; ++i)
        {
            float t = (Mathf.PI * 2.0f * i) / ring.positionCount;
            ring.SetPosition(i, new Vector3(Mathf.Cos(t) * ringRadius, Mathf.Sin(t) * ringRadius, 0.0f));
        }
    }

    void CreateSatellites()
    {
        for (int i = 0; i < 4; ++i)
        {
            var satellite = GameObject.CreatePrimitive(PrimitiveType.Cube);
            satellite.name = $"Orbit Marker {i + 1}";
            satellite.transform.SetParent(orbitRoot, false);
            float angle = i * Mathf.PI * 0.5f;
            satellite.transform.localPosition = new Vector3(Mathf.Cos(angle), Mathf.Sin(angle), 0.28f) * radius * 2.35f;
            satellite.transform.localScale = Vector3.one * radius * 0.28f;
            satellite.transform.localRotation = Quaternion.Euler(25.0f, i * 45.0f, 40.0f);
            Destroy(satellite.GetComponent<Collider>());
            satellite.GetComponent<Renderer>().sharedMaterial = satelliteMaterial;
        }
    }

    void ApplyColor(Color color)
    {
        activeColor = color;
        SetMaterialColor(coreMaterial, color, true);
        SetMaterialColor(ringMaterial, color, true);
        SetMaterialColor(satelliteMaterial, Color.Lerp(color, Color.white, 0.62f), true);
        SetMaterialColor(innerGlowMaterial, WithAlpha(color, isHovered ? 0.30f : 0.22f), true);
        SetMaterialColor(outerGlowMaterial, WithAlpha(color, isHovered ? 0.16f : 0.10f), true);

        var light = GetComponent<Light>();
        if (light != null)
        {
            light.color = color;
            light.intensity = isSelected ? 2.8f : isHovered ? 2.2f : 1.5f;
        }
    }

    Transform CreateGlowShell(string shellName, float diameter, Material material)
    {
        var shell = GameObject.CreatePrimitive(PrimitiveType.Sphere);
        shell.name = shellName;
        shell.transform.SetParent(transform, false);
        shell.transform.localScale = Vector3.one * diameter;
        Destroy(shell.GetComponent<Collider>());
        shell.GetComponent<Renderer>().sharedMaterial = material;
        return shell.transform;
    }

    static Material CreateLitMaterial(Color color, bool emissive)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            shader = Shader.Find("Standard");
        if (shader == null)
            shader = Shader.Find("Universal Render Pipeline/Unlit");

        var material = new Material(shader)
        {
            color = color
        };
        if (material.HasProperty("_Metallic"))
            material.SetFloat("_Metallic", 0.12f);
        if (material.HasProperty("_Smoothness"))
            material.SetFloat("_Smoothness", 0.88f);
        if (material.HasProperty("_Glossiness"))
            material.SetFloat("_Glossiness", 0.82f);
        SetMaterialColor(material, color, emissive);
        return material;
    }

    static Material CreateGlowMaterial(Color color, float alpha)
    {
        Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
        if (shader == null)
            shader = Shader.Find("Sprites/Default");
        if (shader == null)
            shader = Shader.Find("Standard");

        var material = new Material(shader)
        {
            color = WithAlpha(color, alpha),
            renderQueue = 3000
        };
        material.SetOverrideTag("RenderType", "Transparent");
        if (material.HasProperty("_Surface"))
            material.SetFloat("_Surface", 1.0f);
        if (material.HasProperty("_Blend"))
            material.SetFloat("_Blend", 1.0f);
        if (material.HasProperty("_SrcBlend"))
            material.SetFloat("_SrcBlend", (float)UnityEngine.Rendering.BlendMode.SrcAlpha);
        if (material.HasProperty("_DstBlend"))
            material.SetFloat("_DstBlend", (float)UnityEngine.Rendering.BlendMode.One);
        if (material.HasProperty("_ZWrite"))
            material.SetFloat("_ZWrite", 0.0f);
        material.EnableKeyword("_SURFACE_TYPE_TRANSPARENT");
        material.EnableKeyword("_ALPHABLEND_ON");
        SetMaterialColor(material, WithAlpha(color, alpha), true);
        return material;
    }

    static void SetMaterialColor(Material material, Color color, bool emissive)
    {
        if (material == null)
            return;

        if (material.HasProperty("_BaseColor"))
            material.SetColor("_BaseColor", color);
        if (material.HasProperty("_Color"))
            material.SetColor("_Color", color);
        if (emissive && material.HasProperty("_EmissionColor"))
        {
            material.EnableKeyword("_EMISSION");
            material.SetColor("_EmissionColor", color * 2.2f);
        }
    }

    static Color WithAlpha(Color color, float alpha)
    {
        color.a = Mathf.Clamp01(alpha);
        return color;
    }

    static void SetLayerRecursively(Transform root, int layer)
    {
        root.gameObject.layer = layer;
        foreach (Transform child in root)
            SetLayerRecursively(child, layer);
    }
}

static class DesktopPeripheralSearchBootstrap
{
    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    static void AutoInstallInDesktopPreview()
    {
        if (!Application.isPlaying)
            return;

        Scene scene = SceneManager.GetActiveScene();
        if (!scene.name.Contains("DesktopPreview"))
            return;

        if (Object.FindObjectsByType<DesktopPeripheralSearchTask>(FindObjectsInactive.Exclude, FindObjectsSortMode.None).Length > 0)
            return;

        var highQuality = FindFirst<DesktopHighQualityFisheye>();
        if (highQuality == null)
            return;

        var camera = highQuality.GetComponent<Camera>();
        if (camera == null)
            camera = Camera.main;

        var taskObject = new GameObject("Desktop Peripheral Search Task");
        var task = taskObject.AddComponent<DesktopPeripheralSearchTask>();
        task.Configure(camera, camera != null ? camera.GetComponent<CameraFovController>() : null, FindFirst<GaussianSplatRenderer>(), highQuality);
    }

    static T FindFirst<T>() where T : Object
    {
        var objects = Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return objects.Length > 0 ? objects[0] : null;
    }
}
