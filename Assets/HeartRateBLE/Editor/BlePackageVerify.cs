using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.PackageManager;
using UnityEngine;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

[InitializeOnLoad]
public static class BlePackageVerify
{
    const string Request="Assets/HeartRateBLE/Editor/VerifyPackage.request";
    static BlePackageVerify() { EditorApplication.delayCall += Run; }
    static void Run()
    {
        if (!File.Exists(Request) || EditorApplication.isPlayingOrWillChangePlaymode) return;
        try
        {
            var package=PackageInfo.FindForAssembly(typeof(BleConnection).Assembly);
            if (package==null || package.name!="com.kaan.ble-sensors") throw new Exception("Runtime nicht als eigenes Package geladen.");
            if (typeof(BleConnection).Assembly.GetName().Name!="Kaan.BleSensors.Runtime") throw new Exception("Falsche Runtime-Assembly.");
            var upstream=PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p=>p.name=="com.velorexe.androidbluetoothlowenergy");
            if (upstream==null || upstream.version!="0.0.6") throw new Exception("Geprüftes Velorexe-Paket fehlt.");
            string path=AssetDatabase.GUIDToAssetPath("f92ea5ca7783d4b17846d57e3a334570");
            if (path!="Packages/com.kaan.ble-sensors/Runtime/HeartRateBLE.cs") throw new Exception("Script-GUID wurde nicht erhalten: "+path);
            BleModularSetup.Apply();
            var prefab=AssetDatabase.LoadAssetAtPath<GameObject>("Assets/HeartRateBLE/HeartRateCanvas.prefab");
            var ui=prefab.GetComponent<HeartRateUI>();
            if (ui==null || ui.bluetooth==null || ui.sensor==null || ui.bluetooth.profile==null) throw new Exception("Prefab-Referenz fehlt.");
            Directory.CreateDirectory("BLE-Delivery");
            File.WriteAllText("BLE-Delivery/Package-Ergebnis.txt",package.name+"@"+package.version+"\nQuelle: "+package.source+"\nPfad: "+package.resolvedPath+"\nVelorexe: "+upstream.version+"\nRuntime-Assembly, Script-GUID, Canvas-Referenzen und Geräteprofil geprüft.\n");
            AssetDatabase.DeleteAsset(Request);
            Debug.Log("[BLE Package] Package und bestehende Szene erfolgreich geprüft.");
        }
        catch(Exception e) { Debug.LogException(e); Directory.CreateDirectory("BLE-Delivery"); File.WriteAllText("BLE-Delivery/Package-Fehler.txt",e.ToString()); }
    }
}
