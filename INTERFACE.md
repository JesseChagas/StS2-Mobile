# STS 2 Mobile (unofficial) by Jessé chagas

Autoria e contato: https://linktr.ee/Jessechagasdev

## Interface desta edição

- Identificação no Android, no bloco principal e no rodapé: STS 2 Mobile (unofficial) by Jessé chagas. O nome no bloco principal e no rodapé abre o link do criador.
- Tema inspirado no cliente Steam: fundo azul-acinzentado, painéis escuros com bordas, botões retangulares, destaque azul e botão Jogar verde. Sem logotipo na interface; ícone nativo fan-made com bufão de capa e splash discreto.
- Botões para tema claro/escuro e português/inglês. As preferências são salvas no aparelho. A troca é imediata e mantém os campos de login preenchidos.
- Login com mensagens específicas para usuário/senha incorretos, confirmação no aplicativo Steam, código por e-mail, código Steam Guard incorreto, falha de conexão e excesso de tentativas. Os avisos de autenticação vêm de eventos do autenticador, não da interpretação dos textos do console.
- Console técnico recolhido em “Detalhes da conexão”, disponível somente no login. O painel principal, o download e as telas de preparação não exibem console.
- Tela principal: Auto Sync, Baixar da nuvem, Buscar atualizações e Jogar. Versão estável/beta permanece no cabeçalho do painel. Backup local, envio manual, acesso à nuvem já recolhido e créditos ficam em Mais opções.
- Se Auto Sync estiver ativo e houver confirmação de importação/sincronização de dados da nuvem para a conta atual, Baixar da nuvem passa para Mais opções. Ao desligar Auto Sync, volta à área principal. O registro é separado por conta. Transferência manual parcial ou com falha não cria uma confirmação nova.
- Download com porcentagem e tamanho transferido. Preparação de shaders e abertura do jogo têm telas próprias, também com temas e idiomas.
- Confirmações para substituir saves continuam presentes. Operações manuais de nuvem impedem iniciar o jogo enquanto a transferência está em andamento. Falhas de transferência são mostradas como falhas, e envio é descrito como enfileirado para upload.
- No primeiro download em modo bootstrap, o reinício para carregar as bibliotecas do jogo continua necessário. O botão informa “Reiniciar para jogar”; os controles completos aparecem após esse reinício.

## Logout e interface Steam (v8)

Em Mais opções → Sair da conta, o usuário escolhe se apaga os arquivos baixados. A opção começa em “não”. A confirmação encerra a sessão neste aparelho e reinicia o processo Android para remover as credenciais e os serviços antigos da memória. Não revoga sessões de outros dispositivos Steam.

Quando marcada, a exclusão remove somente game e .godot/mono/publish/arm64, no armazenamento privado do app. Os diretórios são renomeados antes da remoção para impedir que o próximo processo carregue um PCK ou DLL antiga. Saves em app_userdata, backups externos, mods e preferências permanecem. Os arquivos de credenciais e autorização de propriedade são removidos mesmo se o jogo for mantido. A próxima conta deve passar pela verificação de propriedade.

Uploads pendentes precisam terminar antes de sair. A contagem agora inclui o envio que o trabalhador já retirou da fila, mas ainda está executando. Um timeout impede o logout, em vez de interromper silenciosamente o upload.

A autoria foi transferida para o lugar de Biblioteca. Menu, autoria, rodapé, tema/idioma, créditos e ações foram ampliados. Os botões de volume foram removidos. Tema e idioma ficam abaixo dos nomes no bloco direito. O menu fica à esquerda, com recuo lateral adicional de 40 unidades de layout (além da margem de 40 da página). Apenas o menu rola; identidade e rodapé permanecem fixos. Os textos das ações ficaram maiores. Versão estável/beta tem borda visível. A frase promocional da jornada foi removida. O ícone é uma ilustração fan-made, sem vínculo oficial.

Testes novos: manter/apagar; cancelamento e confirmação do logout no Godot; exclusão de credenciais e marcador; preservação dos saves; rejeição de links simbólicos e reversão do rename; espera de upload ativo; recurso PNG compilado em APK mínimo. Não houve instalação deste pacote em um celular nesta sessão.

## Rolagem e versão no rodapé (v8)

O menu tem um ScrollContainer independente. DragScrollContainer observa o gesto antes dos botões consumirem a entrada, inicia a rolagem após o limiar vertical e cancela o clique pendente. Arrastar sobre Jogar, Auto Sync e as outras ações não executa a ação. Toques normais continuam funcionando. A barra de rolagem continua disponível. Confirmações abertas bloqueiam o arraste atrás da janela.

