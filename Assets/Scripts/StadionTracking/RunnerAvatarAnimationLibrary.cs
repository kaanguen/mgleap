using System;
using UnityEngine;

namespace StadionTracking
{
    [Serializable]
    public sealed class RunnerAvatarAnimation
    {
        public string id;
        public string label;
        public string category;
        public AnimationClip clip;
        public string stateName;
        public AnimationClip startClip, holdClip, endClip;
        public bool IsSequence => startClip != null && holdClip != null && endClip != null;
        public float Duration => clip != null ? clip.length : 0;
    }

    public sealed class RunnerAvatarAnimationLibrary : ScriptableObject
    {
        public RunnerAvatarAnimation[] animations = Array.Empty<RunnerAvatarAnimation>();
        public RunnerAvatarAnimation Find(string id)
        {
            foreach (var entry in animations)
                if (entry != null && string.Equals(entry.id, id, StringComparison.Ordinal)) return entry;
            return null;
        }
    }
}
