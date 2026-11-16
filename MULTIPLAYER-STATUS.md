# Steam multiplayer — integração Android r9.10 / steam-p2p-v11

Esta edição integra o transporte P2P ao cliente de partida original do jogo. É uma implementação para validação no Android, não uma declaração de que uma partida completa já foi executada neste ambiente.

## O que está integrado

- Na tela de entrar em uma partida, há um campo SteamID64 e o botão Entrar na partida / Join game. Informe o amigo que está hospedando uma sala.
- O launcher reutiliza a autenticação já autorizada; launcher, nuvem e multiplayer compartilham uma sessão CM, com LoginID exclusivo, mantida durante a partida. Não é preciso manter um PC intermediário ligado.
- A descoberta da sala, JoinLobby, identidade Steam, certificado assinado, sinalização Steam e ICE são derivados do protótipo que conectou ao host real no teste do usuário.
- MobileSteamInitializer inicializa o NetClientGameService do jogo com PlatformType.Steam e um MobileSteamNetClient.
- Ao conectar, o adaptador chama OnConnectedToHost na thread do jogo. Os datagramas recebidos seguem para OnPacketReceived, preservando o remetente, a confiabilidade e o canal.
- HandshakeManager, NetMessageBus, JoinFlow e os manipuladores de gameplay continuam sendo os do jogo. Versão, hash de modelos e lista de mods são conferidos pelo protocolo original, sem respostas fabricadas ou verificações desativadas.
- As respostas e ações produzidas pelo jogo são enviadas pelo transporte GNS. A desconexão sai do lobby e limpa a atividade Steam, preservando a sessão da conta.
- Nomes e identidade dos jogadores Steam são resolvidos pela sessão autenticada. O multiplayer LAN continua disponível.

## Instalação

Mantenha original.apk e as ferramentas Java usadas anteriormente em Downloads. Com o celular conectado por ADB, execute REBUILD-APK.ps1 -UsePrebuilt -Install. Essa opção usa a DLL precompilada incluída e dispensa as referências de desenvolvimento do jogo. Não desinstale o aplicativo.

O script confere os hashes da DLL, da biblioteca ARM64 e dos arquivos dentro do APK assinado. A inicialização precisa mostrar [build=mobile-steam-p2p-v11] e o registro da integração. O rodapé mostra r9.10 · Steam P2P.

No jogo, abra a tela de entrar em uma partida, informe o SteamID64 do host e toque em Entrar na partida. A conta cliente e a conta do host precisam ser diferentes, amigas na Steam e usar a mesma versão do jogo e mods compatíveis. Uma conta com apenas arquivos locais e sem sessão autenticada não conecta à Steam.

## Evidência disponível

- Teste real anterior do usuário, em Windows: JoinLobby Success, certificado Steam validado, transporte P2P Connected, 14 sinais recebidos/aceitos e um pacote real de 21 bytes vindo do host. Isso comprova a rota do protótipo naquela rede; não comprova a integração Android desta edição.
- Compilação da integração contra a sts2.dll fornecida pelo usuário, GodotSharp 4.5.1 e Harmony 2.4.2.
- Testes locais executam a DLL original de rede: serialização de handshake, recebimento da versão do host, envio da resposta confiável e transição de NetClientGameService para IsConnected. Incompatibilidades de versão, hash e mods são recusadas.
- O teste de linha de comando substitui apenas consultas/saída do motor Godot e a inicialização de telemetria por implementações de teste. Não altera a DLL do jogo nem a lógica de handshake. Os substitutos de teste não entram no APK.
- Testes do bridge cobrem sinalização protobuf, filtros, filas, presença, cancelamento e limpeza. O transporte nativo é exercitado localmente no Linux.

## Limites e validação pendente

