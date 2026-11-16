using System;
using System.Collections.Generic;
using System.Globalization;

namespace STS2Mobile.Launcher.Design;

public static class LauncherText
{
    private static readonly Dictionary<string, string> Portuguese = new()
    {
        ["Sign out"] = "Sair da conta",
        ["Signing out..."] = "Saindo da conta…",
        ["Sign out of this device? Local saves and backups will be kept. The app will restart."] =
            "Sair da conta neste celular? Seus saves locais e backups serão mantidos. O app será reiniciado.",
        ["Delete downloaded game files: no"] = "Apagar o jogo baixado: não",
        ["Delete downloaded game files: yes"] = "Apagar o jogo baixado: sim",
        ["Could not sign out. Your local saves are safe. Please try again."] =
            "Não foi possível sair da conta. Seus saves locais estão preservados. Tente novamente.",
        ["Starting game..."] = "Abrindo o jogo…",
        ["Loading your game"] = "Carregando seu jogo",
        ["Library"] = "Biblioteca",
        ["Sign in."] = "Entre na sua conta.",
        ["Slay the Spire 2"] = "Slay the Spire 2",
        ["Your game. Ready when you are."] = "Seu jogo. Pronto quando você estiver.",
        ["Use your Steam account to access the game you own."] =
            "Use sua conta Steam para acessar o jogo da sua biblioteca.",
        ["Your account"] = "Sua conta",
        ["Getting ready"] = "Preparando tudo",
        ["Ready to play"] = "Pronto para jogar",
        ["Game files"] = "Arquivos do jogo",
        ["Welcome back, {0}"] = "Bem-vindo de volta, {0}",
        ["Logged in as {0}"] = "Conectado como {0}",
        ["Initializing..."] = "Iniciando…",
        ["Enter your Steam credentials"] = "Entre com sua conta Steam.",
        ["Steam username"] = "Nome de usuário Steam",
        ["Password"] = "Senha",
        ["Sign in"] = "Entrar",
        ["Enter your username and password."] = "Preencha o usuário e a senha.",
        ["Show password"] = "Mostrar senha",
        ["Hide password"] = "Ocultar senha",
        ["Your password is not stored by this launcher."] = "Sua senha não é salva pelo launcher.",
        ["Connecting to Steam..."] = "Conectando à Steam…",
        ["Authenticating..."] = "Verificando sua conta…",
        ["Verifying game ownership..."] = "Verificando o jogo na sua biblioteca…",
        ["Approve this sign-in in the Steam app on your phone."] =
            "Aprove este acesso no aplicativo Steam do seu celular.",
        ["Steam Guard code"] = "Código Steam Guard",
        ["Enter Steam Guard code"] = "Digite o código do Steam Guard.",
        ["Enter the code sent to your email."] = "Digite o código enviado ao seu e-mail.",
        ["That code was incorrect. Try the latest code."] =
            "Esse código está incorreto. Tente o código mais recente.",
        ["Verify code"] = "Verificar código",
        ["Verifying code..."] = "Verificando o código…",
        ["Incorrect username or password. Please try again."] =
            "Usuário ou senha incorretos. Tente novamente.",
        ["Sign-in was declined in Steam. Please try again."] =
            "O acesso foi recusado na Steam. Tente novamente.",
        ["Too many sign-in attempts. Wait a moment and try again."] =
            "Muitas tentativas de acesso. Aguarde um pouco e tente novamente.",
        ["Could not connect to Steam. Check your internet connection."] =
            "Não foi possível conectar à Steam. Verifique sua internet.",
        ["Sign-in failed. Please try again or open connection details."] =
            "Não foi possível entrar. Tente novamente ou veja os detalhes da conexão.",
        ["You don't own Slay the Spire 2. Purchase on Steam to play."] =
            "Slay the Spire 2 não está nesta conta. É preciso ter o jogo na Steam.",
        ["Connection details"] = "Detalhes da conexão",
        ["Hide connection details"] = "Ocultar detalhes da conexão",
        ["Dark theme"] = "Tema escuro",
        ["Light theme"] = "Tema claro",
        ["More options"] = "Mais opções",
        ["Fewer options"] = "Menos opções",
        ["Auto Sync: on"] = "Auto Sync: ativo",
        ["Auto Sync: off"] = "Auto Sync: desativado",
        ["Local backup: on"] = "Backup local: ativo",
        ["Local backup: off"] = "Backup local: desativado",
        ["Pull from cloud"] = "Baixar da nuvem",
        ["Push to cloud"] = "Enviar à nuvem",
        ["Check updates"] = "Buscar atualizações",
        ["Verify game files"] = "Verificar arquivos do jogo",
        ["Play"] = "Jogar",
        ["Restart to play"] = "Reiniciar para jogar",
        ["Try again"] = "Tentar novamente",
        ["Version: stable"] = "Versão: estável",
        ["Version: beta"] = "Versão: beta",
        ["Download game"] = "Baixar jogo",
        ["Download update"] = "Baixar atualização",
        ["Retry download"] = "Tentar baixar novamente",
        ["Preparing download..."] = "Preparando o download…",
        ["Downloading your game"] = "Baixando seu jogo",
        ["Download complete. Restart the app to play."] =
            "Download concluído. Reinicie o aplicativo para jogar.",
        ["Download complete. Your game is ready."] = "Download concluído. Seu jogo está pronto.",
        ["Download failed. Check your connection and try again."] =
            "O download falhou. Verifique a conexão e tente novamente.",
        ["Download cancelled"] = "Download cancelado",
        ["Update available"] = "Atualização disponível",
        ["Checking..."] = "Verificando…",
        ["Up to date"] = "Tudo atualizado",
        ["Check failed"] = "Falha na verificação",
        ["Could not check for updates. Try again."] =
            "Não foi possível buscar atualizações. Tente novamente.",
        ["No connection. You can use your saved sign-in."] =
            "Sem conexão. Você pode usar seu acesso salvo.",
        ["Internet is required for the first sign-in."] =
            "É preciso ter internet no primeiro acesso.",
        ["Pull cloud saves to this device?\nYour local saves will be replaced."] =
            "Baixar os saves da nuvem para este aparelho?\nSeus saves locais serão substituídos.",
        ["Send local saves to the cloud?\nYour cloud saves will be replaced."] =
            "Enviar os saves locais para a nuvem?\nSeus saves na nuvem serão substituídos.",
        ["Downloading cloud saves..."] = "Baixando os saves da nuvem…",
        ["Queueing cloud upload..."] = "Preparando o envio à nuvem…",
        ["Cloud saves downloaded."] = "Saves da nuvem baixados.",
        ["Saves queued for cloud upload."] = "Saves preparados para envio à nuvem.",
        ["Some saves could not be transferred. Please try again."] =
            "Alguns saves não foram transferidos. Tente novamente.",
        ["Cloud transfer failed. Check your connection and try again."] =
            "A transferência falhou. Verifique sua conexão e tente novamente.",
        ["No cloud saves were found."] = "Nenhum save foi encontrado na nuvem.",
        ["Preparing your first run"] = "Preparando sua primeira partida",
        ["Scanning game resources..."] = "Verificando os recursos do jogo…",
        ["Optimizing graphics..."] = "Otimizando os gráficos…",
        ["This may take a few minutes. Keep the app open."] =
            "Isso pode levar alguns minutos. Mantenha o aplicativo aberto.",
        ["Ready."] = "Tudo pronto.",
        ["Scanning scenes: {0} / {1}"] = "Verificando cenas: {0} / {1}",
        ["Optimizing: {0} / {1}"] = "Otimizando: {0} / {1}",
        ["Resources found: {0}"] = "Recursos encontrados: {0}",
        ["Graphics prepared in {0}s"] = "Gráficos preparados em {0}s",
        ["About & credits"] = "Sobre e créditos",
        ["Cancel"] = "Cancelar",
        ["Continue"] = "Continuar",
        ["Close"] = "Fechar",
        ["Unofficial launcher. Not affiliated with Mega Crit or Valve."] =
            "Launcher não oficial. Sem vínculo com a Mega Crit ou a Valve.",
        ["Auto Sync keeps your saves in sync when the game opens."] =
            "O Auto Sync sincroniza seus saves ao abrir o jogo.",
        ["Keep your Steam account secure. Never share your Guard code."] =
            "Proteja sua conta Steam. Não compartilhe seu código Guard.",
        ["An upstream launcher update is available. Review it in More options."] =
            "Há uma atualização do projeto original. Veja em Mais opções.",
        ["Original launcher"] = "Launcher original",
        ["Original project & licenses"] = "Projeto original e licenças",
    };

