using System;
using System.IO;
using System.Linq;
using StadionTracking;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

internal static class RunnerAvatarBehaviourSetup
{
    public const string ControllerPath = "Assets/Anim/Controller.controller";
    public const string LibraryPath = "Assets/Resources/JoggerBehaviourLibrary.asset";
    private const string ClipsFolder = "Assets/JoggerWarmups/RuntimeClips";
    private static readonly string[][] Entries = {
        new[] { "Mixamo_BatterOnDeck", "Arme & Beine aufwärmen", "Warmup", "Assets/JoggerWarmups/Mixamo/Batter_On_Deck.fbx" },
        new[] { "Mixamo_StartJumpingJacks", "Start Jumping Jacks", "Warmup", "Assets/JoggerWarmups/Mixamo/Start_Jumping_Jacks.fbx" },
        new[] { "Mixamo_WarmingUp", "Warming Up · ohne Schläge", "Warmup", "Assets/JoggerWarmups/Mixamo/Warming_Up.fbx" },
        new[] { "Mixamo_ArmStretching", "Arm Stretching · Arme dehnen", "Warmup", "Assets/JoggerWarmups/Mixamo/Arm_Stretching.fbx" },
        new[] { "Mixamo_AirSquat", "Air Squat · Kniebeugen", "Warmup", "Assets/JoggerWarmups/Mixamo/Air_Squat.fbx" },
        new[] { "Mixamo_Plank", "Plank · Start, halten, Ende", "Warmup", "Assets/JoggerWarmups/Mixamo/Start_Plank.fbx" },
        new[] { "Idle", "Ruhig stehen", "Locomotion", "Assets/Anim/Idle.fbx" },
        new[] { "Walking", "Gehen", "Locomotion", "Assets/Anim/Walking.fbx" },
        new[] { "Jogging", "Joggen", "Locomotion", "Assets/Anim/Ch20_nonPBR@Jogging.fbx" },
        new[] { "Sprint", "Sprinten", "Locomotion", "Assets/Anim/Sprint.fbx" }
    };

