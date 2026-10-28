using System;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class TrackCollectiblesSetup
{
    private const string Folder = "Assets/TreadmillPrototype";
    public const string PrefabPath = Folder + "/BlueTrackSphere.prefab";

    [MenuItem("Tools/Treadmill/Add floating blue collectibles")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        if (world == null) throw new InvalidOperationException("Open the treadmill scene first.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "TreadmillPrototype");
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
        if (prefab == null)
        {
            const string materialPath = Folder + "/BlueTrackSphere.mat";
            var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit shader missing.");
                material = new Material(shader) { name = "BlueTrackSphere" };
                material.SetColor("_BaseColor", new Color(.025f, .32f, 1f));
                material.SetFloat("_Smoothness", .8f);
                material.SetFloat("_Metallic", .25f);
                material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.None;
                material.EnableKeyword("_EMISSION");
                material.SetColor("_EmissionColor", new Color(.01f, .12f, .65f));
                AssetDatabase.CreateAsset(material, materialPath);
            }
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            sphere.name = "BlueTrackSphere";
            sphere.transform.localScale = Vector3.one * .24f;
            UnityEngine.Object.DestroyImmediate(sphere.GetComponent<Collider>());
            var renderer = sphere.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            prefab = PrefabUtility.SaveAsPrefabAsset(sphere, PrefabPath);
            UnityEngine.Object.DestroyImmediate(sphere);
        }
        var pickups = world.GetComponent<TrackCollectibles>();
        if (pickups == null) pickups = Undo.AddComponent<TrackCollectibles>(world.gameObject);
        var data = new SerializedObject(pickups);
        data.FindProperty("world").objectReferenceValue = world;
        if (data.FindProperty("collectiblePrefab").objectReferenceValue == null)
            data.FindProperty("collectiblePrefab").objectReferenceValue = prefab;
        data.ApplyModifiedProperties();
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        EditorSceneManager.SaveScene(world.gameObject.scene);
        AssetDatabase.SaveAssets();
        Selection.activeGameObject = world.gameObject;
        Debug.Log("[TrackCollectibles] Installed. Replace Collectible Prefab on VirtualWorld with any 3D prefab.");
    }
}
