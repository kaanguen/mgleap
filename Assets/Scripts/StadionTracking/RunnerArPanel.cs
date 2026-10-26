using System.Collections.Generic;
using UnityEngine;

namespace StadionTracking
{
    [ExecuteAlways, RequireComponent(typeof(Canvas), typeof(CanvasGroup))]
    public sealed class RunnerArPanel : MonoBehaviour
    {
        [SerializeField] private Camera headCamera;
        [SerializeField, Min(0.4f)] private float distance = 1.8f;
        [SerializeField] private int priority;
        [SerializeField, Tooltip("Viewport centre of the live running HUD (0..1).") ]
        private Vector2 runningHudCentre = new Vector2(0.30f, 0.68f);
        private Canvas panel;
        private CanvasGroup group;
        private static readonly List<RunnerArPanel> Active = new List<RunnerArPanel>();
        // The setup menu is centered; in-session panels keep the track view clear.
        public bool IsCentered => GetComponent<RunnerMainMenu>() != null;
        public bool IsRunningHud => GetComponent<ActiveStatsDisplay>() != null;
        public int Priority { get => priority; set => priority = value; }

        private void OnEnable()
        {
            panel = GetComponent<Canvas>();
            group = GetComponent<CanvasGroup>();
            if (!Active.Contains(this)) Active.Add(this);
        }
        private void OnDisable() => Active.Remove(this);
        private void LateUpdate()
        {
            if (panel == null) return;
            if (headCamera == null) headCamera = Camera.main;
            if (headCamera == null) return;
            RunnerArPanel winner = this;
            foreach (var item in Active)
                if (item != null && item.isActiveAndEnabled &&
                    (item.priority > winner.priority || (item.priority == winner.priority && item.GetInstanceID() < winner.GetInstanceID()))) winner = item;
            panel.enabled = winner == this;
            if (group != null) group.blocksRaycasts = group.interactable = panel.enabled;
            panel.worldCamera = headCamera;
            Rect r = ((RectTransform)transform).rect;
            float width = Vector3.Distance(headCamera.ViewportToWorldPoint(new Vector3(0, 0.5f, distance)),
                headCamera.ViewportToWorldPoint(new Vector3(1, 0.5f, distance)));
            float scale;
            float viewHeight = Vector3.Distance(headCamera.ViewportToWorldPoint(new Vector3(0.5f, 0, distance)),
                headCamera.ViewportToWorldPoint(new Vector3(0.5f, 1, distance)));
            scale = Mathf.Min(width / 1280f, viewHeight / 720f);
            float x = IsCentered ? 0.5f : (48 + r.width / 2) * scale / width;
            float y = IsCentered ? 0.5f : 1 - (40 + r.height / 2) * scale / viewHeight;
            if (IsRunningHud)
            {
                // Keep the live values near the gaze direction, with room above and beside the card.
                float halfWidth = r.width * scale / (2 * width);
                float halfHeight = r.height * scale / (2 * viewHeight);
                x = Mathf.Clamp(runningHudCentre.x, halfWidth + .04f, 1 - halfWidth - .04f);
                y = Mathf.Clamp(runningHudCentre.y, halfHeight + .04f, 1 - halfHeight - .04f);
            }
            transform.localScale = Vector3.one * scale;
            transform.SetPositionAndRotation(headCamera.ViewportToWorldPoint(new Vector3(x, y, distance)), headCamera.transform.rotation);
        }
    }
}
