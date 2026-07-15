using UnityEngine;

public static class CardGameStartConfig
{
    private static bool hasStarter = false;
    private static ulong starterClientId = 0UL;

    public static void SetStarter(ulong clientId)
    {
        hasStarter = true;
        starterClientId = clientId;
    }

    public static bool TryGetStarter(out ulong clientId)
    {
        clientId = starterClientId;
        return hasStarter;
    }

    public static void Clear()
    {
        hasStarter = false;
        starterClientId = 0UL;
    }
}
