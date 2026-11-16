# Transporte revisão 7: cliente e host Android

ABI C permanece 4; sts_gns_transport_revision retorna 7. Os exports adicionais informam a revisão e a contagem de endereços, sem retornar IPs, certificados ou chaves ao diagnóstico.

Base: GameNetworkingSockets d534c19aa760df3fb75fd20db13ba1932b8a5463, https://github.com/ValveSoftware/GameNetworkingSockets . O commit inclui ICE nativo. Antes da compilação, aplique:

    python3 apply-android-patch.py /caminho/GameNetworkingSockets

O patch remove a exclusão do Android da enumeração POSIX e inclui ifaddrs.h nesse alvo. Bionic oferece getifaddrs/freeifaddrs desde API 24; esta distribuição usa API 26. O CMake recusa a compilação Android se a exclusão antiga permanecer. O script de aplicação é idempotente e a alteração completa está em android-interfaces.patch.

Dependências da distribuição: NDK r27d, arm64-v8a, android-26, c++_static, ANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON; OpenSSL 3.5.9 estático e protobuf 3.21.12 estático. O protoc do host também deve ser 3.21.12. Confira as licenças em ../LICENSES.

Compile protobuf com seu CMake e a toolchain Android, protobuf_BUILD_TESTS=OFF, protobuf_BUILD_PROTOC_BINARIES=OFF, CMAKE_POSITION_INDEPENDENT_CODE=ON. Compile OpenSSL com Configure android-arm64 -D__ANDROID_API__=26 no-shared no-tests no-apps -fPIC e make build_libs, usando o binário clang do NDK no PATH e ANDROID_NDK_ROOT configurado.

Configure este diretório com CMake >=3.24, informando GNS_SOURCE já corrigido, CMAKE_TOOLCHAIN_FILE do NDK, ANDROID_ABI=arm64-v8a, ANDROID_PLATFORM=android-26, ANDROID_STL=c++_static, ANDROID_SUPPORT_FLEXIBLE_PAGE_SIZES=ON, Protobuf_INCLUDE_DIR/Protobuf_LIBRARY/Protobuf_LITE_LIBRARY do alvo e Protobuf_PROTOC_EXECUTABLE do host. Passe OPENSSL_INCLUDE_DIR/OPENSSL_CRYPTO_LIBRARY/OPENSSL_SSL_LIBRARY do alvo, USE_CRYPTO=OpenSSL, USE_CRYPTO25519=Reference e CMAKE_BUILD_TYPE=Release. Compile o alvo sts2_gns, remova símbolos de depuração com llvm-strip --strip-unneeded e copie libsts2_gns.so para ../arm64-v8a.

O binário Linux usa o mesmo código, protobuf estático e OpenSSL do sistema. Ele permite testar a lógica nativa neste ambiente; isso não equivale a executar Android.

## Testes de descoberta

    dotnet run --project tests/NativeNetworkDiscovery

O teste gerenciado exercita a biblioteca real e o roundtrip local. A enumeração real pode ser bloqueada por permissões do ambiente: nesse caso o teste informa SKIP nessa consulta. A descoberta de interfaces é testada separadamente substituindo somente a lista fornecida pelo sistema operacional; o parser continua sendo o da biblioteca distribuída:

    gcc -shared -fPIC tests/NativeNetworkDiscovery/interfaces_fixture.c -o /tmp/interfaces_fixture.so
    gcc tests/NativeNetworkDiscovery/native_discovery.c -ldl -o /tmp/native_discovery
    LD_PRELOAD=/tmp/interfaces_fixture.so /tmp/native_discovery "$PWD/runtimes/linux-x64/native/libsts2_gns.so" 2
    STS2_TEST_INTERFACES_FAIL=1 LD_PRELOAD=/tmp/interfaces_fixture.so /tmp/native_discovery "$PWD/runtimes/linux-x64/native/libsts2_gns.so" -1

O primeiro cenário contém IPv4, IPv6, loopback, link-local e endereço ausente; somente dois endereços são aceitos. O segundo confirma que uma falha do sistema não vira uma lista válida. Execute a fixture apenas com o harness C, nunca com o runtime dotnet ou o jogo. Ela não entra no APK.

O ELF ARM64 foi conferido para AArch64, alinhamento 0x4000, dependências apenas libc/libm/libdl/liblog e imports getifaddrs/freeifaddrs. O APK Android não foi instalado nem executado neste ambiente. A rota continua ICE direta; Steam Datagram Relay não está implementado.

A revisão 6 acrescenta sts_gns_begin_close. O adaptador encerra a conexão, mantém o contexto e a sinalização ativos por 200 ms, encaminhando filas de fechamento/ack, e somente então chama sts_gns_close. O ABI C continua 4. Essa rotina não confirma entrega do fechamento ao host e não inclui relay SDR.

A revisão 7 acrescenta listener P2P de host, lista de membros permitidos, eventos/envio/recepção por peer e self-test com três pares locais. Certificados e identidades continuam obrigatórios. Consulte sts2_gns.cpp para os exports sts_gns_host_*; o ABI permanece 4.
