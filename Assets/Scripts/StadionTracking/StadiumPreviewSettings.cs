using System;
using UnityEngine;

namespace StadionTracking
{

    public enum StadiumAnchorMode
    {

        AvatarSpawnPreview,

        KnownMarkerPosition
    }

    [Serializable]
    public sealed class StadiumPreviewSettings
    {

        public bool Enabled = true;

        [Min(0.1f), Tooltip("Running speed in metres per second when the avatar's speed slider is at maximum.")]
        public float SpeedAtFullSlider = 6f;
        [Tooltip("Preview places the reference path point at the avatar spawn. KnownMarkerPosition requires a surveyed marker placement.")]
        public StadiumAnchorMode AnchorMode = StadiumAnchorMode.AvatarSpawnPreview;

        [Range(1, 8)] public int ReferenceLane = 1;
        [Tooltip("Distance along the reference lane assigned to the spawn or known marker. Zero is the lower end of the right bend.")]
        public float ReferenceMetres;
        [Tooltip("Marker-local direction pointing along the running direction at the reference point. Up means the printed top edge of a floor marker.")]

        public Vector3 MarkerLocalRunDirection = Vector3.up;
        [Tooltip("Additional horizontal heading correction in degrees.")]
        public float HeadingOffsetDegrees;
        [Tooltip("Known marker offset from the reference path point in track-local X/Z metres. Ignored in spawn preview mode.")]

        public Vector2 MarkerOffsetFromReference;
        [Min(0.1f), Tooltip("Maximum permitted sideways correction from spawn to the path in known-marker mode.")]

        public float MaxEntryCorrection = 10f;
        [Tooltip("Optional material. Otherwise a bundled XR-compatible line shader is loaded.")]
        public Material LineMaterial;

        [Min(0.01f)] public float LineWidth = 0.08f;
        [Min(0.01f)] public float HeightAboveGround = 0.06f;

        public Color RouteColor = Color.cyan;
        public Color DirectionColor = Color.yellow;
    }
}
