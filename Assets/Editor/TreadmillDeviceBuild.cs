using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;

internal static class TreadmillDeviceBuild
{
    [MenuItem("Tools/Treadmill/Build device verification APK")]
    public static void Build()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Stop Play mode first.");
        EditorApplication.delayCall += () =>
        {
            Directory.CreateDirectory("Build");
            Directory.CreateDirectory("Temp/TreadmillPreviews");
            try
            {
                var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
                {
                    scenes = new[] { "Assets/Scenes/TreadmillStraightTest.unity" },
                    locationPathName = "Build/TreadmillMenuFix.apk",
                    target = BuildTarget.Android,
                    options = BuildOptions.Development
                });
                File.WriteAllText("Temp/TreadmillPreviews/device-build-result.txt", report.summary.result.ToString());
                if (report.summary.result != BuildResult.Succeeded)
                    throw new InvalidOperationException("Treadmill device build failed: " + report.summary.result);
                UnityEngine.Debug.Log("[Treadmill] Full device APK ready: Build/TreadmillMenuFix.apk");
            }
            catch (Exception error)
            {
                File.WriteAllText("Temp/TreadmillPreviews/device-build-result.txt", error.ToString());
                UnityEngine.Debug.LogException(error);
            }
        };
    }
}
