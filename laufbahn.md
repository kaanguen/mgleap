# Laufbahn: Berechnung, Bahnerkennung und Avatarbewegung

Stand: 23. Oktober 2026. Dieses Dokument enthält den Gesamtplan und Pseudocode. Unter `Assets/Scripts/StadionTracking` sind Stadionberechnung, Avatar-Ausrichtung, Streckenanzeige und ein Lauf über 400 Meter implementiert. Der Geschwindigkeitsregler im `RunnerStatsCanvas` startet und pausiert den Lauf. Der Canvas folgt der Brille und wird automatisch mit dem zuletzt gespawnten Läufer verbunden.

## Aktueller Prototyp in Unity

Der vorhandene `MarkerAndAnchorTracker` erzeugt die Vorschau automatisch beim Spawn. Im Inspector gibt es dafür **Stadium route and running**. Im Standardmodus **Avatar Spawn Preview** beginnt die berechnete Bahn am Spawnpunkt; die Markerausrichtung bestimmt die Laufrichtung. Das ist eine virtuelle Vorschau, keine automatische Erkennung eines echten Stadions.

Bei Reglerwert 0 bleibt der Avatar stehen. Ein positiver Wert bewegt ihn entlang des Pfads; nach 400 Metern stoppt er. Das maximale Tempo ist über **Speed At Full Slider** einstellbar (Standard: 6 m/s). Eine türkise Linie zeigt die folgenden 400 Meter, ein gelber Pfeil die Laufrichtung. Standardmäßig wird die gedruckte Oberkante eines Bodenmarkers als Richtung verwendet (`Marker Local Run Direction = (0, 1, 0)`). Für eine feste Zuordnung zum echten Stadion steht **Known Marker Position** mit Referenzbahn, Meterposition und Markerabstand bereit.

Die Bedienung und die Grenzen des Prototyps stehen in [Assets/Scripts/StadionTracking/README.md](<Assets/Scripts/StadionTracking/README.md>). Die folgenden Abschnitte beschreiben auch zukünftige Funktionen wie weitere Laufmodi und Bahnwechsel. Ein abgeschlossenes 400-m-Rennen wird derzeit durch Neustarten der Anwendung zurückgesetzt.

## Einfach erklärt: vom Marker bis zu 400 Metern

Dieser Teil erklärt den gesamten Ablauf ohne vorausgesetztes Programmierwissen. Die technischen Details und der Pseudocode folgen weiter unten.

### Die Grundidee

Stell dir vor, du legst eine durchsichtige Stadionzeichnung genau über die echte Laufbahn. Auf dieser Zeichnung gibt es für jede Bahn eine unsichtbare Schiene. Dein Avatar fährt auf dieser Schiene wie eine kleine Modelleisenbahn.

Dabei haben drei Dinge unterschiedliche Aufgaben:

| Teil | Seine Aufgabe |
|---|---|
| Referenz aus der PDF | Beschreibt, welche Form und Größe die Strecke hat. |
| Bekannter Marker und die Brille | Helfen, diese Strecke an der richtigen Stelle in der echten Welt auszurichten. |
| Dein Skript | Berechnet den Einstieg, bewegt den Avatar und zählt die gelaufenen Meter. |

Der Avatar entscheidet den Weg also nicht selbst. Dein Programm berechnet ihn und bewegt die Figur entsprechend.

### Schritt 1: Die Maße aus der Referenz übernehmen

Die Zeichnung zeigt zwei gerade Abschnitte und zwei runde Enden. Du übernimmst die Maße in dein Programm. Das erledigst du bei der Vorbereitung, nicht bei jedem Scannen erneut.

Die wichtigsten Werte sind bereits in Abschnitt 2 aufgeführt: Geradenlänge, Innenradius und Bahnbreite. Damit kann dein Programm das gesamte Standardoval berechnen.

**Ergebnis:** Dein Programm kennt die Form des Stadions. Es weiß zu diesem Zeitpunkt noch nicht, wo das echte Stadion vor dir liegt.

### Schritt 2: Daraus eine unsichtbare Strecke bauen

Aus diesen Maßen erzeugt dein Programm für jede Bahn einen geschlossenen Laufpfad: Gerade, Kurve, Gerade, Kurve.

Jeder Punkt entlang eines solchen Pfads bekommt eine Meterposition. Stell dir ein Maßband vor, das einmal rund um die Strecke liegt:

```text
Meter 0 → Meter 1 → Meter 2 → ... → Ende der Runde → wieder Meter 0
```

Für jede Meterposition kann das Programm zwei Fragen beantworten:

- Wo muss der Avatar stehen?
- In welche Richtung muss er schauen?

**Ergebnis:** Die unsichtbaren Schienen sind berechnet. Man kann sie beim Entwickeln als Linien anzeigen, damit du sie kontrollieren kannst.

### Schritt 3: Einen festen Bezugspunkt für den Marker festlegen

Jetzt musst du die virtuelle Zeichnung mit dem echten Stadion verbinden. Dafür platzierst du den Marker an einer genau bekannten Stelle und in einer bekannten Ausrichtung.

Ein mögliches Beispiel wäre ein definierter Punkt an der Ziellinie. Du speicherst, wo der Marker relativ zu diesem Punkt steht und wie er gedreht ist. Wenn er auf einer Halterung steht, gehört auch seine Höhe dazu.

Es reicht nicht, nur „Marker bei der Ziellinie“ zu speichern. Das Programm braucht eine eindeutige Zuordnung: genauer Punkt, Abstand zur Bahn und Ausrichtung. Du kannst dafür auch eine andere bekannte Stelle verwenden.

