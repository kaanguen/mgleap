# cosinuss-Sensorwerte auf der Magic Leap 2

Das vorhandene Herzfrequenz-Canvas zeigt jetzt zusätzliche Werte, **wenn der Sensor die entsprechenden Kanäle anbietet**:

- Herzfrequenz und RR-Abstände.
- Temperatur, SpO2 und Perfusionsindex.
- Akku und Signalqualität.
- Modell, Hersteller, Firmware, Software und Hardware-Revision.
- Vom Sensor gemeldete Hinweise sowie dessen Bluetooth-Adresse.

`tree`/`thre`/`three` bleibt der Namensfilter. Ein passender Name bestätigt kein bestimmtes Sensormodell.

## Im aktuellen Projekt

Die Skripte, die kleine native Ergänzung, das Prefab und SampleScene wurden ergänzt. Inspector-Verweise sind gesetzt. Nach dem Import **Build And Run** ausführen, scannen, Gerät auswählen und verbinden. Die Kanäle werden automatisch nacheinander eingerichtet. Bei „nicht angeboten“ fehlt der Kanal. „Wartet auf Daten“ bestätigt noch keinen empfangenen Wert.

## Dateien

```text
Assets/
  HeartRateBLE/
    HeartRateBLE.cs               Verbindung und Herzfrequenz
    HeartRateParser.cs            Herzfrequenz/RR-Parser
    CosinussMeasurements.cs       Zusätzliche Kanäle und serielle GATT-Aufträge
    CosinussParsers.cs            Temperatur, PLX, Akku, Sensorhinweise
    HeartRateUI.cs                Oberfläche
    HeartRateCanvas.prefab        Fertiges Canvas mit Verknüpfungen
    Editor/HeartRateCanvasSetup.cs
  Plugins/Android/
    cosinuss-gatt.jar             Kleine native Ergänzung
Native/
  CosinussGatt.java               Vollständiger Java-Quellcode
  build_native.py                Reproduzierbarer JAR-Build
  keep-rules.pro                 Regeln für optionales Android-Minify
```

Im Exportpaket liegen Java-Quellcode und Tests außerhalb von Assets. Für ein anderes Projekt den Assets-Inhalt mit seinen `.meta`-Dateien kopieren und HeartRateCanvas in eine funktionierende Magic-Leap-XR-Szene ziehen. Canvas Event Camera auf die XR-Kamera setzen und vor dem Benutzer platzieren. Genau ein EventSystem mit XR UI Input Module und ein eingerichteter Hand-/Controller-Ray müssen vorhanden sein. Für die neu hinzugefügten Werte sind keine manuellen Inspector-Verknüpfungen nötig.

## Kostenloses Velorexe-Plugin

Geprüfte Version **0.0.6**, Git-Revision **55d8c12e155cc45bc26cea120d5e8c1e5f231b9f**. Package-Manager-Git-URL:

`https://github.com/Velorexe/Unity-Android-Bluetooth-Low-Energy.git#55d8c12e155cc45bc26cea120d5e8c1e5f231b9f`

Das ursprüngliche Plugin bleibt unverändert. Seine bestehenden Scan-, Verbindungs- und Daten-Callbacks werden weiterverwendet. Keinen zweiten BleManager oder das Beispiel gleichzeitig starten.

Temperatur braucht Indications. `cosinuss-gatt.jar` ergänzt deshalb das Setzen des CCCD anhand der tatsächlich vorhandenen NOTIFY-/INDICATE-Properties. Sie nutzt die bestehende Velorexe-Verbindung. Dafür greift sie per Java-Reflection auf das private Plugin-Feld `mLeGattServers` zu. Das ist keine Android-Hidden-API, bindet diese Ergänzung aber ausdrücklich an den geprüften Plugin-Stand. Plugin-Upgrades erfordern eine erneute Prüfung.

Leseaufträge und CCCD-Schreibvorgänge laufen nacheinander. Bei einem GATT-Zeitlimit wird die Verbindung geschlossen, um keine weiteren Aufträge über einen unklaren Zustand zu starten. Das ursprüngliche Plugin reicht den Status eines DescriptorWrite nicht vollständig weiter: Erst eingehende Messpakete bestätigen den Datenfluss.

## Android-Einrichtung

Magic-Leap-OpenXR-Projekt, Minimum API 29, Target SDK mindestens 31 mit installiertem SDK, IL2CPP, x86-64 und Vulkan. Ein schon funktionierendes ML2-Projekt beibehalten. Custom Main Manifest muss aktiv sein. Die BLE-Ergänzungen im vorhandenen Manifest direkt unter `<manifest>` einfügen, falls noch nicht vorhanden:

