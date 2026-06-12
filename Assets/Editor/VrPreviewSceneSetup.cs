using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

public static class VrPreviewSceneSetup
{
    const string ScenePath = "Assets/Scenes/VRPreview.unity";

    static readonly Vector3 OriginPosition = new(0.0f, 0.0f, -7.0f);
    static readonly Vector3 EyeLocalPosition = new(0.0f, 1.6f, 0.0f);
    static readonly Vector3 LookTarget = new(0.0f, 1.4f, 0.0f);

    [MenuItem("Tools/VR Preview/Create 3DGS VR Preview Scene")]
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
        scene.name = "VRPreview";

        var origin = new GameObject("VR Preview Origin");
        origin.transform.position = OriginPosition;
        origin.transform.rotation = Quaternion.identity;

        var cameraObject = new GameObject("Main Camera");
        cameraObject.tag = "MainCamera";
        cameraObject.transform.SetParent(origin.transform, false);
        cameraObject.transform.localPosition = EyeLocalPosition;

        var lookDirection = (LookTarget - cameraObject.transform.position).normalized;
        cameraObject.transform.rotation = Quaternion.LookRotation(lookDirection, Vector3.up);

        var camera = cameraObject.AddComponent<Camera>();
        camera.gameObject.tag = "MainCamera";
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.02f, 0.025f, 0.03f, 1);
        camera.nearClipPlane = 0.03f;
        camera.farClipPlane = 500.0f;
        camera.fieldOfView = 60.0f;

        var fovController = camera.gameObject.GetComponent<CameraFovController>() ?? camera.gameObject.AddComponent<CameraFovController>();
        var fovControllerSo = new SerializedObject(fovController);
        fovControllerSo.FindProperty("targetCamera").objectReferenceValue = camera;
        fovControllerSo.FindProperty("verticalFov").floatValue = camera.fieldOfView;
        fovControllerSo.ApplyModifiedPropertiesWithoutUndo();

        var controller = origin.AddComponent<DesktopVrPreviewController>();
        var controllerSo = new SerializedObject(controller);
        controllerSo.FindProperty("cameraTransform").objectReferenceValue = cameraObject.transform;
        controllerSo.ApplyModifiedPropertiesWithoutUndo();

        var splatObject = new GameObject("ChristmasTree Gaussian Splat");
        var splat = splatObject.AddComponent<GaussianSplatRenderer>();
        splat.m_Asset = asset;
        splat.m_SplatScale = 0.75f;
        splat.m_OpacityScale = 1.0f;
        splat.m_SHOrder = 3;
        splat.m_SortNthFrame = 1;
        splat.m_FisheyeStrength = 0.0f;
        splat.m_NearFadeDistance = 0.8f;

        var lightObject = new GameObject("Directional Light");
        var light = lightObject.AddComponent<Light>();
        light.type = LightType.Directional;
        light.intensity = 1.0f;
        lightObject.transform.rotation = Quaternion.Euler(50.0f, -30.0f, 0.0f);

        EditorSceneManager.SaveScene(scene, ScenePath);
        EditorSceneManager.OpenScene(ScenePath);
        AddSceneToBuildSettings(ScenePath);

        Debug.Log("Created Assets/Scenes/VRPreview.unity. Hold right mouse to look, use WASD to move, Q/E vertical, Shift fast.");
    }

    static void ApplyProjectSettings()
    {
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.StandaloneWindows64, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.StandaloneWindows64, new[] { GraphicsDeviceType.Direct3D12 });
        AssetDatabase.SaveAssets();
    }

    static void AddSceneToBuildSettings(string scenePath)
    {
        var scenes = EditorBuildSettings.scenes;
        foreach (var scene in scenes)
        {
            if (scene.path == scenePath)
                return;
        }

        System.Array.Resize(ref scenes, scenes.Length + 1);
        scenes[^1] = new EditorBuildSettingsScene(scenePath, true);
        EditorBuildSettings.scenes = scenes;
    }
}
