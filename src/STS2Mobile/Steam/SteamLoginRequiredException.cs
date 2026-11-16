using SteamKit2;

namespace STS2Mobile.Steam;

public sealed class SteamLoginRequiredException : IOException
{
    public EResult Result { get; }
    public SteamLoginRequiredException(EResult result)
        : base("A Steam precisa renovar o login desta conta (" + result + "). Use Reconectar Steam.") => Result = result;
}
