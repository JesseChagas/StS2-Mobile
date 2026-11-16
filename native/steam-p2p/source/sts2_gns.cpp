#include "steamnetworkingsockets/clientlib/csteamnetworkingsockets.h"
#include "steamnetworkingsockets/clientlib/steamnetworkingsockets_lowlevel.h"
#include "steamnetworkingsockets/steamnetworkingsockets_certstore.h"
#include "steamnetworkingsockets_messages_certs.pb.h"
#include <chrono>
#include <cstdio>
#include <cstring>
#include <ctime>
#include <deque>
#include <mutex>
#include <map>
#include <set>
#include "steamnetworkingsockets_messages.pb.h"
#include <steam/isteamnetworkingutils.h>
#include <steam/steamnetworkingcustomsignaling.h>
#include <steam/steamnetworkingsockets.h>
#include <thread>
#include <vector>

#ifdef _WIN32
#define API extern "C" __declspec(dllexport)
#else
#define API extern "C" __attribute__((visibility("default")))
#endif
using namespace SteamNetworkingSocketsLib;
struct Signal {
  uint64 peer;
  std::vector<unsigned char> bytes;
};
struct Event {
  uint64 peer = 0;
  int state;
  int reason;
  char text[256];
};
struct Context {
  std::mutex mutex;
  std::deque<Signal> signals;
  std::deque<Event> events;
  uint64 self = 0, peer = 0;
  HSteamNetConnection conn = 0;
  bool certified = false;
  bool closing = false;
  bool host = false, hostClosed = false;
  int maxPeers = 0;
  HSteamListenSocket listen = 0;
  std::map<uint64, HSteamNetConnection> peers;
  std::set<uint64> allowed;
  uint64 pendingPeer = 0;
  size_t receiveCursor = 0;
  SteamNetworkingMessage_t *pending = nullptr;
};
static Context *ctx = nullptr;
static int failure(char *out, int cap, const char *message) {
  if (out && cap > 0)
    std::snprintf(out, cap, "%s", message);
  return 0;
}
static void OnState(SteamNetConnectionStatusChangedCallback_t *change) {
  if (!ctx) return;
  uint64 peer = change->m_info.m_identityRemote.GetSteamID64();
  if (ctx->host) {
    auto found = ctx->peers.find(peer);
    if (found == ctx->peers.end() || found->second != change->m_hConn) return;
  } else if (change->m_hConn != ctx->conn) return;
  Event e{};
  e.peer = peer;
  e.state = change->m_info.m_eState;
  e.reason = change->m_info.m_eEndReason;
  std::snprintf(e.text, sizeof(e.text), "%s", change->m_info.m_szEndDebug);
  std::lock_guard<std::mutex> lock(ctx->mutex);
  if (ctx->events.size() < 256)
    ctx->events.push_back(e);
}
class Signaling final : public ISteamNetworkingConnectionSignaling {
  uint64 destination;
public:
  explicit Signaling(uint64 peer = 0) : destination(peer) {}
  bool SendSignal(HSteamNetConnection, const SteamNetConnectionInfo_t &info,
                  const void *data, int len) override {
    if (!ctx || !data || len <= 0 || len > 131072) return false;
    uint64 peer = destination ? destination : ctx->peer;
    if (!peer || info.m_identityRemote.GetSteamID64() != peer) return false;
    Signal s;
    s.peer = peer;
    s.bytes.assign((const unsigned char *)data,
                   (const unsigned char *)data + len);
    std::lock_guard<std::mutex> lock(ctx->mutex);
    if (ctx->signals.size() >= 256)
      return false;
    ctx->signals.push_back(std::move(s));
    return true;
  }
  void Release() override { delete this; }
};
class ReceiveContext final : public ISteamNetworkingSignalingRecvContext {
public:
  ISteamNetworkingConnectionSignaling *
  OnConnectRequest(HSteamNetConnection connection, const SteamNetworkingIdentity &identity,
                   int port) override {
    uint64 peer = identity.GetSteamID64();
    if (!ctx || !ctx->host || ctx->hostClosed || ctx->closing || port != 0 || !peer || peer == ctx->self ||
        !ctx->allowed.count(peer) || ctx->peers.count(peer) || ctx->peers.size() >= (size_t)ctx->maxPeers)
      return nullptr;
    ctx->peers[peer] = connection;
    if (SteamNetworkingSockets()->AcceptConnection(connection) != k_EResultOK) {
      ctx->peers.erase(peer);
      SteamNetworkingSockets()->CloseConnection(connection, 1000, "Accept failed", false);
      return nullptr;
    }
    return new Signaling(peer);
  }
  void SendRejectionSignal(const SteamNetworkingIdentity &, const void *,
                           int) override {}
};
API int sts_gns_abi_version() { return 4; }
API int sts_gns_transport_revision() { return 7; }
API int sts_gns_local_address_count() {
  if (!ctx) return -1;
  SteamNetworkingGlobalLock lock("sts_local_addresses");
  CUtlVector<LocalAddress_t> addresses;
  if (!GetLocalAddresses(&addresses)) return -1;
  return addresses.Count();
}
API int sts_gns_init(uint64_t self, uint32_t app, char *error, int cap) {
  if (ctx)
    return failure(error, cap, "A native transport is already active");
  if (!self || !app)
    return failure(error, cap, "Invalid Steam identity or AppID");
  SteamNetworkingIdentity id;
  id.Clear();
  id.SetSteamID64(self);
  SteamNetworkingErrMsg msg;
  if (!GameNetworkingSockets_Init(&id, msg))
    return failure(error, cap, msg);
  ctx = new Context;
  ctx->self = self;
  ((CSteamNetworkingUtils *)SteamNetworkingUtils())->SetAppID(app);
  SteamNetworkingUtils()->SetGlobalCallback_SteamNetConnectionStatusChanged(
      OnState);
  // These do NOT turn off authentication, encryption, or signature validation.
  SteamNetworkingUtils()->SetGlobalConfigValueInt32(
      k_ESteamNetworkingConfig_IP_AllowWithoutAuth, 0);
  SteamNetworkingUtils()->SetGlobalConfigValueInt32(
      k_ESteamNetworkingConfig_Unencrypted, 0);
  SteamNetworkingUtils()->SetGlobalConfigValueInt32(
      k_ESteamNetworkingConfig_P2P_Transport_ICE_Enable,
      k_nSteamNetworkingConfig_P2P_Transport_ICE_Enable_Private |
          k_nSteamNetworkingConfig_P2P_Transport_ICE_Enable_Public);
  SteamNetworkingUtils()->SetGlobalConfigValueString(
      k_ESteamNetworkingConfig_P2P_STUN_ServerList,
      "stun.l.google.com:19302,stun1.l.google.com:19302");
  return 1;
}
API int sts_gns_public_key(unsigned char *out, int capacity, char *error,
                           int cap) {
  if (!ctx || !out || capacity < 32)
    return failure(
        error, cap,
        "Transport is not initialized or public key buffer is too small");
  int size = 0;
  SteamNetworkingErrMsg msg;
  if (!SteamNetworkingSockets()->GetCertificateRequest(&size, nullptr, msg))
    return failure(error, cap, msg);
  std::vector<unsigned char> data(size);
  if (!SteamNetworkingSockets()->GetCertificateRequest(&size, data.data(), msg))
    return failure(error, cap, msg);
  CMsgSteamDatagramCertificateRequest request;
  if (!request.ParseFromArray(data.data(), size) ||
      request.cert().key_data().size() != 32)
    return failure(error, cap, "Invalid Ed25519 certificate request");
  std::memcpy(out, request.cert().key_data().data(), 32);
  return 32;
}
API int sts_gns_add_ca(const char *cert, char *error, int cap) {
  if (!ctx || !cert)
    return failure(error, cap, "No certificate authority data");
  SteamNetworkingErrMsg msg;
  SteamNetworkingGlobalLock lock("sts_add_ca");
  if (!CertStore_AddCertFromBase64(cert, msg))
    return failure(error, cap, msg);
  return 1;
}
API void sts_gns_revoke_ca(uint64_t key) {
  if (ctx) {
    SteamNetworkingGlobalLock lock("sts_revoke_ca");
    CertStore_AddKeyRevocation(key);
  }
}
API int sts_gns_install_cert(const unsigned char *cert, int length, uint64_t ca,
                             const unsigned char *signature, int siglen,
                             char *error, int cap) {
  if (!ctx || !cert || length <= 0 || length > 65536 || !signature ||
      siglen != 64)
    return failure(error, cap, "Invalid signed certificate input");
  CMsgSteamDatagramCertificateSigned signedCert;
  signedCert.set_cert(cert, length);
  signedCert.set_ca_key_id(ca);
  signedCert.set_ca_signature(signature, siglen);
  CMsgSteamDatagramCertificate parsed;
  SteamNetworkingErrMsg msg;
  {
    SteamNetworkingGlobalLock lock("sts_install_cert");
    if (!CertStore_CheckCert(signedCert, parsed, std::time(nullptr), msg))
      return failure(error, cap, msg);
  }
  std::string wire;
  signedCert.SerializeToString(&wire);
  if (!SteamNetworkingSockets()->SetCertificate(wire.data(), (int)wire.size(),
                                                msg))
    return failure(error, cap, msg);
  ctx->certified = true;
  return 1;
}
API uint32_t sts_gns_connect(uint64_t peer, char *error, int cap) {
  if (!ctx || !ctx->certified || !peer || peer == ctx->self) {
    failure(error, cap,
            "A validated Steam certificate and a remote host are required");
    return 0;
  }
  if (ctx->conn) {
    failure(error, cap, "Connection already started");
    return 0;
  }
  ctx->peer = peer;
  SteamNetworkingIdentity id;
  id.Clear();
  id.SetSteamID64(peer);
  SteamNetworkingConfigValue_t options[2];
  options[0].SetInt32(k_ESteamNetworkingConfig_TimeoutInitial, 20000);
  options[1].SetInt32(k_ESteamNetworkingConfig_TimeoutConnected, 20000);
  ctx->conn = SteamNetworkingSockets()->ConnectP2PCustomSignaling(
      new Signaling, &id, 0, 2, options);
  if (!ctx->conn)
    failure(error, cap, "ConnectP2PCustomSignaling failed");
  return ctx->conn;
}
API int sts_gns_input_signal(const unsigned char *bytes, int length) {
  if (!ctx || !ctx->conn || !bytes || length <= 0 || length > 131072)
    return 0;
  ReceiveContext receiver;
  return SteamNetworkingSockets()->ReceivedP2PCustomSignal(bytes, length,
                                                           &receiver)
             ? 1
             : 0;
}
API void sts_gns_pump() {
  if (ctx)
    SteamNetworkingSockets()->RunCallbacks();
}
API int sts_gns_poll_signal(uint64_t *peer, unsigned char *bytes,
                            int capacity) {
  if (!ctx)
    return 0;
  std::lock_guard<std::mutex> lock(ctx->mutex);
  if (ctx->signals.empty())
    return 0;
  auto &s = ctx->signals.front();
  int length = (int)s.bytes.size();
  if (capacity < length)
    return -length;
  *peer = s.peer;
  std::memcpy(bytes, s.bytes.data(), length);
  ctx->signals.pop_front();
  return length;
}
API int sts_gns_poll_event(int *state, int *reason, char *text, int capacity) {
  if (!ctx)
    return 0;
  std::lock_guard<std::mutex> lock(ctx->mutex);
  if (ctx->events.empty())
    return 0;
  auto e = ctx->events.front();
  ctx->events.pop_front();
  *state = e.state;
  *reason = e.reason;
  if (text && capacity > 0)
    std::snprintf(text, capacity, "%s", e.text);
  return 1;
}
API int sts_gns_send(const unsigned char *bytes, int length, int flags) {
  if (!ctx || !ctx->conn || !bytes || length <= 0 || length > 524288)
    return -1;
  return (int)SteamNetworkingSockets()->SendMessageToConnection(
      ctx->conn, bytes, (uint32)length, flags, nullptr);
}
API int sts_gns_receive(unsigned char *bytes, int capacity, int *flags,
                        int *channel) {
  if (!ctx || !ctx->conn)
    return 0;
  if (!ctx->pending) {
    int n = SteamNetworkingSockets()->ReceiveMessagesOnConnection(
        ctx->conn, &ctx->pending, 1);
    if (n <= 0)
      return n;
  }
  auto *msg = ctx->pending;
  if (capacity < msg->m_cbSize)
    return -msg->m_cbSize;
  int length = msg->m_cbSize;
  std::memcpy(bytes, msg->m_pData, length);
  *flags = msg->m_nFlags;
  *channel = msg->m_nChannel;
  msg->Release();
  ctx->pending = nullptr;
  return length;
}
API int sts_gns_host_listen(int maxPeers, char *error, int cap) {
  if (!ctx || !ctx->certified || ctx->conn || ctx->listen || maxPeers < 1 || maxPeers > 3)
    return failure(error, cap, "Validated certificate and 1-3 remote player slots required");
  ctx->host = true; ctx->maxPeers = maxPeers;
  SteamNetworkingConfigValue_t options[2];
  options[0].SetInt32(k_ESteamNetworkingConfig_TimeoutInitial, 20000);
  options[1].SetInt32(k_ESteamNetworkingConfig_TimeoutConnected, 20000);
  ctx->listen = SteamNetworkingSockets()->CreateListenSocketP2P(0, 2, options);
  if (!ctx->listen) { ctx->host = false; return failure(error, cap, "P2P listen socket failed"); }
  return 1;
}
API void sts_gns_host_allow(uint64 peer, int allow) {
  if (!ctx || !ctx->host || !peer || peer == ctx->self) return;
  if (allow) ctx->allowed.insert(peer); else ctx->allowed.erase(peer);
}
API void sts_gns_host_set_closed(int closed) { if (ctx && ctx->host) ctx->hostClosed = closed != 0; }
API int sts_gns_host_input(uint64 sender, const unsigned char *bytes, int length) {
  if (!ctx || !ctx->host || !sender || sender == ctx->self || !bytes || length <= 0 || length > 131072) return 0;
  CMsgSteamNetworkingP2PRendezvous message;
  SteamNetworkingIdentity identity;
  if (!message.ParseFromArray(bytes, length) || !identity.ParseString(message.from_identity().c_str()) ||
      identity.GetSteamID64() != sender || (!ctx->allowed.count(sender) && !ctx->peers.count(sender))) return 0;
  ReceiveContext receiver;
  return SteamNetworkingSockets()->ReceivedP2PCustomSignal(bytes, length, &receiver) ? 1 : 0;
}
API int sts_gns_host_poll_event(uint64 *peer, int *state, int *reason, char *text, int capacity) {
  if (!ctx || !ctx->host) return 0;
  std::lock_guard<std::mutex> lock(ctx->mutex);
  if (ctx->events.empty()) return 0;
  auto event = ctx->events.front(); ctx->events.pop_front();
  *peer = event.peer; *state = event.state; *reason = event.reason;
  if (text && capacity > 0) std::snprintf(text, capacity, "%s", event.text);
  return 1;
}
API int sts_gns_host_send(uint64 peer, const unsigned char *bytes, int length, int flags) {
  if (!ctx || !ctx->host || !bytes || length <= 0 || length > 524288) return -1;
  auto found = ctx->peers.find(peer); if (found == ctx->peers.end()) return -1;
  return (int)SteamNetworkingSockets()->SendMessageToConnection(found->second, bytes, length, flags, nullptr);
}
API int sts_gns_host_receive(uint64 *peer, unsigned char *bytes, int capacity, int *flags, int *channel) {
  if (!ctx || !ctx->host) return 0;
  if (!ctx->pending && !ctx->peers.empty()) {
    auto it = ctx->peers.begin(); std::advance(it, ctx->receiveCursor % ctx->peers.size());
    for (size_t i = 0; i < ctx->peers.size(); ++i) {
      int received = SteamNetworkingSockets()->ReceiveMessagesOnConnection(it->second, &ctx->pending, 1);
      ctx->receiveCursor++;
      if (received > 0) { ctx->pendingPeer = it->first; break; }
      if (++it == ctx->peers.end()) it = ctx->peers.begin();
    }
  }
  if (!ctx->pending) return 0;
  auto *message = ctx->pending; if (capacity < message->m_cbSize) return -message->m_cbSize;
  int length = message->m_cbSize; std::memcpy(bytes, message->m_pData, length);
  *peer = ctx->pendingPeer; *flags = message->m_nFlags; *channel = message->m_nChannel;
  message->Release(); ctx->pending = nullptr; return length;
}
API void sts_gns_host_close_peer(uint64 peer, int reason, const char *detail, int linger) {
  if (!ctx || !ctx->host) return;
  auto found = ctx->peers.find(peer); if (found == ctx->peers.end()) return;
  SteamNetworkingSockets()->CloseConnection(found->second, reason, detail, linger != 0);
  ctx->peers.erase(found); ctx->allowed.erase(peer);
}
API int sts_gns_begin_close() {
  if (!ctx || ctx->closing) return 0;
  ctx->closing = true;
  if (ctx->host) {
    ctx->hostClosed = true;
    for (auto &peer : ctx->peers) SteamNetworkingSockets()->CloseConnection(peer.second, 1000, "Host closed", false);
    if (ctx->listen) { SteamNetworkingSockets()->CloseListenSocket(ctx->listen); ctx->listen = 0; }
  } else if (ctx->conn) SteamNetworkingSockets()->CloseConnection(ctx->conn, 1000, "Client closed", false);
  return 1;
}
API void sts_gns_close() {
  if (!ctx)
    return;
  if (ctx->pending) {
    ctx->pending->Release();
    ctx->pending = nullptr;
  }
  if (!ctx->closing) sts_gns_begin_close();
  GameNetworkingSockets_Kill();
  delete ctx;
  ctx = nullptr;
}
API int sts_gns_selftest(char *error, int capacity) {
  if (ctx)
    return failure(error, capacity,
                   "Close the active transport before running self-test");
  SteamNetworkingErrMsg msg;
  if (!GameNetworkingSockets_Init(nullptr, msg))
    return failure(error, capacity, msg);
  int passed = 0;
  for (int mode = 0; mode < 2; mode++) {
    HSteamNetConnection a = 0, b = 0;
    int result = 0;
    if (SteamNetworkingSockets()->CreateSocketPair(&a, &b, mode != 0, nullptr,
                                                   nullptr)) {
      const unsigned char packet[] = {0x53, 0x54, 0x53, 0x32, 0x01, 0x00, 0xff};
      if (SteamNetworkingSockets()->SendMessageToConnection(
              a, packet, sizeof(packet), k_nSteamNetworkingSend_Reliable,
              nullptr) == k_EResultOK) {
        for (int i = 0; i < 500; i++) {
          SteamNetworkingSockets()->RunCallbacks();
          SteamNetworkingMessage_t *received = nullptr;
          int count = SteamNetworkingSockets()->ReceiveMessagesOnConnection(
              b, &received, 1);
          if (count == 1) {
            result =
                received->m_cbSize == sizeof(packet) &&
                std::memcmp(received->m_pData, packet, sizeof(packet)) == 0;
            received->Release();
            break;
          }
          std::this_thread::sleep_for(std::chrono::milliseconds(2));
        }
      }
      SteamNetworkingSockets()->CloseConnection(a, 1000, "Self-test complete",
                                                false);
      SteamNetworkingSockets()->CloseConnection(b, 1000, "Self-test complete",
                                                false);
    }
    if (!result)
      break;
    passed++;
  }
  GameNetworkingSockets_Kill();
  if (passed != 2)
    return failure(error, capacity,
                   "GNS memory/UDP-localhost roundtrip failed");
  return 1;
}

