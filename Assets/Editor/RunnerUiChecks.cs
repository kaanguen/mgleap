using System;
using StadionTracking;
using TMPro;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

internal static class RunnerUiChecks
{
    static void Require(bool condition, string message) { if (!condition) throw new InvalidOperationException("[RunnerUI] " + message); }
    static RunnerMainMenu Menu() { Require(Application.isPlaying, "Enter Play mode first."); return UnityEngine.Object.FindFirstObjectByType<RunnerMainMenu>(); }

    [MenuItem("Tools/Runner UI/Checks/Check menu and start tempo partner session")]
    static void CheckMenu()
    {
        var menu = Menu(); Require(menu != null, "Main menu missing.");
        menu.ChangePace(-10000); Require(menu.PaceSeconds == 180, "Minimum pace.");
        menu.ChangePace(10000); Require(menu.PaceSeconds == 1200, "Maximum pace.");
        menu.ChangePace(375 - menu.PaceSeconds);
        menu.ChangeHeight(-10000); Require(menu.HeightCm == 80, "Minimum height.");
        menu.ChangeHeight(10000); Require(menu.HeightCm == 230, "Maximum height.");
        menu.ChangeHeight(183 - menu.HeightCm);
        menu.transform.Find("SideBySide").GetComponent<Button>().onClick.Invoke();
        Require(menu.Mode == TreadmillRunMode.SideBySide, "Side mode button.");
        menu.transform.Find("Pacer").GetComponent<Button>().onClick.Invoke();
        Require(menu.Mode == TreadmillRunMode.TempoPartner, "Tempo partner button.");
        RunnerSessionSettings.ShowTrack = true;
        menu.Refresh();
        var toggle = menu.transform.Find("TrackVisibility").GetComponent<Button>();
        toggle.onClick.Invoke(); Require(!menu.ShowTrack, "Track off button.");
        toggle.onClick.Invoke(); Require(menu.ShowTrack, "Track on button.");
        toggle.onClick.Invoke();
        CheckPanels();
        menu.transform.Find("StartSession").GetComponent<Button>().onClick.Invoke();
        Debug.Log("[RunnerUI CHECK] Menu buttons and boundaries passed; loading 6:15 / 183 cm / Tempo-Partner.");
    }

    [MenuItem("Tools/Runner UI/Checks/Check track and pause")]
    static void CheckTrack()
    {
        var world = UnityEngine.Object.FindFirstObjectByType<StraightTreadmillWorld>();
        Require(world != null && world.PlacementReady, "Track placement not ready.");
        Require(Mathf.Abs(world.PaceSecondsPerKilometre - 375) < .1f, "Pace was not passed to track.");
        Require(world.BodyHeightCm == 183, "Height was not passed to track.");
        var companion = UnityEngine.Object.FindFirstObjectByType<TreadmillCompanion>();
        Require(companion != null && companion.Mode == TreadmillRunMode.TempoPartner, "Tempo partner mode was not passed.");
        Require(!world.ShowTrack, "Track visibility setting was not passed.");
        var surfaces = world.GetComponentsInChildren<MeshRenderer>(true);
        Require(surfaces.Length > 0, "Track geometry missing.");
        foreach (var surface in surfaces)
            if (surface.GetComponentInParent<Animator>() == null) Require(!surface.enabled, "Hidden track is visible.");
        var playback = UnityEngine.Object.FindFirstObjectByType<LapGhostPlayback>();
        var avatar = playback != null && playback.Avatar != null ? playback.Avatar.GetComponentInChildren<Animator>() : null;
        Require(companion.Avatar == null || !companion.Avatar.activeInHierarchy, "Only the tempo partner may be visible.");
        Require(avatar != null && avatar.gameObject.activeInHierarchy, "Avatar must remain active without track.");
        foreach (var renderer in avatar.GetComponentsInChildren<SkinnedMeshRenderer>())
            Require(renderer.enabled, "Avatar must remain visible without track.");
        double distance = world.TravelledMetres;
        world.ShowTrack = true;
        foreach (var surface in surfaces)
            if (surface.GetComponentInParent<Animator>() == null) Require(surface.enabled, "Track did not reappear.");
        Require(world.TravelledMetres == distance && world.PlacementReady, "Visibility changed session progress.");
        world.ShowTrack = false;
        var nav = UnityEngine.Object.FindFirstObjectByType<RunnerSessionNavigation>(); Require(nav != null, "HUD navigation missing.");
        float speed = world.SpeedKmh;
        nav.TogglePause(); Require(world.SpeedKmh == 0, "Pause failed.");
        nav.TogglePause(); Require(Mathf.Abs(world.SpeedKmh - speed) < .001f, "Resume failed.");
        var hud = nav.transform;
        Require(hud.GetComponent<Canvas>().enabled, "Live HUD must be the visible panel.");
        Require(hud.Find("Timer").GetComponent<TMP_Text>().fontSize == 56, "Timer must be 56.");
        CheckPanels();
        Debug.Log("[RunnerUI CHECK] Scene transition, pace, height, mode, placement, pause/resume, track visibility with active avatar and UI tokens passed.");
    }

