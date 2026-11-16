# Revisão 4 — dependências estáticas e DLL interna desatualizada

## Problemas confirmados

1. O patch original cadastrava objetos sem executar construtores. Os 1.867 construtores falhavam por duplicidade; depois InitIds recebia modelos sem Id.
2. A inicialização original depende do momento em que o runtime executa inicializadores estáticos. Sob a execução antecipada observada no Android, BlitzRunModel consulta Sharp antes do cadastro. A análise ampliada também identificou consultas de outros modelos, como BowlbugsNormal consultando BowlbugEgg.
3. O aplicativo guarda uma cópia privada das DLLs e pula sua atualização se isNewVersion retorna false. A versão usada pelo método vem de BuildConfig.VERSION_CODE, uma constante embutida no DEX. Alterar somente versionCode no manifesto do APK não muda essa constante. Os logs enviados continuaram carregando a DLL antiga, sem os marcadores das revisões 2 e 3.

## Alterações

- ModelDbInitPatch: ordena os modelos pelas consultas genéricas ao ModelDb em seus inicializadores estáticos e nos inicializadores das classes base. GetId<T>, que apenas calcula IDs, não é tratado como consulta ao cadastro. A inicialização e os construtores originais são preservados. Não cadastra objetos incompletos nem ignora falhas de construção.
- GodotApp.java: atualiza os assemblies gerenciados pelo APK em toda abertura. O custo é recopiá-los a cada inicialização; evita manter código antigo silenciosamente. Saves e arquivos do jogo não são apagados.
- REBUILD-APK.ps1: decodifica também o DEX, modifica isNewVersion no GodotApp.smali, incorpora a DLL v4, reconstrói e assina o APK. O método compilado passa a solicitar a recópia sempre; portanto a correção funciona mesmo reutilizando original.apk.
- APK-HELPERS.ps1: funções verificadas para patch de smali, versionCode e hash da DLL dentro do APK assinado.
- Com -Install, o script instala exatamente o APK que acabou de gerar, verifica o versionCode instalado, captura o log e exige os marcadores de atualização, DLL v4 e aplicação do patch. Não desinstala nem limpa dados.

## Uso

Coloque StS2-Launcher-v4.zip em Downloads e extraia substituindo a pasta StS2-Launcher-main.

No PowerShell:

powershell -NoProfile -ExecutionPolicy Bypass -File "$env:USERPROFILE\Downloads\StS2-Launcher-main\REBUILD-APK.ps1" -Install

Requer original.apk, apktool.jar e uber-apk-signer.jar em Downloads ou subpastas; Java, .NET SDK 9 e adb no PATH. Conecte somente um celular, com depuração USB autorizada. O script compila, reconstrói, assina, instala, abre e confirma os marcadores automaticamente. Ao ver CONFIRMADO, inicie o jogo pelo launcher. Pressione Enter no terminal após observar o resultado; o log fica na pasta STS2-v4-...\startup-log.txt.

Não use scripts das revisões anteriores nem selecione manualmente APKs antigos.

## Verificação executada

1. Build/publicação Release da DLL: aprovados.
2. Teste negativo: ao antecipar Blitz antes de Sharp e executar seu inicializador, foi reproduzida a mesma TypeInitializationException com ENCHANTMENT.SHARP ausente.
3. Teste ampliado com a ordenação: 1.867 modelos reais construídos, todos com Id válido. Foram forçados antecipadamente os 13 inicializadores que consultam outros modelos; as seis opções de encantamento de Blitz apontaram para os objetos canônicos cadastrados.
4. Harmony real: prefixo aplicado a ModelDb.Init e ordem de cadastro alterada efetivamente pelo hook, com todos os 1.867 modelos registrados.
5. Testes de ordenação: preservação de elementos, entrada, listas vazias, subconjuntos injetados e idempotência.
6. PowerShell 7.4.6: sintaxe de ambos os scripts, substituição de smali, idempotência, versões YAML com e sem aspas e CRLF e verificação do hash da DLL no APK.
7. Apktool 2.12.1: método smali corrigido montado em classes.dex num APK mínimo e decodificado de volta. Confirmados o marcador AssemblyRefresh-v4 e retorno true.

Para executar os modelos fora do Android foi usada uma cópia descartável da sts2.dll com o inicializador de módulo do Sentry desativado. Esse ajuste não está no ZIP entregue: a sts2.dll original permanece byte a byte preservada. O teste não executa renderização Godot, FMOD, carregamento do PCK ou a abertura completa no Android. Apenas os inicializadores que consultam modelos foram forçados antecipadamente; outros inicializadores que requerem localização e recursos do motor não foram forçados fora do motor.

Não foi gerado nem testado aqui o APK real do usuário, pois original.apk está apenas no computador dele. O script faz essa reconstrução no ambiente em que as etapas anteriores já funcionaram. A execução completa no aparelho continua sendo a validação final; não há garantia de ausência de falhas independentes.

## Marcadores esperados

[AssemblyRefresh-v4] refreshing DLLs from APK assets
[build=model-dependencies-v4]
Patched ModelDb.Init (critical)
[ModelDb] Static dependency order applied: 1867 models; original constructors preserved [v4]

A mensagem CONFIRMADO do script confirma carregamento do código; não significa que a inicialização inteira do jogo terminou. O marcador [Startup] GameStartup completed indica que essa chamada terminou sem exceção.

## Ajuste do monitoramento no Windows

Depois da instalação, o script encontrou compartilhamento de arquivo incompatível ao ler o log ainda aberto para escrita pelo processo adb. A leitura agora abre explicitamente FileShare.ReadWrite. Esse erro é no monitoramento, não na instalação ou no jogo. Não requer reconstruir nem reinstalar o APK existente.
