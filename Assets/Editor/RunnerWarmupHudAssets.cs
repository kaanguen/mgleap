using System;
using StadionTracking;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

// A Resources material keeps the stereo overlay shader in Android builds, including shader stripping.
[InitializeOnLoad]
internal sealed class RunnerWarmupHudAssets : IPreprocessBuildWithReport
{
    private const string Path = "Assets/Resources/RunnerUI/WarmupText.mat";
    public int callbackOrder => 0;
    static RunnerWarmupHudAssets() { EditorApplication.delayCall += Ensure; }
    public void OnPreprocessBuild(BuildReport report) => Ensure();
    public static void Ensure()
    {
        var library = Resources.Load<RunnerAvatarAnimationLibrary>(RunnerAvatarBehaviour.LibraryResource);
        bool changed = false;
        if (library != null) foreach (var entry in library.animations)
        {
            if (entry == null) continue;
            var name = RunnerAnimationNames.For(entry.id, entry.label);
            if (entry.label == name) continue;
            entry.label = name; changed = true;
        }
        if (changed) { EditorUtility.SetDirty(library); AssetDatabase.SaveAssets(); }
        if (AssetDatabase.LoadAssetAtPath<Material>(Path) != null) return;
        var font = RunnerUiTheme.Font;
        var shader = AssetDatabase.LoadAssetAtPath<Shader>("Assets/TextMesh Pro/Shaders/TMP_SDF-Mobile Overlay.shader");
        if (font == null || shader == null) throw new InvalidOperationException("Warm-up HUD font or overlay shader missing.");
        var material = new Material(font.material) { name = "WarmupText", shader = shader, renderQueue = 4000 };
        AssetDatabase.CreateAsset(material, Path);
        AssetDatabase.SaveAssets();
    }
}
