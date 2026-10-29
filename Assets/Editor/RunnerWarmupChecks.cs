using System;
using System.IO;
using System.Reflection;
using Newtonsoft.Json.Linq;
using StadionTracking;
using UnityEditor;
using UnityEngine;

internal static class RunnerWarmupChecks
{
    private static void Check(bool result, string message) { if (!result) throw new InvalidOperationException("[Warmup CHECK] " + message); }
    private static void Command(RunnerAvatarBehaviourChecks.Fixture f, string type, JObject data) => RunnerAvatarBehaviourChecks.Call(f.client, "Apply", type, data);
    private static JObject Plan(params string[] ids)
    {
        var steps = new JArray();
        foreach (var id in ids) steps.Add(new JObject { ["animationId"] = id, ["seconds"] = 5 });
        return new JObject { ["steps"] = steps };
    }
    private static bool Reject(RunnerAvatarBehaviourChecks.Fixture f, string type, JObject data)
    {
        try { Command(f, type, data); return false; }
        catch (TargetInvocationException e) { return e.InnerException is ArgumentException || e.InnerException is InvalidOperationException; }
    }
    private static void FinishApproach(RunnerAvatarBehaviourChecks.Fixture f, RunnerWarmup warmup, Action sync, Animator animator)
    {
        var avatar = f.companion.Mode == TreadmillRunMode.SideBySide ? f.companion.Avatar : f.session.GhostPlayback.Avatar;
        var previous = avatar.transform.position;
        bool walked = false;
        for (int frame = 0; frame < 1000 && warmup.Approaching; frame++)
        {
            warmup.Tick(.05f); sync(); animator.Update(.05f);
            Check(Vector3.Distance(previous, avatar.transform.position) <= RunnerWarmup.TravelSpeed * .05f + .001f, "approach has no teleport, including the initial pose");
            previous = avatar.transform.position;
            walked |= animator.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash("Behaviour_Walking");
            Check(warmup.StepElapsed == 0 && warmup.TotalElapsed == 0, "exercise timer waits for arrival");
        }
        Check(warmup.Active && !warmup.Approaching && warmup.Status == "Running" && walked, "original Walking clip brings avatar to warmup position");
        sync();
    }

    private static void FinishReturn(RunnerAvatarBehaviourChecks.Fixture f, RunnerWarmup warmup, Action sync, Animator animator)
    {
        Vector3 previous = (f.companion.Mode == TreadmillRunMode.SideBySide ? f.companion.Avatar : f.session.GhostPlayback.Avatar).transform.position;
        bool walked = false;
        for (int frame = 0; frame < 1000 && warmup.Active; frame++)
        {
            warmup.Tick(.05f); sync(); animator.Update(.05f);
            var avatar = f.companion.Mode == TreadmillRunMode.SideBySide ? f.companion.Avatar : f.session.GhostPlayback.Avatar;
            Check(Vector3.Distance(previous, avatar.transform.position) <= RunnerWarmup.TravelSpeed * .05f + .001f, "return stays continuous, including normal-placement handoff");
            previous = avatar.transform.position;
            walked |= animator.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash("Behaviour_Walking");
        }
        Check(!warmup.Active && walked, "return completes with actual original Mixamo walking in both avatar modes");
        f.world.GetLanePoseAtDistance(0, f.world.AvatarLane, out var destination, out var heading);
        Check(new Vector2(previous.x - destination.x, previous.z - destination.z).magnitude < .001f, "return ends on selected lane");
        var returnedAvatar = f.companion.Mode == TreadmillRunMode.SideBySide ? f.companion.Avatar : f.session.GhostPlayback.Avatar;
        Check(Quaternion.Angle(returnedAvatar.transform.rotation, heading) < .1f, "return finishes aligned with running direction");
        Check(f.world.SessionPaused && f.world.TravelledMetres == 0 && f.session.ElapsedSeconds == 0 && f.session.GhostPlayback.TravelledMetres == 0, "return does not count as running distance or elapsed time");
    }