**Ergebnis:** Dein Programm weiß, welche Stelle seiner unsichtbaren Stadionzeichnung zu diesem Marker gehört.

### Schritt 4: Den Marker mit der Magic Leap scannen

Die Brille erkennt den Marker und liefert seine räumliche Position und Drehung innerhalb ihres Trackingraums. Vereinfacht heißt das: „Der Marker ist dort und so ausgerichtet.“

Dein Programm verbindet diese Messung mit der gespeicherten Zuordnung aus Schritt 3. Damit kann es die gesamte virtuelle Strecke verschieben und drehen, bis sie zur echten Laufbahn passt.

Dieses Ausrichten heißt **Kalibrierung**. In Unity übernimmt ein gemeinsames Objekt namens `TrackRoot` die Position und Ausrichtung der berechneten Strecke.

**Ergebnis:** Die unsichtbare Stadionzeichnung liegt jetzt an der vorgesehenen Stelle in der echten Welt. Zur Kontrolle blendest du zunächst einige Hilfslinien ein und vergleichst sie mit den echten Markierungen.

### Schritt 5: Den Avatar erscheinen lassen

„Spawnen“ bedeutet einfach: Eine Figur wird erzeugt und an eine Position gesetzt.

Für den ersten Versuch lässt du den Avatar an einer festgelegten Position relativ zur kalibrierten Strecke erscheinen, beispielsweise auf Bahn 1. Später kannst du eine andere Position wählen lassen.

Wenn du ihn stattdessen vor deiner Blickrichtung erzeugst, ist dadurch noch nicht garantiert, dass er auf einer Bahn steht. Deshalb folgt die Prüfung im nächsten Schritt.

**Ergebnis:** Der Avatar hat eine Position, aber das Programm muss sie noch der Strecke zuordnen.

### Schritt 6: Erkennen, auf welcher Bahn er steht

Das Programm nimmt den Punkt zwischen den Füßen des Avatars. Es berechnet dessen Abstand von der inneren Begrenzung des Ovals nach außen.

Die erste Bahn liegt direkt an der Innenkante, die zweite etwas weiter außen und so weiter. Aus Abstand und Bahnbreite ergibt sich die Bahnnummer.

Zum Beispiel: Der Fußpunkt liegt 3 Meter außerhalb der Innenkante. Bei 1,22 Metern pro Bahn gehört diese Position zu Bahn 3.

Liegt er im Innenfeld oder außerhalb aller Bahnen, meldet das Programm: „Keine Bahn erkannt.“ Dann muss eine passende Position gewählt werden.

**Ergebnis:** Das Programm kennt die Bahnnummer. Dafür muss es keine aufgemalten Linien im Kamerabild erkennen: Es vergleicht die Position mit der vorher berechneten und ausgerichteten Geometrie.

### Schritt 7: Den genauen Einstieg auf der Schiene finden

Auch innerhalb einer Bahn kann der Avatar links oder rechts vom vorgesehenen Laufpfad stehen. Das Programm sucht deshalb den nächstgelegenen Punkt auf der Schiene seiner Bahn.

Dieser Punkt bekommt eine Meterposition, zum Beispiel Meter 125. Diese Zahl wird als persönlicher Startpunkt gespeichert.

Im einfachen Prototyp wird der Avatar direkt auf diesen Punkt gesetzt. In einer späteren Version kann er erst dorthin gehen. Die 400 Meter beginnen erst, wenn er den Einstieg erreicht hat.

**Ergebnis:** Startposition und Bahn stehen fest. Der Avatar kann ab hier dem Pfad folgen.

### Schritt 8: Die Figur Stück für Stück vorwärts bewegen

Während die Anwendung läuft, berechnet sie die nächste Position immer wieder neu. Der Avatar springt dabei nur um sehr kleine Abstände weiter; für dich sieht das wie eine flüssige Bewegung aus.

Beispiel: Bei 4 Metern pro Sekunde läuft er in einer halben Sekunde 2 Meter weiter. Sein Meterstand steigt dann von 125 auf 127.

Das Programm fragt den Pfad: „Wo liegt Meter 127, und wohin zeigt die Strecke dort?“ Anschließend setzt es die Figur an diesen Punkt und dreht sie in Laufrichtung.

Auf einer Geraden ergibt das eine gerade Bewegung. In einer Kurve ändern sich Position und Richtung fortlaufend entlang des Kreisbogens. Die Laufanimation bewegt zusätzlich Arme und Beine.

**Ergebnis:** Der Avatar folgt automatisch der berechneten Strecke. Er muss nicht erst vor jeder Kurve eine Entscheidung treffen.

### Schritt 9: Die gelaufene Entfernung getrennt mitzählen

Das Programm merkt sich zwei unterschiedliche Zahlen:

| Zahl | Bedeutung |
|---|---|
| Position auf der Bahn | Wo befindet sich der Avatar gerade auf der geschlossenen Schiene? |
| Seit dem Start gelaufene Meter | Wie weit ist er seit seinem persönlichen Einstieg gelaufen? |

Das ist nötig, weil die Position auf einer geschlossenen Strecke irgendwann wieder bei null beginnt. Die gelaufene Entfernung darf dabei nicht auf null zurückspringen.

Vereinfachtes Beispiel mit einer 400-m-Schiene: Start bei Meter 125, dann 300 Meter laufen. Die aktuelle Bahnposition ist Meter 25, aber die gelaufene Entfernung beträgt weiterhin 300 Meter.

**Ergebnis:** Das Programm kann an jeder Stelle starten und trotzdem zuverlässig die Zieldistanz zählen.

