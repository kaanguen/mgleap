using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

// Retarget once in the Editor. The headset plays ordinary baked Humanoid curves;
// no per-frame mesh baking, raycasts or root-placement changes at runtime.
internal static class RunnerPlankGroundingBaker
{
    private sealed class Surface
    {
        public SkinnedMeshRenderer skin;
        public int[] side;
    }
    internal static void Bake(AnimationClip clip,string stateName,bool looping)
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var avatar=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/JoggerRealistic/Jogger_Realistic.prefab"));
        SceneManager.MoveGameObjectToScene(avatar,scene);
        var animator=avatar.GetComponentInChildren<Animator>();
        animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;animator.applyRootMotion=false;
        animator.Rebind();animator.SetFloat("behaviourRate",1);animator.Play("Base Layer.idle",0,0);animator.Update(0);
        var surfaces=avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s=>new Surface{skin=s,side=Classify(s,animator)}).ToArray();
        var mesh=new Mesh();var vertices=new List<Vector3>();
        try
        {
            float floor=Measure(surfaces,mesh,vertices)[0];
            float length=clip.length;int frames=Mathf.CeilToInt(length*30);
            var root=new float[frames+1];var left=new Vector3[frames+1];var right=new Vector3[frames+1];
            for(int i=0;i<=frames;i++)
            {
                animator.Play("Base Layer."+stateName,0,looping && i==frames?0:Mathf.Min(.999999f,(float)i/frames));animator.Update(0);
                var heights=Measure(surfaces,mesh,vertices);
                float lift=floor-heights[0];root[i]=lift/animator.humanScale;
                Vector3 up=Quaternion.Inverse(animator.bodyRotation)*Vector3.up/animator.humanScale;
                // Keep raised stepping feet raised. Plant only the source's support
                // feet; compensate for the torso-height correction in both goals.
                left[i]=up*((heights[1]<floor+.07f?floor-heights[1]:0)-lift);
                right[i]=up*((heights[2]<floor+.07f?floor-heights[2]:0)-lift);
            }
            foreach(var binding in AnimationUtility.GetCurveBindings(clip))
            {
                var original=AnimationUtility.GetEditorCurve(clip,binding);
                var name=binding.propertyName;
                bool rootY=name=="RootT.y",leftGoal=name.StartsWith("LeftFootT."),rightGoal=name.StartsWith("RightFootT.");
                if(!rootY && !leftGoal && !rightGoal)continue;
                int axis=name.EndsWith(".x")?0:name.EndsWith(".y")?1:2;
                var values=new float[frames+1];
                for(int i=0;i<=frames;i++)values[i]=original.Evaluate(length*i/frames)+(rootY?root[i]:leftGoal?left[i][axis]:right[i][axis]);
                if(looping)values[frames]=values[0];
                var keys=new Keyframe[frames+1];float dt=length/frames;
                for(int i=0;i<=frames;i++)
                {
                    float slope=i==0?(looping?(values[1]-values[frames-1])/(2*dt):(values[1]-values[0])/dt):i==frames?(looping?(values[1]-values[frames-1])/(2*dt):(values[frames]-values[frames-1])/dt):(values[i+1]-values[i-1])/(2*dt);
                    keys[i]=new Keyframe(length*i/frames,values[i],slope,slope);
                }
                AnimationUtility.SetEditorCurve(clip,binding,new AnimationCurve(keys));
            }
            EditorUtility.SetDirty(clip);
        }
        finally{UnityEngine.Object.DestroyImmediate(mesh);EditorSceneManager.ClosePreviewScene(scene);}
    }
    internal static void Verify()
    {
        var scene=EditorSceneManager.NewPreviewScene();
        var avatar=UnityEngine.Object.Instantiate(AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Characters/JoggerRealistic/Jogger_Realistic.prefab"));SceneManager.MoveGameObjectToScene(avatar,scene);
        var animator=avatar.GetComponentInChildren<Animator>();animator.cullingMode=AnimatorCullingMode.AlwaysAnimate;
        animator.Rebind();animator.SetFloat("behaviourRate",1);animator.Play("Base Layer.idle",0,0);animator.Update(0);
        var surfaces=avatar.GetComponentsInChildren<SkinnedMeshRenderer>().Select(s=>new Surface{skin=s,side=Classify(s,animator)}).ToArray();var mesh=new Mesh();var vertices=new List<Vector3>();
        try
        {
            float floor=Measure(surfaces,mesh,vertices)[0],worst=0;
            foreach(var phase in new[]{"Start","Hold","End"})
            {
                for(int i=0;i<=40;i++)
                {
                    animator.Play("Base Layer.Behaviour_Mixamo_Plank_"+phase,0,Mathf.Min(.999999f,i/40f));animator.Update(0);
                    var heights=Measure(surfaces,mesh,vertices);worst=Mathf.Max(worst,Mathf.Abs(heights[0]-floor));
                    if(Mathf.Abs(heights[0]-floor)>.025f)throw new InvalidOperationException("Plank floor drift "+phase+" "+i+": "+(heights[0]-floor));
                    if(phase=="End" && i==40 && (Mathf.Abs(heights[1]-floor)>.025f || Mathf.Abs(heights[2]-floor)>.025f))throw new InvalidOperationException("Exit must end with both feet on the floor.");
                }
            }
            Debug.Log("[Plank grounding CHECK] PASS: 123 target-mesh poses, both exit feet grounded; maximum floor deviation="+worst.ToString("F4")+" m.");
        }
        finally{UnityEngine.Object.DestroyImmediate(mesh);EditorSceneManager.ClosePreviewScene(scene);}
    }

    private static int[] Classify(SkinnedMeshRenderer skin,Animator animator)
    {
        var feet=new[]{animator.GetBoneTransform(HumanBodyBones.LeftFoot),animator.GetBoneTransform(HumanBodyBones.RightFoot)};
        var weights=skin.sharedMesh.boneWeights;var result=new int[weights.Length];
        for(int i=0;i<weights.Length;i++)
        {
            var w=weights[i];var indices=new[]{w.boneIndex0,w.boneIndex1,w.boneIndex2,w.boneIndex3};var values=new[]{w.weight0,w.weight1,w.weight2,w.weight3};
            for(int side=0;side<2;side++)
            {
                float sum=0;for(int j=0;j<4;j++)if(indices[j]<skin.bones.Length && (skin.bones[indices[j]]==feet[side] || skin.bones[indices[j]].IsChildOf(feet[side])))sum+=values[j];
                if(sum>.5f)result[i]=side+1;
            }
        }
        return result;
    }
    private static float[] Measure(Surface[] surfaces,Mesh mesh,List<Vector3> vertices)
    {
        var heights=new[]{float.PositiveInfinity,float.PositiveInfinity,float.PositiveInfinity};
        foreach(var surface in surfaces)
        {
            surface.skin.BakeMesh(mesh);mesh.GetVertices(vertices);
            for(int i=0;i<vertices.Count;i++)
            {
                float y=surface.skin.transform.TransformPoint(vertices[i]).y;
                heights[0]=Mathf.Min(heights[0],y);
                if(surface.side[i]>0)heights[surface.side[i]]=Mathf.Min(heights[surface.side[i]],y);
            }
        }
        return heights;
    }
}
