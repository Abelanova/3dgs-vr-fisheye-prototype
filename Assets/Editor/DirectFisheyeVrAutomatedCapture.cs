using GaussianSplatting.Runtime;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

[InitializeOnLoad]
public static class DirectFisheyeVrAutomatedCapture
{
    const string TemplateScenePath = "Assets/Scenes/XRSimulatorTemplate.unity";
    const string OutputDirectory = "Recordings/DirectFisheyeVrValidation";
    const string PendingCaptureKey = "DirectFisheyeVrAutomatedCapture.Pending";

    static DirectFisheyeVrAutomatedCapture()
    {
        EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
        EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
    }

    [MenuItem("Tools/VR Preview/Capture Direct Fisheye VR Validation")]
    public static void RunFromMenu()
    {
        Run();
    }

    public static void Run()
    {
        EditorSceneManager.OpenScene(TemplateScenePath, OpenSceneMode.Single);
        DirectFisheyeVrValidationSetup.ConfigureOpenScene();

        SessionState.SetBool(PendingCaptureKey, true);
        Debug.Log($"Starting direct fisheye VR automated capture. Output: {OutputDirectory}");
        EditorApplication.isPlaying = true;
    }

    static void OnPlayModeStateChanged(PlayModeStateChange state)
    {
        if (state != PlayModeStateChange.EnteredPlayMode ||
            !SessionState.GetBool(PendingCaptureKey, false))
            return;

        SessionState.SetBool(PendingCaptureKey, false);
        CreateRuntimeDriver();
    }

    static void CreateRuntimeDriver()
    {
        Camera camera = Camera.main != null ? Camera.main : Object.FindFirstObjectByType<Camera>();
        GaussianSplatRenderer splat = Object.FindFirstObjectByType<GaussianSplatRenderer>();
        CameraFovController fovController = camera != null ? camera.GetComponent<CameraFovController>() : null;
        if (camera == null || splat == null)
        {
            Debug.LogError("Direct fisheye VR automated capture requires a camera and GaussianSplatRenderer.");
            EditorApplication.Exit(1);
            return;
        }

        var driverObject = new GameObject("__DirectFisheyeVrCaptureDriver");
        var driver = driverObject.AddComponent<DirectFisheyeVrCaptureDriver>();
        var so = new SerializedObject(driver);
        so.FindProperty("targetCamera").objectReferenceValue = camera;
        so.FindProperty("targetSplat").objectReferenceValue = splat;
        so.FindProperty("fovController").objectReferenceValue = fovController;
        so.FindProperty("outputDirectory").stringValue = OutputDirectory;
        so.ApplyModifiedPropertiesWithoutUndo();
        Debug.Log("Direct fisheye VR capture driver created in Play Mode.");
    }
}