- Não foi instalada nem executada esta integração em um Android durante o desenvolvimento. Não foi iniciada ou concluída uma partida Android ↔ PC aqui.
- A rota implementada é ICE direta; Steam Datagram Relay proprietário não está implementado. A conectividade pode falhar em redes que exigem relay.
- Esta edição permite entrar como cliente na sala de um amigo. Não implementa hospedagem Steam pelo celular, convites pelo overlay nem migração de host.
- Saves e funcionamento do jogo dependem dos componentes existentes; não foram usados credenciais ou arquivos pessoais de save nos testes locais.

No log do aparelho, TRANSPORTE_P2P_CONECTADO confirma somente o transporte. HANDSHAKE_STS2_APROVADO confirma o handshake original. Entrar no lobby do jogo, começar uma partida e jogar juntos são verificações adicionais necessárias.

## Reproduzir testes

Com a própria sts2.dll no caminho de referências descrito no projeto:

    dotnet run --project tests/SteamMultiplayer/SteamMultiplayer.csproj

Os testes não exigem login e não contatam amigos. O projeto SteamTestSentry é um substituto isolado de telemetria para o teste e nunca é referência do launcher.

A biblioteca nativa e seus avisos de licença ficam em native/steam-p2p. Sua implementação vem do GameNetworkingSockets e do wrapper do protótipo. SteamKit2 é resolvida pelo NuGet do projeto.

## Ajuste r9.1 de abertura

O usuário relatou fechamento ao abrir a r9. O registro Harmony do multiplayer foi retirado do bootstrap e movido para depois de GameStartup. Os adaptadores de nome/identidade agora interceptam métodos de instância da estratégia Steam, sem forçar o inicializador estático de PlatformUtil. A entrada Steam também é adicionada ao abrir a tela de partidas, pois ela pode já ter sido criada durante o carregamento. O launcher e as bibliotecas de referência mantêm os arquivos da base anterior.

Sem o log de falha do aparelho, não se afirma que a causa única do fechamento foi demonstrada. A revisão foi compilada e verificada localmente, mas continua sem validação no Android. CAPTURAR-ERRO.ps1 coleta uma nova abertura por ADB, incluindo libc/DEBUG/mono, para distinguir falhas nativas e gerenciadas.

## r9.2 — tela e diagnóstico de conexão

A tela usa um painel Steam separado da opção LAN. A descoberta UDP é pausada no modo Steam. O ID manual é preservado; não foi adicionada descoberta automática de amigos nesta revisão. A tela acompanha cada etapa e permite cancelar, abrir detalhes e copiar o diagnóstico sem conectar o celular ao PC.

A r9.1 convertia exceções diferentes em SecureConnectionFailed, fazendo o jogo exibir uma sugestão genérica sobre o relógio. Essa conversão foi retirada. Somente a falha de conexão deste adaptador/host é apresentada no painel próprio; as validações originais de versão, modelos e mods continuam ativas. Os detalhes são limitados em tamanho e removem o refresh token antes de copiar ou registrar.

A biblioteca ARM64 é verificada antes do login e carregada pelo caminho absoluto retornado pelo Android (ApplicationInfo.nativeLibraryDir). O script insere um método Java/smali na atividade do APK para expor esse caminho. A rotina passou por montagem/desmontagem em APK de teste; não foi executada em aparelho nesta sessão.

Foram renderizados e conferidos os estados normal, conectando, erro e detalhes no Godot 4.5.1. Testes de interação verificaram troca Steam/LAN, bloqueio da troca durante a conexão, envio do ID, cancelamento, cópia e fechamento dos detalhes. Testes do relatório verificaram a etapa/erro real, remoção de tokens, supressão do popup limitada ao host correto e histórico limitado.

As imagens do diretório Previews/multiplayer-* são cenários locais de interface, inclusive erros simulados, e não diagnósticos extraídos do aparelho do usuário. A captura enviada pelo usuário não revela a exceção original; não se atribui uma causa definitiva ao erro de conexão sem esse diagnóstico. Uma partida Android ↔ PC continua pendente de validação real.

## r9.3 — persistência do login

