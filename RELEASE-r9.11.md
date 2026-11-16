# STS2 Mobile r9.11 — recuperação do Steam Guard e publicação do host

- O login retenta falhas de transporte durante a confirmação pelo aplicativo Steam. Reutiliza a solicitação pendente; não inicia outro login com senha durante essa recuperação.
- A solicitação pendente de Steam Guard é armazenada com o Android Keystore por até dez minutos. Ao abrir novamente o launcher, a autenticação pendente tem prioridade e pode ser retomada. Senha e código não entram nesse arquivo.
- Recusas de autenticação não entram em um ciclo de reconexões. Cancelamento e expiração continuam encerrando a tentativa.
- O host agora solicita e valida a entrada do próprio anfitrião na sala criada, confirma a disponibilidade FriendsOnly/Private com nova consulta à Steam e renova a atividade após essas operações.
- O encerramento aguarda atualizações de disponibilidade para evitar republicar uma sala antiga. A saída da conta também apaga solicitações pendentes.
- O diagnóstico inclui host-lobby-entered, host-lobby-availability e host-self-presence. Presença consultada pela própria conta não comprova a visão da conta de um amigo.

Validação: compilação Release; teste com a máquina de estados real de aprovação do SteamKit, incluindo falha de job, retomada dos mesmos IDs, AccessDenied, cancelamento e limite de tentativas; persistência/expiração com ponte Keystore simulada; regressões de diretório de amigos e logout.

Não foi possível testar a retomada após encerramento real do Android, a instalação do APK ou a descoberta da sala por uma segunda conta Steam neste ambiente. O ajuste de host precisa desse teste antes de ser apresentado como uma correção confirmada.
