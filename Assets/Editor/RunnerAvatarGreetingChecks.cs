using System;
using System.IO;
using StadionTracking;
using UnityEditor;
using UnityEngine;

internal static class RunnerAvatarGreetingChecks
{
    private static void Check(bool condition,string message){if(!condition)throw new InvalidOperationException("[Greeting CHECK] "+message);}
    private static void Place(RunnerAvatarBehaviourChecks.Fixture fixture)
    {
        RunnerAvatarBehaviourChecks.Set(fixture.world,"requireMarkerPlacement",true);
        fixture.world.BeginMarkerPlacement();
        Check(fixture.world.PlaceFromMarkerPlane(Vector3.zero,Vector3.forward,Vector3.up),"marker placement accepted");
    }
    private static void Step(RunnerAvatarBehaviourChecks.Fixture fixture,RunnerAvatarGreeting greeting,float seconds)
    {
        greeting.Tick(seconds);fixture.companion.Synchronize();
        if(greeting.Active)greeting.Apply(fixture.companion.Avatar,fixture.companion.Animator,fixture.world.RunnerRotation,seconds);
        fixture.companion.Animator.Update(seconds);
    }
    [MenuItem("Tools/Jogger/Checks/Check marker greeting")]
    public static void Verify()
    {
        using(var f=new RunnerAvatarBehaviourChecks.Fixture())
        {
            var greeting=RunnerAvatarGreeting.GetOrCreate(f.world);
            f.companion.Synchronize();
            Check(!greeting.Active,"height-only placement must not greet before marker scan");
            Place(f);f.companion.Synchronize();
            Check(greeting.Active,"marker scan begins greeting without starting run");
            Check(f.world.SessionPaused && f.session.ElapsedSeconds==0,"greeting preserves paused run clock");
            var head=f.companion.Animator.GetBoneTransform(HumanBodyBones.Head);
            f.world.HeadCamera.transform.rotation=Quaternion.LookRotation(head.position-f.world.HeadCamera.transform.position,Vector3.up);
            Step(f,greeting,.3f);
            Check(f.companion.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash(RunnerAvatarGreeting.WaveState),"actual Waving animator state plays");
            Step(f,greeting,.3f);Check(!greeting.Fading,"brief glance cannot end greeting before minimum duration");
            for(int i=0;i<5;i++)Step(f,greeting,.1f);
            Check(greeting.Active && !greeting.Fading,"greeting keeps waving longer even when already noticed");
            for(int i=0;i<19;i++)Step(f,greeting,.1f);
            Check(greeting.Active && !greeting.Fading,"eye contact keeps greeting active beyond three seconds");
            for(int i=0;i<23;i++)Step(f,greeting,.1f);
            Check(greeting.Fading,"sustained headset look gently ends wave after longer minimum");
            Vector3 toward=f.world.HeadCamera.transform.position-f.companion.Avatar.transform.position;toward.y=0;
            Check(Vector3.Dot(f.companion.Avatar.transform.forward,toward.normalized)>.95f,"avatar turns smoothly toward user");
            Check(f.companion.Avatar.GetComponentInChildren<RunnerGreetingLook>()!=null,"bounded head/torso look is attached");
            Step(f,greeting,.4f);Check(greeting.Active,"arm lowering has a real fade interval");
            Step(f,greeting,.4f);Check(!greeting.Active && greeting.Completed,"greeting completes once");
            for(int i=0;i<5;i++)Step(f,greeting,.2f);
            Check(!greeting.Active,"does not wave again when user keeps looking");
            f.world.UpdateMarkerFloorPlane(Vector3.zero,Vector3.up);f.companion.Synchronize();Check(!greeting.Active,"floor refinements do not replay greeting");

            Place(f);f.companion.Synchronize();
            Check(greeting.Active,"explicit new marker placement permits greeting again");
            head=f.companion.Animator.GetBoneTransform(HumanBodyBones.Head);
            f.world.HeadCamera.transform.rotation=Quaternion.LookRotation(f.world.HeadCamera.transform.position-head.position,Vector3.up);
            for(int i=0;i<45;i++)Step(f,greeting,.1f);
            Check(greeting.Active,"unnoticed greeting remains active beyond old timeout");
            for(int i=0;i<20;i++)Step(f,greeting,.1f);
            Check(!greeting.Active && greeting.Completed,"wave has a short timeout even without attention");

            Place(f);f.companion.Synchronize();f.behaviour.Play("Mixamo_Plank",false);greeting.Tick(.01f);f.companion.Synchronize();f.companion.Animator.Update(.3f);
            Check(!greeting.Active && greeting.Completed,"manual exercise cancels greeting");
            Check(f.companion.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash("Behaviour_Mixamo_Plank_Start"),"manual Plank keeps animator priority");
            f.behaviour.Stop();
            Place(f);f.companion.Synchronize();Check(greeting.Active,"greeting ready before run start");
            RunnerAvatarBehaviourChecks.Call(f.client,"SetSessionState",RunnerSessionState.Running);greeting.Tick(.01f);f.companion.Synchronize();
            Check(!greeting.Active && greeting.Completed,"run start ends greeting immediately");
            RunnerAvatarBehaviourChecks.Call(f.client,"SetSessionState",RunnerSessionState.Paused);f.companion.Synchronize();
            Check(!greeting.Active,"a mid-run pause does not greet again");
        }
        using(var f=new RunnerAvatarBehaviourChecks.Fixture())
        {
            Place(f);f.companion.SetMode(TreadmillRunMode.TempoPartner);
            f.session.GhostPlayback.Synchronize(new LapCheckpointTracker(400,360),null,f.world,false,f.companion,.1f);
            var greeting=RunnerAvatarGreeting.GetOrCreate(f.world);
            Check(greeting.Active,"tempo partner greets after marker scan too");
            var animator=f.session.GhostPlayback.Avatar.GetComponentInChildren<Animator>();animator.Update(.3f);
            Check(animator.speed==1 && animator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash(RunnerAvatarGreeting.WaveState),"paused tempo partner really plays Waving");
        }
        Directory.CreateDirectory("Library/GreetingVerification");
        File.WriteAllText("Library/GreetingVerification/result.txt","PASS: marker gate, actual Waving state, paused clock, head-direction dwell, minimum duration, soft fade, timeout, no duplicate greeting, floor refinement, rescan, manual priority, run-start cancellation, paused-run suppression and tempo partner.\n");
        Debug.Log("[Greeting CHECK] PASS: marker scan, Waving, gaze dwell, smooth fade, short timeout, both avatar modes, no replay, manual/run priority.");
    }
}
