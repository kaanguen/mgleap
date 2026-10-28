using TMPro;
using UnityEngine;

namespace StadionTracking
{
    /// <summary>Headset-editable connection setup, shown before opening a server connection.</summary>
    public sealed class RunnerServerSetup : MonoBehaviour
    {
        private RunnerServerClient client;
        private TMP_InputField address;
        private TMP_Text status;
        private UnityEngine.UI.Button connect;

        public static RunnerServerSetup Create(RunnerServerClient client, string initialAddress, string error)
        {
            var canvas = RunnerUiTheme.Panel("ServerSetupCanvas", 650);
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(canvas.gameObject, client.gameObject.scene);
            canvas.GetComponent<RectTransform>().sizeDelta = new Vector2(580, 650);
            canvas.GetComponent<RunnerArPanel>().Priority = 100;
            var setup = canvas.gameObject.AddComponent<RunnerServerSetup>();
            setup.client = client;
            setup.Build(initialAddress, error);
            return setup;
        }

        private void Build(string initialAddress, string error)
        {
            RunnerUiTheme.Text("Title", transform, "MIT SERVER VERBINDEN", 24, 20, 532, 42, 26, true);
            RunnerUiTheme.Text("Hint", transform, "WLAN-Adresse des Rechners eingeben.\nZum Beispiel: 192.168.178.120", 24, 68, 532, 54, 19);
            var field = RunnerUiTheme.Rect("ServerAddress", transform, 24, 138, 532, 54);
            var background = field.gameObject.AddComponent<UnityEngine.UI.Image>();
            background.color = new Color(1, 1, 1, .12f);
            var viewport = RunnerUiTheme.Rect("Viewport", field, 12, 0, 508, 54);
            viewport.gameObject.AddComponent<UnityEngine.UI.RectMask2D>();
            var text = RunnerUiTheme.Text("Text", viewport, "", 0, 0, 508, 54, 22);
            address = field.gameObject.AddComponent<TMP_InputField>();
            address.textViewport = viewport;
            address.textComponent = text;
            address.targetGraphic = background;
            address.lineType = TMP_InputField.LineType.SingleLine;
            address.richText = false;
            address.characterLimit = 256;
            address.text = initialAddress ?? "";
            address.onSubmit.AddListener(_ => Connect());

            string[] keys = { "1", "2", "3", "4", "5", "6", "7", "8", "9", ".", "0", ":" };
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                AddButton("Key" + i, key, 24 + i % 3 * 180, 208 + i / 3 * 52, 172,
                    () => address.text += key, 44);
            }
            AddButton("Clear", "LEEREN", 24, 420, 262, () => address.text = "", 44);
            AddButton("Backspace", "ZURÜCK", 294, 420, 262, () => {
                if (address.text.Length > 0) address.text = address.text.Substring(0, address.text.Length - 1);
            }, 44);
            status = RunnerUiTheme.Text("Status", transform, "", 24, 472, 532, 108, 17);
            status.overflowMode = TextOverflowModes.Ellipsis;
            connect = AddButton("Connect", "VERBINDEN", 24, 590, 532, Connect, 48);
            SetStatus(string.IsNullOrEmpty(error) ? "Adresse wird auf dieser Brille gespeichert." : error,
                !string.IsNullOrEmpty(error));
        }

        private UnityEngine.UI.Button AddButton(string name, string title, float x, float y, float width,
            UnityEngine.Events.UnityAction action, float height)
        {
            var button = RunnerUiTheme.Button(name, transform, title, x, y, width, action, height);
            var label = button.GetComponentInChildren<TMP_Text>();
            label.fontSize = 19;
            label.characterSpacing = 0;
            return button;
        }

        private void Connect()
        {
            if (!client.ConnectToServer(address.text, out string error)) SetStatus(error, true);
            else SetStatus("Verbinde mit " + client.ServerAddress + " …", false);
        }

        public void SetStatus(string message, bool error)
        {
            gameObject.SetActive(true);
            status.text = message;
            status.color = error ? RunnerUiTheme.Warning : RunnerUiTheme.Label;
            connect.interactable = true;
        }

    }
}
