# Componentes de terceiros

- SteamKit2 3.4.0: LGPL-2.1, obtido por NuGet. Fontes/licença: https://github.com/SteamRE/SteamKit . Não foi modificado.
- Valve GameNetworkingSockets: BSD-3-Clause. Commit d534c19aa760df3fb75fd20db13ba1932b8a5463. Revisão upstream indicada abaixo; wrapper e patch Android preservados em source/. Licença em LICENSES/GameNetworkingSockets-LICENSE.txt. Fonte: https://github.com/ValveSoftware/GameNetworkingSockets . Nosso wrapper é acrescentado ao alvo pelo CMake deste projeto, sem substituir a implementação do protocolo.
- Protocol Buffers C++ 3.21.12: licença BSD do Google, em LICENSES/protobuf-LICENSE.txt. Vinculado estaticamente nos binários Windows e Android; no binário Linux desta revisão, também é vinculado estaticamente. Fontes: https://github.com/protocolbuffers/protobuf/tree/v21.12 .
- OpenSSL 3.5.9: Apache-2.0, em LICENSES/OpenSSL-LICENSE.txt. Vinculado estaticamente no binário Android. O Linux usa OpenSSL do sistema. Windows usa BCrypt. Fonte: https://github.com/openssl/openssl/tree/openssl-3.5.9 .
- LLVM-MinGW 20261104 UCRT e runtime C++ LLVM: licenças de runtime preservadas em LICENSES/. Fonte/distribuição: https://github.com/mstorsjo/llvm-mingw/releases/tag/20261104 .

O transporte proprietário Steam Datagram Relay não está incluído. A consulta à configuração pública da Valve é usada para carregar as autoridades de certificado e revogações, não para implementar um relay.

O pacote não é afiliado nem endossado por Valve ou Mega Crit.
