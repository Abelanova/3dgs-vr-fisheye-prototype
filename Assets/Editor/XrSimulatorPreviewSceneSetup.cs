using GaussianSplatting.Runtime;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.UI;
using UnityEngine.InputSystem.XR;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Simulation;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

public static class XrSimulatorPreviewSceneSetup
{
    const string ScenePath = "Assets/Scenes/XRSimulatorPreview.unity";
    const string DesktopScenePath = "Assets/Scenes/DesktopPreview.unity";

    static readonly string[] SimulatorPrefabPaths =
    {
        "Assets/Samples/XR Interaction Toolkit/3.5.1/XR Interaction Simulator/XR Interaction Simulator.prefab",
        "Packages/com.unity.xr.interaction.toolkit/Samples~/XR Interaction Simulator/XR Interaction Simulator.prefab",
        "Assets/Samples/XR Interaction Toolkit/3.5.1/XR Device Simulator/XR Device Simulator.prefab",
        "Packages/com.unity.xr.interaction.toolkit/Samples~/XR Device Simulator/XR Device Simulator.prefab",
    };

    static readonly Vector3 OriginPosition = new(0.0f, 0.0f, -4.0f);
    static readonly Vector3 SplatRotationEuler = new(-90.0f, -35.0f, 0.0f);

    [MenuItem("Tools/VR Preview/Create XR Simulator Preview Scene")]
    public static void CreatePreviewScene()
    {
        ApplyProjectSettings();

        var asset = AssetDatabase.LoadAssetAtPath<GaussianSplatAsset>("Assets/GaussianAssets/ChristmasTree.asset");
        if (asset == null)
        {
            Debug.LogError("Could not load Assets/GaussianAssets/ChristmasTree.asset.");
            return;
        }

        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        scene.name = "XRSimulatorPreview";

        var interactionManager = new GameObject("XR Interaction Manager");
        interactionManager.AddComponent<XRInteractionManager>();

        var originObject = new GameObject("XR Origin");
        originObject.transform.position = OriginPosition;

        var cameraOffset = new GameObject("Camera Offset");
        cameraOffset.transform.SetParent(originObject.transform, false);

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(cameraOffset.transform, false);
        cameraObject.transform.localPosition = new Vector3(0.0f, 1.6f, 0.0f);
        cameraObject.AddComponent<AudioListener>();
        AddTrackedPoseDriver(cameraObject, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");

        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1);
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 500.0f;
        camera.fieldOfView = 60.0f;

        var fovController = cameraObject.AddComponent<CameraFovController>();
        var fovControllerSo = new SerializedObject(fovController);
        fovControllerSo.FindProperty("targetCamera").objectReferenceValue = camera;
        fovControllerSo.FindProperty("verticalFov").floatValue = camera.fieldOfView;
        fovControllerSo.ApplyModifiedPropertiesWithoutUndo();

        var xrOrigin = originObject.AddComponent<XROrigin>();
        xrOrigin.Origin = originObject;
        xrOrigin.CameraFloorOffsetObject = cameraOffset;
        xrOrigin.Camera = camera;
        xrOrigin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Floor;

        var leftController = CreateController("Left Controller", cameraOffset.transform, new Vector3(-0.25f, 1.2f, 0.35f), true);
        var rightController = CreateController("Right Controller", cameraOffset.transform, new Vector3(0.25f, 1.2f, 0.35f), false);

        var splatObject = new GameObject("ChristmasTree Gaussian Splat");
        splatObject.transform.rotation = Quaternion.Euler(SplatRotationEuler);
        var splat = splatObject.AddComponent<GaussianSplatRenderer>();
        splat.m_Asset = asset;
        splat.m_SplatScale = 0.75f;
        splat.m_OpacityScale = 1.0f;
        splat.m_SHOrder = 3;
        splat.m_SortNthFrame = 1;
        splat.m_FisheyeFieldOfView = 60.0f;
        splat.m_FisheyeStrength = 0.0f;
        splat.m_NearFadeDistance = 0.8f;

        fovControllerSo = new SerializedObject(fovController);
        fovControllerSo.FindProperty("targetSplat").objectReferenceValue = splat;
        fovControllerSo.ApplyModifiedPropertiesWithoutUndo();

        var keyboardControls = cameraObject.AddComponent<ProjectionKeyboardControls>();
        var keyboardControlsSo = new SerializedObject(keyboardControls);
        keyboardControlsSo.FindProperty("fovController").objectReferenceValue = fovController;
        keyboardControlsSo.FindProperty("splat").objectReferenceValue = splat;
        keyboardControlsSo.ApplyModifiedPropertiesWithoutUndo();

        CreateProjectionPanel(camera, fovController, splat);
        CreateSimulator(cameraObject.transform, leftController.transform, rightController.transform);

        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        lightObject.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.OpenScene(ScenePath);
        AddSceneToBuildSettings(ScenePath);

        Debug.Log("Created Assets/Scenes/XRSimulatorPreview.unity. In Play Mode, use the XR simulator UI/controls to move the simulated HMD and controllers.");
    }

