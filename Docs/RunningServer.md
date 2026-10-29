# RunningServer ↔ MarkerML2: Bedienung und Protokoll

**Stand: 27.10.2026.** Die vollständige Bedienungsanleitung steht in der [README](../README.md). Diese Datei beschreibt die aktuelle Unity-Schnittstelle. Neue Befehle benötigen einen entsprechend neu gebauten Client auf der Brille.

## Einrichtung



```bash
npm start
```

Dashboard: `http://localhost:8081/`. WebSocket: `ws://<Serveradresse>:8081/ws`.

Für USB:

```bash
adb devices
adb reverse tcp:8081 tcp:8081
```

Die aktuelle WLAN-Adresse in `Assets/Resources/RunnerServer.json` ist `ws://192.168.178.120:8081/ws`. Bei einem Wechsel der Mac-IP muss sie angepasst werden. Eine `runner-server.json` in `Application.persistentDataPath` überschreibt diese Adresse ohne Neubau; auf Android liegt sie unter `/sdcard/Android/data/com.testingCorp.MarkerML2/files/runner-server.json`. Für USB-Portweiterleitung dort stattdessen `ws://127.0.0.1:8081/ws` eintragen.

## Verbindung

Die Unity-App sendet nach dem WebSocket-Verbindungsaufbau:

```json
{"type":"hello","role":"headset","deviceId":"<Geräte-ID>","platform":"Magic Leap 2"}
```

Danach sendet sie `registry` mit `project: "MarkerML2"`, den unterstützten Befehlen und dem HUD `stadium`. Das Dashboard meldet sich als `role: "dashboard"` an. Ein Server unterstützt eine Brille und mehrere Dashboards.

Die App verbindet sich mit wachsender Wartezeit erneut. Ein Verbindungsabbruch pausiert die Session. Ausdrücklich mit **Weiter** fortsetzen. Während einer Trennung gesendete Befehle werden nicht nachgeholt.

## Befehl senden und Bestätigung lesen

Beispiel für unabhängiges festes Nutzertempo:

```bash
curl -X POST http://localhost:8081/api/command \
  -H 'Content-Type: application/json' \
  -d '{"commandType":"runningMate.setUserSpeed","commandPayload":{"source":"FixedTest","speedKmh":10}}'
```

HTTP 202 mit `commandId` bedeutet nur, dass der Server den Befehl zur Weiterleitung angenommen hat. Die Brille prüft und verarbeitet ihn auf dem Unity-Hauptthread und antwortet mit:

```json
{"type":"ack","commandId":"<ID>","success":true,"data":{"state":"Running"}}
```

Bei Fehlern: `success: false` und `error`. Fehlende Bestätigungen laufen nach zehn Sekunden ab. Ohne verbundene Brille liefert `/api/command` HTTP 503. Das Dashboard zeigt Befehle und Bestätigungen im Log. Es deaktiviert Funktionen, die der Client nicht in seiner Registry meldet.

`type`, `commandId` und `commandType` müssen nicht leere Strings sein. `commandPayload` darf ein Objekt sein, fehlen oder null sein. Nachrichten ohne gültigen Typ oder gültige Befehls-ID werden verworfen; bei gültiger ID werden ungültige Befehlsfelder mit `success: false` bestätigt. Sie unterbrechen die Verarbeitung nachfolgender Nachrichten nicht.

## Unterstützte Befehle

