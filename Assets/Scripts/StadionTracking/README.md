# StadionTracking

Berechnet eine Stadionbahn, lässt den Avatar 400 Meter darauf laufen und zeigt seine Position auf einer einfachen Karte.

## Aufgaben der Skripte

| Skript | Aufgabe |
|---|---|
| `MarkerAndAnchorTracker.cs` (unter `Assets`) | Erkennt Marker und Boden, spawnt den Avatar, verbindet die Steuerung und setzt seine Position. |
| `StadiumPreviewSettings.cs` | Enthält Einstellungen für Ausrichtung, Referenzbahn, Tempo und Linien. |
| `StadiumTrackGeometry.cs` | Berechnet Bahnlänge, Bahnnummer, Einstieg sowie Position und Laufrichtung für einen Meterstand. |
| `StadiumTrackPreview.cs` | Richtet die Strecke aus, zeichnet sie und liefert Position und Blickrichtung des Läufers. |
| `StadiumRunProgress.cs` | Zählt Geschwindigkeit × Zeit; pausiert bei Tempo 0 und stoppt bei 400 Metern. |
| `UIScript.cs` (unter `Assets`) | Lässt den RunnerStatsCanvas dem Nutzer folgen und steuert den zuletzt gespawnten Avatar über den Regler. |
| `SimpleTrackMap.cs` | Übersetzt Stadionpositionen in Bildkoordinaten der vereinfachten Karte. |
| `RunnerMapMarker.cs` | Lädt `Resources/SimpleTrack.png` und bewegt darauf den roten Läuferpunkt. |

`Resources/StadiumRoute.shader` zeichnet die Streckenlinie. `Tools/generate_simple_track.py` im Projektstamm erzeugt die Karten-PNG aus den gemeinsamen Maßen.

## Zusammenspiel

1. **Marker und Boden erkennen:** Der Tracker sucht einen geeigneten Bodenpunkt und spawnt dort den Avatar.
2. **Strecke aufbauen:** Die Vorschau verwendet Geometrie und Einstellungen, um Bahn und Einstieg zu bestimmen und die 400-m-Linie zu zeichnen.
3. **Steuerung verbinden:** Der RunnerStatsCanvas bindet den neuen Läufer. Ein zuvor gebundener Läufer pausiert.
4. **Laufen:** Der Regler gibt das Tempo vor. `StadiumRunProgress` zählt Meter, die Geometrie liefert den passenden Punkt und die Vorschau die Richtung. Der Tracker setzt die endgültige Avatarposition.
5. **Karte aktualisieren:** `RunnerMapMarker` liest die Stadionposition des gebundenen Läufers. `SimpleTrackMap` rechnet sie auf die PNG um.

## Bedienung und Grenzen

Regler **0 = Pause**, größer als 0 = weiterlaufen. **Speed At Full Slider** bestimmt das Maximaltempo (Standard: 6 m/s). Nach 400 m stoppt der Läufer; für einen neuen Lauf die Anwendung neu starten.

Im Modus **Avatar Spawn Preview** beginnt die virtuelle Strecke am Spawnpunkt. **Known Marker Position** benötigt eine bekannte Markerzuordnung zum echten Stadion. Nach dem Spawn wird die Ausrichtung beibehalten. Ohne weitere Bodendaten läuft die Figur auf der horizontalen Stadionebene; Hindernisse werden nicht umgangen.

Die PNG zeigt alle Laufbahnen vereinfacht auf einer einzigen Linie. Die tatsächliche Bahn und Laufdistanz bleiben davon unabhängig.

Ausführlicher Plan und Quellen: `laufbahn.md` im Projektstamm. Geometrie- und Laufprüfungen: `Tests/StadionTracking/GeometryChecks.cs`.
