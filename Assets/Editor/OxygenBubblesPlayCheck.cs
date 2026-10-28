using System;
using System.IO;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

// Isolated Play-mode fixture; restores the running scene after checking the actual frames.
[InitializeOnLoad]
internal static class OxygenBubblesPlayCheck
{
    private const string Flag="OxygenBubbles.PlayCheck";
    private static StraightTreadmillWorld world;
    private static TrackCollectibles pickups;
    private static Transform first;
    private static Vector3 initial;
    private static int firstFrame, lastFrame, notifications;
    private static bool observedMovement, observedTrail, running;
    private static double began;
    static OxygenBubblesPlayCheck()
    {
        EditorApplication.playModeStateChanged += State;
        EditorApplication.update += Tick;
    }
    [MenuItem("Tools/Treadmill/Checks/Check oxygen bubbles in Play Mode")]
    public static void Begin()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;
        SessionState.SetString(Flag+".scene",EditorSceneManager.GetActiveScene().path);
        EditorSceneManager.NewScene(NewSceneSetup.EmptyScene,NewSceneMode.Single);
        SessionState.SetBool(Flag,true);
        EditorApplication.isPlaying=true;
    }
    private static void State(PlayModeStateChange state)
    {
        if (!SessionState.GetBool(Flag,false)) return;
        if (state==PlayModeStateChange.EnteredPlayMode)
        {
            var camera = new GameObject("Oxygen test camera").AddComponent<Camera>();
            camera.tag="MainCamera";
            camera.transform.position=new Vector3(0,1.7f,0);
            camera.backgroundColor=new Color(.008f,.015f,.04f);
            camera.clearFlags=CameraClearFlags.SolidColor;
            var host=new GameObject("Oxygen test world");
            host.SetActive(false);
            world=host.AddComponent<StraightTreadmillWorld>();
            OxygenBubblesChecks.Set(world,"headCamera",camera);
            OxygenBubblesChecks.Set(world,"requireMarkerPlacement",false);
            OxygenBubblesChecks.Set(world,"requireHeightCalibration",false);
            OxygenBubblesChecks.Set(world,"automaticHeightOnStart",false);
            OxygenBubblesChecks.Set(world,"alignToViewOnStart",false);
            world.enabled=false; // Explicit progress below isolates timing from the game world.
            world.ResetDistance();
            world.ConfigureUserSpeed(RunnerSpeedSource.FixedTest,12);
            pickups=host.AddComponent<TrackCollectibles>();
            OxygenBubblesChecks.Set(pickups,"world",world);
            OxygenBubblesChecks.Set(pickups,"collectiblePrefab",AssetDatabase.LoadAssetAtPath<GameObject>(OxygenBubblesSetup.PrefabPath));
            OxygenBubblesChecks.Set(pickups,"firstSpawnDistance",3f);
            OxygenBubblesChecks.Set(pickups,"visibleDistance",8f);
            pickups.ConfigureSpawning(CollectibleSpawnSource.Fixed,1);
            host.SetActive(true);
            pickups.Tick(0);
            first=((Transform)OxygenBubblesChecks.Get(pickups,"container")).GetChild(0);
            initial=first.position;
            pickups.Collected += count => notifications++;
            firstFrame=lastFrame=Time.frameCount;
            began=EditorApplication.timeSinceStartup;
            running=true;
            // Batch-like unfocused editor sessions must keep advancing during verification.
            Application.runInBackground=true;
        }
        if (state==PlayModeStateChange.EnteredEditMode)
        {
            SessionState.SetBool(Flag,false);
            string path=SessionState.GetString(Flag+".scene",OxygenBubblesSetup.ScenePath);
            if (!string.IsNullOrEmpty(path)) EditorSceneManager.OpenScene(path);
            Selection.activeObject=AssetDatabase.LoadAssetAtPath<GameObject>(OxygenBubblesSetup.PrefabPath);
            EditorApplication.ExecuteMenuItem("Window/General/Game");
        }
    }
    private static void Tick()
    {
        if (!running || !EditorApplication.isPlaying) return;
        try
        {
            if (Time.frameCount!=lastFrame)
            {
                lastFrame=Time.frameCount;
                world.AdvanceSimulation(Time.deltaTime);
                pickups.Tick(Time.deltaTime);
                observedMovement |= first.position.z < initial.z-.2f;
                observedTrail |= first.GetComponent<TrackOxygenBubbleVisual>().FlightTrail.emitting;
                if (pickups.CollectedCount==1)
                {
                    OxygenBubblesChecks.Check(observedMovement && observedTrail && notifications==1 && Time.frameCount>firstFrame+5,
                        "live frames show approach, trail and exactly one collection");
                    File.AppendAllText("Temp/OxygenBubbleIntegration/result.txt",
                        "PASS: Play Mode advanced "+(Time.frameCount-firstFrame)+" frames; approach, trail and one collection event observed.\n");
                    Debug.Log("[OxygenBubbles PLAY] PASS: approach, trail, one event; frames="+(Time.frameCount-firstFrame));
                    running=false; EditorApplication.isPlaying=false;
                }
            }
            if (EditorApplication.timeSinceStartup-began>25) throw new TimeoutException("Oxygen play check did not advance or collect within 25 seconds.");
        }
        catch (Exception e)
        {
            File.AppendAllText("Temp/OxygenBubbleIntegration/result.txt","FAIL: "+e+"\n");
            Debug.LogException(e);
            running=false; EditorApplication.isPlaying=false;
        }
    }
}
