using System;
using System.IO;
using Newtonsoft.Json.Linq;
using StadionTracking;
using UnityEditor;
using UnityEngine;

internal static class RunnerAvatarFinishChecks
{
    private static void Check(bool value,string message){if(!value)throw new InvalidOperationException("[Run finish CHECK] "+message);}
    private static void Command(RunnerAvatarBehaviourChecks.Fixture f,string action)=>RunnerAvatarBehaviourChecks.Call(f.client,"Apply","runningMate.command",new JObject{["action"]=action});
    [MenuItem("Tools/Jogger/Checks/Check run finish and reset")]
    public static void Verify()
    {
        using(var f=new RunnerAvatarBehaviourChecks.Fixture())
        {
            f.companion.Synchronize();
            RunnerAvatarBehaviourChecks.Set(f.world,"travelledMetres",125d);
            RunnerAvatarBehaviourChecks.Set(f.session,"<ElapsedSeconds>k__BackingField",40f);
            Command(f,"resume");f.companion.Synchronize();
            var avatar=f.companion.Avatar;var animator=f.companion.Animator;
            Vector3 stoppedAt=avatar.transform.position;
            f.world.HeadCamera.transform.position=stoppedAt-avatar.transform.forward*2+Vector3.up*1.7f;
            Command(f,"stop");var finish=RunnerAvatarFinish.GetOrCreate(f.world);
            Check(finish.Active && f.client.State==RunnerSessionState.Stopped && f.world.SessionPaused,"explicit stop begins finish and pauses clock");
            Check(f.world.TravelledMetres==125 && f.session.ElapsedSeconds==40,"stop preserves user distance and run time");
            finish.Apply(avatar,animator,.01f);animator.Update(.2f);
            Check(!finish.GesturePlaying,"avatar faces user before giving thumbs up");
            for(int i=0;i<18;i++){f.companion.Synchronize();finish.Apply(avatar,animator,.1f);animator.Update(.1f);finish.Tick(.1f);}
            Check(finish.GesturePlaying && animator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash(RunnerAvatarFinish.ThumbState),"actual thumbs-up clip plays after turning");
            Vector3 toward=f.world.HeadCamera.transform.position-avatar.transform.position;toward.y=0;
            Check(Vector3.Dot(avatar.transform.forward,toward.normalized)>.98f,"finished avatar looks toward user");
            Check(Vector3.Distance(stoppedAt,avatar.transform.position)<.00001f,"turn and gesture preserve stop position");
            Command(f,"stop");Check(finish.GesturePlaying,"repeated stop does not restart finish");
            f.world.HeadCamera.transform.position=stoppedAt+avatar.transform.forward*.65f+Vector3.up*1.7f;
            finish.Apply(avatar,animator,.1f);animator.Update(.3f);
            Check(finish.WaitingForSpace && !finish.GesturePlaying,"gesture yields when user enters personal space");
            Check(animator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash("idle"),"nearby user sees relaxed idle instead of extending arm");
            var chest=animator.GetBoneTransform(HumanBodyBones.Chest);var beforeChest=chest.rotation;
            var look=avatar.GetComponentInChildren<RunnerGreetingLook>();RunnerAvatarBehaviourChecks.Call(look,"LateUpdate");
            Check(Quaternion.Angle(beforeChest,chest.rotation)<.05f,"finish look does not lean torso into nearby user");
            f.world.HeadCamera.transform.position=stoppedAt+avatar.transform.forward*1.45f+Vector3.up*1.7f;finish.Apply(avatar,animator,.1f);
            Check(finish.WaitingForSpace,"personal-space threshold has hysteresis");
            f.world.HeadCamera.transform.position=stoppedAt+avatar.transform.forward*2+Vector3.up*1.7f;finish.Apply(avatar,animator,.1f);animator.Update(.3f);
            Check(!finish.WaitingForSpace && finish.GesturePlaying && Vector3.Distance(stoppedAt,avatar.transform.position)<.00001f,"gesture resumes once there is space without moving stop position");
            for(int i=0;i<200;i++){finish.Tick(.1f);f.companion.Synchronize();finish.Apply(avatar,animator,.1f);animator.Update(.1f);}
            Check(finish.Active && finish.GestureComplete,"gesture finishes once and avatar remains facing user");
            Check(Vector3.Distance(stoppedAt,avatar.transform.position)<.00001f && f.world.TravelledMetres==125 && f.session.ElapsedSeconds==40,"finished idle keeps position and completed metrics");
            Command(f,"reset");f.companion.Synchronize();
            Check(!finish.Active && f.world.TravelledMetres==0 && f.session.ElapsedSeconds==0 && f.session.GhostPlayback.TravelledMetres==0,"reset alone restores both distances and clock to zero");
            Command(f,"resume");Command(f,"pause");
            Check(!finish.Active,"pause does not trigger thumbs up");
            RunnerAvatarBehaviourChecks.Call(f.client,"PauseRunningSession");Check(!finish.Active,"disconnect does not trigger finish");
        }
        using(var f=new RunnerAvatarBehaviourChecks.Fixture())
        {
            f.companion.SetMode(TreadmillRunMode.TempoPartner);
            RunnerAvatarBehaviourChecks.Set(f.world,"travelledMetres",100d);
            Command(f,"resume");
            var ghost=f.session.GhostPlayback;var tracker=new LapCheckpointTracker(400,360);
            ghost.Synchronize(tracker,null,f.world,true,f.companion,5f);
            var avatar=ghost.Avatar;var animator=avatar.GetComponentInChildren<Animator>();
            Vector3 position=avatar.transform.position;double distance=ghost.TravelledMetres;
            f.world.HeadCamera.transform.position=position-avatar.transform.forward*2+Vector3.up*1.7f;
            Command(f,"stop");var finish=RunnerAvatarFinish.GetOrCreate(f.world);
            for(int i=0;i<18;i++){finish.Tick(.1f);ghost.Synchronize(tracker,null,f.world,false,f.companion,.1f);finish.Apply(avatar,animator,.1f);animator.Update(.1f);}
            Check(finish.GesturePlaying && animator.speed==1,"stopped tempo partner animates thumbs up");
            Check(Vector3.Distance(position,avatar.transform.position)<.00001f && ghost.TravelledMetres==distance && f.world.TravelledMetres==100,"stop preserves independent partner distance and actual position");
            Command(f,"resume");Check(!finish.Active,"resuming exits finish");
            Command(f,"stop");Command(f,"reset");ghost.Synchronize(f.session.Checkpoints,null,f.world,false,f.companion,.1f);
            Check(ghost.TravelledMetres==0 && f.world.TravelledMetres==0 && !finish.Active,"reset places both partners back at zero and suppresses thumbs up");
        }
        using(var f=new RunnerAvatarBehaviourChecks.Fixture())
        {
            f.companion.SetMode(TreadmillRunMode.TempoPartner);
            f.world.SetAvatarLane(7);RunnerAvatarBehaviourChecks.Call(f.client,"SpawnOnSelectedLane");
            Check(f.world.AvatarLane==7,"startup spawn preserves selected lane");
            foreach(int lane in new[]{1,3,6,8})
            {
                Command(f,"pause");
                RunnerAvatarBehaviourChecks.Call(f.client,"Apply","runningMate.setLane",new JObject{["lane"]=lane});
                Command(f,"start");
                var ghost=f.session.GhostPlayback;ghost.Synchronize(f.session.Checkpoints,null,f.world,true,f.companion,.2f);
                Check(f.world.AvatarLane==lane,"start keeps selected tempo-partner lane "+lane);
                f.world.GetLanePoseAtDistance(ghost.TravelledMetres,lane,out var position,out _);
                var actual=ghost.Avatar.transform.position;
                Check(new Vector2(actual.x-position.x,actual.z-position.z).magnitude<.001f,"tempo-partner rendered on selected lane "+lane);
                Command(f,"pause");Command(f,"resume");Check(f.world.AvatarLane==lane,"resume keeps selected lane "+lane);
            }
        }
        using(var f=new RunnerAvatarBehaviourChecks.Fixture())
        {
            f.companion.Synchronize();var animator=f.companion.Animator;
            Vector3 initial=Vector3.zero;float maximum=0;
            for(int sample=0;sample<=40;sample++)
            {
                animator.Play("Base Layer."+RunnerAvatarFinish.ThumbState,0,Mathf.Min(.99999f,sample/40f));animator.Update(0);
                Vector3 hips=animator.GetBoneTransform(HumanBodyBones.Hips).position;
                if(sample==0)initial=hips;
                maximum=Mathf.Max(maximum,new Vector2(hips.x-initial.x,hips.z-initial.z).magnitude);
            }
            Check(maximum<.15f,"actual retargeted torso stays at the stop position instead of lunging into user");
            Debug.Log("[Finish space CHECK] Actual horizontal hip travel="+maximum.ToString("F4")+" m; proximity gate and selected lanes 1, 3, 6, 8 verified.");
        }
        var thumbs=AssetDatabase.LoadAssetAtPath<AnimationClip>("Assets/JoggerWarmups/RuntimeClips/AutomaticFinishThumbsUp.anim");
        foreach(var binding in AnimationUtility.GetCurveBindings(thumbs))
        {
            if(binding.propertyName!="RootT.x" && binding.propertyName!="RootT.z")continue;
            var values=AnimationUtility.GetEditorCurve(thumbs,binding).keys;
            foreach(var value in values)Check(Mathf.Abs(value.value-values[0].value)<.00001f,"thumbs-up body does not lunge forward or sideways");
        }
        Directory.CreateDirectory("Library/RunFinishVerification");
        File.WriteAllText("Library/RunFinishVerification/result.txt","PASS: explicit stop, turn before actual Thumbs Up, frozen world position, retained distance/time, single gesture, repeated-stop idempotency, standing facing user, reset both to zero, both modes, pause/disconnect suppression, resume, personal-space hysteresis, head-only look, planted lower-body/root curves and selected lanes 1/3/6/8.\n");
        Debug.Log("[Run finish CHECK] PASS: stop retains position and metrics, face user then thumbs up, reset alone returns both to zero, both avatar modes.");
    }
}
