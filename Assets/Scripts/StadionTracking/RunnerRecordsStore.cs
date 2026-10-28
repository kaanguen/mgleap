using System;
using System.IO;
using Newtonsoft.Json;

namespace StadionTracking
{
    /// <summary>Device-local personal bests with atomic replacement and a previous-file backup.</summary>
    internal sealed class RunnerRecordsStore
    {
        private const long MaximumFileBytes = 4 * 1024 * 1024;
        private readonly string directory;
        private readonly object fileGate = new object();
        internal RunnerRecordsStore(string directory) { this.directory = directory; }

        internal RunnerPersonalBest Load(string profile, double lapLength, out string warning)
        {
            lock (fileGate) return LoadCore(profile, lapLength, out warning);
        }

        private RunnerPersonalBest LoadCore(string profile, double lapLength, out string warning)
        {
            warning = null;
            string path = GetPath(profile);
            foreach (string candidate in new[] { path, path + ".bak" })
            {
                if (!File.Exists(candidate)) continue;
                try
                {
                    if (new FileInfo(candidate).Length > MaximumFileBytes) throw new InvalidDataException("Datei ist zu groß.");
                    var data = JsonConvert.DeserializeObject<RunnerPersonalBest>(File.ReadAllText(candidate),
                        new JsonSerializerSettings { MaxDepth = 16, TypeNameHandling = TypeNameHandling.None });
                    if (!new LapCheckpointTracker(lapLength).TryRestorePersonalBest(data))
                        throw new InvalidDataException("Ungültige Bestzeiten oder Ghost-Daten.");
                    return data;
                }
                catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
                { warning = "Bestzeiten konnten nicht vollständig geladen werden: " + e.Message; }
            }
            return null;
        }

        internal void SaveBetter(string profile, RunnerPersonalBest candidate)
        {
            lock (fileGate) SaveBetterCore(profile, candidate);
        }

        private void SaveBetterCore(string profile, RunnerPersonalBest candidate)
        {
            if (candidate == null || !candidate.HasRecords()) return;
            if (!new LapCheckpointTracker(candidate.LapLengthMetres).TryRestorePersonalBest(candidate))
                throw new InvalidDataException("Ungültige Bestzeiten werden nicht gespeichert.");
            var saved = LoadCore(profile, candidate.LapLengthMetres, out _);
            var best = RunnerPersonalBest.Merge(saved, candidate);
            Directory.CreateDirectory(directory);
            string path = GetPath(profile), temporary = path + ".tmp";
            try
            {
                File.WriteAllText(temporary, JsonConvert.SerializeObject(best, Formatting.None));
                // Never rotate an invalid primary over the valid backup used for recovery.
                if (File.Exists(path)) File.Replace(temporary, path,
                    IsValidFile(path, candidate.LapLengthMetres) ? path + ".bak" : null);
                else File.Move(temporary, path);
            }
            finally { if (File.Exists(temporary)) File.Delete(temporary); }
        }

        private static bool IsValidFile(string path, double lapLength)
        {
            try
            {
                if (new FileInfo(path).Length > MaximumFileBytes) return false;
                var data = JsonConvert.DeserializeObject<RunnerPersonalBest>(File.ReadAllText(path),
                    new JsonSerializerSettings { MaxDepth = 16, TypeNameHandling = TypeNameHandling.None });
                return new LapCheckpointTracker(lapLength).TryRestorePersonalBest(data);
            }
            catch (Exception e) when (e is IOException || e is UnauthorizedAccessException || e is JsonException)
            { return false; }
        }

        private string GetPath(string profile)
        {
            if (string.IsNullOrEmpty(profile)) throw new ArgumentException("Profile is required.");
            foreach (char c in profile)
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') &&
                    !(c >= '0' && c <= '9') && c != '-' && c != '_') throw new ArgumentException("Invalid profile name.");
            return Path.Combine(directory, profile + ".json");
        }
    }
}
