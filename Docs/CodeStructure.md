# Code-Struktur und Refactoring

Stand: 28.10.2026. Eigener Laufzeit- und Editorcode liegt unter `Assets/Scripts` und `Assets/Editor`. Paketcode und TextMesh-Pro-Beispiele werden nicht umgebaut.

## Server

| Datei | Verantwortung |
| --- | --- |
| `RunnerServerClient.cs` | Unity-Lebenszyklus, Komponentenbindung, Verbindungsereignisse und Sessionzustand |
| `RunnerServerClient.Commands.cs` | Befehle validieren, ausführen und bestätigen |
| `RunnerServerClient.Telemetry.cs` | Laufzustand für das bestehende Serverprotokoll abbilden |
| `RunnerServerConnection.cs` | WebSocket, Retry, Abbruch und verbindungseigene Nachrichtenwarteschlangen |
| `RunnerServerConfiguration.cs` | Adresse normalisieren, gebündelte Konfiguration laden und Geräte-Override speichern |
| `RunnerServerSetup.cs` | Adressdialog und Eingabe auf der Brille |

Die Client-Teile sind eine einzige `partial`-Komponente. Das bisherige Hauptskript und seine GUID bleiben erhalten. Beim Adresswechsel wird der alte Transport abgebrochen; neue und alte Verbindung teilen keine Warteschlangen. Der CancellationTokenSource wird nach Ende des Verbindungsablaufs freigegeben.

`RunnerSessionState` typisiert Stopped/Ready/Running/Paused; ACKs und Telemetrie senden weiterhin deren bisherige Textwerte. Interne Verbindungsereignisse sind ebenfalls typisiert und von Servernachrichten getrennt. Handshake-Timeouts brechen den Socket nach fünf Sekunden explizit ab, auch wenn Unitys WebSocket-Laufzeit das CancellationToken ignoriert. Der Dialog zeigt Zieladresse und konkrete Fehler und erlaubt den Adresswechsel während eines Versuchs.

## Marker, Boden und Avatar

`MarkerAndAnchorTracker.cs` behält die serialisierten Felder, Markerbeobachtungen und Avatar-Platzierung. Die Teilklasse `MarkerAndAnchorTracker.Planes.cs` enthält Plane-Abfrage, Darstellung, Glättung, Polygonauswahl und Diagnose. `MarkerAndAnchorTracker.RunningTrack.cs` platziert die gemeinsame Laufbahn und folgt späteren Bodenkorrekturen.

`FloorPlaneGeometry` enthält die gemeinsame Höhenprojektion und Normalenausrichtung. `AvatarGrounding` ermittelt Fußpunkte für alle Spawn-Pfade. `AvatarVisibility` konfiguriert Entfernungssichtbarkeit und Animations-Culling; die einmal gesetzte Sichtbarkeit wird nach dem Bodenkontakt nicht mehr zurückgestellt. `AvatarLocomotion.Initialize` initialisiert die Laufanimation von Side-by-Side und Ghost Runner einheitlich.

`StraightTreadmillWorld` bleibt für Laufbandbewegung, physische Stadionposition und Kalibrierung zuständig. Die Umrechnung von Nutzer-/Ghost-Distanz in Streckenkoordinaten liegt dort zentral. Runden-/Checkpoint-Zeiten und Aufnahmen bleiben in den Unity-unabhängigen Klassen.

Bodenverfeinerungen projizieren die zuletzt gemessene Kopfposition erneut in die korrigierten Bahnkoordinaten. Der Bewegungsbezug und der Rennstartbezug werden entsprechend angepasst; die gemessene Distanz bleibt unverändert. Der nächste echte Bewegungsschritt zählt weiterhin, auch wenn die Plane laufend korrigiert wird.

## Unabhängige Session

`RunnerSession.cs` besitzt Uhr, Checkpoint-Tracker und Ghost auf einem eigenen Szenenobjekt außerhalb des HUDs. Laufwelt, initialisierte Stadionvorschau und Laufpartner registrieren sich beim Aktivieren und melden sich beim Deaktivieren ab. Es gibt keine Szenensuche pro Frame für diese Quellen. Die Session aktualisiert sich nach der Weltbewegung und vor der HUD-Anzeige.