    [MenuItem("Tools/VR Preview/Create Desktop Preview Scene %#d")]
    public static void CreateDesktopPreviewScene()
    {
        ApplyProjectSettings();

        var previousScene = SceneManager.GetActiveScene();
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        SceneManager.SetActiveScene(scene);
        scene.name = "DesktopPreview";

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.position = new Vector3(0.0f, 1.6f, -4.0f);
        cameraObject.AddComponent<AudioListener>();
        cameraObject.AddComponent<DesktopCameraController>();

        var camera = cameraObject.AddComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1.0f);
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 500.0f;
        camera.fieldOfView = 60.0f;
        camera.stereoTargetEye = StereoTargetEyeMask.None;

        var splatObject = new GameObject("PutAssetsHere");
        var splat = splatObject.AddComponent<GaussianSplatRenderer>();
        splat.m_RenderMode = GaussianSplatRenderer.RenderMode.Splats;
        splat.m_SHOrder = 3;
        splat.m_SortNthFrame = 1;
        splat.m_FisheyeFieldOfView = 60.0f;

        var fovController = cameraObject.AddComponent<CameraFovController>();
        var fovControllerSo = new SerializedObject(fovController);
        fovControllerSo.FindProperty("targetCamera").objectReferenceValue = camera;
        fovControllerSo.FindProperty("targetSplat").objectReferenceValue = splat;
        fovControllerSo.FindProperty("verticalFov").floatValue = camera.fieldOfView;
        fovControllerSo.ApplyModifiedPropertiesWithoutUndo();

        var keyboardControls = cameraObject.AddComponent<ProjectionKeyboardControls>();
        var keyboardControlsSo = new SerializedObject(keyboardControls);
        keyboardControlsSo.FindProperty("fovController").objectReferenceValue = fovController;
        keyboardControlsSo.FindProperty("splat").objectReferenceValue = splat;
        keyboardControlsSo.ApplyModifiedPropertiesWithoutUndo();

        CreateDesktopProjectionPanel(fovController, splat);

        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        lightObject.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

        EditorSceneManager.SaveScene(scene, DesktopScenePath);
        AddSceneToBuildSettings(DesktopScenePath);
        SceneManager.SetActiveScene(previousScene);
        EditorSceneManager.CloseScene(scene, true);