| Befehl | Payload / Wirkung |
| --- | --- |
| `runningMate.configure` | Optional `paceSeconds` (180–1200), `mode` (`SideBySide`/`TempoPartner`), `showTrack` (Boolean). Erhält Session-Statistik und Ghost-Aufnahme und pausiert. Pace betrifft den Avatar, nicht das Nutzertempo. |
| `runningMate.command` | `action`: `spawn`, `start`, `pause`, `resume`, `stop`, `reset`, `despawn`. `start`/`spawn` setzen Bahn 3; `resume` erhält die ausgewählte Bahn. |
| `runningMate.setLane` | `lane` als Ganzzahl 1–8. Gilt für beide Avatar-Modi. |
| `runningMate.setUserSpeed` | `source: "FixedTest"` mit `speedKmh` (0–100) oder `source: "Treadmill"`. In der echten Stadionumgebung nicht aktiv. Quellenwechsel erhält den Laufzustand und die Avatar-Pace. |
| `runningMate.setGhostSpeed` | `speedKmh`: nicht-negative endliche Zahl für unabhängiges Ghost-Testtempo; `null` stellt normale Bestzeit-/Zielpace-Wiedergabe wieder her. |
| `scene.setTrackEnvironment` | `environment: "Treadmill"` oder `"Stadium"`. Beginnt eine neue pausierte Session und setzt deren Statistik zurück. |
| `scene.rescanMarker` | `{}`. Pausiert, blendet Bahn/Avatar aus und wartet auf neuen Bodenmarker samt Plane. |
| `scene.captureTrackPoint` | `point: "A"` oder `"B"`; bei B optional `distanceAlongTrack` (5–100 m, Standard 20). |
| `runningMate.adjust` | Absolute `along`, `across` (je ±100 m), `heading` (±180°) oder `reset: true`. Werte relativ zur letzten Grundkalibrierung. |
| `avatar.configurePlacement` | Nur SideBySide in der Laufbandumgebung: `position` (N/NE/E/SE/S/SW/W/NW), `facing` (FaceUser/BackToUser/LeftShoulder/RightShoulder), `distance` (0,5–20 m). |
| `avatar.reposition` | `{}`. Bei Marker-Platzierung zur gespeicherten Markerposition und -richtung auf der aktuellen Boden-Plane zurückkehren; keine Ausrichtung an der Kopfposition. Im Stadionmodus A/B verwenden. |
| `scene.setRunningMatePathVisible` | `visible` (Boolean). Blendet die Bahn ein/aus. |
| `scene.setRunningMateHudVisible` | `visible` (Boolean). Blendet das Headset-HUD ein/aus. |
| `scene.setHudVisible` | `id: "stadium"`, `visible` (Boolean). |
| `sensor.connect` / `sensor.disconnect` | Vorhandene BLE-Komponente verwenden. Verbinden verlangt bereits entdeckte Geräte. Keine automatische neue BLE-Suche über diese Befehle. |

Kompatibilitätsbefehle bleiben im Code: `heightCm` bei `runningMate.configure`, `scene.recalibrate` mit `includeHeight` und `scene.adjustHeight` mit `deltaMetres`. Bei Marker-/Plane-Platzierung bestimmt ausschließlich die erkannte Boden-Plane die Höhe; manuelle Höhenänderungen werden abgewiesen. Eine erneute Ausrichtung verwendet den gespeicherten Markeranker. Die Körpergröße beeinflusst die Marker-Platzierung nicht.

Kalibrierungs-, Platzierungs- und Bahnauswahlbefehle verlangen eine pausierte und gültig platzierte Session. Das Dashboard pausiert dafür vorab und wartet auf die Bestätigung. Geschwindigkeitsquellen- und Ghost-Tempoänderungen pausieren den laufenden Avatar dagegen nicht. Explizite Pause, Stoppen und Verbindungsabbruch frieren die Session ein.

Die Serverfamilien `avatar.*`, `scene.*`, `exercise.*`, `runningMate.*`, `sensor.*` sind ein Envelope-Filter. Daraus folgt keine Unterstützung aller generischen Avatar-/Übungsfunktionen. Die Unity-Registry und Handler sind maßgeblich.

## Verbindungs- und Trackingdiagnose

Der Startdialog zeigt die Zieladresse sowie Zeitüberschreitungen, abgelehnte Verbindungen und nicht erreichbare Hosts. Die Adresse bleibt während eines Versuchs editierbar; erneutes Verbinden bricht den vorherigen Versuch ab. Ein WebSocket-Handshake wird spätestens nach fünf Sekunden explizit beendet. Verbindungsabbruch pausiert weiterhin den Lauf; Wiederverbindung startet ihn nicht automatisch.

Das HUD unterscheidet Markersuche, Boden erkannt, Bahn platziert und Tracking verloren. Die Telemetrie ergänzt `trackingState`, `trackingStatus` und `recordsStatus`; bestehende Statuswerte bleiben Strings.

## Geschwindigkeiten und Uhr