```xml
<uses-feature android:name="android.hardware.bluetooth_le" android:required="true" />
<uses-permission android:name="android.permission.BLUETOOTH" android:maxSdkVersion="30" />
<uses-permission android:name="android.permission.BLUETOOTH_ADMIN" android:maxSdkVersion="30" />
<uses-permission android:name="android.permission.ACCESS_FINE_LOCATION" android:maxSdkVersion="30" />
<uses-permission android:name="android.permission.BLUETOOTH_SCAN" android:usesPermissionFlags="neverForLocation" />
<uses-permission android:name="android.permission.BLUETOOTH_CONNECT" />
```

Die übrigen Activity-/Magic-Leap-Einträge behalten. Auf Android 10 fragt der Scan Standortzugriff an; außerdem die Standortdienste einschalten. Ab Android 12 werden SCAN/CONNECT angefragt. `neverForLocation` setzt voraus, dass keine Standortbestimmung aus BLE erfolgt, und kann bestimmte Beacons ausfiltern. [Android-Dokumentation](https://developer.android.com/develop/connectivity/bluetooth/bt-permissions)

## SpO2-Format im Inspector

Auf HeartRateCanvas sitzt zusätzlich **CosinussMeasurements** mit `Oxygen Format`:

- **Auto:** erkennt den Legacy-Fall nur bei lesbarem Modell „Two“ und Firmware bis einschließlich 2.0.2. Sonst IEEE 11073 als ausdrücklich angezeigte Standardannahme.
- **Standard11073:** medizinisches SFLOAT mit Dezimalexponent.
- **LegacyIeeeHalf:** altes IEEE-754-Half-Format.

Wenn die Firmware nur eine Commit-Kennung liefert, ist automatische Versionszuordnung nicht möglich. Werte nicht anhand ihres Aussehens einer Kodierung zuordnen; zuerst Modell/Firmware klären. NaN/Infinity werden als nicht verfügbar angezeigt. SpO2-Statusbits werden angezeigt; bei entsprechenden Warnbits steht „Status prüfen“.

## Native Ergänzung selbst bauen

Das fertige JAR ist enthalten. Ein Neubau erfordert keine zusätzliche Bibliothek und keine native CPU-Bibliothek. Verwende das JDK und Android-SDK deiner Unity-Installation. Beispiel auf macOS, Pfade an die Installation anpassen:

```sh
python3 Native/build_native.py \
  --jdk '/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer/OpenJDK' \
  --android-jar '/Applications/Unity/Hub/Editor/2022.3.62f3/PlaybackEngines/AndroidPlayer/SDK/platforms/android-34/android.jar' \
  --plugin-jar '/ABSOLUTER/PROJEKTPFAD/Library/PackageCache/com.velorexe.androidbluetoothlowenergy@55d8c12e15/Plugins/Android/unityandroidble.jar'
```

Der vollständige PackageCache-Ordnername kann variieren. Das Skript schreibt standardmäßig `Assets/Plugins/Android/cosinuss-gatt.jar` innerhalb dieses Pakets. Im Unity-Plugin-Inspector nur Android aktivieren. Bei aktiviertem Android Minify/R8 die Regeln aus `Native/keep-rules.pro` in die **Custom Proguard File** übernehmen. Im aktuellen Projekt ist Minify deaktiviert.

## Testen und Grenzen

1. Neu auf die Brille bauen und die Berechtigungen erlauben.
2. Sensor einschalten, korrekt tragen und andere GATT-Verbindungen trennen.
3. Scannen, auswählen und verbinden.
4. Modell-/Firmwareangaben und vorhandene Kanäle prüfen. Nicht jedes Modell bietet SpO2 an.
5. Im Log nach `[Cosinuss]` und `[HeartRateBLE]` suchen. Diese zeigen die gefundenen UUIDs und den angeforderten Update-Modus.

Temperatur wird nach 35 Sekunden ohne Update ausgeblendet, Akku nach fünf Minuten, weitere Zusatzwerte nach 15 Sekunden. Sensorhinweise sind einzelne empfangene Meldungen und keine bestätigte Defektdiagnose. Proprietäre Rohdaten, PPG, Beschleunigungsdaten, unbekannte Aktivierungs-/Authentifizierungsbefehle und Firmware-Updates sind nicht implementiert.

[Protokollhinweise und Quellen](COSINUSS.md), [tatsächlich ausgeführte Prüfungen](PRUEFUNG.md).