        Debug.Log("Created Assets/Scenes/DesktopPreview.unity. Open it, assign a GaussianSplatAsset to PutAssetsHere, then enter Play Mode. Use right mouse to look, WASD and Q/E to move, and Shift to move faster.");
    }

    static GameObject CreateController(string name, Transform parent, Vector3 fallbackPosition, bool left)
    {
        var controller = new GameObject(name);
        controller.name = name;
        controller.transform.SetParent(parent, false);
        controller.transform.localPosition = fallbackPosition;

        string hand = left ? "LeftHand" : "RightHand";
        AddTrackedPoseDriver(controller, $"<XRController>{{{hand}}}/devicePosition", $"<XRController>{{{hand}}}/deviceRotation");
        AddOfficialRayInteractor(controller, left);
        return controller;
    }

    static void AddTrackedPoseDriver(GameObject target, string positionPath, string rotationPath)
    {
        var driver = target.AddComponent<TrackedPoseDriver>();
        driver.trackingType = TrackedPoseDriver.TrackingType.RotationAndPosition;
        driver.updateType = TrackedPoseDriver.UpdateType.UpdateAndBeforeRender;
        driver.ignoreTrackingState = true;

        var position = new InputAction("Position", InputActionType.Value, positionPath, expectedControlType: "Vector3");
        var rotation = new InputAction("Rotation", InputActionType.Value, rotationPath, expectedControlType: "Quaternion");
        driver.positionInput = new InputActionProperty(position);
        driver.rotationInput = new InputActionProperty(rotation);
    }

    static void AddOfficialRayInteractor(GameObject target, bool left)
    {
        var ray = target.AddComponent<XRRayInteractor>();
        ray.enableUIInteraction = true;
        ray.lineType = XRRayInteractor.LineType.StraightLine;
        ray.maxRaycastDistance = 8.0f;

        string hand = left ? "LeftHand" : "RightHand";
        var triggerPressedAction = new InputAction(left ? "Left UI Press" : "Right UI Press", InputActionType.Button, $"<XRController>{{{hand}}}/triggerPressed");
        var triggerValueAction = new InputAction(left ? "Left UI Press Value" : "Right UI Press Value", InputActionType.Value, $"<XRController>{{{hand}}}/trigger", expectedControlType: "Axis");
        var selectReader = new XRInputButtonReader(left ? "Left Select" : "Right Select")
        {
            inputSourceMode = XRInputButtonReader.InputSourceMode.InputAction,
            inputActionPerformed = triggerPressedAction,
            inputActionValue = triggerValueAction,
        };
        ray.selectInput = selectReader;
        ray.uiPressInput = selectReader;

    }

    static void CreateSimulator(Transform head, Transform leftController, Transform rightController)
    {
        GameObject simulatorObject = null;
        string loadedPath = null;
        foreach (var path in SimulatorPrefabPaths)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
                continue;

            simulatorObject = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            loadedPath = path;
            break;
        }

        if (simulatorObject == null)
        {
            Debug.LogError("Could not load the official XR simulator prefab. Import the XR Interaction Simulator sample from Package Manager, then run Tools > VR Preview > Create XR Simulator Preview Scene again.");
            return;
        }

        simulatorObject.name = "XR Simulator";
        Debug.Log($"Loaded official XR simulator prefab from {loadedPath}.");

        var interactionSimulator = simulatorObject.GetComponent<XRInteractionSimulator>();
        if (interactionSimulator != null)
        {
            var so = new SerializedObject(interactionSimulator);
            SetObjectReferenceIfPresent(so, "m_CameraTransform", head);
            SetObjectReferenceIfPresent(so, "m_LeftControllerTransform", leftController);
            SetObjectReferenceIfPresent(so, "m_RightControllerTransform", rightController);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        var deviceSimulator = simulatorObject.GetComponent<XRDeviceSimulator>();
        if (deviceSimulator != null)
        {
            var so = new SerializedObject(deviceSimulator);
            SetObjectReferenceIfPresent(so, "m_CameraTransform", head);
            so.ApplyModifiedPropertiesWithoutUndo();
        }
    }

    static void SetObjectReferenceIfPresent(SerializedObject so, string propertyName, Object value)
    {
        var property = so.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    static void CreateProjectionPanel(Camera eventCamera, CameraFovController fovController, GaussianSplatRenderer splat)
    {
        CreateEventSystem();

        var canvasObject = new GameObject("Projection Control Panel");

        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = eventCamera;
        canvas.sortingOrder = 20;
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();

        var rect = canvasObject.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(360.0f, 146.0f);
        rect.pivot = new Vector2(0.5f, 0.5f);

        var fixedPose = canvasObject.AddComponent<FixedProjectionPanelPose>();
        var fixedPoseSo = new SerializedObject(fixedPose);
        fixedPoseSo.FindProperty("targetCamera").objectReferenceValue = eventCamera;
        fixedPoseSo.ApplyModifiedPropertiesWithoutUndo();

        PopulateProjectionPanel(canvasObject, fovController, splat, "Drag with trigger or mouse; keys: , . fisheye   - = FOV");
    }

    static void CreateDesktopProjectionPanel(CameraFovController fovController, GaussianSplatRenderer splat)
    {
        var eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<InputSystemUIInputModule>();

        var canvasObject = new GameObject("Projection Control Panel");
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.ScreenSpaceOverlay;
        canvas.sortingOrder = 20;
        var canvasScaler = canvasObject.AddComponent<CanvasScaler>();
        canvasScaler.uiScaleMode = CanvasScaler.ScaleMode.ConstantPixelSize;
        canvasScaler.scaleFactor = 1.0f;
        canvasObject.AddComponent<GraphicRaycaster>();

        var panelObject = new GameObject("Panel");
        panelObject.transform.SetParent(canvasObject.transform, false);
        var panelRect = panelObject.AddComponent<RectTransform>();
        panelRect.anchorMin = new Vector2(0.5f, 0.18f);
        panelRect.anchorMax = new Vector2(0.5f, 0.18f);
        panelRect.pivot = new Vector2(0.5f, 0.5f);
        panelRect.anchoredPosition = Vector2.zero;
        panelRect.sizeDelta = new Vector2(360.0f, 146.0f);
        panelRect.localScale = Vector3.one * 0.65f;

        PopulateProjectionPanel(panelObject, fovController, splat, "Right mouse: look   WASD/QE: move   Shift: faster");
    }

    static void PopulateProjectionPanel(GameObject panelObject, CameraFovController fovController, GaussianSplatRenderer splat, string hintText)
    {
        var background = panelObject.AddComponent<Image>();
        background.color = new Color(0.02f, 0.025f, 0.03f, 0.62f);

        var title = CreateText("Title", panelObject.transform, "Projection", 17, TextAnchor.MiddleLeft);
        SetRect(title.rectTransform, new Vector2(16, -10), new Vector2(328, 24), new Vector2(0, 1), new Vector2(0, 1));

        var fovLabel = CreateText("FOV Label", panelObject.transform, "FOV", 13, TextAnchor.MiddleLeft);
        SetRect(fovLabel.rectTransform, new Vector2(16, -48), new Vector2(58, 20), new Vector2(0, 1), new Vector2(0, 1));
        var fovValue = CreateText("FOV Value", panelObject.transform, "", 13, TextAnchor.MiddleRight);
        SetRect(fovValue.rectTransform, new Vector2(292, -48), new Vector2(44, 20), new Vector2(0, 1), new Vector2(0, 1));
        var fovSlider = CreateSlider("FOV Slider", panelObject.transform, 20.0f, 360.0f, 60.0f);
        SetRect((RectTransform)fovSlider.transform, new Vector2(78, -48), new Vector2(202, 20), new Vector2(0, 1), new Vector2(0, 1));

        var fisheyeLabel = CreateText("Fisheye Label", panelObject.transform, "Fisheye", 13, TextAnchor.MiddleLeft);
        SetRect(fisheyeLabel.rectTransform, new Vector2(16, -84), new Vector2(58, 20), new Vector2(0, 1), new Vector2(0, 1));
        var fisheyeValue = CreateText("Fisheye Value", panelObject.transform, "", 13, TextAnchor.MiddleRight);
        SetRect(fisheyeValue.rectTransform, new Vector2(292, -84), new Vector2(44, 20), new Vector2(0, 1), new Vector2(0, 1));
        var fisheyeSlider = CreateSlider("Fisheye Slider", panelObject.transform, 0.0f, 1.0f, 0.0f);
        SetRect((RectTransform)fisheyeSlider.transform, new Vector2(78, -84), new Vector2(202, 20), new Vector2(0, 1), new Vector2(0, 1));

        var hint = CreateText("Hint", panelObject.transform, hintText, 12, TextAnchor.MiddleLeft);
        hint.color = new Color(0.78f, 0.82f, 0.86f, 0.82f);
        SetRect(hint.rectTransform, new Vector2(16, -116), new Vector2(328, 18), new Vector2(0, 1), new Vector2(0, 1));

        var panel = panelObject.AddComponent<ProjectionControlPanel>();
        var panelSo = new SerializedObject(panel);
        panelSo.FindProperty("fovController").objectReferenceValue = fovController;
        panelSo.FindProperty("splat").objectReferenceValue = splat;
        panelSo.FindProperty("fovSlider").objectReferenceValue = fovSlider;
        panelSo.FindProperty("fisheyeSlider").objectReferenceValue = fisheyeSlider;
        panelSo.FindProperty("fovValueText").objectReferenceValue = fovValue;
        panelSo.FindProperty("fisheyeValueText").objectReferenceValue = fisheyeValue;
        panelSo.ApplyModifiedPropertiesWithoutUndo();
    }

    static void CreateEventSystem()
    {
        var eventSystemObject = new GameObject("EventSystem");
        eventSystemObject.AddComponent<EventSystem>();
        eventSystemObject.AddComponent<XRUIInputModule>();
    }

    static Text CreateText(string name, Transform parent, string text, int fontSize, TextAnchor anchor)
    {
        var textObject = new GameObject(name);
        textObject.transform.SetParent(parent, false);
        var uiText = textObject.AddComponent<Text>();
        uiText.text = text;
        uiText.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf") ?? Resources.GetBuiltinResource<Font>("Arial.ttf");
        uiText.fontSize = fontSize;
        uiText.alignment = anchor;
        uiText.color = new Color(0.9f, 0.94f, 0.96f, 0.95f);
        uiText.raycastTarget = false;
        return uiText;
    }

    static Slider CreateSlider(string name, Transform parent, float minValue, float maxValue, float value)
    {
        var sliderObject = new GameObject(name);
        sliderObject.transform.SetParent(parent, false);
        var sliderRect = sliderObject.AddComponent<RectTransform>();

        var background = CreateImage("Background", sliderObject.transform, new Color(0.18f, 0.2f, 0.22f, 0.75f));
        SetStretch(background.rectTransform, new Vector2(0, 8), new Vector2(0, -8));

        var fillArea = new GameObject("Fill Area");
        fillArea.transform.SetParent(sliderObject.transform, false);
        var fillAreaRect = fillArea.AddComponent<RectTransform>();
        SetStretch(fillAreaRect, new Vector2(5, 8), new Vector2(-5, -8));

        var fill = CreateImage("Fill", fillArea.transform, new Color(0.36f, 0.38f, 0.41f, 0.72f));
        SetStretch(fill.rectTransform, Vector2.zero, Vector2.zero);

        var handleArea = new GameObject("Handle Slide Area");
        handleArea.transform.SetParent(sliderObject.transform, false);
        var handleAreaRect = handleArea.AddComponent<RectTransform>();
        SetStretch(handleAreaRect, new Vector2(8, 0), new Vector2(-8, 0));

        var handle = CreateImage("Handle", handleArea.transform, new Color(0.92f, 0.95f, 0.98f, 0.95f));
        handle.rectTransform.sizeDelta = new Vector2(14, 24);

        var slider = sliderObject.AddComponent<Slider>();
        slider.minValue = minValue;
        slider.maxValue = maxValue;
        slider.value = value;
        slider.direction = Slider.Direction.LeftToRight;
        slider.fillRect = fill.rectTransform;
        slider.handleRect = handle.rectTransform;
        slider.targetGraphic = handle;

        return slider;
    }

    static Image CreateImage(string name, Transform parent, Color color)
    {
        var imageObject = new GameObject(name);
        imageObject.transform.SetParent(parent, false);
        var image = imageObject.AddComponent<Image>();
        image.color = color;
        return image;
    }

    static void SetRect(RectTransform rect, Vector2 anchoredPosition, Vector2 size, Vector2 anchorMin, Vector2 anchorMax)
    {
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.pivot = new Vector2(0, 1);
        rect.anchoredPosition = anchoredPosition;
        rect.sizeDelta = size;
    }

    static void SetStretch(RectTransform rect, Vector2 offsetMin, Vector2 offsetMax)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = offsetMin;
        rect.offsetMax = offsetMax;
    }

    static void ApplyProjectSettings()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });
        AssetDatabase.SaveAssets();
    }

    static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = new System.Collections.Generic.List<EditorBuildSettingsScene>
        {
            new(scenePath, true)
        };

        foreach (var scene in EditorBuildSettings.scenes)
        {
            if (scene.path == scenePath)
                continue;

            scenes.Add(scene);
        }

        EditorBuildSettings.scenes = scenes.ToArray();
    }
}
