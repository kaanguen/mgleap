using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using Android.BLE;
using Android.BLE.Commands;
using UnityEngine;
using UnityEngine.Android;

public sealed class HeartRateBLE : MonoBehaviour
{
    [Header("Bluetooth-Standard-UUIDs, jeweils vier Hex-Zeichen")]
    public string serviceUuid = "180d";
    public string characteristicUuid = "2a37";
    [Range(3, 60)] public int scanSeconds = 15;
    [Tooltip("Velorexe verwendet Android autoConnect. Der erste Verbindungsaufbau kann länger als 30 Sekunden dauern.")]
    [Range(30, 180)] public int connectTimeoutSeconds = 90;

    public event Action Changed;
    public event Action<string> GattReady;
    public event Action<BleObject> RawMessage;
    public event Action<string> GattError;
    readonly HashSet<string> foundCharacteristics = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    public string ConnectedDeviceId => connectedId;
    public bool HasCharacteristic(string service, string characteristic) => foundCharacteristics.Contains(service + "/" + characteristic);
    public void ShowStatus(string text) { SetStatus(text); }
    public readonly List<string> DeviceIds = new List<string>();
    public readonly List<string> DeviceNames = new List<string>();
    public string Status { get; private set; } = "Bereit. Scan drücken.";
    public string Measurement { get; private set; } = "— bpm";
    public string RrIntervals { get; private set; } = "";
    public bool Scanning { get; private set; }
    public bool Busy { get; private set; }
    public bool Connected { get; private set; }
    public int SelectedIndex { get; private set; }

    BleManager manager;
    BleAdapter adapter;
    DiscoverDevices scan;
    ConnectToDevice connection;
    SubscribeToCharacteristic subscription;
    string connectedId;
    bool hasHeartRate, requesting, shuttingDown, waitingForHeartRate, gattOpen;
    float connectionDeadline, scanRestartAfter, lastMeasurement, firstMeasurementDeadline;

    void Notify() { Changed?.Invoke(); }
    void SetStatus(string text) { Status = text; Debug.Log("[HeartRateBLE] " + text); Notify(); }

    public void StartScan()
    {
        if (requesting || Busy || Connected || Scanning) return;
        if (Time.realtimeSinceStartup < scanRestartAfter)
        {
            SetStatus("Vor erneutem Scan noch kurz warten (Plugin-Scan-Timer).");
            return;
        }
#if UNITY_ANDROID && !UNITY_EDITOR
        StartCoroutine(InitializeAndScan());
#else
        SetStatus("BLE funktioniert hier nur im Android-Build auf der Brille.");
#endif
    }

    IEnumerator InitializeAndScan()
    {
        requesting = true;
        if (manager == null)
        {
            int api;
            using (var version = new AndroidJavaClass("android.os.Build$VERSION"))
                api = version.GetStatic<int>("SDK_INT");
            string[] permissions = api >= 31
                ? new[] { "android.permission.BLUETOOTH_SCAN", "android.permission.BLUETOOTH_CONNECT" }
                : new[] { "android.permission.ACCESS_FINE_LOCATION" };

            foreach (string permission in permissions)
            {
                if (Permission.HasUserAuthorizedPermission(permission)) continue;
                SetStatus("Bitte Berechtigung erlauben: " + permission);
                bool answered = false;
                var callbacks = new PermissionCallbacks();
                Action<string> answer = _ => answered = true;
                callbacks.PermissionGranted += answer;
                callbacks.PermissionDenied += answer;
                callbacks.PermissionDeniedAndDontAskAgain += answer;
                Permission.RequestUserPermission(permission, callbacks);
                float until = Time.realtimeSinceStartup + 90;
                while (!answered && Time.realtimeSinceStartup < until) yield return null;
                callbacks.PermissionGranted -= answer;
                callbacks.PermissionDenied -= answer;
                callbacks.PermissionDeniedAndDontAskAgain -= answer;
                if (!Permission.HasUserAuthorizedPermission(permission))
                {
                    requesting = false;
                    SetStatus("Berechtigung fehlt. In den Android-App-Einstellungen freigeben.");
                    yield break;
                }
            }
            try
            {
                // Do not place another BleManager or the upstream sample in this scene.
                manager = BleManager.Instance;
                adapter = FindObjectOfType<BleAdapter>();
                if (adapter == null) throw new InvalidOperationException("BleAdapter wurde nicht erstellt.");
                adapter.OnMessageReceived += OnMessage;
                adapter.OnErrorReceived += OnError;
            }
            catch (Exception e)
            {
                requesting = false; SetStatus("Initialisierung: " + e.Message); yield break;
            }
        }
        requesting = false;
        DeviceIds.Clear(); DeviceNames.Clear(); SelectedIndex = 0;
        scan = new DiscoverDevices(OnDeviceFound, OnScanFinished, scanSeconds * 1000);
        Scanning = true;
        // Upstream stop does not cancel its delayed timer: don't overlap scan sessions.
        scanRestartAfter = Time.realtimeSinceStartup + scanSeconds + 1;
        SetStatus("Suche BLE-Geräte …");
        try { scan.Start(); }
        catch (Exception e) { Scanning = false; SetStatus("Scan: " + e.Message); }
    }

