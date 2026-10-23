# StadionTracking

Berechnet eine Stadionbahn, lässt den Avatar mehrere Runden darauf laufen und zeigt seine Position auf einer einfachen Karte.

## Aufgaben der Skripte

| Skript | Aufgabe |
|---|---|
| `MarkerAndAnchorTracker.cs` (unter `Assets`) | Erkennt Marker und Boden, spawnt den Avatar, verbindet die Steuerung und setzt seine Position. |
| `StadiumPreviewSettings.cs` | Enthält Einstellungen für Ausrichtung, Referenzbahn, Tempo und Linien. |
| `StadiumTrackGeometry.cs` | Berechnet Bahnlänge, Bahnnummer, Einstieg sowie Position und Laufrichtung für einen Meterstand. |
| `StadiumTrackPreview.cs` | Richtet die Strecke aus, zeichnet sie und liefert Position und Blickrichtung des Läufers. |
| `StadiumRunProgress.cs` | Zählt Geschwindigkeit × Zeit sowie aktuelle und abgeschlossene Runden; Tempo 0 pausiert. |
| `StadiumPacing.cs` | Berechnet im Zielzeit-Modus das Tempo aus Bahnlänge ÷ gewünschter Rundenzeit. |
| `UIScript.cs` (unter `Assets`) | Lässt den RunnerStatsCanvas dem Nutzer folgen und steuert den zuletzt gespawnten Avatar über den Regler. |
| `SimpleTrackMap.cs` | Übersetzt Stadionpositionen in Bildkoordinaten der vereinfachten Karte. |
| `RunnerMapMarker.cs` | Lädt `Resources/SimpleTrack.png` und bewegt darauf den roten Läuferpunkt. |

`Resources/StadiumRoute.shader` zeichnet die Streckenlinie. `Tools/generate_simple_track.py` im Projektstamm erzeugt die Karten-PNG aus den gemeinsamen Maßen.

## Zusammenspiel

1. **Marker und Boden erkennen:** Der Tracker sucht einen geeigneten Bodenpunkt und spawnt dort den Avatar.
2. **Strecke aufbauen:** Die Vorschau verwendet Geometrie und Einstellungen, um Bahn und Einstieg zu bestimmen und eine vollständige Runde zu zeichnen.
3. **Steuerung verbinden:** Der RunnerStatsCanvas bindet den neuen Läufer. Ein zuvor gebundener Läufer pausiert.
4. **Laufen:** Der Regler gibt das Tempo vor. `StadiumRunProgress` zählt Meter, die Geometrie liefert den passenden Punkt und die Vorschau die Richtung. Der Tracker setzt die endgültige Avatarposition.
5. **Karte aktualisieren:** `RunnerMapMarker` liest die Stadionposition des gebundenen Läufers. `SimpleTrackMap` rechnet sie auf die PNG um.

## Bedienung und Grenzen

Im RunnerStatsCanvas gibt es **MANUELL** und **ZIELZEIT**:

- **Manuell:** Der Regler steuert das Tempo. **Speed At Full Slider** bestimmt das Maximaltempo (Standard: 6 m/s).
- **Zielzeit:** Der Regler bestimmt die Minuten pro vollständiger Runde (bis 20 min, Standard 4 min). Das Tempo wird automatisch berechnet. Auf Bahn 1 ergeben 4 min etwa **1,67 m/s**. Die Anzeige zeigt die Zeit als Minuten:Sekunden.
- **Regler 0:** Pause ohne Verlust des Meterstands. Im Zielzeit-Modus startet die Schaltfläche **ZIELZEIT** wieder mit der zuletzt eingestellten Zeit. Beim ersten Wechsel in diesen Modus startet der Lauf mit der Zielzeit aus den Einstellungen.

Der Läufer läuft in beiden Modi über mehrere Runden weiter. Die Zielzeit ist die reine Laufzeit einer ganzen Runde ohne Pausen; beim Ändern der Zielzeit wird ein neues konstantes Tempo verwendet. Außenbahnen sind länger und benötigen für dieselbe Rundenzeit ein höheres Tempo. **Pacing Mode** und **Target Lap Minutes** am Tracker legen die Vorgaben für neue Läufer fest. Beim Spawn bleibt der Regler zunächst auf 0.

Im Modus **Avatar Spawn Preview** beginnt die virtuelle Strecke am Spawnpunkt. **Known Marker Position** benötigt eine bekannte Markerzuordnung zum echten Stadion. Nach dem Spawn wird die Ausrichtung beibehalten. Ohne weitere Bodendaten läuft die Figur auf der horizontalen Stadionebene; Hindernisse werden nicht umgangen.

Die PNG zeigt alle Laufbahnen vereinfacht auf einer einzigen Linie. Die tatsächliche Bahn und Laufdistanz bleiben davon unabhängig.

Ausführlicher Plan und Quellen: `laufbahn.md` im Projektstamm. Geometrie- und Laufprüfungen: `Tests/StadionTracking/GeometryChecks.cs`.
