// Test only. Replace the OS address list, leaving the shipped GNS parser intact.
// The fixture includes IPv4, IPv6, loopback, link-local and missing addresses.
#include <ifaddrs.h>
#include <arpa/inet.h>
#include <net/if.h>
#include <string.h>
#include <stdlib.h>
#include <errno.h>

static struct ifaddrs entries[5];
static struct sockaddr_in v4, loopback, linklocal, mask4;
static struct sockaddr_in6 v6, mask6;

int getifaddrs(struct ifaddrs **out) {
    if (getenv("STS2_TEST_INTERFACES_FAIL")) { *out = NULL; errno = EACCES; return -1; }
    memset(entries, 0, sizeof(entries));
    v4.sin_family = loopback.sin_family = linklocal.sin_family = mask4.sin_family = AF_INET;
    inet_pton(AF_INET, "192.0.2.123", &v4.sin_addr);
    inet_pton(AF_INET, "127.0.0.1", &loopback.sin_addr);
    inet_pton(AF_INET, "169.254.1.2", &linklocal.sin_addr);
    inet_pton(AF_INET, "255.255.255.0", &mask4.sin_addr);
    v6.sin6_family = mask6.sin6_family = AF_INET6;
    inet_pton(AF_INET6, "2001:db8::123", &v6.sin6_addr);
    inet_pton(AF_INET6, "ffff:ffff:ffff:ffff::", &mask6.sin6_addr);
    struct sockaddr *addresses[] = { (void*)&v4, (void*)&v6, (void*)&loopback, (void*)&linklocal, NULL };
    for (int i=0;i<5;i++) {
        entries[i].ifa_next = i==4 ? NULL : &entries[i+1];
        entries[i].ifa_name = "fixture";
        entries[i].ifa_flags = IFF_UP | (i==2 ? IFF_LOOPBACK : 0);
        entries[i].ifa_addr = addresses[i];
    }
    entries[0].ifa_netmask = (void*)&mask4;
    entries[1].ifa_netmask = (void*)&mask6;
    *out = entries; return 0;
}
void freeifaddrs(struct ifaddrs *list) { (void)list; }
