# Oxygen Bubbles in MarkerML2

Die Lauf-Szene `Assets/Scenes/TreadmillStraightTest.unity` verwendet
`Assets/OxygenPickups/Prefabs/OxygenTrackBubble.prefab` für TrackCollectibles.
Das etwa 24 cm große O₂-Modell ersetzt die einfache blaue Kugel.

Die vorhandene TrackCollectibles-Komponente bestimmt weiterhin Spawn-Reihen,
Sensor-Dichte, Kurven-/Bahnposition, Magnetbewegung, Pause, Zähler und Server-Telemetrie.
TrackOxygenBubbleVisual ergänzt Ausrichtung zur Head-Kamera, ein leichtes Pulsieren,
eine blaue Spur während der Magnetbewegung und einen kurzen Ring beim Einsammeln.
Beim Pooling wird die Flugspur gelöscht. Die Blasen enthalten keine zweite Flug- oder
Sammellogik. Der Sauerstoff-Sensorwert wird durch das Einsammeln nicht verändert.

Anpassungen: Collectible Prefab / Visual Scale / Magnet Distance auf VirtualWorld.
Das Prefab enthält Modell und Trail; Farben stehen unter OxygenPickups/Materials.
Die beiden Shader sind auf URP 14 ausgelegt und enthalten XR-Stereo-/Instancing-Makros.

Menüs:
- Tools > Treadmill > Install blue oxygen bubbles
- Tools > Treadmill > Checks > Check floating collectibles
- Tools > Treadmill > Checks > Check blue oxygen bubbles
- Tools > Treadmill > Checks > Check oxygen bubbles in Play Mode

Der Play-Check nutzt eine isolierte Testszene und öffnet anschließend die vorige Szene.
Ein Test im Magic-Leap-Headset bleibt für die endgültige Stereo-Darstellung erforderlich.
