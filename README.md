# ApkShellext2

[![release](https://img.shields.io/badge/release_4.1.2-blue)](https://github.com/nkh0472/apkshellext/releases)
[![.NET](https://img.shields.io/badge/.NET_Framework-4.8-512BD4)](#build-from-source)
[![license](https://img.shields.io/badge/license-MIT-green)](LICENSE)

**English** | [简体中文](README.zh-CN.md)

A Windows shell extension that shows the real app icon, package info tip and a management context menu for mobile app packages in File Explorer.

在资源管理器中为手机应用安装包显示真实图标、悬浮信息与右键管理菜单的 Windows 外壳扩展。支持 Windows 10 / 11，运行于 .NET Framework 4.8。

## Supported file types

| Extension | Icon | Info tip | Context menu | Thumbnail |
|-----------|:----:|:--------:|:------------:|:---------:|
| `.apk`    | ✔ | ✔ | ✔ | ✔ |
| `.apks` `.apkm` `.xapk` (split bundles, since 3.4.0) | ✔ | ✔ | ✔ | — |
| `.aab`    | ✔ | — | — | — |
| `.ipa`    | ✔ | ✔ | ✔ | — |
| `.appx` `.appxbundle` | ✔ | ✔ | ✔ | — |

`.xap` is not supported.

## Features

Everything below runs inside Explorer itself — one DLL, no service and no background process.

| Area | What it does | Details |
|------|--------------|---------|
| **Icons** | Real app icon at every Explorer zoom level | Android adaptive icons are drawn with Android's own 1.5x center-crop; when a variant cannot be drawn the legacy icon is used instead |
| **Thumbnails** | Preview in the folder view for `.apk` files | Filled to the requested size; 256 px in the large-icon view |
| **Info tip** | Version, package, size, Min/Max SDK and packer | A fact the package does not carry takes its whole line with it, label included |
| **Context menu** | One menu, same items on Windows 10 and 11 | Windows 11 22H2+ gets the modern `IExplorerCommand` menu; Windows 10 and the Windows 11 *Show more options* entry keep the classic `IContextMenu` menu |
| **Rename** | Batch rename from a pattern | `%AppName%`, `%Version%`, `%Package%`, `%Attr(tag,attr)` and more; Preferences lists the variables next to the field and inserts one at the caret |
| **Extract information** | Export to CSV, or extract an XML entry | CSV: UTF-8 with BOM, one row per selected file, 20 columns in a fixed order — MinSdk, TargetSdk, Debuggable and Packer sit between Type and SizeBytes, and Verdict plus Evidence sit between Packer and SizeBytes — plus SHA-256 and XXH3 columns and a status column so an unreadable file still gets a row. Column order is part of the contract: read the header instead of hard-coding column numbers. XML: written into a folder you pick, file name prefixed with the rename pattern |
| **Copy information** | File paths, app names, package names, versions | A multi-selection yields one value per line, in selection order |
| **Store pages** | Google Play, Amazon Appstore, VirusTotal | The VirusTotal entry hashes the file in the background only when clicked, then opens `virustotal.com/gui/file/<sha256>` |
| **Preferences** | One place for every switch | UI language, app-name language, overlay badges, per-store menu items, thumbnail and adaptive-icon switches, error log |

| Built on | Detail |
|----------|--------|
| **Own shell integration** | Icon, info-tip, thumbnail and context-menu handlers are implemented in this repository directly against the Windows shell interfaces — no third-party shell framework, no dependency on a menu helper |
| **19 languages** | Every string ships in 19 languages; a guard test keeps each translation in step with the neutral resource, so a half-translated menu fails the build |
| **Hardened parsing** | Every read path has a size and decompression-ratio cap, and hostile input is bounded before it allocates; libwebp 1.6.0 draws WebP icons |
| **Small footprint** | One ~0.9 MB DLL plus two `libwebp` binaries, the install scripts and the language folders |
## Install

1. Download the latest release archive and extract it to a permanent folder (e.g. `C:\Program Files\ApkShellext2\`).
2. Run `install.bat` **as administrator** — it registers the extension for both 64-bit and 32-bit Explorer hosts and generates NGEN native images (faster first icons after boot). No admin rights? Run `install_user.bat` instead: per-user registration under `HKCU`, no UAC prompt (remove again with `uninstall_user.bat`); it skips NGEN by design, so only the very first icons after each boot JIT-compile slightly slower. A machine-wide registration always takes precedence over the per-user one. Note: `install.bat` refuses folders that ordinary users can write to (that would let any local account replace the DLL every other user's Explorer loads) — move the extracted folder to `%ProgramFiles%`, or just use `install_user.bat`.
3. If icons do not refresh, run `restart_explorer.bat`.

**Upgrade:** extract the new archive anywhere and run `update.bat` from it (optional `/quiet` for scripting; `/ngen` also refreshes native images). It reuses the folder already recorded in the COM `CodeBase`, leaves that registry path unchanged, backs up the old files (including ones this version no longer ships), and can be undone with `update.bat /rollback` from that folder.

**Uninstall:** run `uninstall.bat` as administrator — it unregisters both 64-bit and 32-bit views, sweeps the registration even if the DLL was already deleted (safe to run from any install folder of this version), and removes the legacy COM entries left by pre-3.4.2 builds. If file icons persist afterwards, the leftover is the Windows icon cache (`IconCache.db` / `thumbcache_*.db`), not the extension; delete it and restart Explorer.

## Settings

Right-click a package → **ApkShellext2 → Preferences** (modeless, never blocks Explorer). Settings live under `HKCU\SOFTWARE\ApkShellext2`, including `SupportAdaptiveIcon`, `UseAppNameLanguage` / `AppNameLanguage`, `CheckForUpdates` (set to `False` to stop the daily background version check) and info-tip customization.

## Build from source

The full build & deploy guide — toolchain choice (VS vs dotnet CLI and why only one produces deployable binaries), packaging, libwebp rebuild, common pitfalls — is [compile_guidance.md](compile_guidance.md) ([简体中文](compile_guidance.zh-CN.md)).

Quick version: clone with `--recursive`, then `msbuild ApkShellext2.sln /p:Configuration=Release /p:Platform="Any CPU"`. Requires Visual Studio 2019+ and the .NET Framework 4.8 targeting pack; release builds sign with your own `sn -k` key.

## Project history & maintainers

| Period | Maintainer | Contribution |
|--------|-----------|--------------|
| 2009 – | [kkguo](https://github.com/kkguo) | Original author of ApkShellext2 |
| – 2023 | [1595901624](https://github.com/1595901624) | Maintained v3.x, created the `ApkShellext2_hy` branch (.aab support and more) |
| 2026 – | [nkh0472](https://github.com/nkh0472) | Current maintainer: dependency modernization (libwebp 1.6.0), Windows 11 icon fixes, localized labels, split-APK bundles, archive hardening |

## Changelog

Full bilingual release notes are on the [Releases page](https://github.com/nkh0472/apkshellext/releases). Releases are published only by the tag-driven workflow; the tag, signing and dry-run procedure is in [compile_guidance.md §11](compile_guidance.md#11-releasing).

| Version | Summary |
|---------|---------|
| 4.1.2 | Settings: every feature switch is read as a boolean, so a DWORD (`1`/`0`) or `yes`/`on`/`1` written by hand or by an older build no longer silently turns a feature off (overlay badges, thumbnail stretching, adaptive icons, store entries, debug log); 19 tests pin the accepted forms, and the README features section is now two tables — dll 935,424 B, 7z 714,456 B |
| 4.1.1 | Translations: the 24 strings the 3.x/4.x commands added (copy menu, VirusTotal entry, folder picker, rename-variable descriptions) now exist in all 19 languages instead of falling back to English, and a test keeps every satellite in step with the neutral file — dll 935,936 B (no size change), 7z 708,473 B → 714,213 B |
| 4.1.0 | Context menu: **Copy info** submenu (file path, app name, package and version, one line per file of a multi-selection), **Extract information** submenu that now holds the CSV export and both XML extractions with a folder picker and rename-prefixed file names, **Check the hash on VirusTotal** entry, SHA-256 and XXH3-64 columns in the CSV, min/target SDK and packer facts in the info tip, and menu bitmaps sized like the shell icons — dll 909,312 B → 935,936 B, 7z 698,828 B → 708,473 B | 
| 4.0.0 | Shell layer moved in-repo: the four handlers and the context menu are implemented against the shell interfaces directly instead of a vendored third-party shell framework, `IExplorerCommand` supplies the modern Windows 11 22H2+ menu while Windows 10 and "Show more options" keep the classic one; new "Export info (CSV)…" command; Preferences inserts rename variables by click; `update.bat` reads all handler slots back and `/rollback` restores exactly the previous file set — dll 1,165,312 B → 909,312 B, 7z 746,997 B → 698,946 B |
| 3.6.0 | Dependency modernization: the source-embedded SharpZipLib is replaced by the BCL `ZipArchive` behind one capped `ZipSafety` reader (new 65,535-entry guard), ExCSS is removed, the unused SVG text/filter/extensibility/raster-image subtrees are trimmed, and the Span-based parsers are rewritten on plain strings to drop the System.Memory facade family — deployment 3.24 MiB → 2.24 MiB, 7z 1.00 MB → 0.73 MB, no behavior change (87 sample icons byte-identical) |
| 3.5.1 | Hardening pass: malformed packages no longer crash Explorer, failures are logged for diagnosis, 32-bit-view registration and per-store Preferences fixed |
| 3.5.0 | Adaptive icons out of beta: Android-exact 1.5x zoom, framework color references, case-exact zip entries, DPI-independent vector rendering; `<bitmap>` support |
| 3.4.5 | Fixed the scripts' NGEN steps (`/quiet` is not an ngen.exe flag — native images were never being generated) |
| 3.4.4 | Security/perf audit hardening; all 19 locales completed; AssemblyVersion finally tracks the release |
| 3.4.3 | NGEN native images; optional per-user (no-admin) install; dark-theme Preferences; update check opt-out |
| 3.4.2 | Modernized install/uninstall scripts: dual-bitness registration, offline registry sweep, legacy COM-entry cleanup; COM surface restricted to the four shell handlers |
| 3.4.1 | VectorDrawable rendering re-based onto the embedded SVG engine (gradients incl. multi-stop/angle/radial, nested groups, clip paths); indirect color references resolved |
| 3.4.0 | Split-APK bundles (`.apks`/`.apkm`/`.xapk`); zip-bomb & overflow hardening in all readers; MSIX bundle member-name decoding |
| 3.3.1 | FacadeResolver — fixes silent vector-icon failure inside Explorer; vector group transforms; adaptive-icon fallback chain |
| 3.3.0 | Localized app names; SVG/PNG icon regression fixes; modeless Preferences |
| 3.2.0 | Case-insensitive icon extensions (#99); FileShare.Delete (#103); WOW64-safe elevation (#116) |
| 3.1.0 | Dependency modernization: libwebp 1.6.0 (CVE-2023-4863 et al.), SharpZipLib 1.4.2, QRCoder 1.4.2, SVG.NET 3.5.0 — *had an SVG regression, fixed in 3.3.1* |
| 3.0.0 | Fixed Chrome/Edge download crash; upgraded to .NET Framework 4.8 |

## Thanks

The project stands on these open-source works:

| Component | Role |
|-----------|------|
| [libwebp](https://chromium.googlesource.com/webm/libwebp) | WebP codec (1.6.0, built from source) |
| [WebP-wrapper](https://github.com/JosePineiro/WebP-wrapper) | WebP C# bindings |
| [SVG](https://github.com/vvvv/SVG) | SVG / vector-drawable rendering |
| [PlistCS](https://github.com/animetrics/PlistCS) | iOS plist reader |
| [PNGDecrush](https://github.com/MikeWeller/PNGDecrush) | PNG zlib re-encoding |
| [apk-info](https://github.com/delvinru/apk-info) | Split-APK container layout & anti-zip-bomb doctrine |
| [apkspec](https://apkspec.readthedocs.io/) | Unofficial split-APK bundle specification |
| [Ultimate Social](https://www.iconfinder.com/iconsets/ultimate-social) | Overlay icon set |

## License

[MIT](LICENSE) — Copyright (c) 2015 Jingang Guo, KK; Copyright (c) 2023 1595901624; Copyright (c) 2026 nkh0472.
