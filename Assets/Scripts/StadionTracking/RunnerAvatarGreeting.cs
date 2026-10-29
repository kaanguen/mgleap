using System.Collections.Generic;
using UnityEngine;

namespace StadionTracking
{
    // One greeting per marker-placement session, shared by both avatar modes.
    public sealed class RunnerAvatarGreeting : MonoBehaviour
    {
        public const string WaveState="AutomaticGreetingWave";
        internal const float MinimumWave=5.25f, MaximumWave=5.25f, GazeDwell=.4f, FadeSeconds=.75f;
        private StraightTreadmillWorld world;
        private int placementRevision=-1;
        private bool consumed, runStarted, fading;
        private float elapsed, gazeSeconds, fadeElapsed;
        private readonly Dictionary<Animator,Display> displays=new Dictionary<Animator,Display>();
        private sealed class Display
        {
            internal bool wave, idle;
            internal Quaternion facing;
            internal RunnerGreetingLook look;
        }
        public bool Active {get;private set;}
        public bool Fading => Active && fading;
        public bool Completed => consumed;
        public static RunnerAvatarGreeting GetOrCreate(StraightTreadmillWorld world)
        {
            var greeting=world.GetComponent<RunnerAvatarGreeting>();
            if(greeting==null)greeting=world.gameObject.AddComponent<RunnerAvatarGreeting>();
            greeting.world=world;return greeting;
        }
        private void Update()=>Tick(Time.deltaTime);
        private bool Observe()
        {
            if(world==null)return false;
            if(placementRevision!=world.MarkerPlacementRevision)
            {
                Cancel(false);consumed=false;runStarted=false;placementRevision=world.MarkerPlacementRevision;
            }
            if(!world.SessionPaused){runStarted=true;Cancel(true);return false;}
            var manual=RunnerAvatarBehaviour.ForScene(world.gameObject.scene);
            if(manual!=null && manual.Active){if(world.MarkerFloorConfirmed)Cancel(true);return false;}
            bool ready=world.RequiresMarkerPlacement && world.MarkerFloorConfirmed && world.PlacementReady && world.HeightConfirmed && world.HeadPoseReady;
            if(!ready && Active)Cancel(true);
            return ready && !runStarted;
        }
        internal void NotifyRunStarted()
        {
            Observe();runStarted=true;Cancel(true);
        }
        public void Tick(float seconds)
        {
            if(!Observe() || !Active || seconds<=0 || float.IsNaN(seconds) || float.IsInfinity(seconds))return;
            elapsed+=seconds;
            if(fading)
            {
                fadeElapsed+=seconds;
                if(fadeElapsed>=FadeSeconds){Active=false;consumed=true;foreach(var display in displays.Values)if(display.look!=null)display.look.SetTarget(null,0);}
                return;
            }
            bool looking=false;var camera=world.HeadCamera;
            foreach(var pair in displays)
            {
                if(pair.Key==null || !pair.Key.isActiveAndEnabled)continue;
                var head=pair.Key.GetBoneTransform(HumanBodyBones.Head);
                if(head!=null && IsLookingAt(camera,head.position)){looking=true;break;}
            }
            gazeSeconds=looking?gazeSeconds+seconds:0;
            if(elapsed>=MaximumWave || (elapsed>=MinimumWave && gazeSeconds>=GazeDwell)){fading=true;fadeElapsed=0;}
        }
        internal static bool IsLookingAt(Camera camera,Vector3 target)
        {
            if(camera==null)return false;
            Vector3 direction=target-camera.transform.position;
            return direction.sqrMagnitude>.01f && Vector3.Dot(camera.transform.forward,direction.normalized)>=Mathf.Cos(18*Mathf.Deg2Rad);
        }
        public bool Apply(GameObject avatar,Animator animator,Quaternion laneHeading,float seconds)
        {
            if(!Observe() || avatar==null || animator==null || !animator.isActiveAndEnabled)return false;
            if(!Active && consumed)return false;
            if(!animator.HasState(0,Animator.StringToHash("Base Layer."+WaveState)))return false;
            if(!Active)
            {
                Active=true;elapsed=gazeSeconds=fadeElapsed=0;fading=false;
            }
            if(!displays.TryGetValue(animator,out var display))
            {
                display=new Display{facing=laneHeading,look=animator.GetComponent<RunnerGreetingLook>()};
                if(display.look==null)display.look=animator.gameObject.AddComponent<RunnerGreetingLook>();
                displays[animator]=display;
            }
            animator.applyRootMotion=false;animator.speed=1;
            animator.SetBool("isRunning",false);animator.SetFloat("speed",0);animator.SetFloat("behaviourRate",1);
            if(!display.wave)
            {
                animator.CrossFadeInFixedTime("Base Layer."+WaveState,.2f,0,0);display.wave=true;
            }
            if(fading && !display.idle)
            {
                animator.CrossFadeInFixedTime("Base Layer.idle",FadeSeconds,0,0);display.idle=true;
            }
            float weight=fading?1-Mathf.SmoothStep(0,1,fadeElapsed/FadeSeconds):1;
            Vector3 toward=world.HeadCamera.transform.position-avatar.transform.position;toward.y=0;
            if(toward.sqrMagnitude>.01f)display.facing=Quaternion.RotateTowards(display.facing,Quaternion.LookRotation(toward,Vector3.up),150*Mathf.Max(0,seconds));
            avatar.transform.rotation=Quaternion.Slerp(laneHeading,display.facing,weight);
            display.look.SetTarget(world.HeadCamera,weight);
            return true;
        }
        private void Cancel(bool complete)
        {
            foreach(var pair in displays)
            {
                if(pair.Value.look!=null)pair.Value.look.SetTarget(null,0);
                if(Active && pair.Key!=null && pair.Key.isActiveAndEnabled)
                {
                    pair.Key.speed=1;pair.Key.CrossFadeInFixedTime("Base Layer.idle",.15f,0,0);
                }
            }
            displays.Clear();Active=false;fading=false;elapsed=gazeSeconds=fadeElapsed=0;
            if(complete)consumed=true;
        }
        private void OnDisable()=>Cancel(true);
    }
}
