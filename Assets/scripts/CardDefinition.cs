using UnityEngine;

[CreateAssetMenu(fileName = "CardDefinition", menuName = "CardGame/Card Definition", order = 1)]
public class CardDefinition : ScriptableObject
{
    public int Id; // unique integer used in network messages
    public string DisplayName;
    public int Attack = 1;
    public int Health = 1;
    public int TributeCost = 0;
    public Sprite Artwork; // optional, for future UI
}
