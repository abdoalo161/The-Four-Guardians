using UnityEngine;

public enum Element
{
    Fire = 0,
    Water = 1,
    Air = 2,
    Earth = 3
}

[CreateAssetMenu(fileName = "ElementTheme", menuName = "CardGame/Element Theme", order = 1)]
public class ElementTheme : ScriptableObject
{
    [Header("Sprites")]
    public Sprite Background;
    public Sprite SlotSprite;

    [Header("Optional Accents")]
    public Sprite SlotHighlight;
    public Color AccentColor = Color.white;
}
