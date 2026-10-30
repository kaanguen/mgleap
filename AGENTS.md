# MarkerML2

AR-Lauftraining für Magic Leap 2 mit Unity/C#, URP und OpenXR. Zweck, Architektur und Bedienung: [README.md](README.md).

- Unity-Version aus `ProjectSettings/ProjectVersion.txt` verwenden. Das Magic-Leap-SDK in `Packages/manifest.json` ist über einen rechnerabhängigen lokalen Pfad eingebunden.
- Assets über Unity verschieben oder ihre `.meta`-Dateien mitnehmen, damit GUIDs und Szenenreferenzen erhalten bleiben.
- Neue Lauftrainingsskripte im passenden Fachordner unter `Assets/Scripts/StadionTracking/` ablegen. Editor-Werkzeuge bleiben unter `Assets/Editor/`.
- Logikänderungen mit `python3 Tools/run_logic_checks.py` prüfen (.NET SDK ≥ 6; Projekt zuvor einmal in Unity öffnen). Die eigenständigen Checks sind keine Unity-Test-Runner-Tests. Details: README, Abschnitt „Tests und Hilfsprogramme“.
- Nach C#-Änderungen Unity kompilieren lassen und die Konsole auf Fehler prüfen. Geräte-Build: `Tools > Treadmill > Build device verification APK`; Implementierung: `Assets/Editor/TreadmillDeviceBuild.cs`.
- XR-Platzierung und Animationen zusätzlich in Unity beziehungsweise auf der Brille prüfen; nicht durchgeführte Prüfungen benennen.
- Der RunningServer ist ein separates Repository. Protokoll: [Docs/RunningServer.md](Docs/RunningServer.md). C#-Änderungen erfordern einen neuen Geräte-Build.

Weitere Dokumentation nur bei Bedarf lesen: [BLE-Paket](Packages/com.kaan.ble-sensors/README.md) für Sensoranbindung, [UI-Styleguide](Assets/UI/STYLE_GUIDE_IMPLEMENTATION.md) für Oberflächen.

## Our Roles

- The user leads the conceptual work and makes scientific and methodological decisions.
- The agent implements individual code sections within the agreed scope.
- The agent researches and explains relevant knowledge and options to support the user's thinking.
- The agent critically examines the user's concepts and decisions for scientific soundness and explains concerns and uncertainties.
- The agent points out overlooked assumptions, missing information, and relevant considerations the user may have forgotten.