`ActiveStatsDisplay` formatiert und zeigt nur Sessiondaten an. Seine bisherigen öffentlichen Methoden delegieren zur Session. Deaktivierte oder fehlende HUDs unterbrechen weder Uhr noch Rekorde noch Ghost. Der Server liest dieselbe Session direkt. Alle vom Server ausgelösten Zustandswechsel laufen über `SetSessionState`.

Der HUD-Status unterscheidet Markersuche, erkannte Bodenfläche, platzierte Bahn und Trackingverlust. `RunnerTrackingStatus` leitet ihn aus Kopftracking, Floor-Erkennung, bestätigter Plane und Platzierungszustand ab. Er zeigt keine erfolgreiche Platzierung an, wenn die bestätigte Boden-Plane nicht mehr getrackt wird.

## Gespeicherte persönliche Bestleistungen

`RunnerPersonalBest` enthält ausschließlich gemessene Bestzeiten, Checkpointzeiten der besten Runde und deren Ghost-Samples. `RunnerRecordsStore` speichert versionierte JSON-Dateien unter `Application.persistentDataPath/runner-records/`. Laufband, reale Stadionbahn und Vorschauen mit unterschiedlicher Rundenlänge haben getrennte Profile. Ein neuer langsamerer Lauf überschreibt keine bessere gespeicherte Leistung; unabhängige 100-m-/1-km-Rekorde werden einzeln zusammengeführt.

Die Session speichert bei verbesserten Rekorden und vor einem Reset oder Quellenwechsel. Beim Start und normalen Reset werden persönliche Bestleistungen wieder geladen; Uhr, Distanz und die laufende Aufnahme beginnen neu. Unvollständige Läufe werden nicht als Ghost gespeichert. Schreibvorgänge verwenden eine temporäre Datei mit anschließendem Austausch und eine `.bak`-Sicherung. Ungültige Zeiten, falsche Rundenlängen und beschädigte Ghost-Samples werden beim Laden verworfen; eine gültige Sicherung wird nach Möglichkeit verwendet. Lade-/Schreibprobleme werden geloggt und als `recordsStatus` in der Telemetrie angezeigt.

`RunnerRecordPersistence` führt Lesen, JSON-Verarbeitung und Schreiben im Hintergrund aus. Pro Speicherverzeichnis wird eine gemeinsame Warteschlange verwendet. Die Session übergibt kopierte Datenstände; fehlgeschlagene Aufträge bleiben ausstehend und werden nach zwei Sekunden erneut versucht, ohne dass dafür ein neuer Rekord entstehen muss. Diese Aufträge überleben den Reset und den Wechsel eines Sessionobjekts. Ein Auftrag wird erst nach erfolgreichem Schreiben bestätigt; eine inzwischen eingetroffene Verbesserung bleibt separat ausstehend. Asynchrones Laden führt gespeicherte und noch ausstehende Bestleistungen zusammen und überschreibt keine währenddessen gemessene Verbesserung.

Beim Wiederherstellen aus einer Sicherung wird eine beschädigte Hauptdatei beim nächsten Schreiben nicht in `.bak` rotiert. Die bisher gültige Sicherung bleibt erhalten. Die Validierung und Dateizugriffe sind innerhalb des Stores synchronisiert.

Eine vorübergehend deaktivierte Laufquelle setzt die Session nicht zurück: Uhr, Checkpoint-Objekt, Distanz und Ghost-Fortschritt bleiben erhalten und werden während der Inaktivität angehalten. Bei Reaktivierung derselben Quelle läuft dieselbe Session weiter. Eine deaktivierte Stadionvorschau wechselt auch nicht unbemerkt zur vorhandenen Laufbandwelt. Ein expliziter Reset oder ein tatsächlicher Quellen-/Umgebungswechsel startet weiterhin einen neuen Lauf.

Beim Binden einer neuen Laufquelle wird deren aktueller Distanzstand als Messbeginn erfasst. Der Checkpoint-Tracker startet an diesem Distanzstand statt bei null; vorher gelaufene Meter zählen nicht als neu gemessene Leistung. Unvollständige erste Abschnitte und Runden ergeben keine Rekorde oder Ghost-Aufnahme. Bewegung zwischen Binden und erstem Session-Update wird weiterhin mitgemessen. Ein Reset ohne Zurücksetzen der Quelle verwendet ebenfalls deren aktuellen Stand als Beginn.

