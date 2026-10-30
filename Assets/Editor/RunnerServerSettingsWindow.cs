using System;
using System.IO;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using StadionTracking;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

public sealed class RunnerServerSettingsWindow : EditorWindow
{
    private const string ConfigPath = "Assets/Resources/RunnerServer.json";
    private TextField address;
    private Toggle preferBundled;
    private HelpBox feedback;
    private Button save;

    [MenuItem("Tools/RunningServer/Serveradresse einstellen")]
    public static void Open()
    {
        var window = GetWindow<RunnerServerSettingsWindow>("RunningServer-Adresse");
        window.minSize = new Vector2(540, 420);
    }

    public void CreateGUI()
    {
        var root = rootVisualElement;
        root.Clear();
        root.style.paddingLeft = root.style.paddingRight = 16;
        root.style.paddingTop = root.style.paddingBottom = 16;
        var scroll = new ScrollView();
        root.Add(scroll);
        var heading = new Label("RunningServer verbinden");
        heading.style.fontSize = 19;
        heading.style.unityFontStyleAndWeight = FontStyle.Bold;
        heading.style.marginBottom = 12;
        scroll.Add(heading);
        scroll.Add(new HelpBox("Trage die aktuelle WLAN-/LAN-IP des Rechners ein, auf dem der RunningServer läuft. Brille und Rechner müssen sich erreichen können. Eine IP allein genügt; Port 8081 und /ws werden ergänzt.", HelpBoxMessageType.Info));
        address = new TextField("Serveradresse") { name = "server-address" };
        address.style.marginTop = 12;
        scroll.Add(address);
        preferBundled = new Toggle("Build-Adresse beim App-Start bevorzugen") { name = "prefer-build-address" };
        scroll.Add(preferBundled);
        scroll.Add(new HelpBox("Eingeschaltet: Eine früher auf der Brille gespeicherte IP wird beim Start ignoriert. Du kannst die Adresse im Brillen-Dialog trotzdem für die laufende Sitzung ändern. Ausgeschaltet: Die auf der Brille gespeicherte Adresse hat Vorrang.", HelpBoxMessageType.Info));
        save = new Button(Save) { text = "Adresse für nächsten Build speichern", name = "save-server-address" };
        save.style.marginTop = 12;
        save.style.height = 30;
        scroll.Add(save);
        feedback = new HelpBox("", HelpBoxMessageType.Info) { name = "server-address-feedback" };
        scroll.Add(feedback);
        scroll.Add(new Label("IP-Adressen dieses Rechners zum Übernehmen:") { style = { marginTop = 16 } });
        try
        {
            int count = 0;
            foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (network.OperationalStatus != OperationalStatus.Up || network.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                foreach (var item in network.GetIPProperties().UnicastAddresses)
                {
                    if (item.Address.AddressFamily != AddressFamily.InterNetwork) continue;
                    string ip = item.Address.ToString();
                    scroll.Add(new Button(() => address.value = ip) { text = ip + "  (" + network.Name + ")" });
                    count++;
                }
            }
            if (count == 0) scroll.Add(new Label("Keine aktive IPv4-Adresse erkannt. Adresse manuell eingeben."));
        }
        catch (Exception e) { scroll.Add(new HelpBox("IP-Adressen konnten nicht gelesen werden: " + e.Message, HelpBoxMessageType.Warning)); }
        scroll.Add(new Button(() => address.value = "ws://127.0.0.1:8081/ws") { text = "USB-Adresse übernehmen" });
        scroll.Add(new HelpBox("Nach dem Speichern die Android-App neu bauen und auf der Brille installieren. Das ändert keine bereits installierte App. USB benötigt zusätzlich: adb reverse tcp:8081 tcp:8081.", HelpBoxMessageType.Info));
        try
        {
            string json = File.ReadAllText(ConfigPath);
            var config = JsonUtility.FromJson<RunnerServerConfiguration>(json);
            address.SetValueWithoutNotify(config.serverUrl);
            preferBundled.SetValueWithoutNotify(!json.Contains("\"preferBundledAddress\"") || config.preferBundledAddress);
        }
        catch (Exception e)
        {
            preferBundled.SetValueWithoutNotify(true);
            feedback.text = "Konfiguration nicht geladen: " + e.Message;
            feedback.messageType = HelpBoxMessageType.Warning;
        }
        address.RegisterValueChangedCallback(_ => ValidateAddress());
        ValidateAddress();
    }

    private void ValidateAddress()
    {
        bool valid = RunnerServerConfiguration.TryNormalizeAddress(address.value, out var uri);
        save.SetEnabled(valid && !EditorApplication.isPlayingOrWillChangePlaymode);
        feedback.text = valid ? "Adresse im nächsten Build: " + uri.AbsoluteUri : "Bitte eine gültige IP-Adresse oder ws:// bzw. wss:// Adresse eingeben.";
        feedback.messageType = valid ? HelpBoxMessageType.Info : HelpBoxMessageType.Warning;
    }

    private void Save()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) return;
        if (!RunnerServerConfiguration.TryNormalizeAddress(address.value, out var uri)) return;
        try
        {
            var config = new RunnerServerConfiguration { serverUrl = uri.AbsoluteUri, preferBundledAddress = preferBundled.value };
            Directory.CreateDirectory(Path.GetDirectoryName(ConfigPath));
            File.WriteAllText(ConfigPath, JsonUtility.ToJson(config, true) + "\n");
            AssetDatabase.ImportAsset(ConfigPath);
            address.SetValueWithoutNotify(uri.AbsoluteUri);
            feedback.text = "Gespeichert. Jetzt neu bauen und auf der Brille installieren: " + uri.AbsoluteUri;
            feedback.messageType = HelpBoxMessageType.Info;
        }
        catch (Exception e) { feedback.text = "Speichern fehlgeschlagen: " + e.Message; feedback.messageType = HelpBoxMessageType.Error; }
    }
}
