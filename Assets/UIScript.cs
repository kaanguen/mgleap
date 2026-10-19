using UnityEngine;

public class UIScript : MonoBehaviour
{
    [SerializeField]
    private Canvas uiPanel;

    [SerializeField]
    private Camera mainCamera;

    [SerializeField]
    private TextMesh textMesh;
   void Start()
    {
        mainCamera = Camera.main;
        uiPanel.worldCamera = mainCamera;

    }


}