- **Nutzer-Testtempo:** frei eingestellte Nutzer-km/h; bewegt die virtuelle Welt.
- **Laufbandquelle:** zukünftiger Messwert-Eingang. Keine aktuelle Hardware-Anbindung. Ohne gültiges, höchstens zwei Sekunden altes Signal: 0 km/h Nutzerfortschritt.
- **Avatar:** eigene Zielpace, Bestzeitwiedergabe oder Ghost-Testgeschwindigkeit. Ein laufender Tempo-Partner läuft auch bei 0 km/h Nutzerfortschritt mit eigener Runden-Uhr über Rundengrenzen weiter, ohne auf den Nutzer zu warten. SideBySide behält die relative Nutzerposition und verwendet seine Zielpace für die Laufanimation.
- **Echte Stadionbahn:** feste Welt und aus Headtracking projizierter Nutzerfortschritt.

Die Session-Uhr läuft nach ausdrücklichem Start bei gültiger Platzierung auch dann, wenn das Nutzertempo 0 ist. Vor einem Start bleibt der automatisch erschienene Avatar stehen.

Ein späterer Laufband-Adapter liefert auf dem Unity-Hauptthread `ReportTreadmillSpeedKmh(...)` und meldet eine Trennung über `ClearTreadmillSignal()`. Diese API steuert keinen Laufbandmotor.

## Telemetrie

Unity sendet ungefähr mit 5 Hz ein `telemetry`-Objekt. Die aktuellen Laufwerte stehen in `data.treadmill`; `data.runningMate` enthält dieselben Daten zur Kompatibilität.

| Feld | Bedeutung |
| --- | --- |
| `state` | Stopped / Ready / Running / Paused |
| `distance`, `elapsed`, `laps` | Nutzerdistanz in Metern, Zeit in Sekunden, vollständige 400-m-Referenzrunden |
| `speedKmh`, `speed` | Nutzer-Geschwindigkeit in km/h bzw. m/s |
| `userSpeedSource` | FixedTest / Treadmill / HeadTracking |
| `fixedUserSpeedKmh`, `treadmillSignalReady` | Eingestelltes Testtempo und Frische des zukünftigen Laufbandsignals |
| `environment`, `avatarLane`, `mode` | Treadmill/Stadium, Bahn 1–8, SideBySide/TempoPartner |
| `headPoseReady`, `heightConfirmed`, `placementReady`, `trackVisible` | Tracking-/Platzierungsstatus und gewünschte Streckensichtbarkeit |
| `trackPositionMetres` | Position auf der 400-m-Referenzlinie |
| `markerPlacement` | `required`, `floorConfirmed`, `markerId`, lesbarer `status` |
| `calibration` | `along`, `across`, `heading`, `surfaceHeight` |
| `stadiumCalibration` | `pointA`, `calibrated`, `baselineMetres`, `raceStartTrackMetres` |
| `placement` | Sweet-Spot-Entwurf/Anwendung und `laneActive` |
| `ghost` | `active`, `distance`, `trackPositionMetres`, `speedKmh`, `gapMetres`, `testMode`, `testSpeedKmh`, `recordedLap`, `hasRecording`, `playbackMode` (`TestSpeed`/`RecordedLap`/`TargetPace`) |
| `records` | `fastest100mSeconds`, `bestLapSeconds`, `fastest1KmSeconds`: gemessene Zeiten oder `null` |

Zusätzlich: `data.ble.connected`, `data.ble.spo2` (frischer gültiger SpO₂-Wert oder `null`) und `data.huds.stadium`. Andere BLE-Vitalwerte und Übungserkennungen werden von dieser Brücke noch nicht geliefert. Verschachtelte Objekte im Relay werden ersetzt; der Client sendet vollständige Teilobjekte.

## Persönliche Bestleistungen auf der Brille

Gemessene 100-m-/Runden-/1-km-Bestzeiten und das Ghost-Profil der besten vollständigen Runde liegen unter `Application.persistentDataPath/runner-records/`. Laufband, reale Stadionbahn und Vorschau-Rundenlängen verwenden getrennte Profile. Schnellere Einzelrekorde werden zusammengeführt; langsamere Runden überschreiben keine bessere gespeicherte Runde. Beim Neustart oder Lauf-Reset werden passende persönliche Bestleistungen geladen, während Uhr und Nutzerdistanz neu beginnen. Eine laufende oder unvollständige Aufnahme wird nicht wiederaufgenommen.

