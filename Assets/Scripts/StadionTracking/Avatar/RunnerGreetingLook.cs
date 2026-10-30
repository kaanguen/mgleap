using UnityEngine;
namespace StadionTracking
{
    // Runs after the Animator and both avatar placement owners. Automatic greetings and run-end gestures
    // use this adjustment; manual exercise poses keep their original head.
    [DefaultExecutionOrder(1000)]
    public sealed class RunnerGreetingLook : MonoBehaviour
    {
        private Animator animator;
        private Camera target;
        private float weight;
        private bool includeTorso=true;
        internal void SetTarget(Camera camera,float amount,bool useTorso=true){includeTorso=useTorso;if(animator==null)animator=GetComponent<Animator>();target=camera;weight=Mathf.Clamp01(amount);}
        private void Awake()=>animator=GetComponent<Animator>();
        private void LateUpdate()
        {
            if(target==null || weight<=0 || animator==null || !animator.isHuman)return;
            var head=animator.GetBoneTransform(HumanBodyBones.Head);if(head==null)return;
            Vector3 direction=transform.InverseTransformDirection(target.transform.position-head.position).normalized;
            float yaw=Mathf.Clamp(Mathf.Atan2(direction.x,direction.z)*Mathf.Rad2Deg,includeTorso?-55:-35,includeTorso?55:35);
            float pitch=Mathf.Clamp(-Mathf.Asin(Mathf.Clamp(direction.y,-1,1))*Mathf.Rad2Deg,includeTorso?-20:-12,includeTorso?20:12);
            if(includeTorso)Apply(HumanBodyBones.Chest,yaw,pitch,.2f);
            Apply(HumanBodyBones.Neck,yaw,pitch,includeTorso?.25f:.35f);Apply(HumanBodyBones.Head,yaw,pitch,includeTorso?.55f:.65f);
        }
        private void Apply(HumanBodyBones bone,float yaw,float pitch,float share)
        {
            var joint=animator.GetBoneTransform(bone);if(joint==null)return;
            Quaternion turn=Quaternion.AngleAxis(yaw*weight*share,transform.up)*Quaternion.AngleAxis(pitch*weight*share,transform.right);
            joint.rotation=turn*joint.rotation;
        }
    }
}