    void OnDeviceFound(string id, string name)
    {
        if (!Scanning || string.IsNullOrEmpty(id)) return;
        // Only show matching advertised names; all other nearby BLE devices are ignored.
        if (string.IsNullOrEmpty(name)
            || (name.IndexOf("tree", StringComparison.OrdinalIgnoreCase) < 0
                && name.IndexOf("thre", StringComparison.OrdinalIgnoreCase) < 0)) return;
        int index = DeviceIds.FindIndex(x => string.Equals(x, id, StringComparison.OrdinalIgnoreCase));
        if (index < 0) { DeviceIds.Add(id); DeviceNames.Add(string.IsNullOrEmpty(name) ? "Ohne Namen" : name); }
        else if (!string.IsNullOrEmpty(name)) DeviceNames[index] = name;
        Notify();
    }
    void OnScanFinished()
    {
        Scanning = false;
        SetStatus(DeviceIds.Count > 0
            ? "Scan beendet: " + DeviceIds.Count + " passende Geräte. Gerät auswählen."
            : "Keine Geräte mit tree, thre oder three im Namen gefunden. Liste ist leer.");
    }
    public void StopScan()
    {
        if (!Scanning) return;
        Scanning = false;
        try { scan?.End(); } catch (Exception e) { SetStatus(e.Message); }
        scan = null; Notify();
    }
    public void SelectDevice(int index) { SelectedIndex = index; }
    public void Connect()
    {
        if (Busy || Connected || manager == null) return;
        if (SelectedIndex < 0 || SelectedIndex >= DeviceIds.Count) { SetStatus("Zuerst ein Gerät auswählen."); return; }
        StopScan();
        connectedId = DeviceIds[SelectedIndex]; hasHeartRate = false; foundCharacteristics.Clear();
        Busy = true; gattOpen = false; Measurement = "— bpm"; RrIntervals = ""; waitingForHeartRate = false; lastMeasurement = 0; connectionDeadline = Time.realtimeSinceStartup + connectTimeoutSeconds;
        connection = new ConnectToDevice(connectedId, OnConnected, OnDisconnected, OnService, OnCharacteristic);
        SetStatus("Verbinde mit " + DeviceNames[SelectedIndex] + " …");
        try { connection.Start(); } catch (Exception e) { OnDisconnected(connectedId); SetStatus(e.Message); }
    }
    void OnService(string device, string service)
    {
        Debug.Log("[HeartRateBLE] Service " + service + " auf " + device);
    }
    void OnCharacteristic(string device, string service, string characteristic)
    {
        Debug.Log("[HeartRateBLE] Characteristic " + service + "/" + characteristic + " auf " + device);
        foundCharacteristics.Add(service + "/" + characteristic);
        if (string.Equals(service, FullUuid(serviceUuid), StringComparison.OrdinalIgnoreCase)
            && string.Equals(characteristic, FullUuid(characteristicUuid), StringComparison.OrdinalIgnoreCase)) hasHeartRate = true;
    }
    static string FullUuid(string shortUuid) { return "0000" + shortUuid.Trim().ToLowerInvariant() + "-0000-1000-8000-00805f9b34fb"; }
    void OnConnected(string id)
    {
        if (subscription != null || !Busy) return;
        Busy = false; Connected = true;
        if (hasHeartRate)
        {
            subscription = new SubscribeToCharacteristic(id, serviceUuid.Trim().ToLowerInvariant(),
                characteristicUuid.Trim().ToLowerInvariant(), OnHeartRate);
            waitingForHeartRate = true;
            firstMeasurementDeadline = Time.realtimeSinceStartup + 25;
        }
        if (!hasHeartRate) Measurement = "nicht angeboten";
        SetStatus("Verbunden. Verfügbare Sensorkanäle werden eingerichtet …");
        try
        {
            // The optional sensor component serializes all reads and CCCD writes,
            // including heart rate. Without it the original HR-only path still works.
            if (GattReady != null) GattReady.Invoke(id);
            else if (subscription != null) subscription.Start();
            else SetStatus("Verbunden, aber Standard-Herzfrequenzkanal fehlt.");
        }
        catch (Exception e) { SetStatus("Sensorkanäle: " + e.Message); }
    }
    void OnHeartRate(byte[] packet)
    {
        waitingForHeartRate = false;
        try
        {
            var reading = HeartRateParser.ParseMeasurement(packet);
            Measurement = reading.Bpm + " bpm";
            var rr = new List<string>();
            for (int i = 0; i < Math.Min(3, reading.RrMilliseconds.Length); i++)
                rr.Add(reading.RrMilliseconds[i].ToString("0.#", CultureInfo.InvariantCulture));
            RrIntervals = rr.Count == 0 ? "" : string.Join(" / ", rr) + " ms"
                + (reading.RrMilliseconds.Length > 3 ? " …" : "");
            lastMeasurement = Time.realtimeSinceStartup;
            SetStatus("Herzfrequenz empfangen.");
        }
        catch (FormatException e) { Measurement = "— bpm"; RrIntervals = ""; SetStatus(e.Message); }
    }
    void OnDisconnected(string id)
    {
        Busy = false; Connected = false; gattOpen = false; connection = null; subscription = null; lastMeasurement = 0;
        Measurement = "— bpm"; RrIntervals = ""; waitingForHeartRate = false; connectedId = null; SetStatus("Verbindung getrennt.");
    }
    public void Disconnect()
    {
        StopScan();
        // Closing GATT also ends updates. Do not issue a CCCD write while an ATT read/write is pending.
        subscription = null;
        // Upstream only stores a GATT handle after ConnectedToGattServer. Before
        // that point disconnectDevice fails with "Can't find connected device".
        // A late callback is closed in OnMessage, even after our command is gone.
        try { if (gattOpen || Connected) connection?.Disconnect(); } catch (Exception e) { Debug.LogWarning(e.Message); }
        OnDisconnected(connectedId);
    }
    void OnMessage(BleObject message)
    {
        if (shuttingDown) return;
        if (message.Command == "ConnectedToGattServer")
        {
            if (connection == null || !string.Equals(message.Device, connectedId, StringComparison.OrdinalIgnoreCase))
            {
                Debug.LogWarning("[HeartRateBLE] Verspätete Verbindung nach Abbruch wird geschlossen: " + message.Device);
                new ConnectToDevice(message.Device).Disconnect();
                return;
            }
            gattOpen = true;
            SetStatus("Bluetooth verbunden. Sensorkanäle werden gesucht …");
        }
        // Deliberately dispatch existing commands directly. No retained parallel commands
        // or subscription timeouts in the upstream BleManager queue.
        if (Scanning && scan != null && scan.CommandReceived(message)) { scan.End(); scan = null; }
        if (connection != null) connection.CommandReceived(message);
        if (subscription != null) subscription.CommandReceived(message);
        RawMessage?.Invoke(message);
    }
    void OnError(string error) { SetStatus("BLE-Fehler: " + error); GattError?.Invoke(error); }
    void Update()
    {
        if (Busy && Time.realtimeSinceStartup > connectionDeadline) { Disconnect(); SetStatus("Verbindungszeitlimit nach " + connectTimeoutSeconds + " s. Sensor neu starten; andere Sensor-Apps schließen und erneut versuchen."); }
        if (waitingForHeartRate && Connected && Time.realtimeSinceStartup > firstMeasurementDeadline)
        {
            waitingForHeartRate = false;
            SetStatus("Verbunden, aber keine Herzfrequenz-Pakete. Sensor tragen; Sensormodus und Notifications prüfen.");
        }
        if (lastMeasurement > 0 && Time.realtimeSinceStartup - lastMeasurement > 10)
        {
            lastMeasurement = 0; RrIntervals = ""; Measurement = "— bpm (seit 10 s keine Daten)"; Notify();
        }
        if (Scanning && Time.realtimeSinceStartup > scanRestartAfter + 2) StopScan();
    }
    void OnApplicationPause(bool paused) { if (paused && !requesting) Disconnect(); }
    void OnDestroy()
    {
        shuttingDown = true;
        if (adapter != null) { adapter.OnMessageReceived -= OnMessage; adapter.OnErrorReceived -= OnError; }
        Disconnect(); Changed = null; GattReady = null; RawMessage = null; GattError = null;
        // BleManager and its adapter are scene objects and are cleaned up by Unity.
    }
}