    [MenuItem("Tools/Jogger/Install RunningServer behaviour controls")]
    public static void Install()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("Exit Play mode first.");
        JoggerWarmupImporter.ConfigureAll();
        EnsureFolder(ClipsFolder);
        EnsureFolder("Assets/Resources");
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (controller == null) throw new InvalidOperationException("Jogger Animator controller is missing.");
        if (!controller.parameters.Any(p => p.name == "behaviourRate"))
            controller.AddParameter(new AnimatorControllerParameter { name="behaviourRate", type=AnimatorControllerParameterType.Float, defaultFloat=1 });
        var machine = controller.layers[0].stateMachine;
        var library = AssetDatabase.LoadAssetAtPath<RunnerAvatarAnimationLibrary>(LibraryPath);
        if (library == null)
        {
            library = ScriptableObject.CreateInstance<RunnerAvatarAnimationLibrary>();
            AssetDatabase.CreateAsset(library, LibraryPath);
        }
        var entries = new RunnerAvatarAnimation[Entries.Length];
        for (int i=0;i<Entries.Length;i++)
        {
            var spec=Entries[i];
            var source=AssetDatabase.LoadAllAssetsAtPath(spec[3]).OfType<AnimationClip>().FirstOrDefault(c=>!c.name.StartsWith("__preview__"));
            if (source==null || !source.humanMotion) throw new InvalidOperationException("Missing Humanoid clip: "+spec[3]);
            string path=ClipsFolder+"/"+spec[0]+".anim";
            var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
            if (clip==null)
            {
                clip=UnityEngine.Object.Instantiate(source);
                clip.name=spec[0];
                AssetDatabase.CreateAsset(clip,path);
            }
            else EditorUtility.CopySerialized(source,clip);
            clip.name=spec[0];
            if(spec[0]=="Mixamo_WarmingUp") TrimWarmingUpBeforePunches(source,clip);
            if(spec[0]=="Mixamo_BatterOnDeck") KeepBatterWarmup(source,clip);
            if(spec[0]=="Mixamo_StartJumpingJacks") CompleteJumpingJacksCycle(source,clip);
            if(spec[2]=="Warmup" && spec[0]!="Mixamo_Plank")
            {
                SmoothWarmupLoop(clip,spec[0]);
            }
            EditorUtility.SetDirty(clip);
            string stateName="Behaviour_"+spec[0];
            var state=machine.states.Select(s=>s.state).FirstOrDefault(s=>s.name==stateName);
            if(state==null) state=machine.AddState(stateName,new Vector3(650+(i%3)*240,100+(i/3)*110,0));
            state.motion=clip;
            state.speed=1;
            state.speedParameter="behaviourRate";
            state.speedParameterActive=true;
            state.writeDefaultValues=true;
            // No transitions out: session start/stop or explicit return restores normal locomotion.
            entries[i]=new RunnerAvatarAnimation {id=spec[0],label=spec[1],category=spec[2],clip=clip,stateName=stateName};
            if(spec[0]=="Mixamo_Plank")
            {
                var sequence=entries[i];
                sequence.startClip=CreatePlankPhase(machine,stateName,"Start","Start_Plank",false);
                sequence.holdClip=CreatePlankPhase(machine,stateName,"Hold","Plank",true);
                sequence.endClip=CreatePlankPhase(machine,stateName,"End","End_Plank",false);
                ComposePlank(sequence.startClip,clip);
                state.iKOnFeet=true;
            }
            EditorUtility.SetDirty(state);
        }
        foreach(var obsolete in machine.states.Where(s=>s.state.name.StartsWith("Behaviour_Warmup_")).ToArray())
            machine.RemoveState(obsolete.state);
        InstallGreeting(machine);
        InstallFinish(machine);
        library.animations=entries;
        EditorUtility.SetDirty(library);
        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        Debug.Log("[AvatarBehaviour] Installed 10 manual animation states and runtime clip library.");
    }
    private static void InstallFinish(AnimatorStateMachine machine)
    {
        var source=AssetDatabase.LoadAllAssetsAtPath("Assets/JoggerWarmups/Mixamo/Standing_Thumbs_Up.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
        if(!source.humanMotion)throw new InvalidOperationException("Thumbs Up must be Humanoid.");
        string path=ClipsFolder+"/AutomaticFinishThumbsUp.anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}else EditorUtility.CopySerialized(source,clip);
        clip.name="AutomaticFinishThumbsUp";
        KeepFinishStandingInPlace(clip);
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=false;settings.loopBlend=false;AnimationUtility.SetAnimationClipSettings(clip,settings);
        var state=machine.states.Select(x=>x.state).FirstOrDefault(x=>x.name=="AutomaticFinishThumbsUp")??machine.AddState("AutomaticFinishThumbsUp");
        state.motion=clip;state.speed=1;state.iKOnFeet=true;
        RunnerPlankGroundingBaker.Bake(clip,state.name,false);
        EditorUtility.SetDirty(clip);EditorUtility.SetDirty(state);
    }

    // The supplied take walks/lunges forward by about .6 m inside its baked
    // Humanoid body curves. Keep its upper-body gesture on the normal idle stance.
    private static void KeepFinishStandingInPlace(AnimationClip clip)
    {
        var standing=AssetDatabase.LoadAllAssetsAtPath("Assets/Anim/Idle.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
        {
            string name=binding.propertyName;
            bool stance=name.StartsWith("RootT.") || name.StartsWith("RootQ.") || name.StartsWith("LeftFoot") || name.StartsWith("RightFoot") || name.Contains("Upper Leg") || name.Contains("Lower Leg") || name.Contains(" Foot ") || name.Contains(" Toes ");
            if(!stance)continue;
            var reference=AnimationUtility.GetEditorCurve(standing,binding);if(reference==null)continue;
            float value=reference.Evaluate(0);
            AnimationUtility.SetEditorCurve(clip,binding,AnimationCurve.Constant(0,clip.length,value));
        }
    }

    private static void InstallGreeting(AnimatorStateMachine machine)
    {
        var source=AssetDatabase.LoadAllAssetsAtPath("Assets/JoggerWarmups/Mixamo/Waving.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
        if(!source.humanMotion)throw new InvalidOperationException("Waving must be Humanoid.");
        string path=ClipsFolder+"/AutomaticGreetingWave.anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}else EditorUtility.CopySerialized(source,clip);
        clip.name="AutomaticGreetingWave";
        SmoothWarmupLoop(clip,"AutomaticGreetingWave");
        var state=machine.states.Select(x=>x.state).FirstOrDefault(x=>x.name=="AutomaticGreetingWave")??machine.AddState("AutomaticGreetingWave");
        state.motion=clip;state.speed=1;state.iKOnFeet=true;
        EditorUtility.SetDirty(clip);EditorUtility.SetDirty(state);
    }

    private static AnimationClip CreatePlankPhase(AnimatorStateMachine machine,string baseName,string phase,string file,bool looping)
    {
        var source=AssetDatabase.LoadAllAssetsAtPath("Assets/JoggerWarmups/Mixamo/"+file+".fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
        string path=ClipsFolder+"/Mixamo_Plank_"+phase+".anim";
        var clip=AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
        if(clip==null){clip=UnityEngine.Object.Instantiate(source);AssetDatabase.CreateAsset(clip,path);}
        else EditorUtility.CopySerialized(source,clip);
        clip.name="Mixamo_Plank_"+phase;
        if(looping)SmoothWarmupLoop(clip,"PlankHold");
        if(phase=="End") AlignPlankExitStand(clip);
        var settings=AnimationUtility.GetAnimationClipSettings(clip);settings.loopTime=looping;AnimationUtility.SetAnimationClipSettings(clip,settings);
        var name=baseName+"_"+phase;
        var state=machine.states.Select(s=>s.state).FirstOrDefault(x=>x.name==name)??machine.AddState(name);
        state.motion=clip;state.speedParameter="behaviourRate";state.speedParameterActive=true;state.iKOnFeet=true;
        RunnerPlankGroundingBaker.Bake(clip,name,looping);
        EditorUtility.SetDirty(clip);EditorUtility.SetDirty(state);
        return clip;
    }

    private static void AlignPlankExitStand(AnimationClip clip)
    {
        var standing=AssetDatabase.LoadAllAssetsAtPath("Assets/JoggerWarmups/Mixamo/Start_Plank.fbx").OfType<AnimationClip>().First(c=>!c.name.StartsWith("__preview__"));
        float length=clip.length;float blendStart=length-.9f;
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var original=AnimationUtility.GetEditorCurve(clip,binding);
            var reference=AnimationUtility.GetEditorCurve(standing,binding);if(reference==null)continue;
            float endValue=reference.Evaluate(0);
            Func<float,float> sample=t=>Mathf.Lerp(original.Evaluate(t),endValue,Mathf.SmoothStep(0,1,(t-blendStart)/.9f));
            int frames=Mathf.CeilToInt(length*60);var keys=new Keyframe[frames+1];
            for(int i=0;i<=frames;i++)
            {
                float time=length*i/frames,lo=Mathf.Max(0,time-.001f),hi=Mathf.Min(length,time+.001f);
                float slope=(sample(hi)-sample(lo))/(hi-lo);keys[i]=new Keyframe(time,sample(time),slope,slope);
            }
            AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(keys));
        }
    }

    internal const float PlankHoldSeconds=10;
    internal static void ComposePlank(AnimationClip start,AnimationClip target)
    {
        var holdSource=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipsFolder+"/Mixamo_Plank_Hold.anim");
        var end=AssetDatabase.LoadAssetAtPath<AnimationClip>(ClipsFolder+"/Mixamo_Plank_End.anim");
        if(!holdSource.humanMotion || !end.humanMotion)throw new InvalidOperationException("Plank clips must be Humanoid.");
        var hold=UnityEngine.Object.Instantiate(holdSource);
        try
        {
            // Hold phase is already looped and grounded on the Jogger.
            const float blend=.18f;
            float holdStart=start.length-blend;
            float endStart=holdStart+PlankHoldSeconds-blend;
            float duration=endStart+end.length;
            var bindings=AnimationUtility.GetCurveBindings(start).Union(AnimationUtility.GetCurveBindings(hold)).Union(AnimationUtility.GetCurveBindings(end)).ToArray();
            // Start, continuous hold and exit share Humanoid muscle bindings,
            // independently of the original FBX bone namespace.
            target.ClearCurves();
            foreach(var binding in bindings)
            {
                var a=AnimationUtility.GetEditorCurve(start,binding);
                var b=AnimationUtility.GetEditorCurve(hold,binding);
                var c=AnimationUtility.GetEditorCurve(end,binding);
                var fallback=a??b??c;
                Func<float,float> opening=t=>a!=null?a.Evaluate(t):fallback.Evaluate(0);
                Func<float,float> holding=t=>b!=null?b.Evaluate(Mathf.Repeat(t,hold.length)):opening(start.length);
                Func<float,float> closing=t=>c!=null?c.Evaluate(t):holding(PlankHoldSeconds);
                Func<float,float> sample=t=>
                {
                    t=Mathf.Clamp(t,0,duration);
                    if(t<holdStart)return opening(t);
                    if(t<start.length)return Mathf.Lerp(opening(t),holding(t-holdStart),Mathf.SmoothStep(0,1,(t-holdStart)/blend));
                    if(t<endStart)return holding(t-holdStart);
                    if(t<endStart+blend)return Mathf.Lerp(holding(t-holdStart),closing(t-endStart),Mathf.SmoothStep(0,1,(t-endStart)/blend));
                    return closing(t-endStart);
                };
                int frames=Mathf.CeilToInt(duration*60);
                var keys=new Keyframe[frames+1];
                for(int i=0;i<=frames;i++)
                {
                    float time=duration*i/frames;
                    float lo=Mathf.Max(0,time-.0005f),hi=Mathf.Min(duration,time+.0005f);
                    float slope=(sample(hi)-sample(lo))/(hi-lo);
                    keys[i]=new Keyframe(time,sample(time),slope,slope);
                }
                AnimationUtility.SetEditorCurve(target,binding,new AnimationCurve(keys));
            }
            AnimationUtility.SetAnimationEvents(target,new AnimationEvent[0]);
            var settings=AnimationUtility.GetAnimationClipSettings(target);
            settings.startTime=0;settings.stopTime=duration;settings.loopTime=true;settings.loopBlend=true;
            AnimationUtility.SetAnimationClipSettings(target,settings);
            Debug.Log("[Plank sequence] Start="+start.length+"s; hold="+PlankHoldSeconds+"s; end="+end.length+"s; overlap="+blend+"s; total="+duration+"s.");
        }
        finally {UnityEngine.Object.DestroyImmediate(hold);}
    }

    // Circular overlap of the existing Mixamo poses. The tail blends into
    // the opening while both continue moving; value AND velocity match at wrap.
    // Remove the neutral lead-in/out rather than replaying it on every cycle.
    internal static void SmoothWarmupLoop(AnimationClip clip,string id)
    {
        float start=0,end=clip.length,overlap=.25f;
        if(id=="AutomaticGreetingWave")overlap=Mathf.Min(.08f,clip.length*.2f);
        if(id=="Mixamo_BatterOnDeck") {start=1.5f;end=15.9f;overlap=.4f;}
        if(id=="Mixamo_StartJumpingJacks") {start=.2f;end=clip.length-.2f;overlap=.12f;}
        float length=end-start;
        float period=length-overlap;
        float join=length-2*overlap;
        foreach(var binding in AnimationUtility.GetCurveBindings(clip))
        {
            var original=AnimationUtility.GetEditorCurve(clip,binding);
            Func<float,float> periodic=time=>
            {
                float phase=Mathf.Repeat(time,length)*period/length;
                if(phase<join)return original.Evaluate(start+phase+overlap);
                float alpha=Mathf.SmoothStep(0,1,(phase-join)/overlap);
                return Mathf.Lerp(original.Evaluate(start+phase+overlap),original.Evaluate(start+phase-join),alpha);
            };
            int frames=Mathf.CeilToInt(length*60);
            var keys=new Keyframe[frames+1];
            for(int i=0;i<=frames;i++)
            {
                float time=length*i/frames;
                float slope=(periodic(time+.0005f)-periodic(time-.0005f))/.001f;
                keys[i]=new Keyframe(time,periodic(time),slope,slope);
            }
            // Exact equality avoids numerical drift at the cyclic boundary.
            var last=keys[frames];last.value=keys[0].value;
            last.inTangent=last.outTangent=keys[0].outTangent;keys[frames]=last;
            AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(keys));
        }
        var settings=AnimationUtility.GetAnimationClipSettings(clip);
        settings.startTime=0;settings.stopTime=length;
        settings.loopTime=true;settings.loopBlend=false;
        AnimationUtility.SetAnimationClipSettings(clip,settings);
    }

    // Source is 6.3 s. Keep frames 0-62 (30 fps), ending before the
    // boxing guard and first punch; retain all original arm and leg curves.
    internal static void TrimWarmingUpBeforePunches(AnimationClip source,AnimationClip target)
    {
        const float end=62f/30f;
        foreach(var binding in AnimationUtility.GetCurveBindings(source))
        {
            var curve=AnimationUtility.GetEditorCurve(source,binding);
            var keys=curve.keys.Where(k=>k.time<end).ToList();
            float slope=(curve.Evaluate(end+.001f)-curve.Evaluate(end-.001f))/.002f;
            keys.Add(new Keyframe(end,curve.Evaluate(end),slope,slope));
            AnimationUtility.SetEditorCurve(target,binding,new AnimationCurve(keys.ToArray()));
        }
        AnimationUtility.SetAnimationEvents(target,new AnimationEvent[0]);
        var settings=AnimationUtility.GetAnimationClipSettings(target);
        settings.startTime=0;settings.stopTime=end;
        AnimationUtility.SetAnimationClipSettings(target,settings);
    }

    // Keep the original arm and leg exercises at 0-12 s and 32 s-end. Omit all
    // bat-holding/swinging. Crossfade the two retained source sections for .3 s.
    // Preserve all original muscle, foot and body curves in retained sections.
    internal static void KeepBatterWarmup(AnimationClip source,AnimationClip target)
    {
        const float firstEnd=12,secondStart=32,blend=.3f;
        float join=firstEnd-blend;
        float duration=join+source.length-secondStart;
        foreach(var binding in AnimationUtility.GetCurveBindings(source))
        {
            var curve=AnimationUtility.GetEditorCurve(source,binding);
            Func<float,float> sample=time=>
            {
                if(time<=join)return curve.Evaluate(time);
                if(time>=firstEnd)return curve.Evaluate(secondStart+time-join);
                float alpha=Mathf.SmoothStep(0,1,(time-join)/blend);
                return Mathf.Lerp(curve.Evaluate(time),curve.Evaluate(secondStart+time-join),alpha);
            };
            int frames=Mathf.CeilToInt(duration*source.frameRate);
            var keys=new Keyframe[frames+1];
            for(int i=0;i<=frames;i++)
            {
                float time=Mathf.Min(i/source.frameRate,duration);
                float lo=Mathf.Max(0,time-.001f),hi=Mathf.Min(duration,time+.001f);
                float slope=(sample(hi)-sample(lo))/(hi-lo);
                keys[i]=new Keyframe(time,sample(time),slope,slope);
            }
            AnimationUtility.SetEditorCurve(target,binding,new AnimationCurve(keys));
        }
        AnimationUtility.SetAnimationEvents(target,new AnimationEvent[0]);
        var settings=AnimationUtility.GetAnimationClipSettings(target);
        settings.startTime=0;settings.stopTime=duration;
        AnimationUtility.SetAnimationClipSettings(target,settings);
    }

    // The supplied Mixamo take only opens the arms and legs. Replay its exact
    // curves backwards for the closing half: no newly authored body poses.
    internal static void CompleteJumpingJacksCycle(AnimationClip source,AnimationClip target)
    {
        float duration=source.length;
        foreach(var binding in AnimationUtility.GetCurveBindings(source))
        {
            var curve=AnimationUtility.GetEditorCurve(source,binding);
            var forward=new System.Collections.Generic.SortedDictionary<float,Keyframe>();
            foreach(var key in curve.keys)
                if(key.time>=0 && key.time<=duration) forward[key.time]=key;
            if(!forward.ContainsKey(0)) forward[0]=new Keyframe(0,curve.Evaluate(0));
            if(!forward.ContainsKey(duration)) forward[duration]=new Keyframe(duration,curve.Evaluate(duration));
            var keys=new System.Collections.Generic.List<Keyframe>();
            foreach(var original in forward.Values)
            {
                var key=original;
                if(Mathf.Approximately(key.time,0) || Mathf.Approximately(key.time,duration))
                {key.inTangent=0;key.outTangent=0;key.weightedMode=WeightedMode.None;}
                keys.Add(key);
            }
            foreach(var original in forward.Values.Reverse())
            {
                if(Mathf.Approximately(original.time,duration))continue;
                var key=new Keyframe(2*duration-original.time,original.value,-original.outTangent,-original.inTangent,original.outWeight,original.inWeight);
                key.weightedMode=original.weightedMode==WeightedMode.In?WeightedMode.Out:original.weightedMode==WeightedMode.Out?WeightedMode.In:original.weightedMode;
                if(Mathf.Approximately(original.time,0))
                {key.inTangent=0;key.outTangent=0;key.weightedMode=WeightedMode.None;}
                keys.Add(key);
            }
            AnimationUtility.SetEditorCurve(target,binding,new AnimationCurve(keys.ToArray()));
        }
        var settings=AnimationUtility.GetAnimationClipSettings(target);
        settings.startTime=0;settings.stopTime=2*duration;
        settings.loopTime=true;settings.loopBlend=false;
        AnimationUtility.SetAnimationClipSettings(target,settings);
    }

    private static void EnsureFolder(string path)
    {
        if(AssetDatabase.IsValidFolder(path))return;
        string parent=Path.GetDirectoryName(path).Replace('\\','/');
        EnsureFolder(parent);
        AssetDatabase.CreateFolder(parent,Path.GetFileName(path));
    }

    public static void InstallAndVerify()
    {
        Install();
        RunnerAvatarBehaviourChecks.Verify();
        RunnerAvatarGreetingChecks.Verify();
        RunnerAvatarFinishChecks.Verify();
        Directory.CreateDirectory("Temp/BehaviourIntegration");
        File.WriteAllText("Temp/BehaviourIntegration/result.txt","PASS: imported clips, controller states, companion and tempo-partner playback, once/loop, validation and server commands.\n");
    }
}
