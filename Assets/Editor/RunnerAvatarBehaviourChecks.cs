using System;
using System.Reflection;
using Newtonsoft.Json.Linq;
using StadionTracking;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

internal static class RunnerAvatarBehaviourChecks
{
    internal static object Get(object target,string name)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).GetValue(target);
    internal static void Set(object target,string name,object value)=>target.GetType().GetField(name,BindingFlags.Instance|BindingFlags.NonPublic).SetValue(target,value);
    internal static object Call(object target,string method,params object[] args)=>target.GetType().GetMethod(method,BindingFlags.Instance|BindingFlags.NonPublic).Invoke(target,args);
    internal static void Check(bool value,string message){if(!value)throw new InvalidOperationException("[AvatarBehaviour CHECK] "+message);}

    internal sealed class Fixture : IDisposable
    {
        public readonly Scene scene;
        public readonly GameObject host;
        public readonly StraightTreadmillWorld world;
        public readonly TreadmillCompanion companion;
        public readonly RunnerAvatarBehaviour behaviour;
        public readonly RunnerServerClient client;
        public readonly RunnerSession session;
        public Fixture()
        {
            scene=EditorSceneManager.NewPreviewScene();
            host=new GameObject("Behaviour integration world");SceneManager.MoveGameObjectToScene(host,scene);
            var head=new GameObject("Behaviour test head").AddComponent<Camera>();SceneManager.MoveGameObjectToScene(head.gameObject,scene);
            head.transform.position=new Vector3(0,1.7f,0);
            world=host.AddComponent<StraightTreadmillWorld>();
            Set(world,"headCamera",head);Set(world,"requireMarkerPlacement",false);Set(world,"requireHeightCalibration",false);
            world.ResetDistance();world.SessionPaused=true;
            companion=host.AddComponent<TreadmillCompanion>();
            Set(companion,"world",world);
            Set(companion,"avatarPrefab",AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/JoggerRealistic/Jogger_Realistic.prefab"));
            companion.SetMode(TreadmillRunMode.SideBySide);
            behaviour=RunnerAvatarBehaviour.GetOrCreate(world);
            session=RunnerSession.GetOrCreate(world);session.BindWorld(world);
            client=host.AddComponent<RunnerServerClient>();
            Set(client,"world",world);Set(client,"companion",companion);Set(client,"session",session);Set(client,"avatarBehaviour",behaviour);
            Set(client,"state",RunnerSessionState.Ready);
        }
        public void Dispose()
        {
            if(behaviour!=null)Call(behaviour,"OnDestroy");
            if(session!=null)
            {
                Call(session.GhostPlayback,"ReleaseAvatar");
                Call(session,"OnDestroy");
                UnityEngine.Object.DestroyImmediate(session.gameObject);
            }
            EditorSceneManager.ClosePreviewScene(scene);
        }
    }

    [MenuItem("Tools/Jogger/Checks/Check RunningServer behaviour controls")]
    public static void Verify()
    {
        using(var fixture=new Fixture())
        {
            var b=fixture.behaviour;
            Check(b.Library!=null && b.Library.animations.Length==10,"10 animation entries available");
            foreach(var entry in b.Library.animations)
            {
                b.Play(entry.id,true,1);
                fixture.companion.Synchronize();
                var animator=fixture.companion.Animator;
                animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
                animator.Update(.3f);
                Check(animator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash(entry.stateName+(entry.IsSequence?"_Start":"")),"companion plays "+entry.id);
                Check(animator.speed==1 && fixture.world.SessionPaused,"manual clip animates while run is paused");
            }
            foreach(var id in new[]{"Mixamo_BatterOnDeck","Mixamo_StartJumpingJacks","Mixamo_WarmingUp","Mixamo_ArmStretching","Mixamo_AirSquat","Jogging"})
            {
                var entry=b.Library.Find(id);
                var animator=fixture.companion.Animator;
                animator.Play("Base Layer."+entry.stateName,0,.15f);animator.Update(.001f);
                Vector3 hand=animator.GetBoneTransform(HumanBodyBones.LeftHand).position;
                Vector3 foot=animator.GetBoneTransform(HumanBodyBones.LeftFoot).position;
                Vector3 origin=animator.transform.position;
                animator.Play("Base Layer."+entry.stateName,0,.65f);animator.Update(.001f);
                Check(Vector3.Distance(hand,animator.GetBoneTransform(HumanBodyBones.LeftHand).position)+Vector3.Distance(foot,animator.GetBoneTransform(HumanBodyBones.LeftFoot).position)>.05f,"actual retargeted bones animate: "+id);
                Check(Vector3.Distance(origin,animator.transform.position)<.001f,"avatar stays placed: "+id);
            }
            Check(b.Library.Find("Warmup_Legs")==null && b.Library.Find("Warmup_StepJacks")==null && b.Library.Find("Warmup_ArmsAndTorso")==null,"retired handmade animations unavailable");
            b.Play("Mixamo_StartJumpingJacks",false,1);fixture.companion.Synchronize();
            var jacksAnimator=fixture.companion.Animator;
            var jacks=b.Library.Find("Mixamo_StartJumpingJacks");
            Check(b.Loop && jacks.clip.isLooping && jacks.Duration>1.2f,"Jumping Jacks always repeats a complete opening/closing cycle");
            jacksAnimator.Play("Base Layer."+jacks.stateName,0,.1f);jacksAnimator.Update(.001f);
            Vector3 closed=jacksAnimator.GetBoneTransform(HumanBodyBones.LeftHand).position;
            jacksAnimator.Play("Base Layer."+jacks.stateName,0,.5f);jacksAnimator.Update(.001f);
            Check(Vector3.Distance(closed,jacksAnimator.GetBoneTransform(HumanBodyBones.LeftHand).position)>.3f,"Jumping Jacks opens its arms");
            jacksAnimator.Play("Base Layer."+jacks.stateName,0,1.1f);jacksAnimator.Update(.001f);
            Check(Vector3.Distance(closed,jacksAnimator.GetBoneTransform(HumanBodyBones.LeftHand).position)<.005f,"Jumping Jacks returns to the same pose in its next cycle");
            jacksAnimator.Update(6);b.Tick(6);
            Check(b.Active && jacksAnimator.GetCurrentAnimatorStateInfo(0).normalizedTime>3,"Jumping Jacks continues animating beyond several cycles");
            var warmup=b.Library.Find("Mixamo_WarmingUp");
            Check(warmup!=null && warmup.clip.humanMotion && Mathf.Abs(warmup.Duration-62f/30f)<.001f,"Warming Up ends before punches at frame 62");
            foreach(var binding in UnityEditor.AnimationUtility.GetCurveBindings(warmup.clip))
            {
                var curve=UnityEditor.AnimationUtility.GetEditorCurve(warmup.clip,binding);
                foreach(var key in curve.keys)Check(key.time<=62f/30f+.0001f,"Warming Up contains no keys from punch section");
            }
            foreach(var id in new[]{"Mixamo_BatterOnDeck","Mixamo_StartJumpingJacks","Mixamo_WarmingUp","Mixamo_ArmStretching","Mixamo_AirSquat"})
            {
                var entry=b.Library.Find(id);
                foreach(var binding in UnityEditor.AnimationUtility.GetCurveBindings(entry.clip))
                {
                    var curve=UnityEditor.AnimationUtility.GetEditorCurve(entry.clip,binding);
                    var keys=curve.keys;
                    Check(Mathf.Abs(keys[0].value-keys[keys.Length-1].value)<.00001f,"matching loop poses: "+id);
                    Check(Mathf.Abs(keys[0].outTangent-keys[keys.Length-1].inTangent)<.00001f,"continuous loop velocity: "+id);
                }
                var animator=fixture.companion.Animator;
                animator.Play("Base Layer."+entry.stateName,0,.999f);animator.Update(0);
                var bones=new[]{HumanBodyBones.LeftHand,HumanBodyBones.RightHand,HumanBodyBones.LeftFoot,HumanBodyBones.RightFoot,HumanBodyBones.Head};
                var positions=Array.ConvertAll(bones,bone=>animator.GetBoneTransform(bone).position);
                animator.Play("Base Layer."+entry.stateName,0,1.001f);animator.Update(0);
                for(int i=0;i<bones.Length;i++)Check(Vector3.Distance(positions[i],animator.GetBoneTransform(bones[i]).position)<.05f,"no retargeted bone snap at loop: "+id);
            }
            var squat=b.Library.Find("Mixamo_AirSquat");
            var squatAnimator=fixture.companion.Animator;
            float minHip=float.MaxValue,maxHip=float.MinValue;
            for(int sample=0;sample<16;sample++)
            {
                squatAnimator.Play("Base Layer."+squat.stateName,0,sample/16f);squatAnimator.Update(0);
                float height=squatAnimator.GetBoneTransform(HumanBodyBones.Hips).position.y;
                minHip=Mathf.Min(minHip,height);maxHip=Mathf.Max(maxHip,height);
            }
            Check(maxHip-minHip>.15f,"Air Squat lowers and raises the actual Jogger hips");
            var plank=b.Library.Find("Mixamo_Plank");
            var plankAnimator=fixture.companion.Animator;
            float[] plankHeights=new float[3];
            for(int i=0;i<3;i++)
            {
                plankAnimator.Play("Base Layer."+plank.stateName,0,new[]{.001f,.5f,.999f}[i]);plankAnimator.Update(0);
                plankHeights[i]=plankAnimator.GetBoneTransform(HumanBodyBones.Hips).position.y;
            }
            Check(plankHeights[0]-plankHeights[1]>.3f && plankHeights[2]-plankHeights[1]>.3f,"Plank goes from standing to low hold and returns to standing");
            b.Play("Mixamo_Plank",false);fixture.companion.Synchronize();
            b.Tick(plank.Duration-.05f);fixture.companion.Synchronize();Check(b.Active,"Plank finishes exit before returning to automatic");
            b.Tick(.1f);Check(!b.Active,"Plank sequence ends automatically after exit");
            b.Play("Mixamo_Plank",true);fixture.companion.Synchronize();b.Tick(plank.Duration*2.5f);
            Check(b.Active,"repeat loops complete Plank sequence");
            foreach(float seconds in new[]{1f,30f,300f})
            {
                b.Play("Mixamo_Plank",false,1,seconds);fixture.companion.Synchronize();
                Check(Mathf.Abs(b.DurationSeconds-(plank.startClip.length+seconds+plank.endClip.length-.36f))<.001f,"custom hold updates total duration");
                b.Tick(plank.startClip.length-.17f);fixture.companion.Synchronize();plankAnimator.Update(.2f);
                Check(plankAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash(plank.stateName+"_Hold"),"Plank enters actual holding state");
                b.Tick(seconds-.3f);fixture.companion.Synchronize();
                Check(b.Active,"custom hold does not end early");
                b.Tick(.3f);fixture.companion.Synchronize();plankAnimator.Update(.2f);
                Check(plankAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash(plank.stateName+"_End"),"custom hold plays actual exit");
                b.Tick(plank.endClip.length+.1f);Check(!b.Active,"custom sequence completes");
            }
            foreach(float bad in new[]{0f,301f,float.NaN})
            {
                bool rejected=false;try{b.Play("Mixamo_Plank",false,1,bad);}catch(ArgumentException){rejected=true;}
                Check(rejected,"invalid hold rejected");
            }
            string selected=b.AnimationId;
            foreach(Action invalid in new Action[]{()=>b.Play("Unknown"),()=>b.Play("Mixamo_BatterOnDeck",true,float.NaN),()=>b.Play("Mixamo_BatterOnDeck",true,0)})
            {
                bool rejected=false;try{invalid();}catch(ArgumentException){rejected=true;}
                Check(rejected && b.AnimationId==selected,"invalid request preserves selected animation");
            }
            b.Play("Mixamo_BatterOnDeck",false,2);fixture.companion.Synchronize();
            b.Tick(b.DurationSeconds/2-.05f);Check(b.Active,"one-shot remains active before its end");
            b.Tick(.1f);Check(!b.Active,"one-shot returns to automatic at its end");
            b.Play("Mixamo_BatterOnDeck",true,1);fixture.companion.Synchronize();b.Tick(25);Check(b.Active,"loop continues beyond clip duration");
            b.Stop();Check(!b.Active,"explicit stop returns to automatic");

            fixture.companion.SetMode(TreadmillRunMode.TempoPartner);
            fixture.world.SetAvatarLane(3);
            var ghost=fixture.session.GhostPlayback;
            b.Play("Mixamo_StartJumpingJacks");
            ghost.Synchronize(new LapCheckpointTracker(400,360),null,fixture.world,false,fixture.companion,0);
            var ghostAnimator=ghost.Avatar.GetComponentInChildren<Animator>();
            ghostAnimator.cullingMode=AnimatorCullingMode.AlwaysAnimate;ghostAnimator.Update(.3f);
            Check(ghostAnimator.speed==1 && ghostAnimator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash("Behaviour_Mixamo_StartJumpingJacks"),"stationary tempo partner plays manual animation");

            fixture.companion.SetMode(TreadmillRunMode.SideBySide);
            Set(fixture.client,"state",RunnerSessionState.Running);fixture.world.SessionPaused=false;
            b.Stop();fixture.companion.Synchronize();fixture.companion.Animator.Update(.3f);
            foreach(var payload in new[]{new JObject{["animationId"]="Bad"},new JObject{["animationId"]="Mixamo_BatterOnDeck",["speed"]=3},new JObject{["animationId"]="Mixamo_BatterOnDeck",["loop"]="true"},new JObject{["animationId"]="Mixamo_BatterOnDeck",["extra"]=1}})
            {
                bool rejected=false;try{Call(fixture.client,"Apply","avatar.playAnimation",payload);}catch(TargetInvocationException e){rejected=e.InnerException is ArgumentException;}
                Check(rejected && fixture.client.State==RunnerSessionState.Running && !fixture.world.SessionPaused,"invalid command does not pause run");
            }
            Call(fixture.client,"Apply","avatar.playAnimation",new JObject{["animationId"]="Mixamo_BatterOnDeck",["loop"]=true,["speed"]=1});
            Check(b.AnimationId=="Mixamo_BatterOnDeck" && fixture.world.SessionPaused && fixture.client.State==RunnerSessionState.Paused,"valid command starts clip and pauses running clock");
            Call(fixture.client,"Apply","avatar.stopAnimation",new JObject());
            Check(!b.Active && fixture.world.SessionPaused,"return does not silently resume the run");
            Check(fixture.companion.Animator.GetCurrentAnimatorStateInfo(0).shortNameHash==Animator.StringToHash("idle") && !fixture.companion.Animator.GetBool("isRunning"),"return clears stale running parameter and leaves a paused avatar in idle");
            Call(fixture.client,"Apply","avatar.playAnimation",new JObject{["animationId"]="Mixamo_BatterOnDeck"});
            Call(fixture.client,"Apply","runningMate.command",new JObject{["action"]="resume"});
            Check(!b.Active && !fixture.world.SessionPaused,"resuming clears manual animation");
            b.Play("Mixamo_BatterOnDeck");Call(fixture.client,"PauseRunningSession");Check(!b.Active,"connection loss stops manual animation");
            var registry=(JObject)typeof(RunnerServerClient).GetMethod("CreateRegistry",BindingFlags.Static|BindingFlags.NonPublic).Invoke(null,null);
            Check(((JArray)registry["data"]["animations"]).Count==10 && registry["data"]["commands"].ToString().Contains("avatar.playAnimation"),"registry exposes catalog and commands");
        }
        RunnerPlankGroundingBaker.Verify();
        Debug.Log("[AvatarBehaviour CHECK] PASS: 10 clips, companion, tempo partner, once/loop, speed, invalid command atomicity, pause, return, resume, disconnect and registry.");
    }
}