O rodapé mostra a versão fornecida pelo APK Android e a revisão desta interface, r8. No preview desktop, onde não existe um pacote Android instalado, aparece apenas App r8. O mesmo rodapé aparece nas telas de preparação e carregamento. O seletor estável/beta continua identificando a versão do jogo separadamente.

Verificação no Godot: eventos reais de mouse e de toque enviados ao viewport sobre um botão; deslocamento do menu confirmado; toque curto continua alternando Auto Sync; arraste não o alterna; identidade permanece fixa; modal bloqueia a rolagem atrás dele. Não houve execução desta revisão em um celular nesta sessão.

## Motor, linguagem e compatibilidade

O launcher já era escrito em C#. Godot é o motor, não uma linguagem. Esta edição mantém Godot/C# e o código Android Java para preservar a integração com o jogo, o carregamento do PCK e as bibliotecas nativas. Não é uma migração de motor.

O pacote Android continua com o identificador com.game.sts2launcher, permitindo atualizar a instalação existente e manter saves, credenciais e preferências. A ordenação de dependências e a atualização das DLLs corrigidas na revisão anterior foram preservadas. O modo de rede continua com o comportamento anterior; esta etapa é a reformulação visual solicitada antes do multiplayer Steam.

A política de comparação de progresso, leitura/escrita de saves, login Steam, verificação de propriedade e download dos depots permanece na base original. O acompanhamento de sucesso da nuvem foi acrescentado para apresentar os controles corretamente.

## Atualizar no Windows

1. Salve STS2-Mobile-JesseChagas.zip em Downloads.
2. Extraia substituindo Downloads\StS2-Launcher-main.
3. Com um celular conectado por USB e autorizado, execute:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\Downloads\StS2-Launcher-main\REBUILD-APK.ps1" -Install
```

O script usa original.apk, apktool.jar e uber-apk-signer.jar já existentes em Downloads ou subpastas. Exige Java, .NET SDK 9 e adb, como a compilação anterior.

Ele compila a DLL, modifica o cache no DEX, aplica nome/ícone/splash no manifesto e recursos, incorpora os arquivos, reconstrói, assina, instala como atualização e confirma a DLL carregada. A leitura do log usa compartilhamento com o processo que o grava. Não é preciso desinstalar nem apagar dados.

Marcador desta interface: [build=mobile-steam-ui-v8].

O pacote contém fontes e a DLL publicada. O APK real é reconstruído no computador do usuário a partir de original.apk; não há um APK completo pré-gerado aqui.

## Validação realizada

Revisão v8: publicação Release, renderizações no Godot das telas e idiomas, interações existentes e scripts de empacotamento. A validação Android real continua dependendo da instalação no aparelho. Os resultados de regressão abaixo foram obtidos nas revisões anteriores e os algoritmos envolvidos não foram alterados nesta edição visual.


- Build e publicação Release aprovados, sem erros e sem avisos.
- Interface renderizada no Godot Mono 4.5.1 com controles C# reais: principal escuro/claro, login PT/EN, erro de senha, aprovação Steam, código Guard, download, shaders, loading e Mais opções.
- Testes no próprio motor: tema/idioma ao vivo; campos de login preservados; console somente no login; botão de nuvem antes/depois da importação e com Auto Sync desligado; versão visível; abertura de Mais opções; isolamento dos registros por conta; descarte de inscrições ao fechar janelas e a interface.
- Nome Unicode e recursos Android verificados em um APK mínimo montado com aapt2/Apktool e decodificado de volta. Pacote Android preservado.
- Scripts PowerShell: sintaxe, correção do cache/smali, troca de versionCode, leitura concorrente do log e hash da DLL dentro do APK.
- Regressão da correção do jogo: construção de 1.867 modelos reais com IDs válidos, execução antecipada dos 13 inicializadores dependentes de modelos e aplicação real do prefixo Harmony.

As capturas em Previews foram renderizadas a partir dos componentes reais. Autenticação, download e nuvem foram representados por estados de demonstração; não houve uso de contas Steam nas capturas. Não foram realizadas autenticação de produção, transferências reais de saves nem a execução completa do APK Android desta interface no aparelho. Essa é a validação final a ser feita após instalar.

Teste isolado do logout e da fila de uploads: `dotnet run --project tests/LogoutTests/LogoutTests.csproj`.

## Créditos

Esta é uma personalização não oficial. O projeto original de Eky / Ekyso, os avisos MIT e os demais créditos estão preservados em LICENSE e THIRD_PARTY_LICENSES.md e na área Sobre e créditos. O nome do criador abre seu próprio link; não implica vínculo com Mega Crit, Valve ou Sony.
