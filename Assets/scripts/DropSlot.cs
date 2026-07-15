using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

public enum BoardSide { P1, P2 }

// Attach to each lane Image (P1_Lane_*, P2_Lane_*). Handles UI side only; forwards to CardUIManager.
public class DropSlot : MonoBehaviour, IDropHandler
{
    public BoardSide OwnerSide = BoardSide.P1;
    public int LaneIndex = 0; // 0..5

    public void OnDrop(PointerEventData eventData)
    {
        var drag = eventData.pointerDrag ? eventData.pointerDrag.GetComponent<CardDragHandler>() : null;
        if (drag == null) return;
        var ui = Object.FindFirstObjectByType<CardUIManager>();
        if (ui == null) return;
        ui.OnCardDroppedToSlot(drag, OwnerSide, LaneIndex);
    }
}
