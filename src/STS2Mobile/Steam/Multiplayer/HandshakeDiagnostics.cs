#nullable enable
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;

namespace STS2Mobile.Steam.Multiplayer;

// Describe the game's original result. Never change peer versions, model hashes,
// packets, or the outcome of HandshakeManager's compatibility checks.
internal static class HandshakeDiagnostics
{
    private static string Value(string? text) => string.IsNullOrWhiteSpace(text) ? "?" : text.Replace('\n', ' ').Replace('\r', ' ')[..Math.Min(text.Length, 120)];
    private static string Mods(List<string>? names) => names == null ? "<none>" : string.Join(", ", names.Take(20).Select(Value));

    public static string Describe(NetErrorInfo info, bool en)
    {
        var extra = info.ConnectionExtraInfo;
        if (extra == null) return en ? $"Game compatibility check failed: {info.GetReason()}. The host did not provide version details."
            : $"A compatibilidade do jogo foi recusada: {info.GetReason()}. O host não forneceu detalhes de versão.";
        var local = extra.localInfo; var remote = extra.remoteInfo;
        if (info.GetReason() == NetError.VersionMismatch) {
            if (string.IsNullOrWhiteSpace(remote.version) || remote.version.StartsWith('<'))
                return en ? "The host's handshake could not be read. Check that both games use the same version."
                    : "Não foi possível ler o handshake do host. Confira se os dois jogos usam a mesma versão.";
            if (local.version != remote.version)
                return en ? $"Different game versions: phone {Value(local.version)}; host {Value(remote.version)}. Update the game and use the same stable/beta version on both devices."
                    : $"Versões do jogo diferentes: celular {Value(local.version)}; host {Value(remote.version)}. Atualize o jogo e use a mesma versão estável/beta nos dois.";
            if (local.idDatabaseHash != remote.idDatabaseHash)
                return en ? $"Game data differs despite matching version {Value(local.version)}. Update/verify both games and check gameplay mods."
                    : $"Os dados do jogo diferem, mesmo com a versão {Value(local.version)} igual. Atualize/verifique os arquivos nos dois e confira os mods de gameplay.";
        }
        if (info.GetReason() == NetError.ModMismatch)
            return en ? "Gameplay mods differ. Use the same gameplay mods on the phone and host."
                : "Os mods de gameplay são diferentes. Use os mesmos mods no celular e no host.";
        return en ? $"Game compatibility check failed: {info.GetReason()}. See the connection details."
            : $"A compatibilidade do jogo foi recusada: {info.GetReason()}. Confira os detalhes da conexão.";
    }

    public static string Details(NetErrorInfo info)
    {
        if (info.ConnectionExtraInfo is not { } extra) return "compatibility=" + info.GetReason() + "; peer-details=unavailable";
        return "compatibility=" + info.GetReason() + "\n" + Peer("local", extra.localInfo) + "\n" + Peer("host", extra.remoteInfo);
    }

    private static string Peer(string label, PeerVersionInfo version) =>
        $"{label}-game-version={Value(version.version)}; branch={version.branch}; model-hash={version.idDatabaseHash}; gameplay-mods=[{Mods(version.gameplayAffectingMods)}]; other-mods=[{Mods(version.otherMods)}]";
}
