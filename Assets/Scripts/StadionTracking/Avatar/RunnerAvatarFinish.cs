using System.Collections.Generic;
using UnityEngine;

namespace StadionTracking
{
    // Explicit run-end reaction. Keeps each rendered avatar at its actual stop
    // position; neither the world distance nor either lap clock is reset here.
    public sealed class RunnerAvatarFinish : MonoBehaviour
    {
        public const string ThumbState="AutomaticFinishThumbsUp";
        public const float MinimumGestureDistance=1.4f;
        private const float ResumeGestureDistance=1.55f;
        private StraightTreadmillWorld world;
        private int placementRevision;
        private readonly Dictionary<Animator,Display> displays=new Dictionary<Animator,Display>();
        private sealed class Display
        {
            internal GameObject avatar;
            internal Vector3 position;
            internal Quaternion facing;
            internal RunnerGreetingLook look;
            internal int phase; // 0 face user, 1 thumbs up, 2 fade, 3 relaxed facing user
            internal float elapsed,clipDuration;
            internal bool idleStarted,spaceBlocked;
        }
        public bool Active {get;private set;}
        public bool WaitingForSpace {get {foreach(var d in displays.Values)if(d.spaceBlocked)return true;return false;}}
        public bool GesturePlaying {get {foreach(var d in displays.Values)if(d.phase==1)return true;return false;}}
        public bool GestureComplete {get {if(displays.Count==0)return false;foreach(var d in displays.Values)if(d.phase!=3)return false;return true;}}
        public static RunnerAvatarFinish GetOrCreate(StraightTreadmillWorld source)
        {
            var finish=source.GetComponent<RunnerAvatarFinish>();
            if(finish==null)finish=source.gameObject.AddComponent<RunnerAvatarFinish>();
            finish.world=source;return finish;
        }
        public void Begin(TreadmillCompanion companion,LapGhostPlayback ghost)
        {
            Cancel();Active=true;placementRevision=world.MarkerPlacementRevision;
            RunnerAvatarGreeting.GetOrCreate(world).NotifyRunStarted();
            if(companion!=null)Capture(companion.Avatar);
            if(ghost!=null)Capture(ghost.Avatar);
        }
        private void Capture(GameObject avatar)
        {
            if(avatar==null || !avatar.activeInHierarchy)return;
            var animator=avatar.GetComponentInChildren<Animator>();
            if(animator==null || displays.ContainsKey(animator))return;
            var display=new Display{avatar=avatar,position=avatar.transform.position,facing=avatar.transform.rotation,clipDuration=1};
            foreach(var clip in animator.runtimeAnimatorController.animationClips)if(clip.name==ThumbState)display.clipDuration=clip.length;
            display.look=animator.GetComponent<RunnerGreetingLook>();
            if(display.look==null)display.look=animator.gameObject.AddComponent<RunnerGreetingLook>();
            displays.Add(animator,display);
        }
        private void Update()=>Tick(Time.deltaTime);
        public void Tick(float seconds)
        {
            if(!Active || world==null)return;
            var manual=RunnerAvatarBehaviour.ForScene(world.gameObject.scene);
            if(!world.SessionPaused || placementRevision!=world.MarkerPlacementRevision || (manual!=null && manual.Active)){Cancel(manual==null || !manual.Active);return;}
            if(seconds<=0 || float.IsNaN(seconds) || float.IsInfinity(seconds))return;
            foreach(var pair in displays)
            {
                if(pair.Key==null || !pair.Key.isActiveAndEnabled)continue;
                var display=pair.Value;
                if(display.phase==1)
                {
                    display.elapsed+=seconds;
                    if(display.elapsed>=Mathf.Max(.2f,display.clipDuration-.55f))
                    {
                        pair.Key.CrossFadeInFixedTime("Base Layer.idle",.55f,0,0);display.phase=2;display.elapsed=0;
                    }
                }
                else if(display.phase==2){display.elapsed+=seconds;if(display.elapsed>=.55f)display.phase=3;}
            }
        }
        public bool Apply(GameObject avatar,Animator animator,float seconds)
        {
            if(!Active || world==null || avatar==null || animator==null || !animator.isActiveAndEnabled)return false;
            var manual=RunnerAvatarBehaviour.ForScene(world.gameObject.scene);
            if(!world.SessionPaused || placementRevision!=world.MarkerPlacementRevision || (manual!=null && manual.Active)){Cancel(false);return false;}
            if(!animator.HasState(0,Animator.StringToHash("Base Layer."+ThumbState)))return false;
            Capture(avatar);if(!displays.TryGetValue(animator,out var display))return false;
            avatar.transform.position=display.position;
            animator.applyRootMotion=false;animator.speed=1;animator.SetBool("isRunning",false);animator.SetFloat("speed",0);animator.SetFloat("behaviourRate",1);
            if(!display.idleStarted){animator.CrossFadeInFixedTime("Base Layer.idle",.2f,0,0);display.idleStarted=true;}
            var camera=world.HeadPoseReady?world.HeadCamera:null;
            if(camera==null){display.look.SetTarget(null,0);return true;}
            Vector3 toward=camera.transform.position-display.position;toward.y=0;
            float separation=toward.magnitude;
            if(separation<MinimumGestureDistance)display.spaceBlocked=true;
            else if(separation>=ResumeGestureDistance)display.spaceBlocked=false;
            if(display.spaceBlocked && (display.phase==1 || display.phase==2))
            {
                animator.CrossFadeInFixedTime("Base Layer.idle",.2f,0,0);display.phase=0;display.elapsed=0;
            }
            if(toward.sqrMagnitude>.01f)
            {
                var target=Quaternion.LookRotation(toward,Vector3.up);
                display.facing=Quaternion.RotateTowards(display.facing,target,150*Mathf.Max(0,seconds));
                avatar.transform.rotation=display.facing;
                if(display.phase==0 && !display.spaceBlocked && Quaternion.Angle(display.facing,target)<=8)
                {
                    animator.CrossFadeInFixedTime("Base Layer."+ThumbState,.2f,0,0);display.phase=1;display.elapsed=0;
                }
            }
            // Facing is handled by the whole-body turn. Do not add a torso lean
            // toward a nearby headset on top of the authored gesture.
            display.look.SetTarget(camera,1,false);
            return true;
        }
        public void Cancel(bool restoreIdle=true)
        {
            foreach(var pair in displays)
            {
                if(pair.Value.look!=null)pair.Value.look.SetTarget(null,0);
                if(restoreIdle && pair.Key!=null && pair.Key.isActiveAndEnabled){pair.Key.speed=1;pair.Key.CrossFadeInFixedTime("Base Layer.idle",.15f,0,0);}
            }
            displays.Clear();Active=false;
        }
        private void OnDisable()=>Cancel();
    }
}
