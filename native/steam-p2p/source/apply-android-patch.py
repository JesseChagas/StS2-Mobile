#!/usr/bin/env python3
"""Enable bionic network interface enumeration in the pinned upstream GNS source."""
from pathlib import Path
import sys

root = Path(sys.argv[1])
path = root / 'src/steamnetworkingsockets/clientlib/steamnetworkingsockets_socketthread.cpp'
source = path.read_text()
old = '#elif IsPosix() && !IsPlaystation() && !IsAndroid() && !IsNintendoSwitch()'
new = '#elif IsPosix() && !IsPlaystation() && !IsNintendoSwitch()'
if old not in source and new not in source:
    raise SystemExit('Unexpected GNS source revision; refusing to apply patch.')
source = source.replace(old, new)
if '#include <ifaddrs.h>' not in source:
    if '#include <atomic>' not in source:
        raise SystemExit('Missing expected include anchor.')
    source = source.replace('#include <atomic>', '#include <atomic>\n\n#if defined(__ANDROID__)\n#include <ifaddrs.h> // bionic API 24+; the distributed Android build targets API 26.\n#endif')
path.write_text(source)
print('Android interface enumeration enabled:', path)