    public static string Get(string key, params object[] args)
    {
        var value =
            LauncherPreferences.Language == "pt" && Portuguese.TryGetValue(key, out var translated)
                ? translated
                : key;
        if (args.Length == 0)
            return value;
        // Some custom Android Mono distributions run with invariant globalization.
        // Translation must still work even if locale formatting data is absent.
        var culture = CultureInfo.InvariantCulture;
        if (LauncherPreferences.Language == "pt")
        {
            try
            {
                culture = CultureInfo.GetCultureInfo("pt-BR");
            }
            catch (CultureNotFoundException) { }
        }
        return string.Format(culture, value, args);
    }

    public static string FriendlyError(string raw)
    {
        raw ??= "";
        if (
            raw.Contains("InvalidPassword", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("InvalidAccountName", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("AccountNotFound", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("incorrect password", StringComparison.OrdinalIgnoreCase)
        )
            return "Incorrect username or password. Please try again.";
        if (
            raw.Contains("TwoFactorCodeMismatch", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("InvalidLoginAuthCode", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("ExpiredLoginAuthCode", StringComparison.OrdinalIgnoreCase)
        )
            return "That code was incorrect. Try the latest code.";
        if (
            raw.Contains("RateLimit", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("TooMany", StringComparison.OrdinalIgnoreCase)
        )
            return "Too many sign-in attempts. Wait a moment and try again.";
        if (
            raw.Contains("Denied", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("declined", StringComparison.OrdinalIgnoreCase)
        )
            return "Sign-in was declined in Steam. Please try again.";
        if (raw.Contains("don't own", StringComparison.OrdinalIgnoreCase))
            return "You don't own Slay the Spire 2. Purchase on Steam to play.";
        if (
            raw.Contains("connect", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("timeout", StringComparison.OrdinalIgnoreCase)
            || raw.Contains("timed out", StringComparison.OrdinalIgnoreCase)
        )
            return "Could not connect to Steam. Check your internet connection.";
        return "Sign-in failed. Please try again or open connection details.";
    }
}