    private static void VerifyOverlappingStart()
    {
        using (var f = new RunnerAvatarBehaviourChecks.Fixture())
        {
            f.world.SetAvatarLane(1); f.companion.SetMode(TreadmillRunMode.TempoPartner);
            var warmup = RunnerWarmup.GetOrCreate(f.world);
            warmup.Begin(new[] { new RunnerWarmupStep("Mixamo_ArmStretching", 5) });
            Action sync = () => f.session.GhostPlayback.Synchronize(f.session.Checkpoints, null, f.world, false, f.companion, .05f);
            sync();
            var animator = f.session.GhostPlayback.Avatar.GetComponentInChildren<Animator>(true);
            // Initially hidden by the normal user-clearance policy; it must still walk out to become visible.
            for (int frame = 0; frame < 1000 && warmup.Approaching; frame++)
            { warmup.Tick(.05f); sync(); if (animator.isActiveAndEnabled) animator.Update(.05f); }
            Check(!warmup.Approaching && warmup.Active && f.session.GhostPlayback.Avatar.activeInHierarchy, "hidden lane-1 avatar can approach without getting stuck in the user clearance zone");
            warmup.Stop();
        }
    }

    [MenuItem("Tools/Jogger/Checks/Check Warm-up mode")]
    public static void Verify()
    {
        foreach (var mode in new[] { TreadmillRunMode.SideBySide, TreadmillRunMode.TempoPartner })
        using (var f = new RunnerAvatarBehaviourChecks.Fixture())
        {
            f.companion.SetMode(mode); f.world.SetAvatarLane(7);
            var stats = new GameObject("Test running stats", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(stats, f.scene);
            var statsGroup = stats.GetComponent<CanvasGroup>();
            RunnerAvatarBehaviourChecks.Set(f.client, "statsCanvas", stats.GetComponent<Canvas>());
            var warmup = RunnerWarmup.GetOrCreate(f.world);
            var ids = new[] { "Mixamo_WarmingUp", "Mixamo_ArmStretching", "Mixamo_BatterOnDeck", "Mixamo_AirSquat", "Mixamo_StartJumpingJacks" };
            Check(Reject(f, "exercise.startWarmup", Plan("Mixamo_Plank")) && !warmup.Active && f.client.State == RunnerSessionState.Ready, "Plank rejected without mutation");
            foreach (var seconds in new JToken[] { 0, 301, 5.5, "30", JValue.CreateNull() })
            {
                var invalid = Plan(ids[0]); invalid["steps"][0]["seconds"] = seconds;
                Check(Reject(f, "exercise.startWarmup", invalid) && !warmup.Active, "invalid duration rejected atomically: " + seconds);
            }
            var bad = Plan(ids[0]); ((JObject)bad["steps"][0])["unknown"] = true;
            Check(Reject(f, "exercise.startWarmup", bad), "unknown nested fields rejected");
            warmup.Begin(new[] { new RunnerWarmupStep(ids[0], 5) });
            warmup.Tick(2); Check(warmup.StepElapsed == 0, "clock waits for actual avatar display");
            warmup.Stop();
            Command(f, "exercise.startWarmup", Plan(ids));
            Check(statsGroup.alpha == 0, "warmup hides running stats at start");
            Command(f, "scene.setRunningMateHudVisible", new JObject { ["visible"] = true });
            Check(statsGroup.alpha == 0, "running HUD stays hidden until run starts");
            Check(warmup.Approaching && warmup.StepElapsed == 0, "start first approaches, no premature exercise clock");
            Check(warmup.Active && f.world.SessionPaused && f.client.State == RunnerSessionState.Ready && warmup.StepCount == 5, "valid pre-run sequence starts paused in " + mode);
            Check(Reject(f, "exercise.startWarmup", Plan(ids[0])) && warmup.StepCount == 5, "duplicate start rejected without restart");
            Check(Reject(f, "avatar.playAnimation", new JObject { ["animationId"] = "Mixamo_Plank" }), "manual animation cannot take over warmup");
            Check(Reject(f, "runningMate.setLane", new JObject { ["lane"] = 1 }) && f.world.AvatarLane == 7, "placement changes do not disrupt active warmup");
            Action sync = () => {
                if (mode == TreadmillRunMode.SideBySide) f.companion.Synchronize();
                else f.session.GhostPlayback.Synchronize(f.session.Checkpoints, null, f.world, false, f.companion, .1f);
            };
            f.world.ConfigureUserSpeed(RunnerSpeedSource.FixedTest, 12);
            f.world.AdvanceSimulation(4);
            f.session.BindCompanion(f.companion);
            RunnerAvatarBehaviourChecks.Call(f.session, "Tick", 4f);
            Check(f.world.TravelledMetres == 0 && f.session.ElapsedSeconds == 0 && f.session.GhostPlayback.TravelledMetres == 0, "actual world and session ticks stay paused at nonzero configured speed");
            sync();
            var avatar = mode == TreadmillRunMode.SideBySide ? f.companion.Avatar : f.session.GhostPlayback.Avatar;
            var animator = avatar.GetComponentInChildren<Animator>(); animator.Update(.3f);
            f.world.GetLanePoseAtDistance(0, f.world.AvatarLane, out var startingGround, out _);
            Check(new Vector2(avatar.transform.position.x - startingGround.x, avatar.transform.position.z - startingGround.z).magnitude < .001f, "warmup starts from actual lane position");
            FinishApproach(f, warmup, sync, animator);
            var initial = avatar.transform.position;
            var camera = f.world.HeadCamera;
            var delta = initial - camera.transform.position; delta.y = 0;
            Check(Mathf.Abs(delta.magnitude - RunnerWarmup.FrontDistance) < .001f && Vector3.Dot(delta.normalized, camera.transform.forward) > .99f, "avatar is 2.5m ahead of head in " + mode);
            Check(Vector3.Dot(avatar.transform.forward, -delta.normalized) > .99f, "avatar faces user");
            camera.transform.Rotate(0, 65, 0); camera.transform.position += Vector3.right * .2f;
            sync(); Check(Vector3.Distance(initial, avatar.transform.position) < .001f, "avatar stays planted when head moves");
            var hud = RunnerWarmupHud.GetOrCreate(f.world);
            RunnerAvatarBehaviourChecks.Call(hud, "LateUpdate");
            var viewport = camera.WorldToViewportPoint(hud.Banner.transform.position);
            Check(hud.Banner.gameObject.activeSelf && hud.Banner.renderMode == RenderMode.WorldSpace && Mathf.Abs(viewport.x - .5f) < .001f && Mathf.Abs(viewport.y - .78f) < .001f, "XR banner at upper viewport centre");
            Check(hud.Banner.GetComponentsInChildren<TMPro.TMP_Text>()[0].text == "Warm UP" && hud.Banner.GetComponent<UnityEngine.UI.GraphicRaycaster>() == null, "Warm UP text does not intercept input");
            for (int index = 0; index < ids.Length; index++)
            {
                Check(warmup.AnimationId == ids[index], "sequence ordering " + index);
                sync(); animator.Update(.3f);
                var entry = f.behaviour.Library.Find(ids[index]);
                Check(animator.GetCurrentAnimatorStateInfo(0).shortNameHash == Animator.StringToHash(entry.stateName), "actual Mixamo clip plays: " + ids[index]);
                warmup.Tick(4); Check(warmup.StepIndex == index && Mathf.Abs(warmup.StepElapsed - 4) < .001f, "specified duration retained");
                sync(); warmup.Tick(1);
            }
            Check(warmup.Finishing && warmup.TotalElapsed == 25 && !f.behaviour.Active, "last step blends to idle");
            sync(); animator.Update(.5f); warmup.Tick(.5f);
            Check(warmup.Returning && warmup.Active, "exercise completion starts physical return instead of teleporting");
            FinishReturn(f, warmup, sync, animator);
            Check(!warmup.Active && warmup.Status == "Completed" && f.world.SessionPaused && f.session.ElapsedSeconds == 0 && f.world.TravelledMetres == 0 && f.session.GhostPlayback.TravelledMetres == 0, "completion never starts or advances the run");
            RunnerAvatarBehaviourChecks.Call(hud, "LateUpdate"); Check(!hud.Banner.gameObject.activeSelf, "banner hides on completion");
            sync(); Check(f.world.AvatarLane == 7, "chosen lane restored after warmup");
            Check(statsGroup.alpha == 0, "stats remain hidden after warmup until run starts");
            Command(f, "exercise.startWarmup", Plan(ids[0], ids[1])); sync();
            FinishApproach(f, warmup, sync, animator);
            Command(f, "exercise.nextWarmup", new JObject()); Check(warmup.StepIndex == 1 && warmup.StepElapsed == 0, "skip advances one exercise");
            Command(f, "exercise.stopWarmup", new JObject());
            Check(warmup.Active && warmup.Finishing && !f.behaviour.Active && f.world.SessionPaused, "explicit exit begins smooth return");
            FinishReturn(f, warmup, sync, animator);
            Command(f, "exercise.startWarmup", Plan(ids[0])); sync();
            FinishApproach(f, warmup, sync, animator);
            Check(Reject(f, "runningMate.command", new JObject { ["action"] = "start" }) && warmup.Active, "run waits for the return rather than teleporting");
            Command(f, "exercise.stopWarmup", new JObject());
            FinishReturn(f, warmup, sync, animator);
            Command(f, "runningMate.command", new JObject { ["action"] = "start" });
            Check(statsGroup.alpha == 1 && statsGroup.interactable && statsGroup.blocksRaycasts, "run start restores running stats");
            Check(!warmup.Active && !f.behaviour.Active && !f.world.SessionPaused && f.world.AvatarLane == 7, "start returns to selected lane and running mode");
            Check(Reject(f, "exercise.startWarmup", Plan(ids[0])) && f.client.State == RunnerSessionState.Running, "warmup cannot interrupt running session");
            Command(f, "runningMate.command", new JObject { ["action"] = "reset" });
            Command(f, "exercise.startWarmup", Plan(ids[0])); sync();
            RunnerAvatarBehaviourChecks.Call(f.client, "PauseRunningSession");
            Check(!warmup.Active && warmup.Status == "Interrupted" && f.world.SessionPaused, "disconnect ends warmup without auto-resuming");
            Command(f, "exercise.startWarmup", Plan(ids[0])); sync();
            f.world.BeginMarkerPlacement(); warmup.Tick(.1f);
            Check(!warmup.Active && warmup.Status == "Interrupted", "tracking or marker replacement interrupts warmup");
        }
        using (var f = new RunnerAvatarBehaviourChecks.Fixture())
        {
            f.world.SetAvatarLane(3);
            var warmup = RunnerWarmup.GetOrCreate(f.world);
            warmup.Begin(new[] { new RunnerWarmupStep("Mixamo_ArmStretching", 5) });
            f.companion.Synchronize();
            var animator = f.companion.Animator;
            FinishApproach(f, warmup, () => f.companion.Synchronize(), animator);
            warmup.Finish();
            f.companion.Synchronize(); warmup.Tick(.5f); f.companion.Synchronize();
            var planted = f.companion.Avatar.transform.position;
            f.world.HeadCamera.transform.position = planted + Vector3.up * 1.7f + Vector3.right * .2f;
            for (int frame = 0; frame < 60 && !warmup.WaitingForSpace; frame++)
            { f.companion.Synchronize(); warmup.Tick(.05f); animator.Update(.05f); }
            Check(warmup.WaitingForSpace && Vector3.Distance(planted, f.companion.Avatar.transform.position) < .001f, "return waits rather than walking into a nearby user");
            f.world.HeadCamera.transform.position = new Vector3(-10, 1.7f, -10);
            FinishReturn(f, warmup, () => f.companion.Synchronize(), animator);
        }
        VerifyOverlappingStart();
        RunnerWarmupRenderChecks.Capture();
        Directory.CreateDirectory("Library/WarmupVerification");
        File.WriteAllText("Library/WarmupVerification/result.txt", "PASS: all five original Mixamo warmups, Plank exclusion, payload atomicity, per-step duration, display-gated clock, order, smooth idle exit, continuous walking approach and return, original target pose and orientation, bounded frame movement, delayed run start, front placement and facing, stationary head-independent pose, both avatar modes, XR banner position/visibility, frozen run metrics, preserved lane, skip/stop, run start, disconnect, marker loss.\n");
        Debug.Log("[Warmup CHECK] PASS: sequence, both modes, real Animator states, duration, placement, HUD, metrics, commands and interruption.");
    }
}
