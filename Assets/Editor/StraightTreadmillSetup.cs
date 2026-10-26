using System;
using System.Collections.Generic;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class StraightTreadmillSetup
{
    private const string ScenePath = "Assets/Scenes/TreadmillStraightTest.unity";
    private const string AssetFolder = "Assets/TreadmillPrototype";

    [MenuItem("Tools/Treadmill/Create straight test scene")]
    public static void CreateScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            throw new InvalidOperationException("Exit Play mode before creating the test scene.");
        if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            throw new InvalidOperationException("The treadmill test scene already exists; open it instead.");
        if (SceneManager.GetActiveScene().isDirty)
            throw new InvalidOperationException("Save the current scene before creating the test scene.");

        // Copy the saved ML rig and project-specific input setup without changing SampleScene.
        if (!EditorSceneManager.SaveScene(SceneManager.GetActiveScene(), ScenePath, true))
            throw new InvalidOperationException("Could not copy the current scene.");
        Scene scene = EditorSceneManager.OpenScene(ScenePath);
        foreach (string name in new[] { "MarkerAndAnchorManager", "RunnerStatsCanvas", "HeartRateCanvas" })
        {
            GameObject obj = GameObject.Find(name);
            if (obj != null) obj.SetActive(false);
        }

        if (!AssetDatabase.IsValidFolder(AssetFolder)) AssetDatabase.CreateFolder("Assets", "TreadmillPrototype");
        Shader shader = Resources.Load<Shader>("StadiumRoute");
        if (shader == null) throw new InvalidOperationException("The XR route shader is missing.");
        var material = new Material(shader) { name = "TreadmillUnlit" };
        AssetDatabase.CreateAsset(material, AssetFolder + "/TreadmillUnlit.mat");
        Mesh mesh = CreateTile();
        AssetDatabase.CreateAsset(mesh, AssetFolder + "/StraightTrackTile.asset");

        var world = new GameObject("VirtualWorld");
        var controller = world.AddComponent<StraightTreadmillWorld>();
        var serialized = new SerializedObject(controller);
        serialized.FindProperty("headCamera").objectReferenceValue = Camera.main;
        serialized.ApplyModifiedPropertiesWithoutUndo();
        // One tile behind the viewer, five ahead. Tiles repeat every 20 metres.
        for (int i = -1; i < 5; i++)
        {
            var tile = new GameObject("Track tile " + (i + 2));
            tile.transform.SetParent(world.transform, false);
            tile.transform.localPosition = Vector3.forward * (i * 20f);
            tile.AddComponent<MeshFilter>().sharedMesh = mesh;
            tile.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        Selection.activeGameObject = world;
        AddHeightSetup(controller);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        SceneView.lastActiveSceneView?.LookAt(new Vector3(0f, 0f, 12f), Quaternion.Euler(40f, 0f, 0f), 22f);
        Debug.Log("[Treadmill] Test scene ready. Select VirtualWorld, enter Play mode, then set Speed Kmh to 6. " +
            "Surface Height sets the belt height. The tracked ML camera is outside VirtualWorld.");
    }

    [MenuItem("Tools/Treadmill/Add height setup to test scene")]
    public static void AddHeightSetupToScene()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath)
            throw new InvalidOperationException("Open the treadmill test scene outside Play mode first.");
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        if (world == null) throw new InvalidOperationException("VirtualWorld is missing.");
        AddHeightSetup(world);
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
        Debug.Log("[Treadmill] Height setup added; track waits for height confirmation.");
    }

    private static void AddHeightSetup(StraightTreadmillWorld world)
    {
        var setup = UnityEngine.Object.FindFirstObjectByType<TreadmillHeightSetup>();
        if (setup == null) setup = new GameObject("TreadmillHeightSetup").AddComponent<TreadmillHeightSetup>();
        var data = new SerializedObject(setup);
        data.FindProperty("world").objectReferenceValue = world;
        data.ApplyModifiedPropertiesWithoutUndo();
        setup.PrepareMenu();
        data = new SerializedObject(world);
        data.FindProperty("requireHeightCalibration").boolValue = true;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    [MenuItem("Tools/Treadmill/Use fixed height 184 cm")]
    public static void UseFixedHeight()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode || SceneManager.GetActiveScene().path != ScenePath)
            throw new InvalidOperationException("Open the treadmill test scene outside Play mode first.");
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        if (world == null) throw new InvalidOperationException("VirtualWorld is missing.");
        var data = new SerializedObject(world);
        data.FindProperty("automaticHeightOnStart").boolValue = true;
        data.FindProperty("bodyHeightCm").intValue = 184;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.MarkSceneDirty(SceneManager.GetActiveScene());
        EditorSceneManager.SaveScene(SceneManager.GetActiveScene());
    }

    [MenuItem("Tools/Treadmill/Verify automatic placement in Play mode")]
    public static void VerifyAutomaticPlacement()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        var setup = UnityEngine.Object.FindFirstObjectByType<TreadmillHeightSetup>(FindObjectsInactive.Include);
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        Require(world != null && world.AutomaticHeightOnStart, "world owns fixed-height setup");
        Require(world.PlacementReady && world.HeightConfirmed, "automatic placement completed");
        Require(setup == null || !setup.GetComponentInChildren<Canvas>(true).gameObject.activeInHierarchy, "no menu interaction needed");
        Require(Math.Abs(world.transform.position.y - (world.HeadCamera.transform.position.y - 1.72f)) < 0.02f,
            "184 cm body height places surface below estimated eyes");
        foreach (var renderer in world.GetComponentsInChildren<MeshRenderer>())
            Require(renderer.enabled, "track is visible");
        Debug.Log("[Treadmill] PASS: 184 cm automatic placement, visible track, no menu.");
    }

    private static Mesh CreateTile()
    {
        var vertices = new List<Vector3>();
        var colors = new List<Color>();
        var indices = new List<int>();
        void Quad(float x0, float x1, float z0, float z1, float y, Color color)
        {
            int n = vertices.Count;
            vertices.Add(new Vector3(x0, y, z0)); vertices.Add(new Vector3(x0, y, z1));
            vertices.Add(new Vector3(x1, y, z1)); vertices.Add(new Vector3(x1, y, z0));
            for (int j = 0; j < 4; j++) colors.Add(color);
            indices.AddRange(new[] { n, n + 1, n + 2, n, n + 2, n + 3 });
        }

        // Three 1.22 m lanes at real scale; the viewer starts at the middle lane's centre.
        Quad(-1.83f, 1.83f, 0f, 20f, 0f, new Color(0.55f, 0.12f, 0.07f));
        for (int lane = 0; lane <= 3; lane++)
        {
            float x = -1.83f + lane * 1.22f;
            Quad(x - 0.025f, x + 0.025f, 0f, 20f, 0.004f, Color.white);
        }
        for (int marker = 0; marker < 4; marker++)
        {
            float z = marker * 5f + 2f;
            // Short white dashes and yellow edge ticks make speed visible on a uniform surface.
            Quad(-0.04f, 0.04f, z, z + 0.6f, 0.006f, Color.white);
            Quad(-1.8f, -1.5f, z, z + 0.08f, 0.006f, Color.yellow);
            Quad(1.5f, 1.8f, z, z + 0.08f, 0.006f, Color.yellow);
        }
        var mesh = new Mesh { name = "Straight track 20m" };
        mesh.SetVertices(vertices); mesh.SetColors(colors); mesh.SetTriangles(indices, 0);
        mesh.RecalculateNormals(); mesh.RecalculateBounds();
        return mesh;
    }

    [MenuItem("Tools/Treadmill/Verify motion")]
    public static void VerifyMotion()
    {
        var go = new GameObject("Temporary treadmill verification");
        var head = new GameObject("Temporary tracked head");
        try
        {
            var treadmill = go.AddComponent<StraightTreadmillWorld>();
            var config = new SerializedObject(treadmill);
            config.FindProperty("requireHeightCalibration").boolValue = false;
            config.ApplyModifiedPropertiesWithoutUndo();
            treadmill.ResetDistance();
            treadmill.SpeedKmh = 6f;
            treadmill.AdvanceSimulation(1f);
            Require(Math.Abs(treadmill.TravelledMetres - 6.0 / 3.6) < 1e-6, "km/h conversion");
            Require(Math.Abs(go.transform.position.z + 6f / 3.6f) < 1e-5f, "world moves backwards");
            Vector3 paused = go.transform.position;
            treadmill.SpeedKmh = 0f;
            treadmill.AdvanceSimulation(30f);
            Require(go.transform.position == paused, "zero speed pauses");
            treadmill.ResetDistance();
            Require(go.transform.position == Vector3.zero && treadmill.TravelledMetres == 0, "reset restores origin");
            treadmill.SpeedKmh = 12f;
            treadmill.AdvanceSimulation(600f);
            Require(Math.Abs(treadmill.TravelledMetres - 2000.0) < 1e-6, "distance survives tile wrapping");
            Require(go.transform.position.magnitude < 20.001f, "long-run coordinates stay bounded");

            treadmill.ResetDistance();
            treadmill.SpeedKmh = 6f;
            for (int i = 0; i < 60; i++) treadmill.AdvanceSimulation(1f / 60f);
            Require(Math.Abs(treadmill.TravelledMetres - 6.0 / 3.6) < 1e-5, "frame-rate independent movement");
            // Align via a separate head camera to verify world-relative movement and fixed tracking.
            Camera camera = head.AddComponent<Camera>();
            head.transform.SetPositionAndRotation(new Vector3(2, 1.65f, 3), Quaternion.Euler(0, 90, 0));
            var data = new SerializedObject(treadmill);
            data.FindProperty("headCamera").objectReferenceValue = camera;
            data.ApplyModifiedPropertiesWithoutUndo();
            treadmill.AlignToCurrentView();
            Vector3 headPosition = head.transform.position;
            Quaternion headRotation = head.transform.rotation;
            treadmill.AdvanceSimulation(1f);
            Require(Math.Abs(go.transform.position.x - (2f - 6f / 3.6f)) < 1e-5, "rotated track moves along its heading");
            Require(head.transform.position == headPosition && head.transform.rotation == headRotation,
                "head tracking transform is untouched");
            treadmill.BeginHeightCalibration();
            treadmill.AdvanceSimulation(10f);
            Require(!treadmill.PlacementReady, "movement waits for height confirmation");
            Require(!treadmill.TryRequestFloorOrigin(out string floorReason) && !string.IsNullOrEmpty(floorReason),
                "missing XR floor support returns an explanation");
            Require(!treadmill.TryCalibrateFromFloorOrigin() && !treadmill.HeightConfirmed,
                "missing floor support does not place at world zero");
            // Simulate a device origin at eye level: floor is 1.65 m BELOW world zero.
            head.transform.position = new Vector3(2f, 0f, 3f);
            Require(treadmill.CalibrateFromEyeHeight(1.65f), "eye-height calibration accepted");
            Require(Math.Abs(go.transform.position.y + 1.65f) < 1e-5, "feet below eye-level origin");
            treadmill.AdjustSurfaceHeight(0.02f);
            treadmill.AdvanceSimulation(1f);
            Require(Math.Abs(go.transform.position.y + 1.63f) < 1e-5, "height correction survives movement");
            Require(!treadmill.CalibrateFromEyeHeight(float.NaN), "invalid height rejected");
            treadmill.SpeedKmh = float.NaN;
            treadmill.AdvanceSimulation(1f);
            Require(treadmill.SpeedKmh == 0f, "invalid speed rejected");
            Debug.Log("[Treadmill] PASS: motion, fixed head, calibration gate, eye-level origin and height correction.");
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(head);
        }
    }

    private static void Require(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException("[Treadmill] FAIL: " + label);
    }

    [MenuItem("Tools/Treadmill/Verify height menu in Play mode")]
    public static void VerifyHeightMenu()
    {
        if (!EditorApplication.isPlaying) throw new InvalidOperationException("Enter Play mode first.");
        var setup = UnityEngine.Object.FindFirstObjectByType<TreadmillHeightSetup>();
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        Require(setup != null && world != null, "height menu exists");
        var input = setup.GetComponentInChildren<UnityEngine.UI.InputField>();
        var canvas = setup.GetComponentInChildren<Canvas>();
        UnityEngine.UI.Button FindButton(string name)
        {
            foreach (var candidate in setup.GetComponentsInChildren<UnityEngine.UI.Button>())
                if (candidate.name == name) return candidate;
            throw new InvalidOperationException("Button missing: " + name);
        }

        var confirm = FindButton("Höhe passt – weiter");
        Require(!world.HeightConfirmed && !confirm.interactable, "height confirmation required before start");
        input.text = "79";
        FindButton("Größe übernehmen / Vorschau").onClick.Invoke();
        Require(!world.HeightConfirmed && !confirm.interactable, "invalid body height cannot start");
        input.text = "180";
        FindButton("Größe übernehmen / Vorschau").onClick.Invoke();
        Require(world.HeightConfirmed && confirm.interactable, "valid body height enables confirmation");
        float expected = world.HeadCamera.transform.position.y - 1.68f;
        Require(Math.Abs(world.transform.position.y - expected) < 1e-5f, "body height converts to eye height");
        FindButton("Bahn +2 cm").onClick.Invoke();
        Require(Math.Abs(world.transform.position.y - expected - 0.02f) < 1e-5f, "fine correction button works");
        Require(world.SpeedKmh == 0, "preview remains stationary");
        confirm.onClick.Invoke();
        Require(!canvas.gameObject.activeSelf && world.PlacementReady, "confirmation closes setup and keeps placement");
        Debug.Log("[Treadmill] PASS: height menu validation, preview, +2 cm correction and confirmation.");
    }
}
