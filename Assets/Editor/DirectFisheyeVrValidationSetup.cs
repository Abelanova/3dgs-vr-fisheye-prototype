using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

public static class DirectFisheyeVrValidationSetup
{
    const string TemplateScenePath = "Assets/Scenes/XRSimulatorTemplate.unity";

    [MenuItem("Tools/VR Preview/Configure Direct Fisheye VR Validation")]
    public static void ConfigureTemplateScene()
    {
        var scene = EditorSceneManager.OpenScene(TemplateScenePath, OpenSceneMode.Single);
        ConfigureOpenScene();
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("Configured XRSimulatorTemplate for direct covariance fisheye VR validation.");
    }

    [MenuItem("Tools/VR Preview/Configure Open Scene For Direct Fisheye VR")]
    public static void ConfigureOpenSceneMenu()
    {
        ConfigureOpenScene();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        Debug.Log("Configured the open scene for direct covariance fisheye VR validation.");
    }

    public static void ConfigureOpenScene()
    {
        Camera camera = Camera.main != null ? Camera.main : FindFirst<Camera>();
        GaussianSplatRenderer splat = FindFirst<GaussianSplatRenderer>();
        CameraFovController fovController = camera != null ? camera.GetComponent<CameraFovController>() : FindFirst<CameraFovController>();

        ConfigureSplat(splat);
        ConfigureFovController(fovController, camera, splat);
        ConfigureProjectionController(camera, fovController, splat);
        ConfigureDiagnostics(camera, splat);
        DisableCubemapFisheye();

        EditorSceneManager.MarkAllScenesDirty();
        AssetDatabase.SaveAssets();
    }

    static void ConfigureSplat(GaussianSplatRenderer splat)
    {
        if (splat == null)
        {
            Debug.LogWarning("No GaussianSplatRenderer found while configuring direct fisheye VR validation.");
            return;
        }

        Undo.RecordObject(splat, "Configure Direct Fisheye Splat");
        splat.m_SortNthFrame = 1;
        splat.m_FisheyeFieldOfView = Mathf.Max(splat.m_FisheyeFieldOfView, 120.0f);
        splat.m_FisheyeStrength = Mathf.Max(splat.m_FisheyeStrength, 0.45f);
        splat.m_StereoIpdMeters = 0.064f;
        splat.m_StereoConvergenceDistance = 2.0f;
        splat.m_StereoScale = 0.25f;
        splat.m_StereoRadialCompression = 2.0f;
        splat.m_StereoMaxShift = 0.004f;
        splat.m_MinPixelSize = Mathf.Max(splat.m_MinPixelSize, 0.75f);
        EditorUtility.SetDirty(splat);
    }

    static void ConfigureFovController(CameraFovController fovController, Camera camera, GaussianSplatRenderer splat)
    {
        if (fovController == null)
            return;

        var so = new SerializedObject(fovController);
        SetObjectReferenceIfPresent(so, "targetCamera", camera);
        SetObjectReferenceIfPresent(so, "targetSplat", splat);
        SetFloatIfPresent(so, "verticalFov", 120.0f);
        SetFloatIfPresent(so, "cameraFovLimit", 140.0f);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureProjectionController(Camera camera, CameraFovController fovController, GaussianSplatRenderer splat)
    {
        if (camera == null)
            return;

        var controller = camera.GetComponent<XRProjectionController>();
        if (controller == null)
            controller = Undo.AddComponent<XRProjectionController>(camera.gameObject);

        var so = new SerializedObject(controller);
        SetObjectReferenceIfPresent(so, "fovController", fovController);
        SetObjectReferenceIfPresent(so, "splat", splat);
        SetObjectReferenceIfPresent(so, "xrCamera", camera);
        SetObjectReferenceIfPresent(so, "rigRoot", RootOf(camera.transform));
        SetFloatIfPresent(so, "defaultFov", 120.0f);
        SetFloatIfPresent(so, "defaultFisheye", 0.45f);
        SetBoolIfPresent(so, "applyDefaultsOnStart", true);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void ConfigureDiagnostics(Camera camera, GaussianSplatRenderer splat)
    {
        if (camera == null)
        {
            Debug.LogWarning("No main camera found while configuring direct fisheye VR diagnostics.");
            return;
        }

        var diagnostics = camera.GetComponent<DirectFisheyeVrDiagnostics>();
        if (diagnostics == null)
            diagnostics = Undo.AddComponent<DirectFisheyeVrDiagnostics>(camera.gameObject);

        var so = new SerializedObject(diagnostics);
        SetObjectReferenceIfPresent(so, "targetCamera", camera);
        SetObjectReferenceIfPresent(so, "targetSplat", splat);
        SetBoolIfPresent(so, "showOverlay", true);
        SetBoolIfPresent(so, "logWarnings", true);
        SetFloatIfPresent(so, "minExpectedIpdMeters", 0.01f);
        SetFloatIfPresent(so, "staticPoseWarningSeconds", 2.0f);
        SetFloatIfPresent(so, "stretchWarningRatio", 8.0f);
        so.ApplyModifiedPropertiesWithoutUndo();
    }

    static void DisableCubemapFisheye()
    {
        var cubemapComponents = Object.FindObjectsByType<VrHighQualityFisheye>(
            FindObjectsInactive.Include, FindObjectsSortMode.None);
        foreach (var component in cubemapComponents)
        {
            if (!component.enabled)
                continue;

            Undo.RecordObject(component, "Disable Cubemap Fisheye");
            component.enabled = false;
            EditorUtility.SetDirty(component);
        }
    }

    static Transform RootOf(Transform transform)
    {
        Transform root = transform;
        while (root.parent != null)
            root = root.parent;
        return root;
    }

    static T FindFirst<T>() where T : Object
    {
        var objects = Object.FindObjectsByType<T>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
        return objects.Length > 0 ? objects[0] : null;
    }

    static void SetObjectReferenceIfPresent(SerializedObject so, string propertyName, Object value)
    {
        var property = so.FindProperty(propertyName);
        if (property != null)
            property.objectReferenceValue = value;
    }

    static void SetFloatIfPresent(SerializedObject so, string propertyName, float value)
    {
        var property = so.FindProperty(propertyName);
        if (property != null)
            property.floatValue = value;
    }

    static void SetBoolIfPresent(SerializedObject so, string propertyName, bool value)
    {
        var property = so.FindProperty(propertyName);
        if (property != null)
            property.boolValue = value;
    }
}
