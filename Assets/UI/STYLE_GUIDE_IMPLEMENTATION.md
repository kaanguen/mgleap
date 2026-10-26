# Runner Stats AR UI

Umsetzung nach „Runner Stats AR UI Style Guide“, Version 1.0 (bereitgestelltes PDF).

## Abgedeckte Oberflächen

- `RunnerMainMenu`: eigene Startszene mit Pace in min/km, Laufmodus (neben mir / Pacer 3 m voraus), Körpergröße, Rennstrecke anzeigen/ausschalten und Start. Die Streckenoption blendet nur die Bahn aus; Avatar, 400-m-Simulation und Laufwerte bleiben aktiv. Standard: Strecke sichtbar, 6:00 min/km, 184 cm. Die Einstellungen werden innerhalb der laufenden App beim Zurückkehren beibehalten.
- `ActiveStats`: drei Livewerte – Zeit, Distanz und Pace. Pause/Weiter und Menü. Rundenstatistiken sind aus dem Live-HUD entfernt.
- `RunnerStatsCanvas`: bestehende Avatar-/Tempo-Steuerung und Kartenmarker.
- `HeartRateCanvas`: vorhandene Sensorverbindung und Vitalwerte.
- Optionales `HeightSetupCanvas`: vorhandene manuelle Höhenkalibrierung; im automatischen Laufbandablauf weiterhin nicht eingeblendet.
- Zugehörige eigene Prefabs sowie SampleScene und TreadmillStraightTest. Importierte SDK-Beispielszenen werden nicht geändert.

`Assets/Scenes/UI.unity` enthält bereits eine verwaiste Canvas-Prefab-Referenz (GUID 63b24016460554b7b8206207385c4d8e). Der fehlende Inhalt kann nicht gestaltet werden. Die Szene gehört nicht zum Start-/Laufablauf und bleibt unverändert; der neue Menüablauf verwendet vollständige eigene Canvas-Objekte.

## Gestaltung

Zentrale Tokens: `RunnerUiTheme`. 1280 × 720 Referenz, Panelbreite 300, Rand links 48 / oben 40, Innenabstand 20. Bei anderem Seitenverhältnis wird mit der kleineren Referenzskalierung eingepasst. Das Hauptmenü ist auf ausdrücklichen Nutzerwunsch horizontal und vertikal im Sichtfeld zentriert. Die Laufanzeige wurde auf Nutzerwunsch nach innen und unten versetzt: Mittelpunkt bei 30 % der Sichtfeldbreite und 68 % der Höhe (von unten). Dadurch sitzt sie links oberhalb der Blickmitte mit Abstand zum Rand; der direkte Blick nach vorne bleibt frei. Die Position ist am RunnerArPanel über Running Hud Centre einstellbar. Ein gemeinsamer Panel-Controller zeigt maximal eine aktive Hauptfläche; höhere Priorität haben interaktive Einstellungen/Sensoren.

- Zeit: #67E8F9; Werte: #FFFFFF; Labels/Einheiten: #E3DEF7.
- Fortschritt/Kartenmarker: #D9F99D; Fehler mit erklärendem Text: #FF8A7A.
- Timer 56 fett; Hauptwerte 28 fett; Labels/Einheiten mindestens 13. Feste Zeichenbreite für laufende Ziffern. Keine automatische Verkleinerung.
- Eigenes Sans-SDF-Font-Asset auf Basis der vorhandenen Liberation Sans.
- Glas: Verlauf Weiß .24 → .07, dunkle Tönung (20,12,45) .28, Radius 28, Rand Weiß .35, oberer Lichtsaum .55 und weicher Schatten. Flache Buttons mit Radius 22, keine weiteren Glasflächen innerhalb des Panels.
- Kein Vollbild-Hintergrund, kein Blinken, keine erfundenen XP-/Levelwerte.

## Grenzen auf Magic Leap 2

Die Brille ist optisch durchsichtig. Ein Canvas-Shader kann die direkt sichtbare reale Umgebung weder weichzeichnen noch ihre Sättigung ändern. Das geforderte Backdrop-Blur 24 px / Saturation 170 % wird daher nicht vorgetäuscht. Verlauf, Tönung, Rundungen, Kontur und Lichtsaum sind gerendert; der Schatten ist angenähert. Das ersetzt keine Prüfung der Lesbarkeit auf der realen Brille bei hellem Umgebungslicht.

Die numerischen Schriftgrößen beziehen sich auf das Referenzlayout. Die Darstellung wird als World-Space-Canvas für das jeweilige Kamerasichtfeld skaliert. Die feste Höhenangabe bleibt eine Schätzung der Augenhöhe (Körpergröße minus 12 cm); Laufbandhöhe und reale Ausrichtung müssen zur bestehenden Kalibrierung passen.

## Prüfung und Bedienung

Im Editor `Assets/Scenes/RunnerMainMenu.unity` öffnen und Play starten. Pace über ±15 Sekunden, Größe über ±1 cm einstellen; Modus wählen, bei Bedarf über „Rennstrecke ausschalten“ die Bahn ausblenden, Lauf starten. Die Auswahl bleibt beim Zurückkehren ins Menü erhalten. Im HUD pausieren oder zum Menü zurückkehren.

Unter `Tools > Runner UI > Checks` liegen manuell gestartete Editor-Prüfungen für Menügrenzen/Buttons, Szenenwechsel, Übernahme der drei Einstellungen, Platzierung, Pause/Weiter und sichtbare Panelgrenzen. Sie setzen für die Testsession 6:15 min/km, 183 cm und Pacer. Sie laufen ausschließlich im Editor. Keine Android-Builds, keine Geräteinstallation und keine Brillentests durchgeführt.

### Ergebnis der Editor-Prüfung

Menübuttons und Eingabegrenzen, Szenenwechsel, Übernahme von 6:15 min/km / 183 cm / Pacer, automatische Bahnplatzierung, Pause/Weiter sowie ein sichtbares Panel außerhalb des mittleren Drittels wurden erfolgreich geprüft. Hauptmenü, HUD, Sensoren und bestehende Avatar-Steuerung wurden in Editor-Vorschauen kontrolliert. Nach den abschließenden Ablaufprüfungen meldete die Unity-Konsole keine Fehler. Auf der Brille wurden weder Darstellung noch Eingabe geprüft.