O diagnóstico do aparelho confirmou carregamento da biblioteca/ABI Android e conexão com o CM, mas recusou steam-login com AccessDenied. Não chegou ao lobby, à emissão de certificado nem à conexão P2P nessa tentativa.

Foi encontrada uma inconsistência de configuração: SteamAuth emite a credencial com IsPersistentSession=true, enquanto o adaptador multiplayer usava ShouldRememberPassword=false, herdado do protótipo de login temporário. A documentação XML da própria SteamKit2 3.4.0 instrui combinar os dois modos. A política agora centraliza PersistentLauncherSession e CreateLauncherLogOn para autenticação, nuvem e multiplayer. Credenciais temporárias do protótipo continuam com false. A senha não é introduzida no logon por token.

Um teste verifica a correspondência persistente, o caso temporário, preservação de conta/token/LoginID explícito, ausência de senha e proteção contra usar a própria conta como host. Não houve login em conta real nesta sessão; a correção da configuração foi validada localmente, mas a aceitação pela Steam no aparelho precisa ser repetida.

## r9.4 — sessão compartilhada e recuperação de autorização

O diagnóstico real da r9.3 continuou em AccessDenied. A alteração anterior de persistência não resolveu esse erro no aparelho. O acesso rápido do launcher pode aceitar uma credencial e verificação de propriedade em cache; isso não comprova um login CM ativo.

Launcher, downloads, nuvem e multiplayer passam a usar a mesma SteamConnection, com um único dono dos callbacks. O multiplayer toma uma referência à sessão autenticada e não envia outro LogOn quando ela está ativa. Sair da sala ou descartar uma operação de nuvem não encerra a sessão mantida pelo jogo. Somente uma autenticação interativa bem-sucedida pode trocar o token; consumidores antigos não conseguem restaurar o token recusado.

Se a credencial salva for recusada, o painel oferece Reconectar Steam. A janela reutiliza o fluxo existente de senha e Steam Guard, confirma login CM e propriedade do jogo antes de salvar a credencial renovada e tenta novamente o mesmo host. Mantém conta, arquivos e saves. Não repete automaticamente o login com a mesma credencial recusada. A senha não é salva.

Testes locais passaram para identidade compartilhada, concorrência, proteção contra encerramento pela nuvem, renovação de token, bloqueio de credencial antiga e logout explícito. A interface real foi renderizada no Godot; cancelamento da recuperação e controles do painel passaram. A compilação não substitui um teste de login Steam real no Android, ainda pendente.

## r9.5 — descoberta de rede Android

O log real da r9.4 passou por login, presença, lobby e instalação de certificado, chegando ao erro de rendezvous P2P. Na revisão upstream usada pela biblioteca, GetLocalAddresses excluía Android do ramo POSIX e retornava false no ramo não implementado. GatherInterfaces do ICE usa essa função; a exclusão impede a descoberta local de candidatos. Isso é um defeito concreto do transporte distribuído, embora não demonstre que seja a única limitação da rota do usuário.

O binário ARM64 foi recompilado com enumeração bionic habilitada, API 26, runtime C++/protobuf/OpenSSL estáticos e páginas de 16 KB. A ABI C continua 4; a revisão de transporte passa a 5, conferida no Android para impedir uso da biblioteca anterior. O script de reconstrução exige o novo SHA256 e verifica a cópia no APK. Fontes do wrapper, patch e instruções de reprodução ficam em native/steam-p2p/source.

O relatório inclui native-revision, local-addresses, estados e motivos P2P, sinais enviados, mensagens CM observadas/filtradas e sinais aceitos/recusados pela biblioteca. A consulta de endereços não inclui IPs no relatório. Uma falha de enumeração é apresentada antes de solicitar certificado/conectar, sem aguardar o timeout.

