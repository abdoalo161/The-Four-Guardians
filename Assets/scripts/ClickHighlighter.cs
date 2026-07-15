using UnityEngine;
using UnityEngine.UI;

public class ClickHighlighter : MonoBehaviour
{
    [System.Serializable]
    public class Item { public Button button; public Image highlight; }

    public Item[] items;
    public int selectedIndex = -1;

    void Awake()
    {
        for (int i = 0; i < items.Length; i++)
        {
            int idx = i;
            if (items[i]?.button != null)
                items[i].button.onClick.AddListener(() => Select(idx));
            if (items[i]?.highlight != null)
                items[i].highlight.enabled = false;
        }
    }

    public void Select(int index)
    {
        for (int i = 0; i < items.Length; i++)
            if (items[i]?.highlight != null)
                items[i].highlight.enabled = (i == index);
        selectedIndex = index;
    }

    public int GetSelectedIndex() => selectedIndex;
    public Button GetSelectedButton() => (selectedIndex >= 0 && selectedIndex < items.Length) ? items[selectedIndex].button : null;
}