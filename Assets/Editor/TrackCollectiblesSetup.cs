using System;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class TrackCollectiblesSetup
{
    private const string Folder = "Assets/TreadmillPrototype";
    public const string PrefabPath = OxygenBubblesSetup.PrefabPath;

    [MenuItem("Tools/Treadmill/Add floating blue collectibles")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        if (world == null) throw new InvalidOperationException("Open the treadmill scene first.");
        if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets", "TreadmillPrototype");
        var prefab = OxygenBubblesSetup.EnsurePrefab();
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