// Offline test: local socket pairs only, never a Steam listener or unsigned
// network connection. Refuses to run while a real context is active.
API int sts_gns_host_selftest(char *error, int capacity) {
  if (ctx) return failure(error, capacity, "Close active transport before host self-test");
  SteamNetworkingErrMsg message;
  if (!GameNetworkingSockets_Init(nullptr, message)) return failure(error, capacity, message);
  ctx = new Context; ctx->self = 76561198000000001ULL;
  // The public listener must still reject a host without a validated certificate.
  if (sts_gns_host_listen(3, error, capacity) != 0) {
    sts_gns_close(); return failure(error, capacity, "Unsigned host listener was allowed");
  }
  ctx->host = true; ctx->maxPeers = 3;
  bool good = true;
  for (int udp = 0; udp < 2 && good; ++udp) {
    HSteamNetConnection clients[3] = {};
    for (int i = 0; i < 3 && good; ++i) {
      HSteamNetConnection host = 0;
      SteamNetworkingIdentity hostId, peerId; hostId.Clear(); peerId.Clear();
      hostId.SetSteamID64(ctx->self); peerId.SetSteamID64(ctx->self + i + 1);
      good = SteamNetworkingSockets()->CreateSocketPair(&host, &clients[i], udp != 0, &hostId, &peerId);
      if (good) ctx->peers[ctx->self + i + 1] = host;
    }
    for (int i = 0; i < 3 && good; ++i) {
      const unsigned char data[] = {0x48, (unsigned char)i};
      good = sts_gns_host_send(ctx->self + i + 1, data, sizeof(data), k_nSteamNetworkingSend_Reliable) == k_EResultOK;
    }
    int received = 0;
    for (int attempt = 0; attempt < 400 && received < 3 && good; ++attempt) {
      for (int i = 0; i < 3; ++i) {
        SteamNetworkingMessage_t *packet = nullptr;
        if (clients[i] && SteamNetworkingSockets()->ReceiveMessagesOnConnection(clients[i], &packet, 1) == 1) {
          good = packet->m_cbSize == 2 && ((unsigned char *)packet->m_pData)[1] == i;
          packet->Release(); received++;
          const unsigned char reply[] = {0x43, (unsigned char)i};
          SteamNetworkingSockets()->SendMessageToConnection(clients[i], reply, sizeof(reply), k_nSteamNetworkingSend_Reliable, nullptr);
        }
      }
      SteamNetworkingSockets()->RunCallbacks(); std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    good = good && received == 3; std::set<uint64> senders;
    for (int attempt = 0; attempt < 400 && senders.size() < 3 && good; ++attempt) {
      unsigned char bytes[32]; uint64 peer = 0; int flags = 0, channel = 0;
      int count = sts_gns_host_receive(&peer, bytes, sizeof(bytes), &flags, &channel);
      if (count > 0) {
        good = count == 2 && bytes[0] == 0x43 && peer == ctx->self + bytes[1] + 1;
        senders.insert(peer);
      }
      SteamNetworkingSockets()->RunCallbacks(); std::this_thread::sleep_for(std::chrono::milliseconds(2));
    }
    good = good && senders.size() == 3;
    sts_gns_host_close_peer(ctx->self + 1, 1000, "Test peer left", 0);
    const unsigned char check[] = {0x4f};
    good = good && ctx->peers.size() == 2 && sts_gns_host_send(ctx->self + 2, check, sizeof(check), k_nSteamNetworkingSend_Reliable) == k_EResultOK;
    for (int i = 0; i < 3; ++i) { sts_gns_host_close_peer(ctx->self + i + 1, 1000, "Test complete", 0); if (clients[i]) SteamNetworkingSockets()->CloseConnection(clients[i], 1000, "Test complete", false); }
  }
  sts_gns_close();
  if (!good) return failure(error, capacity, "Multi-peer host packet routing/cleanup failed");
  return 1;
}