Passaram testes do parser nativo distribuído com lista de interfaces controlada (IPv4/IPv6 aceitos; loopback/link-local/ausentes ignorados) e falha de enumeração preservada. Passaram roundtrip local de pacotes, reinicialização, certificado obrigatório, assinatura falsa recusada, contexto único, presença, sinalização e limpeza. A consulta real à lista de interfaces foi bloqueada pelo sandbox de execução e indicada como SKIP; os cenários positivos do parser usam uma fixture de teste, fora do APK. Os imports bionic e alinhamento do ELF Android foram verificados. Não se afirma conexão Android ↔ host real neste ambiente, nem foi adicionado relay SDR.

## r9.6 — compatibilidade e atualização dos arquivos do jogo

No teste real do usuário com r9.5, a biblioteca encontrou três endereços locais; o P2P chegou a Connected com quatro sinais recebidos/aceitos e nenhum recusado. O jogo então recusou o handshake com VersionMismatch. Isso confirma o transporte naquele teste, mas ainda não uma partida iniciada. O relatório antigo omitia ConnectionFailureExtraInfo, que contém os dados dos dois lados; o código original usa VersionMismatch para versões distintas, hashes de modelos distintos ou handshake ilegível.

Esta revisão preserva e apresenta as versões reais, branches de runtime, hashes e listas de mods recebidos pelo HandshakeManager. O relatório também informa a branch selecionada para download. As mensagens diferenciam versão divergente, dados divergentes e protocolo ilegível. Nenhum campo do handshake é substituído e nenhuma validação é desativada.

Depois de baixar os arquivos, o launcher agora exige reinício, inclusive quando foi aberto a partir de GameStartupWrapper. Antes era possível liberar a continuação de GameStartup com sts2.dll/PCK antigos já carregados no processo. O botão passa a Reiniciar para jogar e chama a rotina Android de reinício já existente.

Mais opções → Verificar arquivos do jogo executa a rotina existente de download/verificação: ela consulta os manifests da branch escolhida, valida SHA-1 dos arquivos e repara divergências. O acesso está disponível mesmo quando o manifesto remoto não mudou; anteriormente a interface só oferecia baixar quando havia atualização. Durante a operação, as ações de iniciar outra transferência/jogar/trocar branch ficam bloqueadas. O progresso é exibido também quando a verificação parte de Mais opções.

Para alinhar versões: atualize os dois jogos pela Steam, usando a mesma branch estável/beta; no celular execute a verificação e depois Reiniciar para jogar. A versão r9.6 identifica o launcher, não a versão dos arquivos de STS2. Atualizar apenas o APK não atualiza os arquivos do jogo.

Passaram testes com a DLL original: handshake compatível conecta; versões divergentes, hashes divergentes, mods divergentes e magic ilegível são recusados e descritos corretamente. A interface de verificação foi renderizada no Godot e testada para visibilidade, bloqueio durante outra operação e evento de verificação único. A compilação passou sem avisos. Não houve execução desta revisão no Android; os números de versão do jogo no celular/host do usuário ainda não foram recebidos, portanto não se atribui uma diferença exata a esse caso.

## r9.7 — versão UNKNOWN e branch None no Android

O teste real da r9.6 revelou versão local UNKNOWN, branch None e hash de modelos 1748280997 igual ao host v0.113.1/PublicBeta. A seleção de download já era public-beta. A igualdade do hash de modelos não comprova, sozinha, que todos os arquivos/versões sejam iguais.

A origem do UNKNOWN foi rastreada no código original: NGame.GetGameVersion consulta ReleaseInfoManager; esse leitor procura release_info.json ao lado de OS.GetExecutablePath. No Android esse é o executável do aplicativo, enquanto DepotDownloader grava os arquivos em OS.GetDataDir()/game. A revisão acrescenta esse caminho à busca original, com precedência, e recria o leitor antes de GameStartup para descartar um resultado ausente que já tenha sido colocado em cache. O parser, a versão semântica e NGame.GetGameVersion continuam sendo os originais; não se atribui versão ao PeerVersionInfo e não se usa a versão do host como identidade local.

