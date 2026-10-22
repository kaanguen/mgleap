using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using Android.BLE;
using UnityEngine;

public sealed class CosinussMeasurements : MonoBehaviour
{
    public enum OxygenFormat { Auto, Standard11073, LegacyIeeeHalf }
    public HeartRateBLE bluetooth;
    [Tooltip("Auto recognizes Two firmware <=2.0.2 only if model/version can be read. Otherwise the SIG standard is assumed.")]
    public OxygenFormat oxygenFormat = OxygenFormat.Auto;
    public event Action Changed;
    const string CustomService = "0000a000-1212-efde-1523-785feabcd123";
    const string CustomStatus = "0000a002-1212-efde-1523-785feabcd123";
    public static string Uuid(string shortUuid) => "0000" + shortUuid + "-0000-1000-8000-00805f9b34fb";
    sealed class Operation {
        public string Service, Characteristic, Label;
        public bool Subscribe;
        public int Mode;
        public Action<byte[]> Read;
    }
    readonly Queue<Operation> queue = new Queue<Operation>();
    readonly Dictionary<string,string> values = new Dictionary<string,string>();
    readonly Dictionary<string,float> received = new Dictionary<string,float>();
    readonly Dictionary<string,float> requested = new Dictionary<string,float>();
    Operation active;
    AndroidJavaClass bridge;
    string address="", model="", firmware="";
    float operationDeadline;
    bool maximumQuality;
    public string Details => "Temperatur: " + Get("Temperatur")
        + "\nSpO2: " + Get("SpO2") + " | Perfusion: " + Get("Perfusion")
        + "\nAkku: " + Get("Akku") + " | Signalqualität: " + Get("Signalqualität")
        + "\nModell: " + Get("Modell") + " | Hersteller: " + Get("Hersteller")
        + "\nFirmware: " + Get("Firmware") + " | Software: " + Get("Software")
        + "\nHardware: " + Get("Hardware")
        + "\nSensorhinweis: " + Get("Sensorhinweis")
        + "\nSpO2-Status: " + Get("SpO2-Status")
        + "\nSpO2-Format: " + EncodingLabel()
        + "\nAdresse: " + (string.IsNullOrEmpty(address) ? "—" : address);
    string Get(string key) => values.TryGetValue(key,out var text) ? text : "—";
    static string Number(double x,string unit) => double.IsNaN(x) || double.IsInfinity(x)
        ? "nicht verfügbar" : x.ToString("0.##",CultureInfo.InvariantCulture) + unit;
    void OnEnable() {
        bluetooth = bluetooth != null ? bluetooth : GetComponent<HeartRateBLE>();
        if (bluetooth == null) { enabled=false; return; }
        bluetooth.GattReady += Configure;
        bluetooth.RawMessage += OnMessage;
        bluetooth.GattError += OnError;
        bluetooth.Changed += OnState;
    }
    void OnDisable() {
        if (bluetooth != null) {
            bluetooth.GattReady -= Configure; bluetooth.RawMessage -= OnMessage;
            bluetooth.GattError -= OnError; bluetooth.Changed -= OnState;
            if (bluetooth.Connected && !string.IsNullOrEmpty(address)) bluetooth.Disconnect();
        }
        Reset(); bridge?.Dispose(); bridge=null;
    }
    void Reset() {
        queue.Clear(); active=null; address=""; model=""; firmware=""; maximumQuality=false;
        values.Clear(); received.Clear(); requested.Clear(); Changed?.Invoke();
    }
    void OnState() { if (!bluetooth.Connected && !string.IsNullOrEmpty(address)) Reset(); }
    void Configure(string device) {
        Reset(); address=device;
#if UNITY_ANDROID && !UNITY_EDITOR
        try {
            bridge = bridge ?? new AndroidJavaClass("com.markerml2.ble.CosinussGatt");
            // Device information first, so legacy SpO2 decoding can be selected before subscribing.
            AddRead("180a","2a24","Modell", b => { model=Text(b); Set("Modell",model); });
            AddRead("180a","2a26","Firmware", b => { firmware=Text(b); Set("Firmware",firmware); });
            AddRead("180a","2a29","Hersteller", b => Set("Hersteller",Text(b)));
            AddRead("180a","2a28","Software", b => Set("Software",Text(b)));
            AddRead("180a","2a27","Hardware", b => Set("Hardware",Text(b)));
            AddSubscription(Uuid(bluetooth.serviceUuid.ToLowerInvariant()),Uuid(bluetooth.characteristicUuid.ToLowerInvariant()),"Herzfrequenz",1);
            AddSubscription(Uuid("1809"),Uuid("2a1c"),"Temperatur",2);
            bool plx=AddSubscription(Uuid("1822"),Uuid("2a5f"),"SpO2",1);
            Set("Perfusion",plx ? "wartet auf Daten" : "nicht angeboten");
            Set("SpO2-Status",plx ? "wartet auf Daten" : "nicht angeboten");
            AddRead("180f","2a19","Akku", b => SetSample("Akku",CosinussParsers.Battery(b)+" %"));
            AddSubscription(Uuid("180f"),Uuid("2a19"),"Akku",1);
            bool soc=AddSubscription(CustomService,CustomStatus,"Signalqualität",1);
            Set("Sensorhinweis",soc ? "noch kein Hinweis empfangen" : "nicht angeboten");
            Next();
        } catch (Exception e) { OnError("Native Ergänzung: " + e.Message); }
#else
        Set("Sensorhinweis","Empfang benötigt Android-Gerätebuild");
#endif
    }
    static string Text(byte[] bytes) => System.Text.Encoding.UTF8.GetString(bytes).TrimEnd('\0').Replace('\n',' ').Replace('\r',' ');
    bool Available(string s,string c) => bluetooth.HasCharacteristic(s,c);
    void AddRead(string service,string characteristic,string label,Action<byte[]> apply) {
        string s=Uuid(service),c=Uuid(characteristic);
        if (!Available(s,c)) { Set(label,"nicht angeboten"); return; }
        Set(label,"wird gelesen"); queue.Enqueue(new Operation {Service=s,Characteristic=c,Label=label,Read=apply});
    }
    bool AddSubscription(string service,string characteristic,string label,int mode) {
        if (!Available(service,characteristic)) { Set(label,"nicht angeboten"); return false; }
        queue.Enqueue(new Operation {Service=service,Characteristic=characteristic,Label=label,Subscribe=true,Mode=mode});
        return true;
    }
    void Next() {
        if (active != null || !bluetooth.Connected || string.IsNullOrEmpty(address)) return;
        while (queue.Count>0) {
            var op=queue.Dequeue();
            try {
                int p=bridge.CallStatic<int>("properties",address,op.Service,op.Characteristic);
                if (op.Subscribe && (p & 0x30)==0) {
                    // A readable battery may have no notification support.
                    if (!received.ContainsKey(op.Label)) Set(op.Label,"keine Updates unterstützt");
                    continue;
                }
                if (!op.Subscribe && (p & 2)==0) { Set(op.Label,"READ nicht unterstützt"); continue; }
                active=op; operationDeadline=Time.realtimeSinceStartup+10;
                if (op.Subscribe) {
                    int mode=bridge.CallStatic<int>("subscribe",address,op.Service,op.Characteristic,op.Mode);
                    requested[op.Label]=Time.realtimeSinceStartup;
                    if (!received.ContainsKey(op.Label)) Set(op.Label,"wartet auf Daten");
                    Debug.Log("[Cosinuss] " + op.Label + ": " + (mode==2 ? "Indication" : "Notification") + " angefordert");
                } else if (!bridge.CallStatic<bool>("read",address,op.Service,op.Characteristic))
                    throw new InvalidOperationException("READ-Auftrag abgelehnt");
                return;
            } catch (Exception e) { active=null; Set(op.Label,"Fehler: " + e.Message); }
        }
        bluetooth.ShowStatus("Kanal-Anfragen abgeschlossen. Messwerte werden angezeigt, sobald sie eintreffen.");
        Changed?.Invoke();
    }
    void OnError(string error) {
        if (string.IsNullOrEmpty(address)) return;
        if (active != null) Set(active.Label,"GATT-Fehler");
        queue.Clear(); active=null;
        bluetooth.Disconnect(); bluetooth.ShowStatus("Sensorkanäle: " + error + ". Bitte erneut verbinden.");
    }
    void OnMessage(BleObject m) {
        if (string.IsNullOrEmpty(address) || !string.Equals(m.Device,address,StringComparison.OrdinalIgnoreCase)) return;
        if (active != null && string.Equals(m.Service,active.Service,StringComparison.OrdinalIgnoreCase)
            && string.Equals(m.Characteristic,active.Characteristic,StringComparison.OrdinalIgnoreCase)
            && ((active.Subscribe && m.Command=="DescriptorWrite" && string.Equals(m.Descriptor,Uuid("2902"),StringComparison.OrdinalIgnoreCase))
                || (!active.Subscribe && m.Command=="ReadFromCharacteristic"))) {
            var done=active; active=null;
            try { if (!done.Subscribe) done.Read(m.GetByteMessage()); }
            catch (Exception e) { Set(done.Label,"Ungültige Daten: " + e.Message); }
            Next();
        }
        if (m.Command != "CharacteristicValueChanged") return;
        string key=(m.Service+"/"+m.Characteristic).ToLowerInvariant();
        try {
            byte[] bytes=m.GetByteMessage();
            if (key==Uuid("1809")+"/"+Uuid("2a1c")) {
                var t=CosinussParsers.Temperature(bytes);
                SetSample("Temperatur",Number(t.Celsius," °C") + (t.Special==null ? "" : " ("+t.Special+")"));
            } else if (key==Uuid("1822")+"/"+Uuid("2a5f")) {
                var p=CosinussParsers.Plx(bytes,Encoding());
                bool statusWarning=(p.MeasurementStatus.GetValueOrDefault() & 0xFC00)!=0 || (p.DeviceStatus.GetValueOrDefault() & 0xFFFE)!=0;
                SetSample("SpO2",(p.Spo2<0 || p.Spo2>100 ? "ungültiger Wertebereich" : Number(p.Spo2," %"))
                    + (statusWarning ? " (Status prüfen)" : ""));
                SetSample("Perfusion",Number(p.Perfusion," %"));
                SetSample("SpO2-Status",(p.MeasurementStatus.HasValue ? "M=0x"+p.MeasurementStatus.Value.ToString("X4") : "kein Messstatus")
                    + (p.DeviceStatus.HasValue ? " D=0x"+p.DeviceStatus.Value.ToString("X6") : ""));
            } else if (key==Uuid("180f")+"/"+Uuid("2a19")) SetSample("Akku",CosinussParsers.Battery(bytes)+" %");
            else if (key==CustomService+"/"+CustomStatus) {
                var p=CosinussParsers.Sensor(bytes);
                if (p.Quality.HasValue && (p.IsMaximumQuality || !maximumQuality)) {
                    maximumQuality=p.IsMaximumQuality; SetSample("Signalqualität",p.Quality.Value.ToString() + (p.IsMaximumQuality ? " (max)" : ""));
                }
                if (p.ErrorCode.HasValue) SetSample("Sensorhinweis","0x"+p.ErrorCode.Value.ToString("X2")+" "+CosinussParsers.ErrorDescription(p.ErrorCode.Value));
            }
        } catch (Exception e) {
            Debug.LogWarning("[Cosinuss] Paket " + key + ": " + e.Message);
            if (key==Uuid("1809")+"/"+Uuid("2a1c")) SetSample("Temperatur","Paket ungültig");
            if (key==Uuid("1822")+"/"+Uuid("2a5f")) { SetSample("SpO2","Paket ungültig"); SetSample("Perfusion","—"); }
        }
    }
    CosinussParsers.PlxEncoding Encoding() {
        if (oxygenFormat==OxygenFormat.LegacyIeeeHalf) return CosinussParsers.PlxEncoding.LegacyIeeeHalf;
        if (oxygenFormat==OxygenFormat.Auto && model.IndexOf("two",StringComparison.OrdinalIgnoreCase)>=0
            && Version.TryParse(firmware.TrimStart('v','V').Split('-')[0],out var v) && v<=new Version(2,0,2))
            return CosinussParsers.PlxEncoding.LegacyIeeeHalf;
        return CosinussParsers.PlxEncoding.Standard11073;
    }
    string EncodingLabel() => Encoding()==CosinussParsers.PlxEncoding.LegacyIeeeHalf ? "IEEE Half (Legacy)"
        : oxygenFormat==OxygenFormat.Auto ? "IEEE 11073 (Standardannahme)" : "IEEE 11073";
    void Set(string key,string value) { values[key]=value; Changed?.Invoke(); }
    void SetSample(string key,string value) { received[key]=Time.realtimeSinceStartup; requested.Remove(key); Set(key,value); }
    void Update() {
        if (active != null && Time.realtimeSinceStartup>operationDeadline) { OnError("GATT-Zeitlimit für " + active.Label); return; }
        float now=Time.realtimeSinceStartup;
        foreach (var key in new List<string>(requested.Keys)) if (now-requested[key]>15) {
            requested.Remove(key); if (!received.ContainsKey(key)) Set(key,"keine Updates empfangen");
        }
        foreach (var key in new List<string>(received.Keys)) {
            float timeout=key=="Akku" ? 300 : key=="Temperatur" ? 35 : 15;
            if (now-received[key]>timeout) { received.Remove(key); Set(key,"— (keine aktuellen Daten)"); }
        }
    }
}
