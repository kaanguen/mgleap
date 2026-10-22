# Tatsächlich durchgeführte Prüfungen

- C#-Dateien gegen die lokal vorhandenen Unity-2022.3.62f3-Assemblies und den installierten Velorexe-Runtime-Quellcode kompiliert; Android-Codepfad aktiviert. Ergebnis: keine Compilerfehler.
- 21 Herzfrequenz-/RR-Prüfungen und 33 zusätzliche Parser-Prüfungen ausgeführt, alle erfolgreich. Abgedeckt: Herstellerbeispiele, 8-/16-Bit-Herzfrequenz, optionale Felder, RR-Einheiten, vorzeichenbehaftete Dezimalexponenten/Mantissen, Fahrenheit, NaN/NRes/Infinity, Legacy-Half einschließlich Subnormalen, PLX-Statusfelder, Paketlängen, Akku und A002-Pakete.
- CosinussGatt.java mit Unity OpenJDK und Android-34 android.jar gegen das installierte Plugin-JAR kompiliert und als cosinuss-gatt.jar gepackt. Dieses JAR enthält ausschließlich die zusätzliche Klasse com.markerml2/ble/CosinussGatt; es ersetzt keine Velorexe-Klassen.
- SampleScene und HeartRateCanvas.prefab durch Unity erweitert und gespeichert. Verweise auf SensorDetailsText und CosinussMeasurements im Editor geprüft. Canvas-Vorschau mit Unity gerendert und visuell geprüft.
- Die DescriptorWrite-Callbacks und das private GATT-Feld des tatsächlich ausgelieferten Velorexe-JARs wurden mit javap geprüft. Der alte separate Java-Quellcode im Upstream-Java-Repository entspricht nicht vollständig diesem JAR.

Kein eigener APK-/IL2CPP-Build dieser Erweiterung, kein Live-Test der Java-Reflection/GATT-Aufträge auf dem Gerät und kein Empfangstest mit einem Sensor durchgeführt. Der zuvor versuchte virtuelle Gamepad-Test ist entfernt und zählt nicht als erfolgreicher Test.


## Verbindungszeitlimit korrigiert (22.10.2026)

Das bereitgestellte ML2-Log zeigt: thre wird gefunden, connect startet um 12:56:58, das bisherige Skript verwirft den Versuch um 12:57:28. ConnectedToGattServer kommt jedoch erst um 12:58:05. Die folgende Service-Erkennung wird deshalb nicht mehr an den verworfenen ConnectToDevice-Befehl zugestellt. Die zusätzlichen Messwert-Abfragen laufen in diesem Versuch noch nicht.

HeartRateBLE wartet jetzt standardmäßig 90 Sekunden (Inspector: Connect Timeout Seconds). Die Meldung unterscheidet Bluetooth-Verbindung und Service-Erkennung. Ein nach Abbruch eintreffendes ConnectedToGattServer wird über die vorhandene öffentliche ConnectToDevice.Disconnect-API geschlossen. Ein Disconnect vor dem ersten GATT-Callback wird nicht mehr sofort an das Plugin gesendet, da dieses den Handle vorher noch nicht kennt. Einschränkung des unveränderten Plugins: Ein noch wartender autoConnect-Versuch kann vor diesem Callback nicht gezielt geschlossen werden; beim Testen nach einem Zeitlimit die App vollständig beenden und den Sensor neu starten.

Alle aktuellen BLE-Laufzeitskripte erneut gegen die vorhandenen Unity-/Plugin-Assemblies kompiliert: Android- und Editor-Konfiguration erfolgreich. Kein neuer APK-/Hardwaretest.
