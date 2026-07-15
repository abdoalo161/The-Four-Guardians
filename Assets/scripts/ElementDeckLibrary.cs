using UnityEngine;

[CreateAssetMenu(fileName = "ElementDeckLibrary", menuName = "CardGame/Element Deck Library", order = 2)]
public class ElementDeckLibrary : ScriptableObject
{
    [Header("Assign card definitions that form each element's deck")]
    public CardDefinition[] FireDeck;
    public CardDefinition[] WaterDeck;
    public CardDefinition[] AirDeck;
    public CardDefinition[] EarthDeck;

    public CardDefinition[] GetDeck(Element element)
    {
        switch (element)
        {
            case Element.Fire: return FireDeck;
            case Element.Water: return WaterDeck;
            case Element.Air: return AirDeck;
            case Element.Earth: return EarthDeck;
            default: return null;
        }
    }
}