Der Server-Handler prüft `type`, `commandId` und `commandType` auf nicht leere Strings und verlangt für `commandPayload` ein Objekt, null oder ein fehlendes Feld. Nicht zuordenbare Nachrichten werden verworfen; zuordenbare fehlerhafte Befehle erhalten einen negativen ACK. Bestehendes ACK-Format, leere Payloads und die Wiederholung gespeicherter ACKs bleiben erhalten.

## Editor und Kompatibilität

Die Generatoren für gerade und ovale Bahnen verwenden `TrackMeshBuilder` für gleichartige Mesh-Daten. Bestehende Mesh-Assets werden durch das Refactoring nicht neu erzeugt.

Legacy-Höhenmenü, lokale UI, Szenen- und Prefab-Felder sowie die Protokollfelder `treadmill` und `runningMate` bleiben für vorhandene Referenzen und Serverkompatibilität erhalten. Sie werden nicht allein aufgrund ähnlicher Namen entfernt.

## Prüfung

Nach Freigabe der Editor-Prüfung kompiliert der Stand ohne Compilerfehler. `Tools/Runner UI/Checks/Check independent session and floor corrections` prüft simulierte Bodenverfeinerung ohne Scheindistanz, gleichzeitig weiterlaufende echte Bewegung, Session/Ghost/Rekorde bei deaktiviertem HUD, Pause, Reset, Quellenwechsel und Befehlszustände. Ein zusätzlicher Play-Mode-Durchlauf in der Laufszene bestätigt genau eine gebundene Session, weiterlaufende Uhr und Ghost bei inaktivem HUD sowie direkte Ab-/Anmeldung der Laufwelt und ausdrückliche Pause.

Die native Marker-Erkennung ist im verwendeten Editor nicht aktiv. Echte Marker-/Plane-Erkennung und das Verhalten auf der Brille sind damit nicht bestätigt. Es wurde kein Build erstellt.

`Tools/Runner UI/Checks/Check saved records and diagnostics` prüft Speichern/Laden von Bestzeiten und Ghost, Zusammenführung schnellerer Einzelrekorde, Sicherungswiederherstellung, getrennte Profile/Szenen, konkrete Verbindungsdiagnosen, Trennung während des Laufens, Trackingverlust und wiederholtes Scannen. `Check connection timeout and address change` verwendet einen lokalen Dummy-Endpunkt, der den WebSocket-Handshake absichtlich nicht beantwortet, und prüft den festen Timeout sowie die Isolation abgebrochener Versuche. Beide Prüfungen bestehen. Zusätzlich sind Startdialog, Trackinganzeige, gespeicherte PB/Ghost-Wiederherstellung und das unveränderte ACK-Textformat im Play Mode geprüft. Die Speicherprüfungen verwenden eigene temporäre Verzeichnisse.

`Check background save retry and session reset` prüft einen absichtlich blockierten Speicherpfad: Lese-/Schreibaufträge blockieren den aufrufenden Thread nicht, ein Reset erhält ausstehende Bestleistungen, und nach Reparatur des Pfads gelingt der Schreibauftrag ohne neuen Rekord. Der Sicherungstest deckt nun auch erneutes Speichern nach einer beschädigten Hauptdatei und eine weitere Beschädigung ab. Die Session-Prüfungen decken mehrere deaktivierte Frames für Laufwelt und Stadionvorschau ab; im Play Mode wurde zusätzlich das vollständige Deaktivieren und Reaktivieren der Laufwelt bestätigt. Alle genannten Prüfungen bestehen. Kein Build wurde erstellt.

Die Session-Prüfung deckt zusätzlich späte Bindung bei 100/150 Metern, Reset bei bestehender Quelldistanz, neue Quellen ab null und eine bereits laufende Stadionvorschau ab. `Check server message validation` prüft ungültiges JSON, falsche Feldtypen, negative ACKs, gültige Folgebefehle, optionale Payloads und Befehls-Deduplizierung. Beide Regressionen bestehen im Editor.