    [MenuItem("Tools/Runner UI/Checks/Return to menu")]
    static void ReturnToMenu() => UnityEngine.Object.FindFirstObjectByType<RunnerSessionNavigation>().BackToMenu();

    [MenuItem("Tools/Runner UI/Checks/Reload saved menu (discard preview)")]
    static void ReloadMenu()
    {
        Require(!Application.isPlaying, "Exit Play mode.");
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/RunnerMainMenu.unity");
    }

    [MenuItem("Tools/Runner UI/Checks/Preview sensors (Edit mode)")]
    static void PreviewSensors() => Preview("Assets/HeartRateBLE/HeartRateCanvas.prefab");
    [MenuItem("Tools/Runner UI/Checks/Preview runner controls (Edit mode)")]
    static void PreviewControls() => Preview("Assets/Materials/RunnerStatsCanvas.prefab");
    static void Preview(string path)
    {
        Require(!Application.isPlaying, "Exit Play mode.");
        UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/Scenes/RunnerMainMenu.unity");
        UnityEngine.Object.FindFirstObjectByType<RunnerMainMenu>().gameObject.SetActive(false);
        var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
        instance.SetActive(true);
        foreach (var text in instance.GetComponentsInChildren<TMP_Text>())
            Require(text.fontSize >= 13 && !text.enableAutoSizing, "Invalid preview typography: " + text.name);
        UnityEditorInternal.InternalEditorUtility.RepaintAllViews();
        Debug.Log("[RunnerUI CHECK] Preview ready: " + path + "; reload main menu without saving to exit.");
    }

    static void CheckPanels()
    {
        int visible = 0;
        foreach (var panel in UnityEngine.Object.FindObjectsByType<RunnerArPanel>(FindObjectsSortMode.None))
        {
            if (!panel.GetComponent<Canvas>().enabled) continue;
            visible++;
            Require(((RectTransform)panel.transform).rect.width == 300, "Panel width must be 300.");
            foreach (var text in panel.GetComponentsInChildren<TMP_Text>())
                Require(text.fontSize >= 13 && !text.enableAutoSizing, "Text below 13 or auto sizing: " + text.name);
            if (panel.IsCentered)
            {
                var centre = Camera.main.WorldToViewportPoint(panel.transform.TransformPoint(((RectTransform)panel.transform).rect.center));
                Require(Mathf.Abs(centre.x - .5f) < .001f && Mathf.Abs(centre.y - .5f) < .001f, "Menu is not centered.");
            }
            var corners = new Vector3[4]; ((RectTransform)panel.transform).GetWorldCorners(corners);
            foreach (var corner in corners)
            {
                var point = Camera.main.WorldToViewportPoint(corner);
                Require(point.x >= 0 && point.x <= 1 && point.y >= 0 && point.y <= 1, "Panel outside view.");
                if (!panel.IsCentered) Require(point.x < (panel.IsRunningHud ? .45f : 1f / 3f), "Live panel enters forward viewing area.");
            }
        }
        Require(visible == 1, "Expected one visible glass panel; got " + visible);
    }
}
