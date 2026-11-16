// Loads the shipped transport; LD_PRELOAD changes only the OS address list.
#include <dlfcn.h>
#include <stdint.h>
#include <stdio.h>
#include <stdlib.h>

int main(int argc, char **argv) {
    if (argc != 3) return 2;
    void *lib = dlopen(argv[1], RTLD_NOW | RTLD_LOCAL);
    if (!lib) { fprintf(stderr, "%s\n", dlerror()); return 3; }
    int (*revision)(void) = dlsym(lib, "sts_gns_transport_revision");
    int (*init)(uint64_t,uint32_t,char*,int) = dlsym(lib, "sts_gns_init");
    int (*count)(void) = dlsym(lib, "sts_gns_local_address_count");
    void (*close_transport)(void) = dlsym(lib, "sts_gns_close");
    if (!revision || !init || !count || !close_transport || revision()!=7) return 4;
    char error[1024] = {0};
    if (!init(76561198000000001ULL,2868840,error,sizeof(error))) {
        fprintf(stderr,"init: %s\n",error); return 5;
    }
    int actual = count(), expected = atoi(argv[2]);
    close_transport();
    if (actual != expected) { fprintf(stderr,"expected=%d actual=%d\n",expected,actual); return 6; }
    printf("PASS: shipped native parser; expected=%d actual=%d (OS address-list fixture).\n",expected,actual);
    dlclose(lib);
    return 0;
}
