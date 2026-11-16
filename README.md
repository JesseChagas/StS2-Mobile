# StS2 Mobile

Unofficial Android launcher for **Slay the Spire 2**, maintained by **Jessé Chagas**, based on [Ekyso/StS2-Launcher](https://github.com/Ekyso/StS2-Launcher).

A Steam account that owns Slay the Spire 2 is required. The launcher downloads game files directly from Steam after authentication. This repository does not include the game, `sts2.dll`, proprietary SDKs, Steam credentials, or signing keys. This project is not affiliated with Mega Crit or Valve.

## Changes in this fork

- Steam multiplayer: discover friends' hosted lobbies automatically, or find a host through an optional SteamID64 dialog.
- Steam hosting from Android, with a separate LAN option and up to four total players.
- Larger multiplayer room cards and touch controls, with connection diagnostics available separately.
- Render resolution from **10% to 100%**, in steps of 5%, with linear upscaling and a saved preference. Lower values reduce rendering work but soften text and detail; performance depends on the device.
- Shared Steam session, improved presence handling and connection cleanup.
- Installed game version/branch detection and game-file verification.

## Current version: r9.10

The certificate request now extends SteamKit's internal job timeout from 10 to 30 seconds, allows one retry on timeout, and reports configuration, request and installation separately. Hosting creates a private lobby before requesting its certificate and publishes it to friends only after validation and listener startup.

Steam client multiplayer and the previous mobile build were confirmed by the user. The r9.10 certificate correction passed local timeout/retry/cancellation tests; its end-to-end hosting result still needs device confirmation. No claim is made that all phones, networks or game versions have been tested. The transport currently uses direct ICE; Steam Datagram Relay is not implemented. Both players must use compatible game versions, branches, model data and gameplay mods; these checks remain enabled.

See [MULTIPLAYER-STATUS.md](MULTIPLAYER-STATUS.md) for test evidence and limitations.

## Build on Windows

The rebuild path requires your locally obtained original launcher APK at `%USERPROFILE%\Downloads\original.apk`, Java, and exactly one Apktool JAR and one Uber APK Signer JAR under Downloads. The original APK and tools are not included here. ADB is required only for installation.

```powershell
powershell -ExecutionPolicy Bypass -File .\REBUILD-APK.ps1 -UsePrebuilt
# With an authorized Android device connected:
powershell -ExecutionPolicy Bypass -File .\REBUILD-APK.ps1 -UsePrebuilt -Install
```

Install the APK from the generated **assinado** folder. The script checks the managed assembly, dependencies, native transport and signed APK. Keep your existing signing key if distributing updates to installed versions.

To build the managed assembly from source, install .NET 9 and provide local references under `upstream/godot-export/.godot/mono/publish/arm64/`: `sts2.dll`, `GodotSharp.dll` (4.5.1), and the original compatible `0Harmony.dll` (2.4.2). These files must be supplied locally, never committed. Omit `-UsePrebuilt` to compile during reconstruction.

The original Android/engine build scripts remain in `scripts/` and `android/`; they require the custom Godot engine and separately obtained proprietary SDKs described by the upstream project. A clean checkout alone is not a complete engine/SDK distribution.

Native transport source, build instructions and license notices are in [native/steam-p2p](native/steam-p2p). Android targets ARM64, API 26 and 16 KB ELF alignment. The `prebuilt/` managed assembly and two native transport binaries are included for the rebuild workflow; game and engine assemblies are excluded.

## Development tests

```powershell
dotnet run --project tests/SteamNetworkJobs
dotnet run --project tests/NativeNetworkDiscovery
```

Other integration tests use local game references and, for UI tests, a compatible Godot .NET runtime. Offline fixtures use synthetic callbacks; production certificate and compatibility validation remain active.

## Credits and licenses

- **Eky / Ekyso**: original launcher and architecture. Preserve the original [MIT license](LICENSE) and copyright notice.
- **Jessé Chagas**: this fork's multiplayer integration, mobile interface and graphics options.
- Dependency notices: [THIRD_PARTY_LICENSES.md](THIRD_PARTY_LICENSES.md) and [native transport notices](native/steam-p2p/THIRD_PARTY.md).

The launcher's MIT license does not replace the licenses of the game, engine components or third-party libraries. Game content remains the property of its respective owners.

## Website

The bilingual landing page is a self-contained HTML file at [docs/index.html](docs/index.html), including screenshots, privacy policy and terms. No build or external assets are needed. To host on GitHub Pages, select Settings → Pages → Deploy from a branch → main → /docs. The main download button opens this repository; Releases contains published APKs when available.
