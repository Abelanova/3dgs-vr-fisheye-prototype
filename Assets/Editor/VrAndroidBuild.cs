using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

public static class VrAndroidBuild
{
    const string DefaultOutput = "Builds/Android/3dgs-vr-fisheye.apk";

    [MenuItem("Tools/VR Preview/Build Android APK")]
    public static void BuildAndroidApkMenu()
    {
        BuildAndroidApk();
    }

    public static void BuildAndroidApk()
    {
        var outputPath = Environment.GetEnvironmentVariable("CODEX_ANDROID_APK");
        if (string.IsNullOrWhiteSpace(outputPath))
            outputPath = DefaultOutput;

        if (!Path.IsPathRooted(outputPath))
            outputPath = Path.Combine(Path.GetDirectoryName(UnityEngine.Application.dataPath), outputPath);
        outputPath = Path.GetFullPath(outputPath);
        Directory.CreateDirectory(Path.GetDirectoryName(outputPath));

        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
        EditorUserBuildSettings.buildAppBundle = false;
        EditorUserBuildSettings.exportAsGoogleAndroidProject = false;

        var options = new BuildPlayerOptions
        {
            scenes = GetEnabledScenes(),
            locationPathName = outputPath,
            target = BuildTarget.Android,
            options = BuildOptions.None
        };

        var report = BuildPipeline.BuildPlayer(options);
        if (report.summary.result != BuildResult.Succeeded)
            throw new InvalidOperationException($"Android APK build failed: {report.summary.result}");

        UnityEngine.Debug.Log($"Android APK build succeeded: {outputPath}");
    }

    static string[] GetEnabledScenes()
    {
        return Array.ConvertAll(
            Array.FindAll(EditorBuildSettings.scenes, scene => scene.enabled),
            scene => scene.path);
    }
}
