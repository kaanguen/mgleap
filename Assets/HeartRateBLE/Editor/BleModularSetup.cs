using System;
using System.IO;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

[InitializeOnLoad]
public static class BleModularSetup
{
    const string Request = "Assets/HeartRateBLE/Editor/Modularize.request";
    const string ProfilePath = "Assets/HeartRateBLE/Profiles/Cosinuss.asset";
    static BleModularSetup() { EditorApplication.delayCall += Requested; }
    static void Requested()
    {
        if (File.Exists(Request) && !EditorApplication.isPlayingOrWillChangePlaymode) Apply();
    }
    [MenuItem("Tools/Herzfrequenz/Modulares Geräteprofil einrichten")]
    public static void Apply()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Play zuerst stoppen.");
        try
        {
            if (!AssetDatabase.IsValidFolder("Assets/HeartRateBLE/Profiles")) AssetDatabase.CreateFolder("Assets/HeartRateBLE", "Profiles");
            var profile = AssetDatabase.LoadAssetAtPath<BleDeviceProfile>(ProfilePath);
            if (profile == null) { profile=CosinussProfileDefaults.Create(); AssetDatabase.CreateAsset(profile,ProfilePath); }
            profile.Validate();
            VerifyMeasurements(profile);
            const string prefabPath="Assets/HeartRateBLE/HeartRateCanvas.prefab";
            var root=PrefabUtility.LoadPrefabContents(prefabPath);
            try { Wire(root,profile); PrefabUtility.SaveAsPrefabAsset(root,prefabPath); }
            finally { PrefabUtility.UnloadPrefabContents(root); }
            const string scenePath="Assets/Scenes/SampleScene.unity";
            var scene=SceneManager.GetSceneByPath(scenePath);
            bool opened = !scene.IsValid() || !scene.isLoaded;
            if (opened) scene=EditorSceneManager.OpenScene(scenePath,OpenSceneMode.Additive);
            foreach (var go in scene.GetRootGameObjects()) if (go.GetComponent<HeartRateUI>() != null) Wire(go,profile);
            EditorSceneManager.MarkSceneDirty(scene); EditorSceneManager.SaveScene(scene);
            if (opened) EditorSceneManager.CloseScene(scene,true);
            AssetDatabase.SaveAssets();
            Directory.CreateDirectory("BLE-Delivery");
            File.WriteAllText("BLE-Delivery/Modular-Ergebnis.txt","Geräteprofil erstellt/zugewiesen. Prefab und SampleScene gespeichert. Bestehende BLE-/Sensor-/UI-Referenzen auf Basistypen geprüft. Unity-Messwertweitergabe mit synthetischen HR-, RR-, Temperatur- und Akku-Paketen geprüft (kein Funk-/Hardwaretest).\n");
            if (File.Exists(Request)) AssetDatabase.DeleteAsset(Request);
            Debug.Log("[BLE] Modulares Geräteprofil eingerichtet und Referenzen geprüft.");
        }
        catch (Exception e) { Debug.LogException(e); Directory.CreateDirectory("BLE-Delivery"); File.WriteAllText("BLE-Delivery/Modular-Fehler.txt",e.ToString()); }
    }
    static void VerifyMeasurements(BleDeviceProfile preset)
    {
        // Inactive objects: no live BLE scan, Android calls or Update loop.
        var go=new GameObject("BLE verification"); go.SetActive(false);
        var session=CosinussProfileDefaults.Create();
        const BindingFlags flags=BindingFlags.NonPublic | BindingFlags.Instance;
        var connected=typeof(BleConnection).GetField("<Connected>k__BackingField",flags);
        var active=typeof(BleConnection).GetField("<ActiveProfile>k__BackingField",flags);
        BleConnection ble=null;
        try
        {
            ble=go.AddComponent<HeartRateBLE>(); ble.profile=preset;
            var sensor=go.AddComponent<CosinussMeasurements>(); sensor.bluetooth=ble;
            connected.SetValue(ble,true); active.SetValue(ble,session);
            var found=(HashSet<string>)typeof(BleConnection).GetField("foundCharacteristics",flags).GetValue(ble);
            foreach(var c in session.channels) found.Add(BleUuid.Key(c.Service,c.Characteristic));
            typeof(SensorMeasurements).GetMethod("Configure",flags).Invoke(sensor,new object[]{"TEST-ONLY"});
            var data=typeof(SensorMeasurements).GetMethod("OnData",flags);
            data.Invoke(sensor,new object[]{BleUuid.Normalize("180d"),BleUuid.Normalize("2a37"),new byte[]{16,72,0,4}});
            if (sensor.Measurement!="72 bpm" || sensor.RrIntervals!="1000 ms") throw new Exception("HR/RR routing failed.");
            data.Invoke(sensor,new object[]{BleUuid.Normalize("1809"),BleUuid.Normalize("2a1c"),new byte[]{0,0x6a,8,0,0xfe}});
            data.Invoke(sensor,new object[]{BleUuid.Normalize("180f"),BleUuid.Normalize("2a19"),new byte[]{96}});
            if (!sensor.Details.Contains("21.54 °C") || !sensor.Details.Contains("96 %")) throw new Exception("Temperature/battery routing failed.");
        }
        finally
        {
            if (ble!=null) { connected.SetValue(ble,false); active.SetValue(ble,null); }
            UnityEngine.Object.DestroyImmediate(go);
            UnityEngine.Object.DestroyImmediate(session);
        }
    }
    static void Wire(GameObject root,BleDeviceProfile preset)
    {
        var ui=root.GetComponent<HeartRateUI>();
        if (ui == null || ui.bluetooth == null || ui.sensor == null || ui.sensor.bluetooth != ui.bluetooth)
            throw new InvalidOperationException("Bestehende UI-/Sensor-Referenzen fehlen: " + root.name);
        if (ui.bluetooth.profile == null)
        {
            var legacy=ui.bluetooth as HeartRateBLE;
            if (legacy != null && (BleUuid.Normalize(legacy.serviceUuid)!=BleUuid.Normalize("180d") || BleUuid.Normalize(legacy.characteristicUuid)!=BleUuid.Normalize("2a37")))
            {
                preset=CosinussProfileDefaults.Create(legacy.serviceUuid,legacy.characteristicUuid);
                AssetDatabase.CreateAsset(preset,AssetDatabase.GenerateUniqueAssetPath("Assets/HeartRateBLE/Profiles/Custom.asset"));
            }
            ui.bluetooth.profile=preset;
        }
        ui.bluetooth.profile.Validate();
        EditorUtility.SetDirty(ui.bluetooth);
        if (PrefabUtility.IsPartOfPrefabInstance(root)) PrefabUtility.RecordPrefabInstancePropertyModifications(ui.bluetooth);
    }
}
