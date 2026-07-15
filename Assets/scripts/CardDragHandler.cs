using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public class CardDragHandler : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    public int CardId;
    public bool WasDropped = false; // set true when a slot accepts this drop

    [HideInInspector] public Transform OriginalParent;
    private RectTransform rect;
    private CanvasGroup canvasGroup;
    private LayoutElement layoutElement;
    private Transform dragLayer;

    private void Awake()
    {
        rect = GetComponent<RectTransform>();
        canvasGroup = GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();
        layoutElement = GetComponent<LayoutElement>();
        if (layoutElement == null) layoutElement = gameObject.AddComponent<LayoutElement>();
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        WasDropped = false;
        OriginalParent = transform.parent;
        var ui = Object.FindFirstObjectByType<CardUIManager>();
        dragLayer = ui != null ? ui.GetDragLayer() : null;
        if (ui != null) ui.OnBeginCardDrag();
        if (ui != null) ui.SetCardDragHighlight(gameObject, true);
        if (dragLayer != null)
        {
            transform.SetParent(dragLayer, true);
        }
        canvasGroup.blocksRaycasts = false;
        layoutElement.ignoreLayout = true;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (rect == null) return;
        rect.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        var ui = Object.FindFirstObjectByType<CardUIManager>();
        if (ui != null) ui.OnEndCardDrag();
        if (ui != null) ui.SetCardDragHighlight(gameObject, false);
        if (WasDropped)
        {
            // Slot accepted: snap back under original parent and hide to avoid floating ghost
            canvasGroup.blocksRaycasts = true;
            if (OriginalParent != null)
            {
                transform.SetParent(OriginalParent, false);
                layoutElement.ignoreLayout = false;
                rect.anchoredPosition = Vector2.zero;
            }
            gameObject.SetActive(false);
            return; // UI will rebuild on server confirmation
        }
        canvasGroup.blocksRaycasts = true;
        // If still under dragLayer, snap back to original parent
        if (dragLayer != null && transform.parent == dragLayer && OriginalParent != null)
        {
            transform.SetParent(OriginalParent, false);
            layoutElement.ignoreLayout = false;
            rect.anchoredPosition = Vector2.zero;
        }
    }
}
