using UnityEngine;
using UnityEngine.UI;

public class LaneCardClickable : MonoBehaviour
{
    private CardUIManager ui;
    private int laneIndex;

    public void Init(CardUIManager manager, int lane)
    {
        ui = manager;
        laneIndex = lane;
        // Ensure we have a raycastable Graphic
        var img = GetComponent<Image>();
        if (img == null)
        {
            img = gameObject.AddComponent<Image>();
            img.color = new Color(1,1,1,0); // invisible, but raycastable
            img.raycastTarget = true;
        }
        var btn = GetComponent<Button>();
        if (btn == null) btn = gameObject.AddComponent<Button>();
        if (btn.targetGraphic == null) btn.targetGraphic = img;
        btn.onClick.RemoveAllListeners();
        btn.onClick.AddListener(OnClicked);
    }

    private void OnClicked()
    {
        if (ui != null)
        {
            ui.OnLaneCardClicked(laneIndex);
        }
    }
}
