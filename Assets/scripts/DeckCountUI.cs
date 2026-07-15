using TMPro;
using UnityEngine;

public class DeckCountUI : MonoBehaviour
{
    [SerializeField] private TMP_Text deckCountText;

    private int lastValue = -1;

    private void Awake()
    {
        // Try to auto-resolve a TMP_Text if not assigned
        if (deckCountText == null)
        {
            deckCountText = GetComponent<TMP_Text>();
            if (deckCountText == null)
            {
                deckCountText = GetComponentInChildren<TMP_Text>(true);
            }
        }
        if (deckCountText != null && lastValue >= 0)
        {
            deckCountText.text = lastValue.ToString();
        }
    }

    public void SetDeckCount(int value)
    {
        lastValue = Mathf.Max(0, value);
        if (deckCountText != null)
        {
            deckCountText.text = lastValue.ToString();
        }
    }
}
