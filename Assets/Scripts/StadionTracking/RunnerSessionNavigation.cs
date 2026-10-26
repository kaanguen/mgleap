using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace StadionTracking
{
    public sealed class RunnerSessionNavigation : MonoBehaviour
    {
        private StraightTreadmillWorld world;
        private float resumeSpeed;
        private TMP_Text pauseLabel;
        private void Start()
        {
            world = FindFirstObjectByType<StraightTreadmillWorld>();
            var pause = transform.Find("PauseSession")?.GetComponent<UnityEngine.UI.Button>();
            if (pause != null) { pause.onClick.AddListener(TogglePause); pauseLabel = pause.GetComponentInChildren<TMP_Text>(); }
            transform.Find("BackToMenu")?.GetComponent<UnityEngine.UI.Button>().onClick.AddListener(BackToMenu);
        }
        public void TogglePause()
        {
            if (world == null) return;
            if (world.SpeedKmh > 0) { resumeSpeed = world.SpeedKmh; world.SpeedKmh = 0; }
            else world.SpeedKmh = resumeSpeed > 0 ? resumeSpeed : 10;
            if (pauseLabel != null) pauseLabel.text = world.SpeedKmh > 0 ? "PAUSE" : "WEITER";
        }
        public void BackToMenu()
        {
            if (world != null) world.SpeedKmh = 0;
            SceneManager.LoadScene("RunnerMainMenu");
        }
    }
}
