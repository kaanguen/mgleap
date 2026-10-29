# Mixamo für den Jogger

Aktiv sind ausschließlich aus Mixamo importierte Bewegungen. Die fünf Aufwärm-FBX stammen unverändert aus den vom Nutzer gelieferten Downloads (Batter On Deck, Start Jumping Jacks, Warming Up, Arm Stretching, Air Squat). Unity erstellt für jedes Mixamo-Skelett einen eigenen Humanoid-Avatar und retargetet auf den Jogger bei der Wiedergabe. Kein CopyFromOther mit fremder Knochenhierarchie.

Root Rotation und Root XZ werden in die Pose gebacken. Y wird anhand der Füße referenziert; der Sprung bleibt erhalten. Loop-Übergänge überblenden die vorhandenen Mixamo-Posen mit übereinstimmender Pose und Geschwindigkeit an der Naht. Die RuntimeClips sind Kopien der importierten Mixamo-Muskelkurven, keine selbst erzeugten Bewegungen. Der Installer aktualisiert diese bei jedem Aufruf.

Die Mixamo-Datei Start Jumping Jacks enthält 0,83 Sekunden Öffnungsbewegung. Der Installer ergänzt den umgekehrten Rückweg aus denselben Mixamo-Kurven zu einem 1,67-Sekunden-Zyklus. Start und Ende stimmen überein; keine neu erfundenen Körperposen. Jumping Jacks läuft bis zum expliziten Stoppen. Die übrigen Clips respektieren weiterhin die Wiederholungs-Auswahl.

Die bisherigen selbst erstellten FBX, RuntimeClips und Animator-States wurden entfernt. Joggen verwendet weiterhin Ch20_nonPBR@Jogging.fbx aus dem bestehenden Anim-Ordner.


Batter On Deck: Aktiv sind die Arm- und Beinübungen aus Quellsekunden 0–12 und 32–37,33. Der Baseballteil 12–32 ist entfernt. Beide Abschnitte überblenden 0,3 Sekunden; Gesamtlänge 17,03 Sekunden. Die ursprünglichen Bein-, Fuß- und Körperbewegungen der erhaltenen Abschnitte sind enthalten; die Beine werden nicht mehr auf eine Standpose fixiert. Die ursprüngliche FBX bleibt unverändert.


Warming Up: Nutzerquelle `Ch20_nonPBR@Warming Up.fbx`, 6,3 Sekunden. Der aktive Humanoid-Clip `Mixamo_WarmingUp` enthält ausschließlich die Originalbewegung bis Frame 62 bei 30 fps (2,0667 Sekunden), vor der Schlagvorbereitung. Sämtliche späteren Animationskeys und Events sind entfernt. Arme und Beine bleiben enthalten. Die vollständige Quell-FBX bleibt unverändert.


Flüssige Loops: Die aktiven RuntimeClips überblenden den Auslauf mit dem Beginn des nächsten Durchlaufs. Beide Quellabschnitte bewegen sich während der Überblendung weiter. Die neutralen Vor-/Nachläufe wurden bei Batter On Deck (aktiv 14,4 Sekunden) und Jumping Jacks (aktiv 1,27 Sekunden) gekürzt. Warming Up bleibt 2,07 Sekunden lang. Die früheren Zeitangaben oben beschreiben die Schnitte vor dieser Loop-Aufbereitung. Es werden ausschließlich die bereits freigegebenen Mixamo-Abschnitte verwendet; keine Schläge oder Schlägerbewegungen.


Arm Stretching und Air Squat: Vom Nutzer gelieferte Mixamo-Originale, eigener Humanoid-Import und Retargeting auf den Jogger. Beide sind über den Behaviour-Tab als `Mixamo_ArmStretching` bzw. `Mixamo_AirSquat` verfügbar. Die vollständigen Bewegungen bleiben erhalten, die Loop-Naht nutzt die vorhandene zyklische Überblendung. Kniebeugen behalten die vertikale Körperbewegung.


