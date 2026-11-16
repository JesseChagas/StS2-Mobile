namespace STS2Mobile.Steam.Multiplayer;
internal static class SteamTransportOwnership
{
    private static int active;
    public static bool TryClaim() => Interlocked.CompareExchange(ref active, 1, 0) == 0;
    public static void Release() => Interlocked.Exchange(ref active, 0);
}
