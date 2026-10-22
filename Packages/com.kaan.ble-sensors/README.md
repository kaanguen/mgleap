# BLE Sensors (Velorexe) — 1.0.0

Eigenes Unity-UPM-Package für profilbasierte BLE-Sensoren auf Android. Erstellt und mit Unity 2022.3.62f3 kompiliert. Die Magic-Leap-Szene verwendet weiterhin ihre vorhandenen XR-Eingaben.

## In diesem Projekt bereits eingebunden

Das Paket liegt als **Embedded Package** unter `Packages/com.kaan.ble-sensors`. Unity erkennt diesen Paketordner automatisch. Im Package Manager unter **In Project** nach **BLE Sensors (Velorexe)** suchen. Kein zusätzlicher Import derselben Dateien unter Assets nötig.

Die Script- und JAR-Metadaten wurden beim Verschieben erhalten. Dein Canvas, das bearbeitbare Profil und die projektspezifischen Canvas-Werkzeuge bleiben unter `Assets/HeartRateBLE`. `Assets/Gestures.cs` sowie die Magic-Leap-Konfiguration bleiben Teil des Projekts. Das Package installiert kein neues XR-Rig und verändert keine Szene automatisch.

## Struktur

```text
com.kaan.ble-sensors/
  package.json                       Name, Version und Abhängigkeiten
  Runtime/
    Kaan.BleSensors.Runtime.asmdef    Eigene Runtime-Assembly
    BleConnection.cs                 Allgemeine BLE-Verbindung
    BleDeviceProfile.cs              Filter und Kanaldefinitionen
    BleGattQueue.cs                  Serielle GATT-Aufträge
    AndroidBleGattBackend.cs         Verbindung zum nativen JAR
    SensorMeasurements.cs           Auswertung der Profilkanäle
    HeartRateParser.cs               Herzfrequenz und RR
    CosinussParsers.cs               Weitere Datenformate
    CosinussProfileDefaults.cs       Standardprofil
    HeartRateUI.cs                   Anbindung einer uGUI-Oberfläche
    HeartRateBLE.cs                  Kompatibilität zu bisherigen Szenen
    CosinussMeasurements.cs          Kompatibilität zu bisherigen Szenen
    Plugins/Android/cosinuss-gatt.jar
  Editor/
    Kaan.BleSensors.Editor.asmdef
    BleProfileMenu.cs                Neues cosinuss-Profil erstellen
  Documentation~/
    Modular.md                       Architektur und Inspector-Einstellungen
    AndroidManifest-BLE.xml          BLE-Einträge zum Zusammenführen
```

## In einem anderen Unity-Projekt installieren

1. Unity **2022.3** verwenden; die tatsächliche Prüfung erfolgte mit 2022.3.62f3.
2. **Window → Package Manager → + → Add package from git URL** öffnen und zuerst das geprüfte Velorexe-Plugin einfügen:

```text
https://github.com/Velorexe/Unity-Android-Bluetooth-Low-Energy.git#55d8c12e155cc45bc26cea120d5e8c1e5f231b9f
```

3. Anschließend **+ → Add package from tarball** wählen und `com.kaan.ble-sensors-1.0.0.tgz` auswählen. Alternativ das Package in einen dauerhaften Ordner entpacken, **Add package from disk** wählen und dessen `package.json` öffnen. Eine Verknüpfung vom Datenträger hängt von diesem Ordner ab; ihn danach nicht verschieben.
4. Unity löst TextMeshPro 3.0.7, uGUI 1.0.0 und Android-JNI über die Paketabhängigkeiten auf. Das Git-Plugin muss zuerst hinzugefügt werden: Die Paketabhängigkeit kann seine Git-URL nicht automatisch bereitstellen.
5. **Assets → Create → BLE → Cosinuss-Standardprofil** wählen und das Profil im Assets-Ordner speichern. Für andere Geräte **Create → BLE → Geräteprofil** verwenden. Namenfilter und UUIDs im Inspector einstellen.
6. Auf einem eigenen GameObject **BleConnection** und **SensorMeasurements** hinzufügen. Das Profil bei BleConnection zuweisen, den Verbindungsbaustein bei SensorMeasurements im Feld Bluetooth zuweisen.
7. Für die mitgelieferte UI-Anbindung eine uGUI-Oberfläche mit vier Buttons, TMP-Dropdown und TMP-Texten verwenden. HeartRateUI hinzufügen: Bluetooth → BleConnection; Sensor → SensorMeasurements; Devices → Dropdown; Status Text → Statusanzeige; Heart Rate Text → Hauptmesswert; Sensor Details Text → Zusatzwerte; die vier Button-Felder → Scan/Stop/Connect/Disconnect. TMP Essential Resources bei Aufforderung importieren.
8. Die BLE-Einträge aus `Documentation~/AndroidManifest-BLE.xml` mit dem eigenen Android-Manifest zusammenführen. Nicht das gesamte vorhandene Manifest ersetzen. Die Laufzeitabfragen erfolgen im Code.
9. Für Magic Leap zusätzlich das eigene OpenXR-/Handtracking-Setup, World-Space-Canvas, XRUIInputModule und TrackedDeviceGraphicRaycaster konfigurieren. Diese projektabhängigen Teile werden nicht durch dieses BLE-Package installiert.
10. Android-Build auf dem Gerät testen. Der Unity-Editor führt keinen BLE-Funkscan aus.

Vorhandene alte Kopien dieser Skripte/JARs unter Assets dürfen in einem Zielprojekt nicht gleichzeitig mit dem Package existieren, sonst entstehen doppelte Typen oder native Klassen. Im aktuellen MarkerML2-Projekt wurden sie bereits verschoben.

## Abhängigkeiten und Grenzen

Velorexe bleibt erforderlich: Paketversion **0.0.6**, geprüfte Git-Revision **55d8c12e155cc45bc26cea120d5e8c1e5f231b9f**. Das native Companion-JAR verwendet ein privates Feld dieser Plugin-Version. Ein Plugin-Update muss daher erneut geprüft werden. Das JAR wurde unverändert übernommen; das frühere Java-Quell-/Buildpaket war zum Zeitpunkt dieser Paketmigration nicht mehr im Projekt vorhanden.

Ein Geräteprofil garantiert keine Unterstützung beliebiger Geräteprotokolle. Unbekannte Formate brauchen einen Decoder. Kein allgemeiner Schreibdialog, keine individuellen Unsubscribe-Schalter. Grenzen bei autoConnect-Abbruch und DescriptorWrite-Status bleiben bestehen. Bei aktivierter Android-Minifizierung müssen die Companion-Klasse und die angesprochenen Plugin-Klassen/Felder erhalten bleiben.

## Prüfstand

- Runtime für Android und Editor kompiliert; Editor-Werkzeuge getrennt dagegen kompiliert.
- 34 automatisierte Profil-/Queue-/Parser-Prüfungen erfolgreich.
- Script-GUIDs und JAR-Hash beim Verschieben erhalten.
- Die tatsächliche Unity-Paket-/Prefab-Prüfung des Arbeitsprojekts wird in `BLE-Delivery/Package-Ergebnis.txt` protokolliert.
- Kein neuer Hardwaretest und kein frischer Android-Build allein durch diese Paketmigration.

[Unity: Eigene Packages erstellen](https://docs.unity3d.com/2022.3/Documentation/Manual/CustomPackages.html)
