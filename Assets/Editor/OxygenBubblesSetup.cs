using System;
using System.IO;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;

internal static class OxygenBubblesSetup
{
    private const string Root = "Assets/OxygenPickups";
    public const string PrefabPath = Root + "/Prefabs/OxygenTrackBubble.prefab";
    public const string ScenePath = "Assets/Scenes/TreadmillStraightTest.unity";

    public static GameObject EnsurePrefab()
    {
        var existing = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (existing != null) return existing;
        var model = AssetDatabase.LoadAssetAtPath<GameObject>(Root + "/Models/OxygenBubble.fbx");
        var shellShader = Shader.Find("OxygenPickups/BlueBubble");
        var fxShader = Shader.Find("OxygenPickups/BlueFX");
        if (model == null || shellShader == null || fxShader == null)
            throw new InvalidOperationException("Import the OxygenPickups model and shaders first.");
        Directory.CreateDirectory(Root + "/Materials");
        Directory.CreateDirectory(Root + "/Prefabs");
        AssetDatabase.Refresh();
        var blue = Material("Oxygen_Blue", shellShader, new Color(.015f,.065f,.65f), new Color(.08f,.4f,1));
        var rim = Material("Oxygen_Rim", shellShader, new Color(.06f,.3f,.9f), new Color(.25f,.65f,1));
        var pearl = Material("Oxygen_Pearl", shellShader, new Color(.7f,.88f,1), Color.white);
        var fx = Material("Oxygen_Trail", fxShader, new Color(.2f,.5f,1,1), Color.white);
        var root = new GameObject("OxygenTrackBubble");
        try
        {
            root.transform.localScale = Vector3.one * .3f; // Match the existing 24 cm pickups.
            var visual = UnityEngine.Object.Instantiate(model, root.transform);
            visual.name = "Blue O2 Bubble";
            visual.transform.localPosition = Vector3.zero;
            visual.transform.localRotation = Quaternion.identity;
            visual.transform.localScale = Vector3.one;
            foreach (var renderer in visual.GetComponentsInChildren<MeshRenderer>())
            {
                var materials = renderer.sharedMaterials;
                for (int i = 0; i < materials.Length; i++)
                {
                    string name = materials[i] != null ? materials[i].name : "";
                    materials[i] = name.Contains("Pearl") ? pearl : name.Contains("Rim") ? rim : blue;
                }
                renderer.sharedMaterials = materials;
                renderer.shadowCastingMode = ShadowCastingMode.Off;
                renderer.receiveShadows = false;
            }
            var trailObject = new GameObject("Blue flight trail");
            trailObject.transform.SetParent(root.transform, false);
            var trail = trailObject.AddComponent<TrailRenderer>();
            trail.sharedMaterial = fx;
            trail.time = .18f;
            trail.minVertexDistance = .018f;
            trail.widthCurve = AnimationCurve.Linear(0, .025f, 1, 0);
            var gradient = new Gradient();
            gradient.SetKeys(new[] { new GradientColorKey(new Color(.3f,.65f,1),0), new GradientColorKey(new Color(.02f,.12f,1),1) },
                new[] { new GradientAlphaKey(.8f,0), new GradientAlphaKey(0,1) });
            trail.colorGradient = gradient;
            trail.emitting = false;
            trail.numCornerVertices = 2;
            trail.numCapVertices = 2;
            trail.shadowCastingMode = ShadowCastingMode.Off;
            trail.receiveShadows = false;
            var behavior = root.AddComponent<TrackOxygenBubbleVisual>();
            var serialized = new SerializedObject(behavior);
            serialized.FindProperty("model").objectReferenceValue = visual.transform;
            serialized.FindProperty("flightTrail").objectReferenceValue = trail;
            serialized.FindProperty("collectionMaterial").objectReferenceValue = fx;
            serialized.ApplyModifiedPropertiesWithoutUndo();
            return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
        }
        finally { UnityEngine.Object.DestroyImmediate(root); }
    }

    private static Material Material(string name, Shader shader, Color color, Color rim)
    {
        string path = Root + "/Materials/" + name + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(path);
        if (material != null) return material;
        material = new Material(shader) { name = name, enableInstancing = true };
        if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
        if (material.HasProperty("_RimColor")) material.SetColor("_RimColor", rim);
        if (material.HasProperty("_Tint")) material.SetColor("_Tint", color);
        AssetDatabase.CreateAsset(material, path);
        return material;
    }

    [MenuItem("Tools/Treadmill/Install blue oxygen bubbles")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode first.");
        var prefab = EnsurePrefab();
        var scene = EditorSceneManager.GetActiveScene();
        if (scene.path != ScenePath) scene = EditorSceneManager.OpenScene(ScenePath);
        int count = 0;
        foreach (var root in scene.GetRootGameObjects())
        foreach (var pickups in root.GetComponentsInChildren<TrackCollectibles>(true))
        {
            var data = new SerializedObject(pickups);
            data.FindProperty("collectiblePrefab").objectReferenceValue = prefab;
            data.ApplyModifiedProperties();
            count++;
        }
        if (count == 0) throw new InvalidOperationException("No TrackCollectibles found in the running scene.");
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        Selection.activeObject = prefab;
        Debug.Log("[OxygenBubbles] Installed in " + count + " track collectible system(s). Spawn settings and events preserved.");
    }

    // Entry point for a GUI Editor launched against this project.
    public static void InstallAndVerify()
    {
        Install();
        TrackCollectiblesChecks.Verify();
        OxygenBubblesChecks.Verify();
        OxygenBubblesChecks.RenderPreview();
        Directory.CreateDirectory("Temp/OxygenBubbleIntegration");
        File.WriteAllText("Temp/OxygenBubbleIntegration/result.txt", "PASS: installed, existing collectible regressions, blue bubble checks and URP preview.\n");
        OxygenBubblesPlayCheck.Begin();
    }
}