Dateien werden über einen temporären Schreibpfad ersetzt und durch `.bak` gesichert. Beschädigte Hauptdateien werden nach Möglichkeit aus dieser Sicherung geladen. Probleme erscheinen im Log und in `recordsStatus`.

Die Dateioperationen laufen in einer gemeinsamen Hintergrundwarteschlange. Fehlgeschlagene Schreibaufträge bleiben über Lauf-Resets und Szenenwechsel hinweg ausstehend und werden automatisch erneut versucht. Beim Laden werden auch noch ausstehende Bestleistungen berücksichtigt. Eine beschädigte Hauptdatei darf die gültige Sicherung beim nächsten Schreiben nicht ersetzen.

## Sessions und Dateien

Server-Session und Unity-Lauf sind getrennte Abläufe. HTTP-Endpunkte:

- `GET /api/status`, `GET /api/registry`
- `POST /api/command`
- `POST /api/session/start` mit optionalem `name`, `POST /api/session/stop`
- `POST /api/recording/start`, `POST /api/recording/stop`

Aufnahmen liegen in `RunningServer/recordings/<ID>/`: `ble_data.csv`, `exercise_detections.csv`, `commands.jsonl`, `session_meta.json`. Es gibt noch keinen eigenen Export für Laufbahnpositionen oder Rekordzeiten. Bei `Ctrl+C` wird eine aktive Aufnahme geordnet abgeschlossen.

## Geprüfter Stand

Der Marker-/Floor-Ray-Spawn wurde mit Marker 39 auf der Magic Leap 2 bestätigt: `floorConfirmed: true`, `placementReady: true`, `avatarLane: 3`, `ghost.active: true`, gestoppte Session. Die danach ergänzte Geschwindigkeitsquellen-Auswahl und unabhängige Pace-Anpassung sind kompiliert/getestet, aber noch nicht im zuletzt installierten Build enthalten. Die feste reale 400-m-Bahn und echte Laufband-Hardware sind noch nicht durch einen vollständigen Geräte-Lauf verifiziert.

## Schwebende Sammelkugeln und Sauerstoffsensor

Im Dashboard unter **Laufband → Sammelkugeln** die Quelle auswählen und mit **Kugelmenge anwenden** an die Brille senden:

- **Feste Eingabe:** 0–30 Kugeln pro Reihe, unabhängig vom Sensor. 0 schaltet die Kugeln aus. Standard: 10.
- **Sauerstoffsensor:** aktuelle, gültige SpO₂-Pakete der bestehenden BLE-Verbindung bestimmen die Anzahl. Der vorhandene Sensor-Dialog auf der Brille wird beim Auswählen geöffnet; dort scannen und verbinden. Ohne frische Daten, bei Warnstatus oder nach einer Trennung entstehen keine Kugeln.

Standard-Spielzuordnung: 90 % → 2 Kugeln, 100 % → 20 Kugeln pro Reihe, dazwischen linear gerundet und an den Enden begrenzt. Die Zuordnung ist in `TrackCollectibles` im Inspector einstellbar. Reihen bleiben beim Mengenwechsel räumlich verankert; der Sammelzähler bleibt erhalten. Das 3D-Objekt ist weiterhin über **VirtualWorld → Track Collectibles → Collectible Prefab** austauschbar.

Befehl: `runningMate.setCollectibles`, Payload `{"source":"Fixed","fixedCount":10}` oder `{"source":"OxygenSensor"}`. `fixedCount` ist optional und bleibt bei Auslassung erhalten. Änderungen pausieren den Lauf nicht. Unbekannte Felder, Quellen und Werte außerhalb ganzzahlig 0–30 werden abgewiesen.

Telemetrie: `data.treadmill.collectibles` und `data.runningMate.collectibles` enthalten `source`, `fixedCount`, `rowCount`, `oxygenReady`, `oxygenPercent` (Zahl oder `null`) und `collected`. `data.ble.spo2` liefert ebenfalls den frischen numerischen Sauerstoffwert oder `null`.

Das Dashboard benötigt die neue Unity-Registry; für die Brille ist ein neuer App-Build nötig. Die Sensorlogik wird mit simulierten PLX-Paketen geprüft; ein Test mit dem realen Sauerstoffsensor bleibt separat erforderlich.

