using System;
using System.IO;
using System.Linq;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.UI;

[InitializeOnLoad]
public static class HeartRateCanvasSetup
{
    const string PrefabPath = "Assets/HeartRateBLE/HeartRateCanvas.prefab";
    const string ScenePath = "Assets/Scenes/SampleScene.unity";
    const string RequestPath = "Assets/HeartRateBLE/Editor/CreateCanvas.request";
    static HeartRateCanvasSetup() { EditorApplication.delayCall += AutoCreate; EditorApplication.delayCall += UpgradeSensorCanvas; }

    static void UpgradeSensorCanvas()
    {
        const string request="Assets/HeartRateBLE/Editor/AddSensorValues.request";
        if (!File.Exists(request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try {
            var contents=PrefabUtility.LoadPrefabContents(PrefabPath);
            try { Enhance(contents); PrefabUtility.SaveAsPrefabAsset(contents,PrefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            var scene=SceneManager.GetSceneByPath(ScenePath);
            if (!scene.IsValid() || !scene.isLoaded) scene=EditorSceneManager.OpenScene(ScenePath,OpenSceneMode.Additive);
            var root=scene.GetRootGameObjects().First(x => x.name=="HeartRateCanvas");
            Enhance(root);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(root.GetComponent<HeartRateUI>());
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene); AssetDatabase.SaveAssets();
            RenderPreview(root.GetComponent<Canvas>());
            var ui=root.GetComponent<HeartRateUI>();
            if (ui.sensor==null || ui.sensorDetailsText==null || ui.sensor.bluetooth!=ui.bluetooth)
                throw new InvalidOperationException("Sensorwerte-Verknüpfung fehlt");
            File.WriteAllText("BLE-Delivery/Sensorwerte-Ergebnis.txt","Prefab und SampleScene erweitert. HeartRate, Temperatur und SpO2 werden angezeigt. Canvas 1000 x 920.\n");
            AssetDatabase.DeleteAsset(request);
            Debug.Log("[Cosinuss] Sensorwerte-Canvas aktualisiert.");
        } catch(Exception e) { File.WriteAllText("BLE-Delivery/Sensorwerte-Fehler.txt",e.ToString()); Debug.LogException(e); }
    }
    static void Enhance(GameObject root)
    {
        var ui=root.GetComponent<HeartRateUI>();
        var sensor=root.GetComponent<CosinussMeasurements>() ?? root.AddComponent<CosinussMeasurements>();
        sensor.bluetooth=ui.bluetooth; ui.sensor=sensor;
        var panel=root.transform.Find("Panel");
        var existing=panel.Find("SensorDetailsText");
        ui.sensorDetailsText=existing!=null ? existing.GetComponent<TMP_Text>() : Label(panel,"SensorDetailsText",sensor.Details,22,300);
        ui.sensorDetailsText.transform.SetSiblingIndex(ui.heartRateText.transform.GetSiblingIndex()+1);
        ui.sensorDetailsText.text=sensor.Details;
        ui.sensorDetailsText.fontSize=24; ui.sensorDetailsText.richText=true;
        ui.sensorDetailsText.alignment=TextAlignmentOptions.Center; ui.sensorDetailsText.raycastTarget=false;
        Height(ui.sensorDetailsText.gameObject,180);
        ui.sensorDetailsText.gameObject.layer=root.layer;
        ui.heartRateText.fontSize=72; Height(ui.heartRateText.gameObject,110);
        ui.statusText.fontSize=22;
        var title=panel.Find("Title").GetComponent<TMP_Text>(); title.text="VITALWERTE"; title.fontSize=30;
        ((RectTransform)root.transform).sizeDelta=new Vector2(1000,920);
        EditorUtility.SetDirty(ui); EditorUtility.SetDirty(sensor);
    }

    static void AutoCreate()
    {
        if (!File.Exists(RequestPath) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try { Create(); File.Delete(RequestPath); AssetDatabase.Refresh(); }
        catch (Exception e) { Debug.LogException(e); File.WriteAllText("BLE-Delivery/Canvas-Fehler.txt", e.ToString()); }
    }

    [MenuItem("Tools/Herzfrequenz/Canvas erstellen")]
    public static void Create()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play Mode zuerst beenden.");
        var scene = SceneManager.GetSceneByPath(ScenePath);
        bool opened = !scene.IsValid() || !scene.isLoaded;
        if (opened) scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
        var existing = scene.GetRootGameObjects().FirstOrDefault(x => x.name == "HeartRateCanvas");
        if (existing != null) { Selection.activeGameObject = existing; return; }
        var camera = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<Camera>(true)).FirstOrDefault(x => x.CompareTag("MainCamera"));
        if (camera == null) throw new InvalidOperationException("MainCamera in SampleScene fehlt.");
        var previous = SceneManager.GetActiveScene();
        SceneManager.SetActiveScene(scene);
        var root = new GameObject("HeartRateCanvas", typeof(RectTransform), typeof(Canvas), typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
        Undo.RegisterCreatedObjectUndo(root, "Herzfrequenz-Canvas erstellen");
        var rect = (RectTransform)root.transform;
        rect.sizeDelta = new Vector2(900, 720);
        rect.localScale = Vector3.one * 0.001f;
        rect.position = camera.transform.position + camera.transform.forward * 1.35f;
        rect.rotation = camera.transform.rotation;
        var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace; canvas.worldCamera = camera;
        root.GetComponent<UnityEngine.UI.CanvasScaler>().dynamicPixelsPerUnit = 10;
        root.layer = LayerMask.NameToLayer("UI");
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(UnityEngine.UI.Image), typeof(UnityEngine.UI.VerticalLayoutGroup));
        panel.transform.SetParent(root.transform, false); panel.layer = root.layer;
        var p = (RectTransform)panel.transform; p.anchorMin = Vector2.zero; p.anchorMax = Vector2.one; p.offsetMin = Vector2.zero; p.offsetMax = Vector2.zero;
        panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0.035f,0.055f,0.08f,0.98f);
        var layout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
        layout.padding = new RectOffset(32,32,24,24); layout.spacing = 12;
        layout.childControlWidth = true; layout.childControlHeight = true;
        layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;
        Label(panel.transform,"Title","HERZFREQUENZ · BLE",34,52);
        var bpm = Label(panel.transform,"HeartRateText","— bpm",72,98); bpm.color = new Color(0.35f,1f,0.72f);
        var status = Label(panel.transform,"StatusText","Bereit. Scan drücken.",24,76);
        var dropdownGo = TMP_DefaultControls.CreateDropdown(new TMP_DefaultControls.Resources());
        dropdownGo.name = "DeviceDropdown"; dropdownGo.transform.SetParent(panel.transform,false); Height(dropdownGo,72);
        var dropdown = dropdownGo.GetComponent<TMP_Dropdown>(); dropdown.ClearOptions(); dropdown.AddOptions(new[] {"Nach Scan Gerät auswählen"}.ToList());
        foreach (var text in dropdownGo.GetComponentsInChildren<TMP_Text>(true)) { text.fontSize = 22; text.color = Color.black; }
        var scan = Button(panel.transform,"ScanButton","Scan starten");
        var stop = Button(panel.transform,"StopButton","Scan stoppen");
        var connect = Button(panel.transform,"ConnectButton","Verbinden");
        var disconnect = Button(panel.transform,"DisconnectButton","Trennen");
        var ble = root.AddComponent<HeartRateBLE>();
        var ui = root.AddComponent<HeartRateUI>();
        ui.bluetooth = ble; ui.devices = dropdown; ui.statusText = status; ui.heartRateText = bpm;
        ui.scanButton = scan; ui.stopButton = stop; ui.connectButton = connect; ui.disconnectButton = disconnect;
        var eventSystem = scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<EventSystem>(true)).FirstOrDefault();
        if (eventSystem == null) { var go = new GameObject("EventSystem",typeof(EventSystem),typeof(XRUIInputModule)); eventSystem = go.GetComponent<EventSystem>(); }
        foreach (var module in eventSystem.GetComponents<BaseInputModule>()) if (!(module is XRUIInputModule)) module.enabled = false;
        var xrModule = eventSystem.GetComponent<XRUIInputModule>(); if (xrModule == null) xrModule = eventSystem.gameObject.AddComponent<XRUIInputModule>(); xrModule.enabled = true;
        foreach (var ray in scene.GetRootGameObjects().SelectMany(x => x.GetComponentsInChildren<XRRayInteractor>(true))) { ray.enableUIInteraction = true; EditorUtility.SetDirty(ray); }
        foreach (var t in root.GetComponentsInChildren<Transform>(true)) t.gameObject.layer = root.layer;
        Canvas.ForceUpdateCanvases();
        RenderPreview(canvas);
        var prefab = PrefabUtility.SaveAsPrefabAssetAndConnect(root,PrefabPath,InteractionMode.AutomatedAction);
        EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
        AssetDatabase.SaveAssets();
        // Inspect the saved references and geometry, instead of assuming creation succeeded.
        var saved = prefab.GetComponent<HeartRateUI>();
        if (saved.bluetooth==null || saved.devices==null || saved.scanButton==null || saved.heartRateText==null) throw new InvalidOperationException("Prefab-Verknüpfungen fehlen.");
        File.WriteAllText("BLE-Delivery/Canvas-Ergebnis.txt", "Erstellt: "+PrefabPath+"\nSzene: "+ScenePath+"\nWorld Space, 900 x 720, Maßstab 0.001, Kamera="+camera.name+"\nButtons=4, TMP-Dropdown=1, TMP-Messwert/Status vorhanden\nInspector-Referenzen geprüft, XRUIInputModule aktiv, TrackedDeviceGraphicRaycaster vorhanden.\n");
        Selection.activeGameObject = root;
        if (previous.IsValid() && previous.isLoaded) SceneManager.SetActiveScene(previous);
        Debug.Log("[HeartRateBLE] Canvas erstellt und SampleScene gespeichert: "+PrefabPath);
    }
    static void RenderPreview(Canvas canvas)
    {
        var go = new GameObject("HeartRatePreviewCamera",typeof(Camera));
        go.hideFlags = HideFlags.HideAndDontSave;
        var camera = go.GetComponent<Camera>(); camera.enabled = false;
        var rect = (RectTransform)canvas.transform;
        int width=1000, height=Mathf.CeilToInt(1000f*rect.rect.height/rect.rect.width);
        camera.orthographic = true; camera.orthographicSize = rect.rect.height*canvas.transform.lossyScale.y*0.5f+0.04f;
        camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(0.08f,0.09f,0.12f);
        camera.cullingMask = 1 << canvas.gameObject.layer;
        go.transform.SetPositionAndRotation(canvas.transform.position - canvas.transform.forward * 2, canvas.transform.rotation);
        var target = new RenderTexture(width,height,24);
        var oldCamera = canvas.worldCamera; var oldTarget = RenderTexture.active;
        var texture = new Texture2D(width,height,TextureFormat.RGB24,false);
        try {
            canvas.worldCamera = camera; camera.targetTexture = target;
            Canvas.ForceUpdateCanvases(); camera.Render(); RenderTexture.active = target;
            texture.ReadPixels(new Rect(0,0,width,height),0,0); texture.Apply();
            File.WriteAllBytes("BLE-Delivery/HeartRateCanvas.png",texture.EncodeToPNG());
        } finally {
            canvas.worldCamera = oldCamera; RenderTexture.active = oldTarget; camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture); target.Release(); UnityEngine.Object.DestroyImmediate(target); UnityEngine.Object.DestroyImmediate(go);
        }
    }
    static void Height(GameObject go,float height)
    {
        var element = go.GetComponent<UnityEngine.UI.LayoutElement>() ?? go.AddComponent<UnityEngine.UI.LayoutElement>();
        element.minHeight = height; element.preferredHeight = height; element.flexibleHeight = 0;
    }
    static TMP_Text Label(Transform parent,string name,string value,float size,float height)
    {
        var go = new GameObject(name,typeof(RectTransform),typeof(TextMeshProUGUI)); go.transform.SetParent(parent,false);
        var text = go.GetComponent<TextMeshProUGUI>(); text.text = value; text.fontSize = size;
        text.font = TMP_Settings.defaultFontAsset; text.alignment = TextAlignmentOptions.Center; text.color = Color.white; text.raycastTarget = false;
        Height(go,height); return text;
    }
    static UnityEngine.UI.Button Button(Transform parent,string name,string title)
    {
        var go = TMP_DefaultControls.CreateButton(new TMP_DefaultControls.Resources()); go.name = name; go.transform.SetParent(parent,false); Height(go,64);
        var text = go.GetComponentInChildren<TMP_Text>(); text.text = title; text.fontSize = 26; text.color = Color.white; text.raycastTarget = false;
        go.GetComponent<UnityEngine.UI.Image>().color = new Color(0.12f,0.25f,0.35f);
        return go.GetComponent<UnityEngine.UI.Button>();
    }
}
