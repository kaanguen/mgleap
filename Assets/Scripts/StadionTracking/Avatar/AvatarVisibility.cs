using UnityEngine;

namespace StadionTracking
{
    internal static class AvatarVisibility
    {
        internal static void Configure(GameObject avatar)
        {
            // Keep the cheaper distant mesh instead of dropping the avatar entirely.
            foreach (var group in avatar.GetComponentsInChildren<LODGroup>(true))
            {
                var levels = group.GetLODs();
                if (levels.Length == 0) continue;
                levels[levels.Length - 1].screenRelativeTransitionHeight = 0f;
                group.SetLODs(levels);
            }
            foreach (var skin in avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                skin.updateWhenOffscreen = true;
            foreach (var renderer in avatar.GetComponentsInChildren<Renderer>(true))
                renderer.allowOcclusionWhenDynamic = false;
            foreach (var animator in avatar.GetComponentsInChildren<Animator>(true))
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
        }
    }
}
