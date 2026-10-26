using System;
using System.Collections.Generic;
using System.IO;
using StadionTracking;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class RunnerUiGuideSetup
{
    private const string MenuPath = "Assets/Scenes/RunnerMainMenu.unity";
    private const string TrackPath = "Assets/Scenes/TreadmillStraightTest.unity";

    [MenuItem("Tools/Runner UI/Apply guide to project and create main menu")]
    public static void ApplyAll()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        EditorSceneManager.SaveOpenScenes();
        EnsureAssets();
        foreach (string path in new[] { "Assets/Materials/RunnerStatsCanvas.prefab", "Assets/HeartRateBLE/HeartRateCanvas.prefab" })
        {
            var root = PrefabUtility.LoadPrefabContents(path);
            try { StyleRoot(root); PrefabUtility.SaveAsPrefabAsset(root, path); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
        }
        foreach (string path in new[] { "Assets/Scenes/SampleScene.unity", TrackPath })
        {
            var scene = EditorSceneManager.OpenScene(path);
            foreach (var root in scene.GetRootGameObjects()) StyleRoot(root);
            foreach (var world in UnityEngine.Object.FindObjectsByType<StraightTreadmillWorld>(FindObjectsSortMode.None))
                if (world.GetComponent<RunnerSessionBootstrap>() == null) world.gameObject.AddComponent<RunnerSessionBootstrap>();
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        }
        CreateMainMenu();
        var builds = new List<EditorBuildSettingsScene> { new EditorBuildSettingsScene(MenuPath, true), new EditorBuildSettingsScene(TrackPath, true) };
        foreach (var scene in EditorBuildSettings.scenes)
            if (scene.path != MenuPath && scene.path != TrackPath) builds.Add(scene);
        EditorBuildSettings.scenes = builds.ToArray();
        AssetDatabase.SaveAssets();
        EditorSceneManager.OpenScene(MenuPath);
        Debug.Log("[RunnerUI] Guide applied to project scenes/prefabs. Main menu is the first scene; live HUD has exactly three values.");
    }

    private static void EnsureAssets()
    {
        if (!AssetDatabase.IsValidFolder("Assets/Resources")) AssetDatabase.CreateFolder("Assets", "Resources");
        if (!AssetDatabase.IsValidFolder("Assets/Resources/RunnerUI")) AssetDatabase.CreateFolder("Assets/Resources", "RunnerUI");
        const string fontPath = "Assets/Resources/RunnerUI/RunnerSans.asset";
        if (AssetDatabase.LoadAssetAtPath<TMP_FontAsset>(fontPath) == null)
        {
            var font = AssetDatabase.LoadAssetAtPath<Font>("Assets/TextMesh Pro/Fonts/LiberationSans.ttf");
            var asset = TMP_FontAsset.CreateFontAsset(font);
            asset.name = "RunnerSans";
            asset.atlasPopulationMode = AtlasPopulationMode.Dynamic;
            AssetDatabase.CreateAsset(asset, fontPath);
            asset.material.name = "RunnerSans Material";
            AssetDatabase.AddObjectToAsset(asset.material, asset);
            foreach (var texture in asset.atlasTextures) { texture.name = "RunnerSans Atlas"; AssetDatabase.AddObjectToAsset(texture, asset); }
            asset.TryAddCharacters("ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789ÄÖÜäöüß:,.!?+-−/()•–→ ");
            EditorUtility.SetDirty(asset);
        }
        const string materialPath = "Assets/Resources/RunnerUI/Glass.mat";
        if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
            AssetDatabase.CreateAsset(new Material(Shader.Find("RunnerStats/LiquidGlass")) { name = "Runner Liquid Glass" }, materialPath);
        AssetDatabase.SaveAssets();
    }

    private static void PrepareCanvas(Canvas canvas, float height, int priority)
    {
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.additionalShaderChannels |= AdditionalCanvasShaderChannels.TexCoord1 | AdditionalCanvasShaderChannels.TexCoord2;
        var rect = (RectTransform)canvas.transform; rect.sizeDelta = new Vector2(300, height);
        var image = canvas.GetComponent<UnityEngine.UI.Image>();
        if (image != null) UnityEngine.Object.DestroyImmediate(image);
        var glass = canvas.GetComponent<RunnerGlassGraphic>() ?? canvas.gameObject.AddComponent<RunnerGlassGraphic>();
        glass.raycastTarget = false;
        var panel = canvas.GetComponent<RunnerArPanel>() ?? canvas.gameObject.AddComponent<RunnerArPanel>();
        panel.Priority = priority;
        if (canvas.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null) canvas.gameObject.AddComponent<UnityEngine.UI.GraphicRaycaster>();
        if (canvas.GetComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>() == null)
            canvas.gameObject.AddComponent<UnityEngine.XR.Interaction.Toolkit.UI.TrackedDeviceGraphicRaycaster>();
        foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
        {
            bool label = text.name.Contains("Label") || text.name.Contains("Title") || text.name.Contains("Status");
            bool value = text.name.Contains("Value") || text.name == "HeartRateText" || text.name == "CurrentSpeed";
            RunnerUiTheme.StyleText(text, value ? 28 : 13, value, label);
            text.alignment = TextAlignmentOptions.MidlineLeft;
            text.enableWordWrapping = true; text.overflowMode = TextOverflowModes.Overflow;
            text.text = text.text.Replace("#AAB8C7", "#E3DEF7").Replace("#F5F8FF", "#FFFFFF")
                .Replace("<size=40%>", "<size=13>").Replace("<size=45%>", "<size=13>")
                .Replace("<size=55%>", "<size=13>").Replace("<size=80%>", "<size=13>").Replace("<size=130%>", "<size=28>");
        }
        foreach (var text in canvas.GetComponentsInChildren<UnityEngine.UI.Text>(true))
        { text.fontSize = text.name.Contains("Height") ? 28 : 13; text.color = RunnerUiTheme.Label; }
        foreach (var button in canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true)) StyleButton(button);
        foreach (var raw in canvas.GetComponentsInChildren<UnityEngine.UI.RawImage>(true)) raw.color = Color.white;
        foreach (var marker in canvas.GetComponentsInChildren<RunnerMapMarker>(true))
        {
            var data = new SerializedObject(marker); data.FindProperty("markerColor").colorValue = RunnerUiTheme.Goal; data.ApplyModifiedPropertiesWithoutUndo();
        }
        foreach (var background in canvas.GetComponentsInChildren<RectTransform>(true))
        {
            if (background.name != "Panel" && background.name != "slidebar") continue;
            Stretch(background);
            var bg = background.GetComponent<UnityEngine.UI.Image>(); if (bg != null) bg.enabled = false;
        }
    }

    private static void StyleRoot(GameObject root)
    {
        foreach (var canvas in root.GetComponentsInChildren<Canvas>(true))
        {
            if (canvas.GetComponent<ActiveStatsDisplay>() != null) StyleHud(canvas);
            else if (canvas.GetComponent<UIScript>() != null) StyleRunnerControls(canvas);
            else if (canvas.GetComponent<HeartRateUI>() != null) StyleVitals(canvas);
            else if (canvas.name == "HeightSetupCanvas") StyleHeight(canvas);
        }
    }

    private static void StyleHud(Canvas canvas)
    {
        PrepareCanvas(canvas, 330, 0);
        var legacyPanel = canvas.transform.Find("Panel");
        if (legacyPanel != null) legacyPanel.gameObject.SetActive(false);
        var display = canvas.GetComponent<ActiveStatsDisplay>();
        var data = new SerializedObject(display);
        var old = data.FindProperty("statsReadout").objectReferenceValue as TMP_Text;
        if (old != null) old.gameObject.SetActive(false);
        RunnerUiTheme.Text("Header", canvas.transform, "LAUFSESSION", 20, 20, 260, 18, 13, false, true);
        RunnerUiTheme.Text("TimeLabel", canvas.transform, "ZEIT", 20, 52, 260, 18, 13, false, true);
        var timer = RunnerUiTheme.Text("Timer", canvas.transform, "00:00", 20, 70, 260, 70, 56, true); timer.color = RunnerUiTheme.Time;
        RunnerUiTheme.Rule(canvas.transform, 154);
        RunnerUiTheme.Text("DistanceLabel", canvas.transform, "DISTANZ", 20, 169, 124, 18, 13, false, true);
        RunnerUiTheme.Text("PaceLabel", canvas.transform, "PACE", 156, 169, 124, 18, 13, false, true);
        var distance = RunnerUiTheme.Text("Distance", canvas.transform, "0,00 <size=13>km</size>", 20, 191, 124, 38, 28, true);
        var pace = RunnerUiTheme.Text("Pace", canvas.transform, "--:--", 156, 191, 124, 38, 28, true);
        RunnerUiTheme.Text("DistanceUnit", canvas.transform, "km", 20, 225, 124, 18, 13, false, true);
        RunnerUiTheme.Text("PaceUnit", canvas.transform, "min/km", 156, 225, 124, 18, 13, false, true);
        var checkpoint = RunnerUiTheme.Text("Checkpoint", canvas.transform, "100 m CHECKPOINT  |  BEISPIEL-PB 00:25.0",
            20, 244, 260, 22, 13, false, true);
        RunnerUiTheme.Button("PauseSession", canvas.transform, "PAUSE", 20, 280, 124, null);
        RunnerUiTheme.Button("BackToMenu", canvas.transform, "MENÜ", 156, 280, 124, null);
        if (canvas.GetComponent<RunnerSessionNavigation>() == null) canvas.gameObject.AddComponent<RunnerSessionNavigation>();
        data.FindProperty("timerReadout").objectReferenceValue = timer;
        data.FindProperty("distanceReadout").objectReferenceValue = distance;
        data.FindProperty("paceReadout").objectReferenceValue = pace;
        data.FindProperty("checkpointReadout").objectReferenceValue = checkpoint;
        data.ApplyModifiedPropertiesWithoutUndo();
    }

    private static void StyleRunnerControls(Canvas canvas)
    {
        PrepareCanvas(canvas, 452, 10);
        foreach (var text in canvas.GetComponentsInChildren<TMP_Text>(true))
        {
            switch (text.name)
            {
                case "RunnerStatus": Place(text.rectTransform, 20, 20, 260, 32); break;
                case "SpeedValue": Place(text.rectTransform, 20, 66, 260, 50); break;
                case "CurrentSpeed": Place(text.rectTransform, 20, 130, 260, 64); text.fontSize = 28; text.color = Color.white; break;
                case "CurrentLap": case "CompletedLaps": text.gameObject.SetActive(false); break;
            }
        }
        var slider = canvas.GetComponentInChildren<UnityEngine.UI.Slider>(true);
        if (slider != null)
        {
            Place((RectTransform)slider.transform, 20, 208, 260, 30);
            foreach (var graphic in slider.GetComponentsInChildren<UnityEngine.UI.Image>(true))
                graphic.color = graphic.name == "Handle" ? Color.white : new Color(1, 1, 1, .2f);
        }
        foreach (var map in canvas.GetComponentsInChildren<UnityEngine.UI.RawImage>(true)) Place(map.rectTransform, 20, 308, 260, 124);
    }

    private static void StyleVitals(Canvas canvas)
    {
        PrepareCanvas(canvas, 642, 20);
        var panel = canvas.transform.Find("Panel");
        if (panel != null)
            foreach (var layout in panel.GetComponents<UnityEngine.UI.LayoutGroup>()) layout.enabled = false;
        var ui = canvas.GetComponent<HeartRateUI>();
        var title = canvas.transform.Find("Panel/Title")?.GetComponent<TMP_Text>();
        if (title != null) { title.text = "SENSOREN"; Place(title.rectTransform, 20, 20, 260, 28); }
        Place(ui.devices.GetComponent<RectTransform>(), 20, 62, 260, 40);
        Place(ui.scanButton.GetComponent<RectTransform>(), 20, 116, 124, 40);
        Place(ui.stopButton.GetComponent<RectTransform>(), 156, 116, 124, 40);
        Place(ui.connectButton.GetComponent<RectTransform>(), 20, 170, 124, 40);
        Place(ui.disconnectButton.GetComponent<RectTransform>(), 156, 170, 124, 40);
        Place(ui.statusText.rectTransform, 20, 224, 260, 54);
        Place(ui.heartRateText.rectTransform, 20, 292, 260, 86);
        Place(ui.sensorDetailsText.rectTransform, 20, 392, 260, 226);
        var dropdownImage = ui.devices.GetComponent<UnityEngine.UI.Image>();
        if (dropdownImage != null) dropdownImage.color = new Color(1, 1, 1, .07f);
        foreach (var background in canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            if (background.name == "Item Background" || background.name == "Template") background.color = RunnerUiTheme.Background;
    }

    private static void StyleHeight(Canvas canvas)
    {
        PrepareCanvas(canvas, 588, 50);
        foreach (var layout in canvas.GetComponents<UnityEngine.UI.LayoutGroup>()) layout.enabled = false;
        int row = 0;
        var setup = canvas.GetComponentInParent<TreadmillHeightSetup>(true);
        var data = new SerializedObject(setup);
        var status = data.FindProperty("status").objectReferenceValue as UnityEngine.UI.Text;
        foreach (Transform child in canvas.transform)
        {
            var rect = (RectTransform)child;
            if (child.name.StartsWith("KeypadRow")) { Place(rect, 20, 108 + row++ * 44, 260, 32); }
            else if (child.name == "BodyHeightCm") Place(rect, 20, 54, 260, 40);
            else if (child.name == "Größe übernehmen / Vorschau") Place(rect, 20, 292, 260, 40);
            else if (child.name == "Bodenreferenz automatisch nutzen") Place(rect, 20, 346, 260, 40);
            else if (child.name == "HeightCorrection") Place(rect, 20, 400, 260, 40);
            else if (child.name == "Höhe passt – weiter") Place(rect, 20, 528, 260, 40);
            else if (status != null && child.gameObject == status.gameObject) Place(rect, 20, 454, 260, 60);
            else if (child.GetComponent<UnityEngine.UI.Text>() != null) child.gameObject.SetActive(false);
        }
        var header = canvas.transform.Find("GuideTitle");
        if (header == null) RunnerUiTheme.Text("GuideTitle", canvas.transform, "KÖRPERGRÖSSE IN CM", 20, 20, 260, 20, 13, false, true);
        foreach (var image in canvas.GetComponentsInChildren<UnityEngine.UI.Image>(true))
            if (image.GetComponent<UnityEngine.UI.Button>() == null) image.color = new Color(1, 1, 1, .07f);
    }

    private static void StyleButton(UnityEngine.UI.Button button)
    {
        var image = button.GetComponent<UnityEngine.UI.Image>();
        if (image != null) { image.enabled = false; UnityEngine.Object.DestroyImmediate(image); }
        var fill = button.GetComponent<RunnerGlassGraphic>() ?? button.gameObject.AddComponent<RunnerGlassGraphic>();
        fill.FlatSurface = true; fill.color = new Color(1, 1, 1, .07f); button.targetGraphic = fill;
        foreach (var label in button.GetComponentsInChildren<TMP_Text>(true))
        { RunnerUiTheme.StyleText(label, 13, false, true); label.alignment = TextAlignmentOptions.Center; label.text = label.text.ToUpperInvariant(); }
    }

    private static void CreateMainMenu()
    {
        var trackScene = EditorSceneManager.OpenScene(TrackPath);
        var menu = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
        foreach (string name in new[] { "ML Rig", "EventSystem", "Directional Light" })
        {
            var source = GameObject.Find(name);
            if (source == null) throw new InvalidOperationException(name + " missing in treadmill scene.");
            var clone = UnityEngine.Object.Instantiate(source); clone.name = name;
            SceneManager.MoveGameObjectToScene(clone, menu);
        }
        SceneManager.SetActiveScene(menu);
        var canvas = RunnerUiTheme.Panel("RunnerMainMenu", 652);
        canvas.GetComponent<RunnerArPanel>().Priority = 100;
        RunnerUiTheme.Text("Eyebrow", canvas.transform, "RUNNER STATS", 20, 20, 260, 18, 13, false, true);
        RunnerUiTheme.Text("Title", canvas.transform, "Dein Lauf.", 20, 52, 260, 36, 28, true);
        RunnerUiTheme.Text("Subtitle", canvas.transform, "400-M-BAHN · LAUFBANDMODUS", 20, 102, 260, 32, 13, false, true);
        RunnerUiTheme.Rule(canvas.transform, 148);
        RunnerUiTheme.Text("PaceLabel", canvas.transform, "ZIELPACE", 20, 163, 260, 18, 13, false, true);
        var pace = RunnerUiTheme.Text("PaceValue", canvas.transform, "6:00", 20, 195, 160, 40, 28, true);
        RunnerUiTheme.Button("PaceMinus", canvas.transform, "−", 190, 195, 38, null);
        RunnerUiTheme.Button("PacePlus", canvas.transform, "+", 242, 195, 38, null);
        RunnerUiTheme.Rule(canvas.transform, 249);
        RunnerUiTheme.Text("ModeLabel", canvas.transform, "LAUFMODUS", 20, 264, 260, 18, 13, false, true);
        var side = RunnerUiTheme.Button("SideBySide", canvas.transform, "NEBEN MIR", 20, 296, 124, null, 48);
        var pacer = RunnerUiTheme.Button("Pacer", canvas.transform, "PACER", 156, 296, 124, null, 48);
        var status = RunnerUiTheme.Text("ModeDescription", canvas.transform, "Avatar läuft neben dir.", 20, 358, 260, 42, 13);
        RunnerUiTheme.Rule(canvas.transform, 414);
        RunnerUiTheme.Text("HeightLabel", canvas.transform, "KÖRPERGRÖSSE", 20, 429, 260, 18, 13, false, true);
        var height = RunnerUiTheme.Text("HeightValue", canvas.transform, "184", 20, 461, 160, 40, 28, true);
        RunnerUiTheme.Button("HeightMinus", canvas.transform, "−", 190, 461, 38, null);
        RunnerUiTheme.Button("HeightPlus", canvas.transform, "+", 242, 461, 38, null);
        RunnerUiTheme.Button("TrackVisibility", canvas.transform, "RENNSTRECKE AUSSCHALTEN", 20, 515, 260, null);
        RunnerUiTheme.Rule(canvas.transform, 569);
        RunnerUiTheme.Button("StartSession", canvas.transform, "LAUF STARTEN  →", 20, 584, 260, null, 48);
        var controller = canvas.gameObject.AddComponent<RunnerMainMenu>();
        var data = new SerializedObject(controller);
        data.FindProperty("paceValue").objectReferenceValue = pace;
        data.FindProperty("heightValue").objectReferenceValue = height;
        data.FindProperty("status").objectReferenceValue = status;
        data.FindProperty("beside").objectReferenceValue = side;
        data.FindProperty("pacer").objectReferenceValue = pacer;
        data.ApplyModifiedPropertiesWithoutUndo();
        EditorSceneManager.SaveScene(menu, MenuPath);
        EditorSceneManager.CloseScene(trackScene, true);
    }

    [MenuItem("Tools/Runner UI/Add track visibility option to menu")]
    public static void AddTrackVisibilityOption()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        var scene = EditorSceneManager.OpenScene(MenuPath);
        var menu = UnityEngine.Object.FindFirstObjectByType<RunnerMainMenu>();
        if (menu == null) throw new InvalidOperationException("Main menu missing.");
        var hint = menu.transform.Find("HeightHint");
        if (hint != null) hint.gameObject.SetActive(false);
        RunnerUiTheme.Button("TrackVisibility", menu.transform, "RENNSTRECKE AUSSCHALTEN", 20, 515, 260, null);
        EditorSceneManager.MarkSceneDirty(scene);
        EditorSceneManager.SaveScene(scene);
        Debug.Log("[RunnerUI] Track visibility option added to main menu.");
    }

    private static void Place(RectTransform rect, float x, float y, float width, float height)
    { rect.anchorMin = rect.anchorMax = rect.pivot = new Vector2(0, 1); rect.anchoredPosition = new Vector2(x, -y); rect.sizeDelta = new Vector2(width, height); }
    private static void Stretch(RectTransform rect)
    { rect.anchorMin = Vector2.zero; rect.anchorMax = Vector2.one; rect.offsetMin = rect.offsetMax = Vector2.zero; }
}
