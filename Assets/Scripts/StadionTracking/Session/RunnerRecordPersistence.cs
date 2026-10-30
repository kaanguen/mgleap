using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace StadionTracking
{
    /// <summary>Shared background writer. Pending snapshots survive scene changes and failed writes.</summary>
    internal sealed class RunnerRecordPersistence
    {
        internal sealed class LoadResult
        {
            internal RunnerPersonalBest Best;
            internal string Warning;
        }
        private sealed class PendingWrite
        {
            internal string Profile;
            internal RunnerPersonalBest Best;
            internal long Version;
        }
        private static readonly object RegistryGate = new object();
        private static readonly Dictionary<string, RunnerRecordPersistence> Registry = new Dictionary<string, RunnerRecordPersistence>();
        private readonly object gate = new object();
        private readonly RunnerRecordsStore store;
        private readonly Dictionary<string, PendingWrite> pending = new Dictionary<string, PendingWrite>();
        private readonly Dictionary<string, string> errors = new Dictionary<string, string>();
        private readonly CancellationTokenSource lifetime = new CancellationTokenSource();
        private readonly int retryMilliseconds;
        private long version;
        private bool running;
        private Task worker;

        internal RunnerRecordPersistence(RunnerRecordsStore store, int retryMilliseconds = 2000)
        { this.store = store; this.retryMilliseconds = retryMilliseconds; }

        internal static RunnerRecordPersistence ForDirectory(string directory)
        {
            lock (RegistryGate)
            {
                if (!Registry.TryGetValue(directory, out var persistence))
                    Registry[directory] = persistence = new RunnerRecordPersistence(new RunnerRecordsStore(directory));
                return persistence;
            }
        }

        internal int PendingCount { get { lock (gate) return pending.Count; } }
        internal string Error
        {
            get { lock (gate) { foreach (var error in errors.Values) return error; return null; } }
        }

        internal void QueueSave(string profile, RunnerPersonalBest best)
        {
            if (profile == null || best == null || !best.HasRecords()) return;
            var snapshot = best.Copy();
            lock (gate)
            {
                if (lifetime.IsCancellationRequested) throw new ObjectDisposedException(nameof(RunnerRecordPersistence));
                if (pending.TryGetValue(profile, out var previous)) snapshot = RunnerPersonalBest.Merge(previous.Best, snapshot);
                pending[profile] = new PendingWrite { Profile = profile, Best = snapshot, Version = ++version };
                if (running) return;
                running = true;
                worker = Task.Run(ProcessPending);
            }
        }

        internal Task<LoadResult> LoadAsync(string profile, double lapLength) => Task.Run(() =>
        {
            // Include pending data both before and after disk access: a concurrent successful write
            // may remove a pending job between reading its previous disk version and taking the lock.
            var before = PendingSnapshot(profile);
            var best = store.Load(profile, lapLength, out string warning);
            if (before != null) best = RunnerPersonalBest.Merge(best, before);
            var after = PendingSnapshot(profile);
            if (after != null) best = RunnerPersonalBest.Merge(best, after);
            return new LoadResult { Best = best == null ? null : best.Copy(), Warning = warning };
        });

        private RunnerPersonalBest PendingSnapshot(string profile)
        {
            lock (gate) return pending.TryGetValue(profile, out var write) ? write.Best : null;
        }

        private async Task ProcessPending()
        {
            try
            {
                while (!lifetime.IsCancellationRequested)
                {
                    PendingWrite[] batch;
                    lock (gate)
                    {
                        if (pending.Count == 0) return;
                        batch = new PendingWrite[pending.Count];
                        pending.Values.CopyTo(batch, 0);
                    }
                    bool failed = false;
                    foreach (var write in batch)
                    {
                        if (lifetime.IsCancellationRequested) return;
                        try
                        {
                            store.SaveBetter(write.Profile, write.Best);
                            lock (gate)
                            {
                                // A newer queued improvement must not be acknowledged by an older write.
                                if (pending.TryGetValue(write.Profile, out var latest) && latest.Version == write.Version)
                                    pending.Remove(write.Profile);
                                errors.Remove(write.Profile);
                            }
                        }
                        catch (Exception e)
                        {
                            lock (gate) errors[write.Profile] = "Bestzeiten konnten nicht gespeichert werden: " + e.Message;
                            failed = true;
                        }
                    }
                    if (failed) await Task.Delay(retryMilliseconds, lifetime.Token).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) when (lifetime.IsCancellationRequested) { }
            finally
            {
                lock (gate)
                {
                    running = false;
                    // A new snapshot can arrive after the empty check but before this finally.
                    if (pending.Count > 0 && !lifetime.IsCancellationRequested)
                    {
                        running = true;
                        worker = Task.Run(ProcessPending);
                    }
                }
            }
        }

        // Used for isolated tests/owned queues. Shared scene queues normally finish on their own;
        // they deliberately outlive a destroyed HUD or session while writes are still pending.
        internal async Task StopAsync()
        {
            Task current;
            lock (gate) { lifetime.Cancel(); current = worker; }
            if (current != null) await current.ConfigureAwait(false);
        }
    }
}