### Schritt 10: Nach 400 Metern stoppen

Sobald die gelaufene Entfernung 400 Meter erreicht, beendet das Programm die Bewegung und wechselt von der Laufanimation in den Stand.

Auf der Messlinie von Bahn 1 entspricht das praktisch einer vollständigen Runde. Auf einer Außenbahn ist eine Runde länger. Dort endet ein Lauf über genau 400 Meter deshalb vor dem Einstiegspunkt.

Wenn du stattdessen auf jeder Bahn wieder am Einstieg ankommen möchtest, lautet das Ziel „eine vollständige Runde“. Das Programm verwendet dann die berechnete Länge der ausgewählten Bahn als Zieldistanz.

**Ergebnis:** Du entscheidest zwischen „400 Meter laufen“ und „einmal ganz herum laufen“. Das Programm kann beides ab demselben Einstieg ausführen.

### Was du vorbereitest und was danach automatisch passiert

**Einmal vorbereiten:** Maße übernehmen, Laufpfade berechnen, Markerzuordnung festlegen und Bewegungsskript erstellen.

**Bei einer neuen Ausrichtung vor Ort:** Marker scannen und prüfen, ob die eingeblendeten Hilfslinien zur echten Bahn passen.

**Bei jedem Lauf automatisch:** Avatarposition prüfen, Bahn und Einstieg bestimmen, Figur entlang des Pfads bewegen und Meter bis zum Ziel zählen.

Ein einzelner Scan ist dabei keine Garantie dafür, dass die virtuelle Strecke über das ganze Stadion perfekt auf den echten Linien liegt. Das wird vor Ort an mehreren Punkten geprüft. Passt die reale Anlage nicht zur Standardform oder verändert sich die Trackingzuordnung, muss die Ausrichtung beziehungsweise das Streckenmodell angepasst werden.

### Welche Teile du unten nachlesen kannst

- **Maße und Formeln:** Abschnitte 2, 4 und 7.
- **Marker und Ausrichtung:** Abschnitt 3.
- **Bahnerkennung und Einstieg:** Abschnitte 5 und 6.
- **Bewegung und Laufziel:** Abschnitte 8 und 9.
- **Reihenfolge der Umsetzung:** Abschnitt 12.
- **Quellen:** Abschnitt 14.
- **Nur die wichtigsten Abläufe als Pseudocode:** Abschnitt 15.

## 1. Ziel

Aus der Stadionreferenz wird eine geschlossene Strecke erzeugt. Der Avatar kann an einer beliebigen Stelle einsteigen, seine Bahn wird erkannt und er folgt anschließend einem festen Pfad. Wahlweise läuft er genau 400 Meter, eine vollständige Runde oder dauerhaft weiter.

Der Ablauf lautet:

```text
Stadionmaße festlegen
  → Bahn im Unity-/AR-Raum ausrichten
  → Avatarposition in Bahnkoordinaten umrechnen
  → Bahnnummer und nächsten Punkt auf dem Laufpfad bestimmen
  → zum Einstieg gehen
  → Strecke in Metern fortschreiben
  → Position, Blickrichtung und Laufanimation aktualisieren
  → am gewählten Ziel stoppen
```

Empfehlung für den ersten Prototyp: analytische Geraden und Halbkreise, automatisch erkannte Bahn beim Einstieg, danach feste Bahn, zunächst eine vollständige Runde.

## 2. Referenz und richtige Maße

Die bereitgestellte Datei ist ein einseitiger Markierungsplan, nicht das gesamte Handbuch:

[Lokaler Markierungsplan](</Users/kaan/Downloads/Track and Field Facilities Manual 2019 Edition - M.pdf>)

