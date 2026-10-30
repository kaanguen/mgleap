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
        public string DisplayName => RunnerAnimationNames.For(id, label);
        public float Duration => clip != null ? clip.length : 0;
    }

    public static class RunnerAnimationNames
    {
        public static string For(string id, string fallback = "")
        {
            switch (id)
            {
                case "Mixamo_BatterOnDeck": return "Arme und Beine";
                case "Mixamo_StartJumpingJacks": return "Hampelmänner";
                case "Mixamo_WarmingUp": return "Aufwärmen";
                case "Mixamo_ArmStretching": return "Arme dehnen";
                case "Mixamo_AirSquat": return "Kniebeugen";
                case "Mixamo_Plank": return "Unterarmstütz";
                case "Idle": return "Stehen";
                case "Walking": return "Gehen";
                case "Jogging": return "Joggen";
                case "Sprint": return "Sprinten";
                default: return fallback;
            }
        }
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
