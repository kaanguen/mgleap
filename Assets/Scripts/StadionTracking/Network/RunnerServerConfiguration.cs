using System;
using System.IO;
using UnityEngine;

namespace StadionTracking
{
    /// <summary>Shared address validation and device-local server configuration.</summary>
    [Serializable]
    public sealed class RunnerServerConfiguration
    {
        private const int DefaultPort = 8081;
        private const string DefaultUrl = "ws://127.0.0.1:8081/ws";
        private const string ResourceName = "RunnerServer";
        private const string OverrideFileName = "runner-server.json";
        public string serverUrl = DefaultUrl;
        public bool preferBundledAddress;
        private static string OverridePath => Path.Combine(Application.persistentDataPath, OverrideFileName);

        internal static string Load(out string error)
        {
            string url = DefaultUrl;
            error = null;
            try
            {
                var bundled = Resources.Load<TextAsset>(ResourceName);
                var config = bundled != null ? JsonUtility.FromJson<RunnerServerConfiguration>(bundled.text) : new RunnerServerConfiguration();
                if (config == null || !TryNormalizeAddress(config.serverUrl, out var bundledUri))
                    throw new InvalidDataException("Ungültige Serveradresse im Build.");
                url = bundledUri.AbsoluteUri;
                if (!config.preferBundledAddress && File.Exists(OverridePath)) url = ReadAddress(File.ReadAllText(OverridePath));
            }
            catch (Exception e) { error = "Gespeicherte Adresse konnte nicht geladen werden: " + e.Message; }
            return url;
        }

        private static string ReadAddress(string json)
        {
            var config = JsonUtility.FromJson<RunnerServerConfiguration>(json);
            if (config == null || string.IsNullOrWhiteSpace(config.serverUrl))
                throw new InvalidDataException("serverUrl fehlt.");
            return config.serverUrl;
        }

        internal static void Save(Uri uri) => File.WriteAllText(OverridePath,
            JsonUtility.ToJson(new RunnerServerConfiguration { serverUrl = uri.AbsoluteUri }, true));

        public static bool TryNormalizeAddress(string value, out Uri uri)
        {
            uri = null;
            value = (value ?? "").Trim();
            if (value.Length == 0) return false;
            if (!value.Contains("://")) value = "ws://" + value;
            if (!Uri.TryCreate(value, UriKind.Absolute, out var parsed) ||
                (parsed.Scheme != "ws" && parsed.Scheme != "wss") || string.IsNullOrEmpty(parsed.Host) ||
                !string.IsNullOrEmpty(parsed.UserInfo) || !string.IsNullOrEmpty(parsed.Fragment)) return false;
            var builder = new UriBuilder(parsed);
            // Preserve explicitly supplied ports, including the standard ws/wss ports.
            string authority = value.Substring(value.IndexOf("://", StringComparison.Ordinal) + 3).Split('/', '?')[0];
            bool explicitPort = authority.StartsWith("[") ? authority.Contains("]:") : authority.Contains(":");
            if (!explicitPort) builder.Port = DefaultPort;
            if (parsed.AbsolutePath == "/") builder.Path = "/ws";
            uri = builder.Uri;
            return true;
        }
    }
}