## Avatar Behaviour (MarkerML2)

`avatar.playAnimation`: `commandPayload` enthält `animationId` (String, erforderlich),
`loop` (Boolean, optional, Standard true) und `speed` (Zahl 0.25–2, optional, Standard 1).
IDs: `Mixamo_BatterOnDeck`, `Mixamo_StartJumpingJacks`, `Mixamo_WarmingUp`, `Mixamo_ArmStretching`, `Mixamo_AirSquat`, `Mixamo_Plank`, `Idle`, `Walking`,
`Jogging`, `Sprint`. Ein gültiger Befehl benötigt die fertige Platzierung und pausiert
gegebenenfalls die laufende Session, ohne deren Statistik zurückzusetzen.

`avatar.stopAnimation`: leere Payload. Stellt automatische Locomotion wieder her;
setzt eine pausierte Session nicht fort.

Registry: `capabilities.avatarBehaviour`, `commands` und `animations` mit Einträgen
`{id,name,category,duration}`. Telemetrie: `treadmill.behaviour` und
`runningMate.behaviour` mit `{active,animationId,label,loop,speed,elapsed,duration}`.
`elapsed` bezeichnet gespielte Clip-Sekunden einschließlich Tempo; bei Wiederholung
läuft der Zähler weiter. Ohne manuelle Auswahl lautet `animationId` `Automatic`.

Ungültige IDs, unbekannte Payload-Felder, falsche Typen und Tempi außerhalb des
Bereichs werden vor einer Änderung der Session abgelehnt. Doppelte Command-IDs
verwenden wie alle anderen Befehle den ACK-Cache.


`Mixamo_Plank` ist ein einzelner Ablauf aus Start Plank → Plank (10 Clip-Sekunden Haltephase) → End Plank. Übergänge überblenden jeweils 0,18 s. `loop=true` wiederholt den gesamten Ablauf; `loop=false` spielt inklusive Aufstehen einmal. Das Abspieltempo skaliert alle Phasen (bei 2× sind es 5 reale Sekunden Halten). Explizites Stoppen unterbricht wie bei anderen Clips sofort.


Einstellbare Plank-Dauer: `avatar.playAnimation` akzeptiert für `Mixamo_Plank` optional `holdSeconds` (1–300, Standard 10). Die Registry meldet `supportsHoldDuration: true` bei diesem Eintrag. Intro, zyklisches Halten und Outro werden als separate Animator-States gespielt; ein anderer Haltewert verändert nur die Haltephase, nicht das Tempo der Bewegungen. Wie das übrige Animationstempo skaliert `speed` auch die Haltezeit: 30 bei 2× entsprechen 15 realen Sekunden. Nicht-Plank-Clips lehnen `holdSeconds` ab.


Die Marker-Begrüßung läuft lokal und automatisch vor dem Laufstart: Winken und zum Nutzer schauen, bei kurzem Anschauen weich beenden. Sie benötigt keine Server-Nachricht. Manuelle `avatar.playAnimation`-Befehle und Laufstart unterbrechen sie.


Laufende und Reset sind getrennt: `runningMate.command {action:"stop"}` pausiert die Laufzeit und erhält Distanzen und die aktuellen Weltpositionen beider Avatare. Nach einem laufenden oder pausierten Lauf dreht sich der aktive Avatar vor Ort zum Nutzer, spielt einmal `Standing Thumbs Up` und bleibt danach entspannt zu ihm ausgerichtet. Weitere Stop-Befehle starten die Geste nicht erneut. Pause und Verbindungsabbruch lösen sie nicht aus. `action:"reset"` beendet die Abschlussreaktion und setzt Nutzer-/Ghost-Distanz und Laufzeit auf null; gespeicherte Bestzeiten bleiben erhalten. Start/Fortsetzen und manuelle Animationen beenden die Abschlussreaktion ebenfalls.


Avatar-Bahn: Bahn 3 ist nur der anfängliche Standard. `runningMate.setLane` bleibt für den automatischen Spawn sowie `start` und `resume` erhalten. Der Start überschreibt die Auswahl nicht mehr. Beim Abschluss bleibt die Position fixiert; eine zu nahe Daumen-hoch-Geste wartet auf ausreichenden Nutzerabstand.
