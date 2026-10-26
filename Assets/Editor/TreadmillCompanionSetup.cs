using System;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class TreadmillCompanionSetup
{
    [MenuItem("Tools/Treadmill/Add running companion")]
    public static void ConfigureScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode first.");
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Jogger.prefab");
        if (world == null || prefab == null) throw new InvalidOperationException("Treadmill world or Jogger prefab missing.");
        var companion = UnityEngine.Object.FindFirstObjectByType<TreadmillCompanion>(FindObjectsInactive.Include);
        if (companion == null) companion = new GameObject("TreadmillCompanion").AddComponent<TreadmillCompanion>();
        companion.gameObject.SetActive(true);
        companion.transform.SetParent(null, true);
        var data = new SerializedObject(companion);
        data.FindProperty("world").objectReferenceValue = world;
        data.FindProperty("avatarPrefab").objectReferenceValue = prefab;
        data.FindProperty("sideOffset").vector3Value = new Vector3(1.22f, 0, 0);
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(world.gameObject.scene);
        EditorSceneManager.SaveScene(world.gameObject.scene);
        Selection.activeGameObject = companion.gameObject;
        Debug.Log("[Treadmill] Companion configured 1.22 m to the right; no marker scan required.");
    }

    [MenuItem("Tools/Treadmill/Verify companion in Play mode")]
    public static void Verify()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        var companion = UnityEngine.Object.FindFirstObjectByType<TreadmillCompanion>();
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        Check(companion != null && world != null && world.PlacementReady, "companion and world ready");
        float originalSpeed = world.SpeedKmh;
        try
        {
            companion.Synchronize();
            var avatar = companion.Avatar;
            Check(avatar != null && avatar.activeInHierarchy && companion.Animator != null, "animated avatar spawned");
            Vector3 relative = Quaternion.Inverse(world.RunnerRotation) * (companion.GroundPosition - world.RunnerGroundPosition);
            Check(Vector3.Distance(relative, new Vector3(1.22f, 0, 0)) < 0.0001f, "neighbouring lane offset");
            Check(!companion.transform.IsChildOf(world.transform), "companion outside moving scenery");
            Check(!companion.Animator.applyRootMotion, "root motion cannot cause drift");
            world.SpeedKmh = 12;
            companion.Synchronize();
            Check(Math.Abs(companion.AppliedSpeedMetresPerSecond - 12f / 3.6f) < 0.0001f, "same treadmill speed");
            Check(companion.Animator.GetBool("isRunning") && companion.Animator.GetFloat("speed") > 0, "locomotion parameters follow speed");
            Vector3 before = avatar.transform.position;
            world.AdvanceSimulation(25f); // Cross bends without an independent companion clock.
            companion.Synchronize();
            Check(Vector3.Distance(before, avatar.transform.position) < 0.0001f, "stays alongside through curves");
            Check(Quaternion.Angle(avatar.transform.rotation, world.RunnerRotation) < 0.01f, "same running heading");
            world.SpeedKmh = 0;
            companion.Synchronize();
            Check(!companion.Animator.GetBool("isRunning") && companion.AppliedSpeedMetresPerSecond == 0,
                "zero speed stops companion");
            companion.Synchronize();
            Check(companion.Avatar == avatar && companion.transform.childCount == 1, "one companion only");
            Debug.Log("[Treadmill] PASS: companion spawn, neighbouring lane, shared speed, curves, animation and pause.");
        }
        finally { world.SpeedKmh = originalSpeed; companion.Synchronize(); }
    }

    private static void Check(bool value, string label)
    {
        if (!value) throw new InvalidOperationException("[Treadmill companion] FAIL: " + label);
    }
}
