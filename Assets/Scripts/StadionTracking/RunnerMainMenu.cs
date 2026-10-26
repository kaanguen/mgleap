using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StadionTracking
{
    public sealed class RunnerMainMenu : MonoBehaviour
    {
        [SerializeField] private TMP_Text paceValue, heightValue, status;
        [SerializeField] private UnityEngine.UI.Button beside, pacer;
        public bool ShowTrack => RunnerSessionSettings.ShowTrack;
        public int PaceSeconds => RunnerSessionSettings.PaceSeconds;
        public int HeightCm => RunnerSessionSettings.HeightCm;
        public TreadmillRunMode Mode => RunnerSessionSettings.Mode;

        private void Start()
        {
            Bind("PaceMinus", () => ChangePace(-15)); Bind("PacePlus", () => ChangePace(15));
            Bind("HeightMinus", () => ChangeHeight(-1)); Bind("HeightPlus", () => ChangeHeight(1));
            Bind("SideBySide", () => SetMode(TreadmillRunMode.SideBySide));
            Bind("Pacer", () => SetMode(TreadmillRunMode.Pacer)); Bind("StartSession", StartSession);
            Bind("TrackVisibility", ToggleTrackVisibility);
            Refresh();
        }
        private void Bind(string name, UnityEngine.Events.UnityAction action)
        {
            var child = transform.Find(name);
            if (child != null) child.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(action);
        }
        public void ChangePace(int seconds) { RunnerSessionSettings.PaceSeconds = Mathf.Clamp(PaceSeconds + seconds, 180, 1200); Refresh(); }
        public void ChangeHeight(int cm) { RunnerSessionSettings.HeightCm = Mathf.Clamp(HeightCm + cm, 80, 230); Refresh(); }
        public void SetMode(TreadmillRunMode mode) { RunnerSessionSettings.Mode = mode; Refresh(); }
        public void ToggleTrackVisibility() { RunnerSessionSettings.ShowTrack = !ShowTrack; Refresh(); }
        public void Refresh()
        {
            if (paceValue == null) return;
            paceValue.text = $"<mspace=16>{PaceSeconds / 60}:{PaceSeconds % 60:00}</mspace> <size=13><color=#E3DEF7>min/km</color></size>";
            heightValue.text = $"<mspace=16>{HeightCm}</mspace> <size=13><color=#E3DEF7>cm</color></size>";
            beside.GetComponent<RunnerGlassGraphic>().color = new Color(1, 1, 1, Mode == TreadmillRunMode.SideBySide ? .2f : .07f);
            pacer.GetComponent<RunnerGlassGraphic>().color = new Color(1, 1, 1, Mode == TreadmillRunMode.Pacer ? .2f : .07f);
            beside.GetComponentInChildren<TMP_Text>().text = Mode == TreadmillRunMode.SideBySide ? "• NEBEN MIR" : "NEBEN MIR";
            pacer.GetComponentInChildren<TMP_Text>().text = Mode == TreadmillRunMode.Pacer ? "• PACER" : "PACER";
            var visibility = transform.Find("TrackVisibility")?.GetComponent<UnityEngine.UI.Button>();
            if (visibility != null)
            {
                visibility.GetComponentInChildren<TMP_Text>().text = ShowTrack ? "RENNSTRECKE AUSSCHALTEN" : "RENNSTRECKE ANZEIGEN";
                visibility.GetComponent<RunnerGlassGraphic>().color = new Color(1, 1, 1, ShowTrack ? .2f : .07f);
            }
            status.text = Mode == TreadmillRunMode.Pacer ? "Avatar läuft 3 m auf der Bahn voraus." : "Avatar läuft auf der Nachbarbahn mit.";
        }
        public void StartSession()
        {
            if (!Application.CanStreamedLevelBeLoaded("TreadmillStraightTest"))
            { status.color = RunnerUiTheme.Warning; status.text = "! Laufstrecke nicht verfügbar."; return; }
            RunnerSessionSettings.Pending = true;
            SceneManager.LoadScene("TreadmillStraightTest");
        }
    }
}