Branch None vinha de NullPlatformUtilStrategy, usada porque a inicialização Steam nativa de desktop é desativada no port. Após o startup, o método de branch dessa estratégia passa a resolver a branch dos metadados instalados. Uma verificação/download completo também registra a branch de distribuição verificada, vinculada ao SHA256 do release_info.json. Um marcador antigo não é aceito para um arquivo de release diferente. A preferência de download não é tratada como comprovação da branch instalada. Branch desconhecida permanece desconhecida.

Antes do login/P2P, o relatório registra a versão instalada e sua branch; se a versão continuar ausente/UNKNOWN, mostra uma orientação para verificar os arquivos e reiniciar, sem abrir outra negociação P2P. O transporte nativo e as validações do jogo permanecem os da revisão anterior.

Testes passaram para precedência de caminhos, fallback desktop, ausência/corrupção de metadados, independência da preferência e vínculo/invalidação do marcador de branch. Um teste no Godot 4.5.1 carregou a DLL original de STS2: forçou o cache inicial UNKNOWN, criou um release_info.json local de fixture e confirmou que o leitor original passou a retornar v0.113.1, SemVer válido e PublicBeta. O diretório de dados desse teste é isolado por uma substituição de OS.GetDataDir apenas no processo de teste; não entra no APK. O teste usa somente o substituto isolado Sentry.Godot do projeto SteamTestSentry para evitar telemetria, sem substituir o leitor, os dados de versão ou o método de branch do jogo.

Para reproduzir esse teste Linux, compile tests/ReleaseMetadataPreview/ReleaseMetadataPreview.csproj com -m:1 e execute o Godot Mono 4.5.1 --headless --path tests/ReleaseMetadataPreview. Neste host, Harmony requer LD_PRELOAD=/lib/x86_64-linux-gnu/libgcc_s.so.1 para disponibilizar os símbolos de unwind ao helper nativo. A consulta no Android do usuário ainda precisa ser validada; os metadados reais do celular não foram recebidos nesta sessão.

## r9.8 — lista de salas, presença entre tentativas e encerramento P2P

O diagnóstico real da r9.7 já mostrou installed-game-version=v0.113.1. Na tentativa posterior, houve 42 sinais enviados e zero mensagens CM de resposta antes do timeout 5003. Isso é diferente da rota que chegou a Connected nos testes anteriores. O log não demonstra que o host saiu, nem prova por si só bloqueio de NAT, conta ou fechamento incorreto da conexão anterior.

A sessão compartilhada agora registra SteamFriendDirectory antes de conectar, retendo os snapshots iniciais e deltas de todos os amigos durante a vida da conexão. O gateway de cada tentativa usa esse estado inicial. Atualizações apenas de presença (inclusive Offline) não apagam o jogo/sala. Um anúncio recente preservado após dados temporariamente vazios é somente uma pista: o gateway consulta GetLobbyData e confere AppID, lobby e proprietário antes de reutilizá-lo. Troca explícita para outro jogo remove a pista. Se a Steam não confirmar a sala, a mensagem informa consulta inconclusiva, sem afirmar que o amigo parou de hospedar.

O painel acrescenta uma seleção por nome e Atualizar. Ao abrir a tela, consulta os amigos da conta autenticada pela SteamKit, pede os dados completos em lotes e procura anúncios de STS2. Consulta os metadados das salas com concorrência limitada, confirma o proprietário como amigo, elimina duplicatas e salas declaradas cheias. Refresh não entra em lobbies. Offline/invisível não é usado como filtro de disponibilidade; sem anúncio acessível e confirmação da Steam, a sala não pode ser descoberta. O campo SteamID64 permanece como alternativa. A seleção e o campo manual ficam sincronizados para evitar exibir um amigo e entrar no ID de outro.

