using UnityEngine;

public struct CardStats
{
    public int Atk;
    public int Hp;
    public int TributeCost;
}

public static class CardRules
{
    public static CardStats GetStats(int cardId)
    {
        var atk = 1 + (cardId % 5);
        var hp = 1 + ((cardId / 5) % 5);
        var tribute = (cardId % 6 == 5) ? 1 : 0; // simple placeholder rule
        return new CardStats { Atk = atk, Hp = hp, TributeCost = tribute };
    }
}
