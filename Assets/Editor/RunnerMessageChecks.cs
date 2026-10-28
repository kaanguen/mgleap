using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

internal static class RunnerMessageChecks
{
    [MenuItem("Tools/Runner UI/Checks/Check server message validation")]
    public static void Verify()
    {
        var scene = EditorSceneManager.NewPreviewScene();
        const BindingFlags hidden = BindingFlags.Instance | BindingFlags.NonPublic;
        try
        {
            var host = new GameObject("Message envelope checks");
            host.SetActive(false);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(host, scene);
            var world = host.AddComponent<StraightTreadmillWorld>();
            var client = host.AddComponent<RunnerServerClient>();
            typeof(RunnerServerClient).GetField("world", hidden).SetValue(client, world);
            var handle = typeof(RunnerServerClient).GetMethod("Handle", hidden);
            var acks = (Dictionary<string, string>)typeof(RunnerServerClient).GetField("acknowledgements", hidden).GetValue(client);
            foreach (string malformed in new[] { "{", "[]", "null", "{}",
                "{\"type\":{},\"commandId\":\"review\"}",
                "{\"type\":[],\"commandId\":\"review\"}",
                "{\"type\":true,\"commandId\":\"review\"}",
                "{\"type\":\"command\",\"commandId\":{}}",
                "{\"type\":\"command\",\"commandId\":123}",
                "{\"type\":\"command\",\"commandId\":\"   \"}",
                "{\"type\":\"telemetry\",\"commandId\":{}}" })
                handle.Invoke(client, new object[] { malformed });
            Check(acks.Count == 0 && client.State == RunnerSessionState.Stopped, "unusable envelopes must be ignored without executing commands");

            int nextId = 0;
            foreach (JToken invalid in new JToken[] { new JObject(), new JArray(), new JValue(true), new JValue(123), new JValue(" "), JValue.CreateNull() })
            {
                string id = "bad-command-" + nextId++;
                handle.Invoke(client, new object[] { new JObject {
                    ["type"] = "command", ["commandId"] = id, ["commandType"] = invalid,
                    ["commandPayload"] = new JObject { ["action"] = "pause" }
                }.ToString() });
                Check(!(bool)JObject.Parse(acks[id])["success"], "invalid commandType must receive a failure ACK");
            }
            foreach (JToken invalid in new JToken[] { new JArray(), new JValue("pause"), new JValue(true), new JValue(123) })
            {
                string id = "bad-payload-" + nextId++;
                handle.Invoke(client, new object[] { new JObject {
                    ["type"] = "command", ["commandId"] = id, ["commandType"] = "runningMate.command", ["commandPayload"] = invalid
                }.ToString() });
                Check(!(bool)JObject.Parse(acks[id])["success"], "non-object payload must receive a failure ACK");
            }
            Check(client.State == RunnerSessionState.Stopped, "malformed commands must not change session state");

            string valid = new JObject {
                ["type"] = "command", ["commandId"] = "valid-after-errors", ["commandType"] = "runningMate.command",
                ["commandPayload"] = new JObject { ["action"] = "pause" }
            }.ToString();
            handle.Invoke(client, new object[] { valid });
            var ack = JObject.Parse(acks["valid-after-errors"]);
            Check((bool)ack["success"] && (string)ack["data"]["state"] == "Paused" && world.SessionPaused,
                "valid commands following malformed messages must still execute with the original ACK format");
            typeof(RunnerServerClient).GetMethod("SetSessionState", hidden).Invoke(client, new object[] { RunnerSessionState.Running });
            handle.Invoke(client, new object[] { valid });
            Check(client.State == RunnerSessionState.Running, "duplicate valid command IDs must replay their cached ACK without executing again");
            foreach (bool explicitNull in new[] { false, true })
            {
                string id = "optional-payload-" + explicitNull;
                var command = new JObject { ["type"] = "command", ["commandId"] = id, ["commandType"] = "scene.rescanMarker" };
                if (explicitNull) command["commandPayload"] = JValue.CreateNull();
                handle.Invoke(client, new object[] { command.ToString() });
                Check((bool)JObject.Parse(acks[id])["success"], "missing and null payload remain compatible with empty-payload commands");
            }
            Debug.Log("[RunnerMessages CHECK] PASS: malformed JSON/envelopes cannot escape; invalid command fields receive failure ACKs; valid commands, optional payloads and deduplication remain compatible.");
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    private static void Check(bool passed, string message)
    { if (!passed) throw new InvalidOperationException("[RunnerMessages CHECK] FAIL: " + message); }
}
