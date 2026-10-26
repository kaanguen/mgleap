using System.Globalization;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace StadionTracking
{
    /// <summary>World-space height setup with a keypad usable through the existing XR UI ray.</summary>
    public sealed class TreadmillHeightSetup : MonoBehaviour
    {
        [SerializeField] private StraightTreadmillWorld world;
        [SerializeField, Tooltip("Place automatically from the configured body height without showing the menu.")]
        private bool skipHeightMenu = true;
        [SerializeField, Range(80, 230)] private int bodyHeightCm = 184;
        [SerializeField, Range(0, 25), Tooltip("Approximate distance from top of head to headset eye centre.")]
        private float headTopToEyeCm = 12f;
        [SerializeField] private UnityEngine.UI.InputField heightInput;
        [SerializeField] private UnityEngine.UI.Text status;
        [SerializeField] private UnityEngine.UI.Button startButton;
        [SerializeField] private Canvas canvas;
        [SerializeField] private Font font;
        private bool menuPlaced;
        private float trackingSettledAt = -1f;
        private bool requestingFloor;
        private float requestedSpeed;
        private static readonly Color Ink = new Color(0.95f, 0.97f, 1f);

        private void Awake()
        {
            if (world == null) world = FindFirstObjectByType<StraightTreadmillWorld>();
            if (world == null) { Debug.LogError("[Treadmill] Height setup cannot find VirtualWorld."); enabled = false; return; }
            requestedSpeed = world.SpeedKmh;
            world.SpeedKmh = 0;
            world.BeginHeightCalibration();
            if (skipHeightMenu && canvas != null) canvas.gameObject.SetActive(false);
        }

        private void Start()
        {
            if (skipHeightMenu)
            {
                StartCoroutine(PlaceFromFixedHeight());
                return;
            }
            if (canvas == null) PrepareMenu();
            else WireSavedMenu();
            canvas.worldCamera = world.HeadCamera;
            Debug.Log($"[Treadmill] Height menu ready; scene={gameObject.scene.name}, " +
                $"camera={canvas.worldCamera}, font={font}, graphics={canvas.GetComponentsInChildren<UnityEngine.UI.Graphic>().Length}.");
        }

        private IEnumerator PlaceFromFixedHeight()
        {
            // Wait for the tracked pose rather than using the pre-tracking camera at world zero.
            float trackedFor = 0f;
            while (trackedFor < 0.5f)
            {
                trackedFor = world.HeadPoseReady ? trackedFor + Time.unscaledDeltaTime : 0f;
                yield return null;
            }
            while (!world.CalibrateFromEyeHeight((bodyHeightCm - headTopToEyeCm) / 100f))
                yield return null;
            world.SpeedKmh = requestedSpeed;
            Debug.Log($"[Treadmill] Automatic placement ready: body={bodyHeightCm} cm, " +
                $"estimated eyes={bodyHeightCm - headTopToEyeCm:F0} cm, trackY={world.transform.position.y:F3} m.");
            enabled = false;
        }

        private void Update()
        {
            if (canvas == null || world.HeadCamera == null) return;
            // Keep the menu reachable across the initial tracking/reference-space update.
            Transform head = world.HeadCamera.transform;
            if (!world.HeadPoseReady) { menuPlaced = false; trackingSettledAt = -1f; }
            else if (trackingSettledAt < 0f) trackingSettledAt = Time.unscaledTime + 2f;
            Vector3 offset = canvas.transform.position - head.position;
            Vector3 flatOffset = Vector3.ProjectOnPlane(offset, Vector3.up);
            Vector3 forward = Vector3.ProjectOnPlane(head.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) return;
            forward.Normalize();
            if (menuPlaced && offset.magnitude < 2.2f && Mathf.Abs(offset.y) < 0.5f &&
                Vector3.Angle(flatOffset, forward) < 35f) return;
            canvas.transform.SetPositionAndRotation(head.position + forward * 1.4f,
                Quaternion.LookRotation(forward, Vector3.up));
            menuPlaced = world.HeadPoseReady && Time.unscaledTime >= trackingSettledAt;
        }

        public void PrepareMenu()
        {
            if (canvas != null) return;
            font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            BuildMenu();
            if (world != null && world.HeadCamera != null)
                canvas.transform.SetPositionAndRotation(world.HeadCamera.transform.position + world.HeadCamera.transform.forward * 1.4f,
                    world.HeadCamera.transform.rotation);
        }

        private void WireSavedMenu()
        {
            heightInput.onValueChanged.AddListener(_ => InvalidatePreview());
            foreach (var button in canvas.GetComponentsInChildren<UnityEngine.UI.Button>(true))
            {
                string key = button.name;
                if (button.transform.parent.name.StartsWith("KeypadRow"))
                    button.onClick.AddListener(() => EnterKey(key));
                else if (key == "Größe übernehmen / Vorschau") button.onClick.AddListener(ApplyHeight);
                else if (key == "Bodenreferenz automatisch nutzen") button.onClick.AddListener(ApplyFloor);
                else if (key == "Bahn −2 cm") button.onClick.AddListener(() => Adjust(-0.02f));
                else if (key == "Bahn +2 cm") button.onClick.AddListener(() => Adjust(0.02f));
                else if (key == "Höhe passt – weiter") button.onClick.AddListener(Finish);
            }
            startButton.interactable = false;
        }

        private void BuildMenu()
        {
            var panel = new GameObject("HeightSetupCanvas", typeof(RectTransform), typeof(Canvas),
                typeof(UnityEngine.UI.CanvasScaler), typeof(UnityEngine.UI.GraphicRaycaster),
                typeof(TrackedDeviceGraphicRaycaster), typeof(UnityEngine.UI.Image),
                typeof(UnityEngine.UI.VerticalLayoutGroup));
            panel.transform.SetParent(transform, false);
            canvas = panel.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = world.HeadCamera;
            var rect = panel.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(500, 920);
            rect.localScale = Vector3.one * 0.001f;
            panel.GetComponent<UnityEngine.UI.Image>().color = new Color(0.035f, 0.055f, 0.09f, 1f);
            var layout = panel.GetComponent<UnityEngine.UI.VerticalLayoutGroup>();
            layout.padding = new RectOffset(24, 24, 20, 20);
            layout.spacing = 8;
            layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true; layout.childForceExpandHeight = false;

            Label(panel.transform, "Laufbahn platzieren", 30, 40);
            Label(panel.transform, "Aufrecht auf dem Laufband stehen.\nGröße eingeben oder Bodenreferenz nutzen.", 20, 56);
            Label(panel.transform, "Körpergröße in cm (80–230)", 21, 28);
            var field = Box(panel.transform, "BodyHeightCm", 52);
            field.AddComponent<UnityEngine.UI.Image>().color = new Color(0.13f, 0.18f, 0.25f);
            heightInput = field.AddComponent<UnityEngine.UI.InputField>();
            heightInput.contentType = UnityEngine.UI.InputField.ContentType.IntegerNumber;
            heightInput.characterLimit = 3;
            var fieldText = Label(field.transform, "", 34, 0);
            var textRect = fieldText.rectTransform;
            textRect.anchorMin = Vector2.zero; textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(12, 3); textRect.offsetMax = new Vector2(-12, -3);
            heightInput.textComponent = fieldText;
            heightInput.targetGraphic = field.GetComponent<UnityEngine.UI.Image>();
            heightInput.text = bodyHeightCm.ToString(CultureInfo.InvariantCulture);
            heightInput.onValueChanged.AddListener(_ => InvalidatePreview());

            for (int row = 0; row < 4; row++)
            {
                var keys = Row(panel.transform, "KeypadRow" + row, 44);
                for (int column = 0; column < 3; column++)
                {
                    string key = row < 3 ? (row * 3 + column + 1).ToString() :
                        column == 0 ? "Löschen" : column == 1 ? "0" : "←";
                    Button(keys.transform, key, () => EnterKey(key), 44);
                }
            }

            Label(panel.transform, "Augenhöhe geschätzt: Größe minus 12 cm.\nBahn danach mit ±2 cm auf Fußhöhe korrigieren.", 18, 50);
            Button(panel.transform, "Größe übernehmen / Vorschau", ApplyHeight, 48);
            Button(panel.transform, "Bodenreferenz automatisch nutzen", ApplyFloor, 48);
            var adjust = Row(panel.transform, "HeightCorrection", 44);
            Button(adjust.transform, "Bahn −2 cm", () => Adjust(-0.02f), 44);
            Button(adjust.transform, "Bahn +2 cm", () => Adjust(0.02f), 44);
            status = Label(panel.transform, "Erst die Höhe bestätigen. Die Bahn bleibt stehen.", 19, 62);
            startButton = Button(panel.transform, "Höhe passt – weiter", Finish, 48);
            startButton.interactable = false;
        }

        private void EnterKey(string key)
        {
            if (requestingFloor) return;
            if (key == "Löschen") heightInput.text = "";
            else if (key == "←")
                heightInput.text = heightInput.text.Length == 0 ? "" : heightInput.text.Substring(0, heightInput.text.Length - 1);
            else if (heightInput.text.Length < 3) heightInput.text += key;
        }

        private void InvalidatePreview()
        {
            world.BeginHeightCalibration();
            if (startButton != null) startButton.interactable = false;
            if (status != null) status.text = "Größe erneut übernehmen, um die Bahn zu platzieren.";
        }

        private void ApplyHeight()
        {
            if (requestingFloor) return;
            if (!int.TryParse(heightInput.text, out int cm) || cm < 80 || cm > 230)
            { status.text = "Bitte eine Größe zwischen 80 und 230 cm eingeben."; return; }
            if (!world.CalibrateFromEyeHeight((cm - headTopToEyeCm) / 100f))
            { status.text = "Auf Headtracking warten und dabei geradeaus schauen."; return; }
            bodyHeightCm = cm;
            startButton.interactable = true;
            status.text = "Vorschau auf Fußhöhe prüfen. Bei Bedarf ±2 cm korrigieren, dann bestätigen.";
        }

        private void ApplyFloor()
        {
            if (!requestingFloor) StartCoroutine(RequestFloor());
        }

        private IEnumerator RequestFloor()
        {
            if (!world.TryRequestFloorOrigin(out string reason))
            { status.text = reason; yield break; }
            requestingFloor = true;
            world.BeginHeightCalibration();
            startButton.interactable = false;
            heightInput.interactable = false;
            status.text = "OpenXR-Bodenreferenz wird angefordert …";
            // Allow the tracked camera pose and XROrigin offset to update after the space switch.
            yield return null;
            yield return null;
            float deadline = Time.realtimeSinceStartup + 5f;
            bool placed = false;
            while (Time.realtimeSinceStartup < deadline)
            {
                if (world.TryCalibrateFromFloorOrigin()) { placed = true; break; }
                yield return null;
            }
            requestingFloor = false;
            heightInput.interactable = true;
            // A reference-space change can move the view relative to the menu; place it again.
            menuPlaced = false;
            startButton.interactable = placed;
            status.text = placed
                ? "Bodenreferenz bestätigt. Auf erhöhtem Laufband die Bahn nach oben korrigieren."
                : "OpenXR hat Floor nicht bestätigt. Bitte Körpergröße verwenden.";
        }

        private void Adjust(float metres)
        {
            if (!world.HeightConfirmed) { status.text = "Zuerst Größe oder Bodenreferenz übernehmen."; return; }
            world.AdjustSurfaceHeight(metres);
        }

        private void Finish()
        {
            if (requestingFloor || !world.HeightConfirmed) return;
            world.SpeedKmh = requestedSpeed;
            Debug.Log("[Treadmill] Height confirmed; track visible, speed=" + requestedSpeed + " km/h.");
            canvas.gameObject.SetActive(false);
            enabled = false;
        }

        private GameObject Box(Transform parent, string name, float height)
        {
            var obj = new GameObject(name, typeof(RectTransform), typeof(UnityEngine.UI.LayoutElement));
            obj.transform.SetParent(parent, false);
            obj.GetComponent<UnityEngine.UI.LayoutElement>().preferredHeight = height;
            return obj;
        }

        private GameObject Row(Transform parent, string name, float height)
        {
            var row = Box(parent, name, height);
            var layout = row.AddComponent<UnityEngine.UI.HorizontalLayoutGroup>();
            layout.spacing = 8; layout.childControlWidth = true; layout.childControlHeight = true;
            layout.childForceExpandWidth = true;
            return row;
        }

        private UnityEngine.UI.Text Label(Transform parent, string text, int size, float height)
        {
            var obj = Box(parent, "Label", height);
            var label = obj.AddComponent<UnityEngine.UI.Text>();
            label.font = font; label.text = text; label.fontSize = size; label.color = Ink;
            label.alignment = TextAnchor.MiddleCenter; label.raycastTarget = false;
            return label;
        }

        private UnityEngine.UI.Button Button(Transform parent, string text, UnityEngine.Events.UnityAction action, float height)
        {
            var obj = Box(parent, text, height);
            obj.AddComponent<UnityEngine.UI.Image>().color = new Color(0.12f, 0.32f, 0.43f);
            var button = obj.AddComponent<UnityEngine.UI.Button>();
            button.targetGraphic = obj.GetComponent<UnityEngine.UI.Image>();
            button.onClick.AddListener(action);
            var label = Label(obj.transform, text, 20, 0);
            label.rectTransform.anchorMin = Vector2.zero; label.rectTransform.anchorMax = Vector2.one;
            label.rectTransform.offsetMin = Vector2.zero; label.rectTransform.offsetMax = Vector2.zero;
            return button;
        }
    }
}