Plank: Die drei Nutzer-FBX Start Plank, Plank und End Plank werden unverändert importiert. `Mixamo_Plank` verbindet sie als einen Humanoid-Clip: Einstieg → 10 Sekunden Halten → Ausstieg. Halten nutzt den zyklisch überblendeten Plank-Quellclip. Die Phasen überblenden 0,18 Sekunden. Der Behaviour-Tab bietet einen einzigen Eintrag an; Wiederholung betrifft den kompletten Ablauf. Stoppen unterbricht den Ablauf wie die anderen manuellen Animationen sofort.


Die Plank-Haltedauer lässt sich im Behaviour-Tab im Feld „Plank halten · Sekunden“ vor dem Abspielen auf 1–300 Sekunden einstellen. Standard ist 10. Bei Normaltempo entspricht die Eingabe der realen Haltezeit; Tempo 2× halbiert sie. Die Änderung gilt ab dem nächsten Abspielen. Dafür die aktualisierte Unity-App mit den drei Plank-States verwenden.


Plank-Bodenkontakt: Die drei Original-FBX verwenden nun denselben ursprünglichen Y-Bezug statt einer je Clip abweichenden Fußreferenz. Die Plank-States aktivieren Humanoid Foot IK. `RunnerPlankGroundingBaker` retargetet die Phasen im Editor auf den tatsächlichen Jogger und bäckt die Höhenkorrektur und kompensierte Fußziele in die Clips. Dabei bleiben angehobene Schrittfüße angehoben und Stützfüße am Boden. Der Ausstieg blendet über die letzten 0,9 Sekunden in die ursprüngliche Standpose des Einstiegs. Zur Laufzeit gibt es dafür keine Mesh-Bakes oder neuen Platzierungs-Offsets. Die einstellbare Haltedauer bleibt erhalten.


Automatische Marker-Begrüßung: `Waving.fbx` ist die unveränderte Nutzerquelle. Der Installer erzeugt daraus die kurze, zyklisch überblendete `AutomaticGreetingWave` im bestehenden Animator. Sie ist kein manueller Dashboard-Clip. `RunnerAvatarGreeting` startet nach bestätigter Marker-/Bodenplatzierung und vor dem ersten Lauf, in beiden Avatar-Modi. Der Avatar dreht sich weich zum Nutzer; Kopf und Oberkörper folgen mit begrenzten Winkeln. Aufmerksamkeit wird über die Kopf-/Blickrichtung der Brillenkamera erkannt (18° Kegel zum Avatar-Kopf, 0,4 s zusammenhängend; kein Eye-Tracking). Nach mindestens 5,25 s Winken wird über 0,75 s in Idle geblendet, spätestens nach 5,25 s beginnt die Ausblendung. Kein Wiederholen durch Bodenverfeinerungen oder spätere Laufpausen. Eine neue explizite Markerplatzierung erlaubt eine erneute Begrüßung. Laufstart und manuelle Übungen haben Vorrang. Die Laufzeit und Distanz werden nicht verändert.


Lauf beenden: Die Server-Schaltfläche „Lauf beenden“ stoppt den Avatar dort, wo er sich gerade befindet. Er dreht sich zum Nutzer, spielt die bereitgestellte Mixamo-Animation Standing Thumbs Up einmal ab und bleibt danach zum Nutzer ausgerichtet. Zeit und Distanzen bleiben erhalten. „Lauf zurücksetzen“ ist getrennt: Beide Laufpartner beginnen wieder bei null, ohne Abschlussgeste; Bestzeiten werden erhalten.


Korrektur der Abschlussgeste: Die Quell-FBX enthält ca. 0,62 m Vorwärtsbewegung in Humanoid RootT.z, obwohl applyRootMotion=false ist. Der Installer übernimmt deshalb nur die Oberkörpergeste auf die normale Idle-Unterkörperpose (RootT/RootQ, Fußziele und Beinmuskeln). Die Bodenkorrektur bleibt aktiv. Bei der tatsächlichen Jogger-Auswertung beträgt die horizontale Hüftbewegung noch ca. 0,0354 m. RunnerAvatarFinish verwendet einen Mindestabstand von 1,4 m (Freigabe ab 1,55 m), hält den Avatar vor Ort und schaut nur über Kopf/Nacken zum Nutzer; keine zusätzliche Torso-Neigung.