O transporte ARM64 foi recompilado para revisão nativa 6 (ABI C permanece 4). A nova função sts_gns_begin_close permite encerrar a conexão mantendo contexto e filas vivos. O adaptador bombeia e encaminha sinais pendentes durante 200 ms antes de destruir a biblioteca e remover o handler de sinalização. A próxima tentativa aguarda essa limpeza. A rotina pode encaminhar fechamentos/acks; não confirma recebimento pelo host e não garante uma rota que a rede ou o host não ofereçam. A biblioteca anterior destruía as filas no Dispose antes desse encaminhamento.

O callback PlayingSessionState da Steam é registrado. Um bloqueio comunicado pela Steam por jogo da mesma conta em outro dispositivo é apresentado como tal. Ausência de resposta de sinalização tem mensagem própria. Nenhum desses estados é convertido em amigo offline. As assinaturas, certificados e checagens de versão permanecem ativos; não foi adicionado relay SDR.

Passaram testes de snapshots/deltas reais serializados SteamKit, retenção entre tentativas, pistas expiradas, troca de jogo, isolamento dos amigos e reset de sessão. Passaram os testes existentes de sinalização, certificados, lifetime, lobby e sessão compartilhada. A UI real foi renderizada e testada para seleção de amigo, sincronização do ID manual, lista vazia sem apagar o ID, bloqueio durante conexão e cancelamento/detalhes. O ELF ARM64 conserva alinhamento 16 KB e exporta a nova função; os hashes do script e pacote foram atualizados. Não houve descoberta de amigos nem conexão com o host real usando credenciais neste ambiente: isso continua dependente da validação no aparelho.

Nesta revisão SteamConnection acompanha o estado autenticado explicitamente: LoggedOff recebido do servidor invalida o login compartilhado e permite renovação da conexão, mesmo se o CM ainda mantiver um socket. A confirmação enfileirada de um LogOff enviado pelo próprio Teardown não é tratada como falha da nova tentativa. Um teste percorre o parser/callback SteamKit real de ClientLoggedOff/LoggedInElsewhere e confere a invalidação. Isso corrige o gerenciamento de sessão, sem atribuir a ele a causa comprovada do timeout do usuário.

## r9.9 — interface, host e escala gráfica

Salas automáticas em cartões grandes; ID manual em diálogo opcional. Host Steam usa a sessão compartilhada, certificado Valve validado, lobby friends-only e até três clientes. Publica após iniciar listener, valida identidade e associação ao lobby e preserva o handshake original. Não há relay SDR.

Transporte revisão 7: compilado para ARM64 Android e Linux; self-test local com três pares de sockets em memória e UDP. Registro dos patches nos fluxos originais de criar/retomar validado em Godot. Estes testes não demonstram hospedagem Steam real nem partida completa; testar celular como host com amigo no PC.

Resolução 50–100% com ampliação linear: testes Godot confirmaram buffers 640x288, 960x432 e 1280x576 com saída 1280x576 e mesmos alvos de toque. Configuração persistente; padrão100%. Não foi medido FPS em celulares.

## r9.10 — prazo de certificado e escala 10–100%

SteamKit AsyncJob cancela por padrão em dez segundos; WaitAsync(25s) não alterava esse prazo. O pedido agora configura Timeout=30s no próprio job antes de aguardar, com uma repetição limitada para timeout sem cancelamento do usuário. Teste com AsyncJob real e callbacks sintéticos confirma prazo, repetição, esgotamento, cancelamento e sessão desconectada. Nenhum certificado de teste é usado em produção.

O host cria lobby privado e confirma sua propriedade antes de solicitar o certificado; inicia listener e só então publica para amigos. Diagnóstico separa configuração, pedido e instalação do certificado. A nova ordem e a obtenção de certificado ainda precisam de validação na Steam real.

Escala ampliada para 10–100% em passos de 5%, persistência e padrão 100% mantidos. Teste Godot confirmado: 10%=128x58 para saída1280x576, coordenadas lógicas e clique preservados; 50/75/100% também aprovados. Em 10% textos ficam bastante desfocados.
