using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace StadionTracking
{
    public sealed partial class RunnerServerClient
    {
        private void StartWarmup(JObject payload)
        {
            RunnerCommandValues.RequireFields(payload, "exercise.startWarmup", "steps");
            if (!(payload["steps"] is JArray input) || input.Count < 1 || input.Count > 20)
                throw new ArgumentException("steps muss 1 bis 20 Übungen enthalten.");
            var plan = new List<RunnerWarmupStep>();
            foreach (var token in input)
            {
                if (!(token is JObject step)) throw new ArgumentException("Jede Übung muss ein Objekt sein.");
                RunnerCommandValues.RequireFields(step, "Warm-up-Schritt", "animationId", "seconds");
                if (step["animationId"]?.Type != JTokenType.String || step["seconds"] == null)
                    throw new ArgumentException("Jede Übung braucht animationId und seconds.");
                plan.Add(new RunnerWarmupStep((string)step["animationId"], RunnerCommandValues.Decimal(step, "seconds", 30, 5, 300)));
            }
            var warmup = RunnerWarmup.GetOrCreate(world);
            warmup.Validate(plan);
            if (warmup.Active) throw new InvalidOperationException("Ein Warm-up läuft bereits. Zuerst beenden.");
            if (state == RunnerSessionState.Running || state == RunnerSessionState.Paused || session.ElapsedSeconds > .01f || world.TravelledMetres > .01)
                throw new InvalidOperationException("Warm-up ist vor dem Lauf verfügbar. Für einen neuen Lauf zuerst zurücksetzen.");
            if (companion == null || companion.AvatarPrefab == null) throw new InvalidOperationException("Avatar ist nicht verfügbar.");
            // All payload and placement validation precedes any state change.
            SetSessionState(RunnerSessionState.Ready);
            companion.enabled = true;
            warmup.Begin(plan);
            RunnerWarmupHud.GetOrCreate(world);
            companion.Synchronize();
        }

        private JObject WarmupTelemetry()
        {
            var warmup = world.GetComponent<RunnerWarmup>();
            if (warmup == null) return new JObject { ["active"] = false, ["status"] = "Idle" };
            var plan = new JArray();
            foreach (var step in warmup.Steps)
                plan.Add(new JObject { ["animationId"] = step.animationId, ["seconds"] = step.seconds });
            return new JObject {
                ["active"] = warmup.Active, ["status"] = warmup.Status, ["finishing"] = warmup.Finishing,
                ["stepIndex"] = warmup.StepIndex, ["stepCount"] = warmup.StepCount,
                ["animationId"] = warmup.AnimationId, ["label"] = warmup.Label,
                ["stepElapsed"] = warmup.StepElapsed, ["stepDuration"] = warmup.StepDuration,
                ["elapsed"] = warmup.TotalElapsed, ["duration"] = warmup.TotalDuration, ["steps"] = plan
            };
        }
    }
}