Er zeigt unter anderem die Kurvenmittelpunkte CP1 und CP2 und den Innenradius. Die ergänzenden Vermessungsangaben stehen im [World-Athletics-Handbuch 2019, Kapitel 2.2.1.1, gedruckte Seiten 29–31](https://worldathletics.org/download/download?filename=7d39b677-8043-45f4-89f4-6c8814023057.pdf&urlslug=Track+and+Field+Facilities+Manual+2019+Edition+-+Chapters+1-3).

| Parameter | Wert |
|---|---:|
| Länge jeder Geraden / Abstand CP1–CP2 | 84,39 m |
| Innenradius bis zur Außenkante der inneren Bordkante | 36,50 m |
| Nominelle Bahnbreite einschließlich äußerer Markierung | 1,22 m |
| Messlinienabstand in Bahn 1 | 0,30 m |
| Messlinienabstand in Bahn 2 und weiter außen | 0,20 m von der äußeren Kante der inneren Bahnmarkierung |

Für die Standardform mit Bordkante gilt:

```text
Geradenlänge G = 84,39
Innenradius R = 36,50
Bahnbreite w = 1,22

Messradius Bahn 1:     r(1) = R + 0,30
Messradius Bahn n ≥ 2: r(n) = R + (n − 1) × w + 0,20
Rundenlänge:          L(n) = 2 × G + 2 × π × r(n)
```

Diese Werte stammen aus Quelle 1. Die folgenden Formeln und technischen Schritte sind daraus abgeleitete Implementierungsvorschläge.

Wichtige Korrektur zur ursprünglichen Erklärung: 36,50 m ist der Innenradius, nicht der Radius der 400-m-Messlinie. Deshalb sind 85,33 m lange Geraden für diese Referenz falsch.

Die Geometrie mit den gerundeten Planmaßen ergibt auf der Messlinie von Bahn 1 ungefähr 400,0012 m. Intern die berechnete Länge verwenden; die Anzeige darf auf 400 m runden.

### Messlinie oder Bahnmitte?

Für eine Figur, die sichtbar mittig laufen soll, kann stattdessen gelten:

```text
rMitte(n) = R + (n − 0,5) × w
```

Das ist eine gestalterische Pfadwahl. Eine Runde auf diesem Pfad hat eine andere Länge als auf der offiziellen Messlinie. Distanz, Position und Rundenzählung müssen immer denselben gewählten Pfad verwenden.

## 3. Projektbezug und AR-Kalibrierung

Beim Erstellen dieses Plans verwendet das Projekt Unity 2022.3.62f3. In `Packages/manifest.json` ist kein direkter Eintrag für Unity Splines vorhanden. `Assets/MarkerAndAnchorTracker.cs` enthält bereits Magic-Leap-Markerauswertung und Boden-/Avatarplatzierung.

Die PDF liefert Maße, aber keine Position des echten Stadions im Trackingraum. Auch ein erkannter Boden verrät weder Bahnnummer noch Stadionausrichtung. Dafür braucht das System eine bekannte Zuordnung zwischen realer Umgebung und mathematischer Strecke.

### Virtuelles Stadion

Ein leeres Objekt `TrackRoot` legt Ursprung und Ausrichtung fest. Darunter werden Bahn und Hilfslinien erzeugt. Alle Berechnungen erfolgen lokal zu diesem Objekt. Für echte Meter bleibt dessen Skalierung bei `(1, 1, 1)`.

### Reales Stadion mit Magic Leap

Mögliche Kalibrierungen:

1. **Ein Marker an einer bekannten Stelle:** Seine bekannte Bahnposition und die Montageausrichtung werden gespeichert. Seine gemessene Pose bestimmt daraus die Pose von `TrackRoot`. Der Marker muss relativ zur Bahn eindeutig ausgerichtet sein.
2. **Zwei bekannte Referenzpunkte:** Ihre lokalen und gemessenen Positionen bestimmen die horizontale Ausrichtung und Verschiebung. Die Boden-/Aufrichtung muss zusätzlich bekannt sein. Der Abstand dient zur Plausibilitätsprüfung.
3. **Mehrere Referenzpunkte:** Eine gemeinsame Anpassung kann Messfehler reduzieren. Über das ganze Oval verteilte Punkte sind aussagekräftiger als eng benachbarte Punkte.

Allgemein für eine bekannte Markerpose:

```text
WeltAusBahn = WeltAusMarker × Inverse(BahnAusMarker)
```

Ein beliebig aufgestellter Marker ohne bekannte Zuordnung reicht nicht aus, um das ganze Stadion zu verorten. Ein Standardplan passt außerdem nur dann auf eine reale Anlage, wenn deren Form und Maße übereinstimmen.

Für den Anfang: Bahn manuell im Editor platzieren. Danach die Marker-Kalibrierung ergänzen. Ein räumlicher Anker stabilisiert eine bereits bestimmte Zuordnung; er ersetzt die Vermessung nicht. Eine Kalibrierung an einem Punkt garantiert keine Zentimetergenauigkeit über das gesamte Stadion.

## 4. Lokales Koordinatensystem

Festlegung für den folgenden Algorithmus:

- X verläuft entlang der Geraden.
- Z verläuft quer zur Bahn.
- Y ist die Höhe.
- Der Ursprung liegt zwischen den Kurvenmittelpunkten.
- `h = G / 2`, linker Mittelpunkt `(-h, 0)`, rechter Mittelpunkt `(h, 0)` in X/Z.
- Die positive Laufrichtung verläuft von oben betrachtet gegen den Uhrzeigersinn.
- Meterposition 0 liegt am unteren Ende der rechten Kurve: `(h, -r)`.

Weltpositionen werden zuerst mit `TrackRoot.InverseTransformPoint(...)` in diesen Raum umgerechnet. Berechnete lokale Punkte gehen mit `TrackRoot.TransformPoint(...)` zurück in die Welt. Quelle 3 erklärt diese Koordinatenumrechnung.

Ein eigener Parameter bildet später die offizielle Ziellinie auf eine Meterposition ab. Der mathematische Nullpunkt muss nicht die Ziellinie sein.

## 5. Bahnnummer erkennen

### Einheitliche Berechnung für Geraden und Kurven

Die Verbindung CP1–CP2 ist ein Liniensegment. Für eine lokale Avatarposition `(x, z)` wird der nächste Punkt auf diesem Segment berechnet:

```text
q.x = Clamp(x, -h, h)
q.z = 0

rho = sqrt((x − q.x)² + z²)
d = rho − R
```

`rho` ist im Kurvenbereich der Abstand zum passenden Kurvenmittelpunkt und zwischen den Mittelpunkten der Abstand zur Mittellinie. `d` ist der seitliche Abstand von der inneren Laufbahnbegrenzung nach außen. Diese Kapselgeometrie deckt das gesamte Oval ohne Sprung am Übergang ab.

```text
wenn d < 0:              Innenfeld, keine Bahn
wenn d >= Bahnanzahl*w:  außerhalb, keine Bahn
sonst:                   Bahnnummer = Floor(d/w) + 1
```

Nicht jede ungültige Position auf Bahn 1 oder die äußerste Bahn begrenzen: Sonst würde auch ein Avatar im Innenfeld als Läufer auf der Strecke gelten. Die verlängerte Sprintgerade ist in diesem Modell kein Teil des Ovals.

Beispiel: In der Kurve ergibt `rho = 39,50 m` den Abstand `d = 3,00 m` und damit Bahn 3.

### Position auf einer Markierung

Die Formel entscheidet anhand des Bezugspunkts der Figur, idealerweise zwischen ihren Füßen. Sie modelliert keine Wettkampfentscheidung über Körperteile oder Linienberührungen.

Bei Messrauschen nahe einer Grenze eine kleine Hysterese verwenden: Eine neue Bahn wird erst übernommen, wenn der Bezugspunkt die Grenze deutlich überschritten hat und kurz stabil bleibt. Schwellen anhand tatsächlicher Trackingdaten einstellen. Beim geführten Lauf bleibt die gespeicherte Bahn vorzugsweise fest.

## 6. Den Einstiegspunkt finden

Zuerst die Bahn bestimmen, dann den Radius des gewählten Laufpfads auswählen. Anschließend die Avatarposition auf diesen Pfad projizieren.

Für die analytische Variante:

1. Nächsten Punkt auf der oberen Geraden bestimmen.
2. Nächsten Punkt auf der unteren Geraden bestimmen.
3. Nächsten Punkt auf dem linken Halbkreis bestimmen.
4. Nächsten Punkt auf dem rechten Halbkreis bestimmen.
5. Den Kandidaten mit dem kleinsten Abstand auswählen.

Bei Geraden wird der Projektionsparameter auf das Segment begrenzt. Bei Halbkreisen den Winkel mit `Atan2` ermitteln, auf den gültigen Halbkreis beschränken und die Endpunkte mitprüfen. Aus Abschnitt und lokalem Abstand ergibt sich die Meterposition `s0`.

Wichtig: Eine Projektion auf die unbeschränkten Vollkreise liefert an manchen Stellen falsche Kandidaten. Bei gleich weit entfernten Kandidaten eine feste Auswahlregel verwenden.

Liegt der Avatar deutlich neben dem Pfad, geht er zunächst zum Einstiegspunkt. Diese Anlaufstrecke wird separat gezählt. Die eigentliche Runde beginnt erst auf der Bahn. Ein sofortiges Versetzen kann als ausdrücklicher Prototypmodus dienen.

## 7. Aus einer Meterposition einen Punkt berechnen

Für einen Pfadradius `r` gilt:

```text
H = π × r                  // Länge eines Halbkreises
L = 2 × G + 2 × H
s = ((s % L) + L) % L      // auch für negative Werte gültig
```

Die folgende Tabelle verwendet lokale X/Z-Koordinaten. Alle Winkel sind im Bogenmaß. Y wird separat gesetzt.

| Abschnitt | Bereich | Position `(x, z)` | Tangente `(tx, tz)` |
|---|---|---|---|
| Rechte Kurve | `0 ≤ s < H` | `θ = -π/2 + s/r`; `(h + r cos θ, r sin θ)` | `(-sin θ, cos θ)` |
| Obere Gerade | `H ≤ s < H+G` | `u = s-H`; `(h-u, r)` | `(-1, 0)` |
| Linke Kurve | `H+G ≤ s < 2H+G` | `u = s-H-G`; `θ = π/2 + u/r`; `(-h + r cos θ, r sin θ)` | `(-sin θ, cos θ)` |
| Untere Gerade | `2H+G ≤ s < L` | `u = s-2H-G`; `(-h+u, -r)` | `(1, 0)` |

Die Abschnitte sind an ihren Grenzen positions- und tangentenstetig. Die Kurven haben echten Kreisradius; ihre Bogenlänge muss nicht numerisch geschätzt werden.

Bei umgekehrter Laufrichtung die Meterposition rückwärts auswerten und die Blicktangente negieren.

## 8. Bewegung, Blickrichtung und Animation

Der Bewegungscontroller speichert mindestens:

```text
laneIndex, pathMode, startDistance, travelledDistance,
speedMetersPerSecond, targetDistance, runningDirection, state
```

Ein möglicher Ablauf, als Pseudocode:

```csharp
// Nach Erreichen des Einstiegspunkts:
travelledDistance = 0;
targetDistance = mode == OneLap ? pathLength : 400.0;

// Während des Laufens; speed >= 0:
travelledDistance = Min(
    travelledDistance + speed * deltaTime,
    targetDistance);

double s = Wrap(startDistance + directionSign * travelledDistance, pathLength);
EvaluatePath(s, out localPosition, out localTangent);
ApplyWorldPose(localPosition, directionSign * localTangent);

if (travelledDistance >= targetDistance)
    state = Finished;
```

`Wrap`, `EvaluatePath` und `ApplyWorldPose` sind hier geplante eigene Funktionen, keine fertigen Unity-API-Aufrufe. Für Dauerlauf entfällt das Begrenzen auf eine Zieldistanz. Die ungekürzte gelaufene Distanz bleibt getrennt von der umlaufenden Position erhalten. Für sehr lange Läufe sind `double`-Werte für Distanzen sinnvoll.

Die Weltrotation kann mit `Quaternion.LookRotation(worldTangent, worldUp)` aus Tangente und Bodenrichtung entstehen; siehe Quelle 4. Ein separates Modellkind korrigiert bei Bedarf die Vorwärtsachse des Avatar-Modells.

Für einen ersten ebenen Prototyp bleiben Höhe und Aufrichtung konstant. Später Bodenhöhe und Bodenneigung über vorhandene Bodenerkennung oder Raycasts berücksichtigen. Bodenanpassung darf die berechnete horizontale Bahnposition nicht überschreiben.

Die Laufanimation wird zur Geschwindigkeit passend abgespielt. Bei skriptgesteuerter Fortbewegung die zusätzliche Translation durch Root Motion deaktivieren oder gezielt abgleichen. Nur eine Komponente darf die endgültige Avatarposition schreiben. Andernfalls können Platzierungsskript, Animator und Laufcontroller gegeneinander arbeiten.

## 9. Genau 400 Meter oder eine vollständige Runde?

Diese Modi ausdrücklich getrennt anbieten:

- **Eine Runde ab Einstieg:** `targetDistance = L(n)`. Der Avatar kommt wieder am Einstieg an.
- **Genau 400 m ab Einstieg:** `targetDistance = 400`. Auf einer Außenbahn endet er vor seinem Einstieg.
- **Dauerlauf:** Position umlaufen lassen; vollständige Runden aus der ungekürzten Distanz bestimmen.
- **400-m-Rennen mit gemeinsamer Ziellinie:** Pro Bahn die Startposition rückwärts von der Ziellinie um 400 m bestimmen. Das ist ein eigener Modus mit versetzten Starts.

Nicht bei jeder Bahn pauschal `Modulo 400` verwenden. Auch beim Ziel „400 m“ läuft die Position modulo der tatsächlichen Pfadlänge.

Bahnwechsel zunächst ausschließen. Später benötigen sie einen eigenen Übergangspfad und dessen tatsächliche Länge. Bloß den Radius umzustellen würde die Figur seitlich versetzen und die Distanz verfälschen.

## 10. Mögliche technische Umsetzungen

| Variante | Geeignet für | Aufwand und Grenzen |
|---|---|---|
| Analytische Geraden und Kreise | Diese Standardform, genaue Meterberechnung | Wenige Parameter; Sonderformen brauchen weitere Abschnitte |
| Unity Splines | Im Editor veränderbare oder individuell vermessene Strecken | Projektion und Bogenlängenabbildung erforderlich; Paketversion passend zum Projekt prüfen |
| Vermessene Punkte / Polylinie | Reale Anlage, die vom Standard abweicht | Einfacher Einstieg; viele Punkte oder Glättung für saubere Kurven nötig |
| NavMesh für den Anlauf | Freies Gehen zur Bahn um Hindernisse | Für die anschließende feste Laufspur allein keine ausreichende Vorgabe |

### Variante A: Analytische Geometrie – Empfehlung

Die Formeln aus Abschnitt 7 liefern Position und Tangente direkt. Dieselben Parameter erzeugen Debuglinien, Bahngrenzen und gegebenenfalls das sichtbare Modell. Dafür ist kein zusätzliches Spline-Paket notwendig.

### Variante B: Unity Splines

Pro Bahn einen geschlossenen Pfad anlegen oder aus einer gemeinsamen Geometrie erzeugen. Die API `SplineUtility.GetNearestPoint` unterstützt die Suche nach einem nahen Punkt. `CalculateLength` und die Umrechnung von Distanzindizes können bei der Längenauswertung helfen; siehe Quelle 5.

Die Spline-Variable `t` ist im Allgemeinen kein gleichmäßiger Meterfortschritt. Deshalb nicht einfach `t += speed * deltaTime` rechnen. Eine Abbildung von kumulierter Bogenlänge auf `t` verwenden, beispielsweise über eine vorberechnete Tabelle und Interpolation. Projektion, Längenberechnung und Auswertung müssen im selben Koordinatenraum erfolgen; Skalierungen berücksichtigen.

Eine frei gezeichnete Bézierkurve entspricht außerdem nicht automatisch einem exakten Kreisbogen. Die Länge des tatsächlich verwendeten Pfads messen.

### Variante C: Vermessene Punkte

Punkte in Laufrichtung speichern und die Länge jedes Segments aufsummieren. Zur Meterposition das passende Segment suchen und darin interpolieren. Bei wenigen Punkten schneidet die Figur Kurven ab. Nach einer Glättung muss die Länge der geglätteten Strecke neu berechnet werden.

## 11. Vorgeschlagene Komponenten

| Geplante Komponente | Aufgabe |
|---|---|
| `TrackDefinition` | Maße, Bahnanzahl, Messlinie oder Bahnmitte |
| `TrackGeometry` | Länge, Position, Tangente, Projektion und Bahnerkennung |
| `TrackCalibration` | Pose von `TrackRoot` aus bekannten Referenzen |
| `TrackEntryResolver` | Einstiegsbahn und Startmeter bestimmen |
| `AvatarTrackRunner` | Anlauf, Laufzustand, Geschwindigkeit, Distanz und Ziel |
| `TrackDebugView` | Mittelpunkte, Bahngrenzen, Pfade und Einstiegsposition anzeigen |

Das sind vorgeschlagene Namen für neue Komponenten. Der vorhandene `MarkerAndAnchorTracker` muss dafür nicht sofort vollständig umgebaut werden. Zuerst seine Positionsschreibstellen prüfen und eine klare Übergabe vom Platzieren an den Laufcontroller schaffen.

Mögliche Zustände: `WaitingForCalibration → Ready → Approaching → Running → Finished`. Bei unzuverlässigem Tracking kann ein zusätzlicher Zustand die Bewegung pausieren. Die dafür geltenden Schwellen erst mit Gerätedaten festlegen.

## 12. Umsetzung in sinnvollen Schritten

1. **Geometrie im Editor:** `TrackRoot` mit Maßstab 1, Mittelpunkte und Bahngrenzen als Gizmos anzeigen. Standardform zunächst ohne AR prüfen.
2. **Meterfunktion:** Position und Tangente für beliebige Meterwerte berechnen. Einen einfachen Würfel auf Bahn 1 laufen lassen.
3. **Bahnerkennung:** Verschiedene Positionen auf Geraden, Kurven und im Innenfeld ausprobieren. Erkannte Bahn und seitlichen Abstand anzeigen.
4. **Beliebiger Einstieg:** Nächsten Pfadpunkt finden, Anlauf und eigentlichen Lauf trennen.
5. **Distanzmodi:** Eine Runde und genau 400 m auf mehreren Bahnen prüfen.
6. **Avatar:** Rotation, Fußkontakt, Animation und Zuständigkeit für Position/Root Motion abstimmen.
7. **Magic Leap:** Bekannte Marker-/Referenzzuordnung ergänzen, dann Ausrichtung und Versatz an mehreren Stellen prüfen.
8. **Ausbau nach Bedarf:** Tempoänderung, Dauerlauf, gespeicherte Kalibrierung, individuelle Stadionmaße und Bahnwechsel.

## 13. Abnahmekriterien

- Alle vier Abschnittsübergänge und der Rundenschluss haben gleiche Position und Laufrichtung auf beiden Seiten.
- Eine Sekunde bei 5 m/s erhöht die interne Distanz um 5 m; das Ergebnis bleibt bei unterschiedlichen Bildraten gleich.
- Die Bahnerkennung funktioniert in beiden Kurven und auf beiden Geraden. Innenfeld und Außenbereich bleiben ungültig.
- Die Rückprojektion eines bekannten Pfadpunkts liefert dieselbe Position; Meterwerte werden modulo der Pfadlänge verglichen.
- Nach einer Runde ist der Avatar wieder am Einstieg; nach genau 400 m auf einer Außenbahn entsprechend davor.
- Eine verschobene und gedrehte `TrackRoot` verändert die Weltlage, aber keine lokalen Meterwerte oder Bahnnummern.
- Bei einer Kalibrierungsänderung wird kein künstlicher Laufweg aus der Weltverschiebung gezählt.
- Kein Sprung entsteht durch konkurrierende Positionsschreiber. Anlaufdistanz und Rundendistanz bleiben getrennt.
- Auf dem Gerät die Übereinstimmung mit realen Markierungen an mehreren weit auseinanderliegenden Stellen prüfen und Abweichungen protokollieren.

## 14. Quellen und Recherche

### Quelle 1: World Athletics – Maße und Messlinien

[Track and Field Facilities Manual 2019, Chapters 1–3](https://worldathletics.org/download/download?filename=7d39b677-8043-45f4-89f4-6c8814023057.pdf&urlslug=Track+and+Field+Facilities+Manual+2019+Edition+-+Chapters+1-3)

Nachlesen: Kapitel 2.2.1.1, Tabelle 2.2.1.1, Abbildungen 2.2.1.1a und 2.2.1.1b auf den gedruckten Seiten 29–31. Suchbegriffe: `running line`, `36.800`, `84.390`. Die Startmarkierungen werden in Kapitel 2.2.1.6 behandelt. Dieser Plan bezieht sich auf die Standardgeometrie dieser Ausgabe.

### Quelle 2: World Athletics – Dokumentübersicht und Markierungsplan

[Technical Information – offizielle Dokumente](https://worldathletics.org/about-iaaf/documents/technical-information)

Dort nach `Track and Field Facilities Manual 2019 Edition - Marking Plan 400m Standard Track` suchen. Das ist die öffentliche Fundstelle zur bereitgestellten Zeichnung. Aktuelle Regeln bei einer späteren Wettkampfsimulation zusätzlich dort recherchieren.

### Quelle 3: Unity 2022.3 – Koordinaten umrechnen

[Transform.InverseTransformPoint](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Transform.InverseTransformPoint.html)

Relevant für Weltposition → lokale Bahnposition. Die Gegenrichtung liefert `Transform.TransformPoint`.

### Quelle 4: Unity 2022.3 – Blickrichtung

[Quaternion.LookRotation](https://docs.unity3d.com/2022.3/Documentation/ScriptReference/Quaternion.LookRotation.html)

Relevant für die Ausrichtung aus Lauftangente und Aufwärtsrichtung.

### Quelle 5: Unity Splines – optionale Alternative

[SplineUtility, Dokumentation Version 2.5](https://docs.unity3d.com/Packages/com.unity.splines@2.5/api/UnityEngine.Splines.SplineUtility.html)

Nachlesen: `GetNearestPoint`, `CalculateLength` und `GetNormalizedInterpolation`. Die Dokumentationsversion ist eine konkrete API-Referenz und keine Installationsvorgabe; vor Umsetzung die zur tatsächlich verwendeten Paketversion gehörende Dokumentation wählen.

### Weiterführende Suchbegriffe

- `capsule signed distance point line segment`: mathematischer Hintergrund der Bahnerkennung.
- `arc length parameterization constant speed spline`: gleichmäßige Geschwindigkeit auf Kurven.
- `rigid transform point correspondences calibration`: räumliche Zuordnung bekannter Referenzpunkte.
- `Unity animator root motion scripted movement`: Abstimmung zwischen Laufanimation und Bewegungsskript.

Diese Suchbegriffe sind Recherchehinweise, keine zusätzlich geprüften Quellen.

## 15. Minimaler Prototyp – nur Pseudocode

Die drei Kernaufgaben: Strecke berechnen, Avatar einer Bahn zuordnen und ab seinem Einstieg eine vollständige Runde laufen lassen. Alle Positionen sind zunächst lokal zur bereits ausgerichteten Bahn, in Metern auf der X/Z-Ebene. Höhe bleibt konstant. Bahnwechsel und Anlaufanimation sind hier ausgeklammert.

### A. Strecke berechnen

```text
KONSTANTEN:
    G = 84.39                 // Länge einer Geraden
    R = 36.50                 // Innenradius
    W = 1.22                  // Bahnbreite
    ANZAHL_BAHNEN = 8
    h = G / 2

FUNKTION Radius(bahn):
    WENN bahn == 1:
        GIB R + 0.30 ZURÜCK
    GIB R + (bahn - 1) * W + 0.20 ZURÜCK

FUNKTION Rundenlaenge(bahn):
    GIB 2 * G + 2 * PI * Radius(bahn) ZURÜCK

FUNKTION PunktUndRichtung(bahn, meter):
    r = Radius(bahn)
    H = PI * r
    L = Rundenlaenge(bahn)
    s = ((meter MOD L) + L) MOD L

    WENN s < H:                           // rechte Kurve
        winkel = -PI/2 + s/r
        punkt = (h + r*COS(winkel), r*SIN(winkel))
        richtung = (-SIN(winkel), COS(winkel))

    SONST WENN s < H + G:                 // obere Gerade
        u = s - H
        punkt = (h - u, r)
        richtung = (-1, 0)

    SONST WENN s < 2*H + G:               // linke Kurve
        u = s - H - G
        winkel = PI/2 + u/r
        punkt = (-h + r*COS(winkel), r*SIN(winkel))
        richtung = (-SIN(winkel), COS(winkel))

    SONST:                               // untere Gerade
        u = s - 2*H - G
        punkt = (-h + u, -r)
        richtung = (1, 0)

    GIB punkt, richtung ZURÜCK
```

### B. Avatar auf der Strecke erkennen und Einstieg finden

Für den einfachen Prototyp wird der nächste Einstiegspunkt durch Abtasten gesucht. Das ist eine Näherung mit 10 cm Schrittweite entlang des Pfads. Sie wird nur beim Einstieg ausgeführt; später kann sie durch die genaue Projektion aus Abschnitt 6 ersetzt werden.

```text
FUNKTION ErkenneBahn(position):
    // Nächster Punkt auf der Verbindung der Kurvenmittelpunkte
    q = (CLAMP(position.x, -h, h), 0)
    abstandNachAussen = ABSTAND(position, q) - R

    WENN abstandNachAussen < 0:
        GIB KEINE_BAHN ZURÜCK             // Innenfeld
    WENN abstandNachAussen >= ANZAHL_BAHNEN * W:
        GIB KEINE_BAHN ZURÜCK             // außerhalb

    GIB FLOOR(abstandNachAussen / W) + 1 ZURÜCK

FUNKTION FindeEinstieg(position, bahn):
    besterMeter = 0
    kleinsterAbstand = UNENDLICH
    L = Rundenlaenge(bahn)

    FÜR meter VON 0 BIS KLEINER L IN SCHRITTEN VON 0.10:
        punkt, richtung = PunktUndRichtung(bahn, meter)
        abstand = ABSTAND(position, punkt)

        WENN abstand < kleinsterAbstand:
            kleinsterAbstand = abstand
            besterMeter = meter

    GIB besterMeter ZURÜCK
```

### C. Eine Runde ab dem Einstieg laufen

```text
ZUSTAND:
    laeuft = FALSCH
    geschwindigkeit = 4.0                  // Meter pro Sekunde
    bahn, startMeter, gelaufen, zielDistanz

FUNKTION StarteRunde(avatar):
    laeuft = FALSCH
    position = WeltZuBahnLokal(avatar.fussPosition)
    bahn = ErkenneBahn(position.xz)

    WENN bahn == KEINE_BAHN:
        MELDE "Avatar steht nicht auf der Laufbahn"
        ABBRECHEN

    startMeter = FindeEinstieg(position.xz, bahn)
    gelaufen = 0
    zielDistanz = Rundenlaenge(bahn)

    // Prototyp: auf den nächsten Pfadpunkt setzen.
    // In der fertigen Anwendung erst dorthin gehen lassen.
    SetzeAvatarAufPfad(avatar, bahn, startMeter)
    laeuft = WAHR

FUNKTION Update(avatar, deltaZeit):
    WENN NICHT laeuft:
        ABBRECHEN

    gelaufen = MIN(gelaufen + geschwindigkeit * deltaZeit, zielDistanz)
    SetzeAvatarAufPfad(avatar, bahn, startMeter + gelaufen)

    WENN gelaufen >= zielDistanz:
        laeuft = FALSCH                    // wieder am Einstieg angekommen

FUNKTION SetzeAvatarAufPfad(avatar, bahn, meter):
    punkt, richtung = PunktUndRichtung(bahn, meter)
    avatar.position = BahnLokalZuWelt(punkt.x, festeBodenhoehe, punkt.z)
    avatar.blickrichtung = BahnRichtungZuWelt(richtung)
```

`WeltZuBahnLokal`, `BahnLokalZuWelt` und `BahnRichtungZuWelt` stehen für die Koordinatenumrechnung über `TrackRoot`. Richtungen werden nur gedreht, nicht verschoben. Die Erkennung verwendet den Fußpunkt; sie erkennt keine visuell aufgemalten Linien. Voraussetzung ist, dass das mathematische Oval mit der sichtbaren oder realen Bahn übereinstimmt.

Die Bahn wird beim Start gespeichert. Die Bewegung benötigt danach keine erneute Bahnerkennung. `zielDistanz = Rundenlaenge(bahn)` bedeutet eine ganze Runde auf dieser Bahn; für genau 400 m wäre stattdessen `zielDistanz = 400` zu setzen. Die Positionsberechnung verwendet weiterhin die tatsächliche Rundenlänge.
