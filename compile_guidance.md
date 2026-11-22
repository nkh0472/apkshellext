# ApkShellExt Build Guide (compile_guidance.md)

> This document records the **complete, actually-executed** procedure for going from a
> fresh clone to a working `ApkShellext2.dll` (with 19 localized satellite assemblies)
> and `apkshellextTest.exe`. Every command was run and verified on 2026-11-20 in
> `E:\src\apkshellext` — upstream 3.4.5 (`076f182`) plus the local modernization
> commits described in §5, which are part of this checkout. Copy them verbatim,
> adjusting drive/paths.
>
> This checkout's build system was **modernized to SDK-style csproj**
> (§5), which is what makes both build routes work with **zero extra MSBuild
> properties**. Companion document: [中文对照版 compile_guidance.zh-CN.md](compile_guidance.zh-CN.md).

## 0. Summary

| Item | Value |
|---|---|
| Main artifact | `ApkShellext2\bin\Release\ApkShellext2.dll` (3.4.5.0, strong-named `Pkt=fb82661d1c53098c`, local key) |
| Satellites | `bin\Release\{ar-SA,…,zh-CN,zh-TW}\ApkShellext2.resources.dll` × 19 |
| Test app | `apkshellextTest\bin\Release\apkshellextTest.exe` |
| Packages | `bin\Release\ApkShellext2-3.5.1.7z` — complete install set incl. all 19 language folders (needs 7-Zip; postbuild packs automatically, name = `ApkShellext2-<version>.7z` from `version.txt` with a trailing `.0` dropped) |
| Native deps | `ApkShellext2\LibWebP\libwebp_x86.dll` + `libwebp_x64.dll` — prebuilt libwebp 1.6.0, committed in-repo; rebuilding is optional (§4.5) |
| Route A | .NET SDK **10.0.404** standalone: `dotnet build ApkShellext2\ApkShellext2.csproj -c Release` — one line, no flags |
| Route B | Visual Studio 2022/2026 with *.NET desktop development*: `msbuild -t:restore` + `msbuild -p:Configuration=Release` — no flags either |
| Key gotchas | ① signing key `apkshellext2_hy.snk` is not in the repo — generate one (§4.2); ② a machine can have **zero NuGet sources** configured and restore fails with NU1100 (§4.1); ③ dotnet-CLI output uses preserialized resources that **fail silently when hosted by explorer** (context menu vanishes) — anything installed into the shell must be a VS-route build (§8); the packaging manifest excludes `System.Resources.Extensions.dll` on both routes |

## 1. Environment and the two build routes

The project targets **.NET Framework 4.8** (`net48`) and both projects are
**SDK-style csproj** files (`<Project Sdk="Microsoft.NET.Sdk">`) with `LangVersion=10.0`.
Consequence: any reasonably modern MSBuild can build them — no Visual Studio required.

- **Route A:** standalone .NET SDK (≥ 8; verified on 10.0.404), plain
  `dotnet build`. No extra properties, no shims, no pregeneration steps.
- **Route B:** Visual Studio ("*.NET desktop development*" workload, which carries
  MSBuild + the .NET SDK + Roslyn + NuGet) or Build Tools. Plain `msbuild` commands,
  also with zero extra properties. Works on VS 2026 (18) and VS 2022 (17.x) alike.
  The .NET Framework 4.8 targeting pack helps but is not strictly required on
  every machine (see §4.4).

The compiled IL is the same, but the resource serialization format differs by route and
that is not cosmetic: only the VS-route output is safe to install into Explorer.
Ship from Route B, use Route A for development — packaging converges to one identical
31-file archive either way (§8).

Reference environment for the example paths below (adjust to your machine):
Windows x64, repo at `E:\src\apkshellext`, toolchain at `E:\src\apkshellext-dep`,
7-Zip at the non-standard path
`D:\Software\7-Zip\` (discoverable via registry `HKLM:\SOFTWARE\7-Zip` → `Path`;
postbuild finds it automatically). v4.8 reference assemblies present at
`C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8`.

## 2. Clone the repository

Default: clone straight from GitHub.

```powershell
git clone https://github.com/nkh0472/apkshellext.git E:\src\apkshellext
```

Only if github.com is unreachable or too slow, use the githubfast mirror instead
(same rule applies to the submodules in §3):

```powershell
git clone https://githubfast.com/nkh0472/apkshellext.git E:\src\apkshellext
```

## 3. Submodules (2 of them)

`.gitmodules` points at github.com, so the plain command works whenever the clone
above was direct:

```powershell
git -C E:\src\apkshellext submodule update --init --recursive --jobs 4
```

If the network cannot reach github.com, do the same thing through a one-shot mirror
rewrite (`-c` keeps the `insteadOf` mapping to this command only):

```powershell
git -C E:\src\apkshellext -c url."https://githubfast.com/".insteadOf="https://github.com/" submodule update --init --recursive --jobs 4
```

Only one submodule is left, and it is genuinely compiled into the build:

| Submodule | Upstream | Notes |
|---|---|---|
| `ApkShellext2\PNGDecrush` | nkh0472/PNGDecrush @ `e926128` | source-level reference |

Notes:
- **PlistCS is not a submodule any more** — upstream (kkguo/PlistCS @ `0fc69e7`, a fork of
  animetrics/PlistCS) is unmaintained, so its single source file was absorbed into the tree as
  `ApkShellext2\PlistCS\PlistCS\Src\Plist.cs` and now carries local hardening (depth/index caps,
  per-call parser state — see `ApkShellext2\PlistCS\NOTICE.txt`). It no longer needs
  `git submodule update`.
- **WebP-wrapper is not a submodule any more** — upstream (JosePineiro/WebP-wrapper @
  `79e4234`) is kept as a single vendored file, `ApkShellext2\WebP-wrapper\WebPWrapper.cs`
  (MIT, provenance and license in `ApkShellext2\WebP-wrapper\NOTICE.txt`), because this repo
  carries local hardening in it (decode-size bound, install-directory native-library preload).
  It no longer needs `git submodule update`; runtime still needs the committed
  `LibWebP\libwebp_x86/x64.dll` files.
- **SharpZipLib is not a submodule any more** — upstream (icsharpcode/SharpZipLib v1.4.2
  @ `33f64eb`) was never used as a general zip library here: only its read-only container
  reader was, so zip containers are now read with the BCL `System.IO.Compression.ZipArchive`
  behind `ApkShellext2\ZipSafety.cs` (≤ 65535 entries, per-entry byte/ratio caps, legacy
  name encoding, precise lookup). The pinned PNGDecrush source still names three
  SharpZipLib types by full name, so `ApkShellext2\SharpZipLibShim.cs` provides exactly
  those (zlib/CRC-32 on `System.IO.Compression`); nothing else of SharpZipLib is compiled.
  Removed: the submodule, the `.gitmodules` stanza, the csproj glob, its `LICENSE.txt`.
- **SharpShell is not a submodule** — its sources (2.7.1) are vendored directly under
  `ApkShellext2\SharpShell\` and compiled by the csproj (minus dead code, §5.1).
- **SVG is not a submodule any more** — upstream (vvvv-github/SVG **v3.5.0** @ `9d564f4`) was
  absorbed at `ApkShellext2\SVG\Source\` together with its Roslyn generator at
  `SVG\Generators\`; the generator still runs automatically as a compiler analyzer (§5).
  Provenance, license and the local trims (ExCSS, and the unused SVG subtrees) are recorded in `ApkShellext2\SVG\NOTICE.txt`;
  its `bin\`/`obj\` now live inside the repo tree and are covered by the root `.gitignore`.

## 4. Toolchain and one-time setup

### 4.1 Install the .NET SDK (Route A only) — and make sure NuGet has a source

```powershell
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile 'E:\src\apkshellext-dep\dotnet-install.ps1' -UseBasicParsing
& 'E:\src\apkshellext-dep\dotnet-install.ps1' -Channel 10.0 -InstallDir 'E:\src\apkshellext-dep\dotnet-sdk-10' -NoPath
# Channel 10.0 resolves to 10.0.404 as of this writing. Any SDK >= 8 works (analyzer needs Roslyn >= 4.8).
```

**Check the NuGet sources once.** A user-level `NuGet.Config` whose `<packageSources>`
section exists but is *empty* silently disables the implicit nuget.org default, and
restore dies with `error NU1100: 无法解析 net48 的 "System.Resources.Extensions (>= 6.0.0)"`
(or the first PackageReference it tries):

```powershell
& E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe nuget list source   # must show nuget.org
# If it shows nothing:
& E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe nuget add source https://api.nuget.org/v3/index.json -n nuget.org
```

This bites both routes (VS shares the same user config). The old repo layout needed no
restore at all (HintPath DLLs in `packages\`), so a source-less machine never noticed —
the SDK-style project restores System.Memory/etc. from nuget.org instead (§5.2).

### 4.2 Signing key snk (generate locally — the wrong key breaks the VS route)

The csproj sets `SignAssembly=true`, `AssemblyOriginatorKeyFile=apkshellext2_hy.snk`.
The official private key is not published with the repo and `*.snk` is gitignored.
The README says: *"Release builds sign with `ApkShellext2\apkshellext2_hy.snk` —
generate your own key with `sn -k`"*. **Generating your own key is enough** (public key
differs from official releases — the output is for local use only and can never
impersonate the official signed build).

`sn -k` (a Windows SDK tool) works, but you don't need any SDK for it — stock Windows
PowerShell 5.1 can produce the *same* key. Use this exact form:

```powershell
& C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -Command "
  `$cp = New-Object Security.Cryptography.CspParameters
  `$cp.ProviderType = 1   # PROV_RSA_FULL
  `$cp.KeyNumber    = 2   # AT_SIGNATURE  <-- this is the part that matters
  `$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider(`$cp)
  [IO.File]::WriteAllBytes('E:\src\apkshellext\ApkShellext2\apkshellext2_hy.snk', `$rsa.ExportCspBlob(`$true))"
# 596 bytes; header should read 07 02 00 00 00 24 00 00 "RSA2" (aiKeyAlg=0x2400 CALG_RSA_SIGN)
```

**Trap:** the naive
`New-Object System.Security.Cryptography.RSACryptoServiceProvider 1024` defaults to a
*key-exchange* key (`aiKeyAlg=0xa400`). Roslyn/csc happily signs with it (dotnet route
all green), but the classic `al.exe` that signs **satellite assemblies under Visual
Studio's MSBuild** refuses: `ALINK : error AL1078 … 未知错误(8013141e)`. If you see
AL1078 on the VS route, regenerate the snk (gotcha 13) before suspecting your VS
install.

### 4.3 7-Zip (optional; only affects the postbuild packaging step)

`postbuild.cmd` searches: `%ProgramFiles%\7-Zip` and `%ProgramFiles(x86)%\7-Zip` →
`HKLM:\SOFTWARE\7-Zip` (InstallPath and Path values, **in both the 64-bit and 32-bit
registry views via `reg /reg:64` / `/reg:32`** — classic `Bin\MSBuild.exe` is 32-bit and
a bare query would miss a 64-bit install, gotcha 14) → `where 7z`. If nothing is
found it only prints a **WARNING** and skips packaging — the DLL output is unaffected.
To produce the `.7z` packages, install 7-Zip by any means, e.g.:

```powershell
winget install 7zip.7zip
```

### 4.4 .NET Framework 4.8 reference assemblies

Needed by every net48 build. Present on current Windows at
`C:\Program Files (x86)\Reference Assemblies\...\.NETFramework\v4.8`, or via the VS
component *".NET Framework 4.8 SDK / Targeting Pack"* (bundled in the *.NET desktop
development* workload). If neither exists, the SDK falls back to the
`Microsoft.NETFramework.ReferenceAssemblies` NuGet package automatically in some SDK
versions; do not rely on that — check the folder exists.

### 4.5 LibWebP native dependency (optional rebuild)

WebP support is native code. `ApkShellext2\WebP-wrapper\WebPWrapper.cs` (vendored in-tree,
compiled straight into the main assembly) P/Invokes the DLL names **`libwebp_x86.dll` /
`libwebp_x64.dll`** — hard-coded in its `[DllImport]` attributes — and picks between them
at runtime with `switch (IntPtr.Size)`. The shell extension runs in-process inside its
host: 64-bit `explorer.exe` loads the x64 DLL, a 32-bit host process (32-bit file dialogs,
legacy apps) loads the x86 one. Both files therefore have to sit in the install directory
under exactly those names.

The two DLLs are **committed prebuilt** under `ApkShellext2\LibWebP\`: official **libwebp
v1.6.0** sources plus one local patch — `build\libwebp\sharpyuv-static.patch` links
libsharpyuv *statically* into libwebp, because an official Windows build would additionally
require a `libsharpyuv.dll` whose single fixed file name cannot serve both bitnesses from
one folder. Both shipped DLLs import only `KERNEL32.dll` (static CRT, static libsharpyuv).
They carry no VERSIONINFO resource — `FileVersion` is empty by design; `WebPGetDecoderVersion()`
returns `0x010600`. The csproj copies them to the output root as `Content` items and
postbuild packs them (§8).

**A normal compile needs nothing from this section.** Rebuild only to change the libwebp
version (feature or security fix) or when the committed binaries do not cover your target
platform.

The rebuild kit lives in `build\libwebp\`: read its `README.md` first (rationale + pinned
ABI facts); `build-libwebp.ps1` does the work.

| Step | Command (from `build\libwebp\`) | Needs |
|---|---|---|
| Fetch + patch v1.6.0 sources | `.\build-libwebp.ps1 -Fetch` | Git; `-Mirror` selects a GitHub mirror |
| Build the x64 DLL — **gcc or MSVC, either works** | `.\build-libwebp.ps1 -Gcc64 -GccRoot <root>` (gcc route) or `.\build-libwebp.ps1 -Msvc64 -GccRoot <root>` (MSVC route) | gcc: MSYS2 ucrt64 (gcc+CMake+Ninja), no Visual Studio required. MSVC: VS with the **Desktop development with C++** workload — the *.NET desktop development* workload of §1 has **no C++ compiler** |
| Build the x86 DLL — **MSVC only** | `.\build-libwebp.ps1 -Msvc32 -GccRoot <root>` | same prerequisites as MSVC x64; the script has no gcc-x86 mode |
| Decode smoke test | `.\build-libwebp.ps1 -Smoke <build-dir>` | gcc from `-GccRoot`; point `<build-dir>` at the `-Gcc64` output |
| Replace the repo DLLs | `.\build-libwebp.ps1 -Deploy` | copies `lw-build-{x64,x86}-msvc\libwebp.dll` → `ApkShellext2\LibWebP\libwebp_x{64,86}.dll` |

What matters in practice:

- **`-GccRoot` selects the tool root**, any folder whose `bin\` holds `gcc.exe`,
  `cmake.exe`, `ninja.exe` (MSYS2 ucrt64 is the usual supplier) — the **MSVC modes take
  CMake/Ninja from there too**, so both routes pass it. The gcc route needs no Visual
  Studio; the MSVC modes run `cl` inside a `VsDevCmd -arch=…` environment.
- **Static CRT is mandatory on the MSVC route** (`/MT`, set via
  `CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded`); the script inspects the built DLL's import
  table and fails if `VCRUNTIME*/MSVCP*` appears — the shipped DLLs must not depend on a
  redist being installed.
- **`-Deploy` only harvests the MSVC outputs** (`lw-build-x64-msvc`,
  `lw-build-x86-msvc`). A gcc-built x64 must be copied to
  `ApkShellext2\LibWebP\libwebp_x64.dll` by hand. And note MSYS2 gcc output links the
  UCRT api-set DLLs (present by default on Windows 10+), while the in-repo DLLs — MSVC
  `/MT` builds — import `KERNEL32.dll` only; prefer the MSVC route for binaries that will
  actually ship.
- **Smoke test** (`smoke.c`): encodes a synthetic image and decodes it back through the
  exports the wrapper actually calls (`WebPGetInfo`, `WebPDecodeBGRInto`,
  `WebPDecodeBGRAInto`, `WebPGetFeatures`). `-Smoke` compiles it with gcc against the
  build dir's import lib, writes `smoke.exe` next to the freshly built `libwebp.dll` and
  prints `SMOKE PASS`. The ABI rests on the decoder ABI major byte — the wrapper passes
  `0x0208`, libwebp 1.6.0 has `0x0210`; the compat rule accepts any same-major version.
  Details in `build\libwebp\README.md`.
- **File names are load-bearing.** A rebuild overwrites the two files in
  `ApkShellext2\LibWebP\` as `libwebp_x86.dll` / `libwebp_x64.dll` — the P/Invoke names in
  the wrapper are hard-coded.
- The upstream WebP-wrapper docs (`BUILDING.md`, `Compile dll.txt`, not vendored here) give
  the upstream recipe — nmake + `Makefile.vc`, pinned to libwebp 1.2.1, which predates
  libsharpyuv entirely. That recipe does not match what this repo ships; use
  `build\libwebp\` for rebuilds.

## 5. What this checkout changed vs. upstream (the modernization)

Upstream ships **old-style** csproj files. Building them with `dotnet msbuild` needs
three ugly workarounds (SVG source-generator pregeneration, a satellite-assembly shim
for `MSB4064`, manual NuGet package backfill). This checkout rewrites both csproj files
as **SDK-style** instead, which removes all three at the root — that is why every
command in §6–§7 is flag-free. All of it lands on top of upstream 3.4.5
(HEAD `076f182`) as a set of focused commits — build-system migration
(`ApkShellext2.sln`, both csproj files, `Resources\postbuild.cmd`), dead-code removal,
donation-UI removal, typo fixes, plus this documentation. As of writing they are
**committed locally but not pushed** — push to your remote (or carry this checkout)
before expecting a fresh clone elsewhere to build with the flag-free commands below.

### 5.1 Legacy workaround → SDK-style replacement

| Upstream / legacy need | Now |
|---|---|
| SVG v3.5.0 element metadata generated by hand before the build (`pregen.ps1` → `SVG\Source\Generated\*.g.cs`) | `<ProjectReference Include="SVG\Generators\Svg.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` — the same upstream generator runs **inside Roslyn** on every compile, on both toolchains |
| Satellites crash under coreclr MSBuild (`MSB4064` AL/ToolPath) → external `dotnet-satellites.targets` shim + 2 command-line flags | SDK-style projects get `GenerateSatelliteAssembliesForCore=true` automatically under coreclr (Csc-based generation); FullFramework MSBuild uses real `al.exe`. Nothing to pass |
| `packages\System.Runtime.CompilerServices.Unsafe.6.0.0` fetched by hand (HintPath project, no restore) | `PackageReference`s restore everything: System.Memory 4.5.5 (drags Buffers + Numerics.Vectors), System.Runtime.CompilerServices.Unsafe 6.0.0 (direct ref overrides the transitive 4.5.3 — identical to the old HintPath resolution), and System.Resources.Extensions 6.0.0 **only when** building under coreclr |
| `-p:GenerateResourceUsePreserializedResources=true` typed by the user | inside the csproj: `Condition="'$(MSBuildRuntimeType)' == 'Core'"` — dotnet CLI uses preserialized resources, VS uses classic ones, automatically (see §8 for the one runtime consequence) |
| `apkShellextService` dead entry in the .sln (directory never existed) | entry deleted from the .sln |
| csproj `Content`-references `SharpShellNativeBridge32/64.dll`, which upstream deleted in `4f30b7c`; the SharpPropertySheet property-page machinery referencing them is dead code | those `Content` items are gone, and the dead consumer directories `NativeBridge\`, `SharpPropertySheet\` and `SharpDeskBand\` have been deleted from the tree; `Helpers\RegAsm.cs` and the vendored `Properties\AssemblyInfo.cs` remain on disk but were never compile inputs. No compiled file references those namespaces |

### 5.2 Other modernization notes

- The legacy `packages\` folder is now **unused** (leftover; harmless). Do not re-add
  HintPath references.
- `AppendTargetFrameworkToOutputPath=false` keeps the flat `bin\Release` layout that the
  install scripts, `postbuild.cmd` and this guide all assume (SDK default would be
  `bin\Release\net48\`).
- `GenerateAssemblyInfo=false`: `Properties\AssemblyInfo.cs` stays the single source of
  version/Guid/ComVisible attributes.
- `EnableDefaultCompileItems/EmbeddedResourceItems/NoneItems=false` with explicit globs —
  the vendored trees contain many files that must *not* be compiled
  (their own AssemblyInfo, samples, tools, WinUI/WPF variants).
- The legacy `Debug2` configuration is kept (`Configurations=Debug;Debug2;Release`); it
  behaves like Debug. The generator ProjectReference maps it to the generator's own
  Debug output via `SetConfiguration`.

### 5.3 Zero-warning baseline

The upstream tree carried seven long-lived compiler warnings; all are resolved, and both
routes now build with `0 warnings / 0 errors`:

| Warning | Where | Resolution |
|---|---|---|
| CS0219 ×4 (`height`, `units`) | `AabReader` / `ApkQuickReader` legacy vector fallback | dead `android:width` unit parse removed — it stored values nothing read (the canvas comes from the clamped viewport) |
| CS0618 `SvgDocument.Open` overload | vendored `SVG\Source\Web\SvgHandler.cs` | ASP.NET handler excluded from compilation (`Compile Remove`); it was the tree's only `System.Web` consumer, and the main dll's reference list shrank accordingly — the rest of the SVG tree is untouched |
| CS0618 `SvgPathSegment.Start` | `VectorDrawableRender.Convert2Path` | marked obsolete in Svg 3.5.0 with no successor API; the single call site is wrapped in `#pragma warning disable CS0618` with a comment |
| CS0108 `ApkXMLElementChunk.Node` hides base | `ApkChunk.cs` | explicit `new` keyword (hiding behaviour unchanged) |
| CS0649 `ApkXMLCDATAChunk.dataid` never assigned | `ApkChunk.cs` | pinned `= 0` with a `TODO(upstream)` note — the CDATA string index was never parsed from the stream |
| CS0162 unreachable code | `apkshellextTest\Form1.cs` | scratch block below an early `return;` wrapped in `#if PARKED_EXPERIMENTS` (test playground kept out of compilation) |

## 6. Build the main project

### Route A — dotnet CLI (development output only — do not install this output into Explorer, §8)

```powershell
Set-Location E:\src\apkshellext
E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe build ApkShellext2\ApkShellext2.csproj -c Release --nologo
```

One line. Restore runs implicitly. Expected: `EXIT=0` with **zero compiler warnings**
(the tree's warning baseline is clean, §5.3) and `ApkShellext2 -> …\bin\Release\ApkShellext2.dll`,
followed by the 7z packaging step (§8).

### Route B — Visual Studio / Build Tools MSBuild

```powershell
# Locate MSBuild (skip if you know the path):
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msb = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1

Set-Location E:\src\apkshellext
& $msb ApkShellext2\ApkShellext2.csproj -t:restore -p:Configuration=Release -nologo -v:m
& $msb ApkShellext2\ApkShellext2.csproj -p:Configuration=Release -nologo -v:m
```

Two commands (restore is *not* implicit for classic MSBuild), still zero custom
properties. Mind the colon: classic MSBuild wants `-v:m`, not `-v m` (gotcha 3). If
restore/build fails with `MSB4236 找不到指定的 SDK“Microsoft.NET.Sdk”`, your VS install
is broken — gotcha 2. `Bin\MSBuild.exe` is the **32-bit** launcher (64-bit lives in
`Bin\amd64\`); either works here — the postbuild 7-Zip discovery is explicitly
bitness-proof (gotcha 14). VS 18 additionally carries its own SDK under
`C:\Program Files\dotnet\sdk\`, so Route B needs nothing from Route A.

The whole solution (main + test) also builds in one go on either route:
`dotnet build ApkShellext2.sln -c Release` / `msbuild ApkShellext2.sln -t:restore` +
`msbuild ApkShellext2.sln -p:Configuration=Release`.

CI (`.github/workflows/ci.yml`) splits the SDK on purpose: the `dotnet-build` job
pins 10.0.404 (`latestPatch`), while the `vs-build` job pins 9.0.100
(`latestFeature`) because the runner's Visual Studio 2022 carries MSBuild 17.14,
which cannot load SDK 10+ (that needs MSBuild >= 18.0.0). The VS route exists for
the FullFramework resource pipeline, not for the compiler version.

### Size budget (the CI gate)

```powershell
& .\eng\check-size.ps1
```

Exits 1 when `ApkShellext2.dll`, the 7z package or the 31-file deployment tree grows
past its budget (currently 1,650,000 / 1,100,000 / 3,600,000 B; the current post-T-H
baseline is 1,551,360 / ~926,000 / 3,027,225 B). The ~5% headroom lets routine edits pass while a
newly bundled dependency trips the gate. The tree is counted from the 12 manifest files
plus the 19 satellites, never from a directory glob, so stray pdb or log files in
`bin\Release` cannot affect it. The thresholds are parameters (`-MaxDllBytes` …) —
deliberately lowering one is how you test the gate itself. Phase 2 targets a <=2.5 MB
package and will tighten these numbers, never relax them.

## 7. Build the test project

```powershell
# Route A:
E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe build apkshellextTest\apkshellextTest.csproj -c Release --nologo
# Route B: the same two msbuild lines pointed at apkshellextTest\apkshellextTest.csproj
```

It is a WinForms app referencing the main project (plus one linked file
`..\ApkShellext2\ApkChunk.cs`), unsigned, with no NuGet dependencies. Debug builds work
identically (`-c Debug` / `-p:Configuration=Debug`); the Debug output additionally gets
`debug.bat` (a dev helper copied only in Debug, §8). Never look for
`apkShellextService` — its dead .sln entry was deleted here and never existed on disk.

### 7.1 Automated unit tests (NUnit)

`tests\ApkShellext2.Tests\ApkShellext2.Tests.csproj` holds the unattended tests that pin the
stage-0 safety guards: the version single source, the WebP decode-size limit, the ZipSafety
entry caps, the binary-plist depth guard and the plist parser's per-call state. It is a net48
NUnit 3 project (NUnit3TestAdapter + Microsoft.NET.Test.Sdk). Its only link to the product is
a ProjectReference that passes `Skip7zPackage=true`, so running the tests rebuilds the product
but never invokes the postbuild 7-Zip packaging — the tests work on a machine without 7-Zip.

```powershell
# whole suite (the test project targets net48, so this needs Windows):
E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe test tests\ApkShellext2.Tests\ApkShellext2.Tests.csproj -c Release --nologo

# one fixture, or one test:
#   ... --filter "FullyQualifiedName~ZipSafetyTests"
#   ... --filter "Name~Rejects33NestedArrays"
```

`dotnet test` exits non-zero when any test fails, which is all the CI step needs. If this
machine has no snk (§4.2), add `-p:SignAssembly=false` — the same switch the build step takes,
because the ProjectReference rebuilds the product.

The tests reach the internal guards by reflection instead of `InternalsVisibleTo`. The product
is strong-named with a per-machine key that the repository does not carry, and an
`InternalsVisibleTo` without the matching public key is a compile error (CS1726); reflection
keeps the product sources untouched and the suite portable to a machine that signs and to one
that does not. Test output lands under `tests\ApkShellext2.Tests\bin\<Configuration>\net48`, so
it never becomes part of the `bin\Release` publish layout that §8 and `eng\check-layout.ps1`
describe.

Every fixture is built inside the test (zip archives with `System.IO.Compression`, binary
plists with a small writer in `TestSupport\BinaryPlistBuilder.cs`), so the suite is hermetic:
it does not read the sample library under `F:\Backups\apks\` and it never touches the network.

CI: `.github\workflows\ci.yml`, job `dotnet-build`, step **Run unit tests** — after
**Build Release (warnings as errors)** and **Check publish layout**, before the artifact
upload. The `vs-build` job does not run the tests. `-warnaserror` is passed to the product
build only; the test project compiles with `TreatWarningsAsErrors=false` so a test-side
analyzer suggestion can never turn into a red build.

## 8. Publish layout, postbuild and deployment notes

**The layout is assembled by MSBuild — postbuild only packages.**

- **MSBuild side** (`ApkShellext2.csproj`): `install.bat`, `install_user.bat`,
  `uninstall.bat`, `uninstall_user.bat`, `restart_explorer.bat` and both `libwebp_*.dll`
  are `Content` items with `CopyToOutputDirectory=PreserveNewest`; `debug.bat` likewise
  under `Condition="'$(Configuration)'=='Debug'"`. `CopyFilesToOutputDirectory` copies
  them before any packaging runs, so `bin\Release` is already a complete publish folder.
  Every item carries `TargetPath=<bare filename>` — without it the copy recreates the
  `Resources\`/`LibWebP\` source subfolders inside the output (gotcha 5). The two
  `libwebp_*.dll` are prebuilt in-repo binaries (§4.5); no native toolchain is needed for
  a normal build.
- **Packaging hook**: implemented as `<Target Name="PackageWith7z" AfterTargets="PostBuildEvent">`
  calling the script via `Exec`, *not* the legacy `<PostBuildEvent>` property — in an
  SDK-style project a property assignment expands its macros at **evaluation time**,
  when `$(ProjectDir)`/`$(TargetDir)` are still undefined, so the old one-liner bakes
  into `call "Resources\postbuild.cmd" "" ""` (gotcha 4).
- **`Resources\postbuild.cmd`** (Release-only; Debug exits immediately): deletes a stale
  `debug.bat`, locates `7z.exe` (§4.3), derives the package name with
  `eng\package-name.ps1` from `version.txt` (`3.5.1.0` → `ApkShellext2-3.5.1.7z`;
  the same rule `check-layout.ps1`/`check-size.ps1` dot-source), deletes stale
  `*.7z` (the old unversioned name and any package from a previous version),
  then packs the new name
  from an **explicit manifest** — the twelve root files (`ApkShellext2.dll`, the five
  bats, two `libwebp_*` binaries, `System.Memory.dll`, `System.Buffers.dll`,
  `System.Runtime.CompilerServices.Unsafe.dll`, `System.Numerics.Vectors.dll`) plus every
  locale folder that exists (`ar-SA` … `zh-TW`). A missing manifest entry **fails the
  build**, so a short archive cannot ship silently. `System.Numerics.Vectors.dll` is a
  required app-local dependency, not a toolchain artifact: inside COM hosts the Fusion
  Load context probes the host app base (`C:\Windows`), so `TypeDescriptor` cannot bind
  Svg.NET's numeric converters unless the file sits next to `ApkShellext2.dll` — without
  it every numeric SVG attribute (gradient coords, offsets, …) fails with
  `FileNotFoundException` and adaptive icons silently render as blank/fragmented tiles
  (root cause of the 2026-11-21 broken-icon report). One dll is an annotated *exclusion*,
  never packed whatever sits in the output folder: `System.Resources.Extensions.dll`
  (see below). The main archive is a complete install set: there are no per-language
  archives (an earlier split shipped `<locale>.7z` separately; that made an install from
  the main archive alone silently English). No 7-Zip ⇒ WARNING + skip, build still
  succeeds.

**Why the VS route is the shipping route — preserialized resources.** Under coreclr
MSBuild (dotnet CLI) the csproj enables preserialized resource generation, embedding
object resources as `System.Resources.Extensions`-encoded blobs. In a standalone
process that works (the test exe resolves the dll normally), but a **shell-extension
host like explorer** probes assembly dependencies only in its own module directory and
the GAC — and `System.Resources.Extensions` is neither, so resolution fails and it
fails *silently*: the context-menu handler dies while fetching its item icons, and
explorer draws the rest of the menu without the extension's items. Observed failure
signature of a dotnet-CLI build installed into the shell: **custom file-type icons keep
working (the icon handler never touches resx object resources) while the context menu
disappears entirely** — reproduced against both builds, and fixed by swapping the
identical assembly built on the VS route (same path, same strong name, no
re-registration needed). A Visual Studio (FullFramework) build uses classic
BinaryFormatter-style resources, never references the package, and matches upstream
packages exactly. Therefore: **ship VS-route assemblies; keep dotnet-CLI output for
development.** `postbuild.cmd` lists the package dll as an annotated exclusion so
neither archive can smuggle it in — and do not hand-copy it into a deployment folder,
it cannot rescue a preserialized build inside a host process.

Deployment (on the target machine, as the user who owns the shell extension): extract
`ApkShellext2-3.5.1.7z` — it already carries the culture folders next to the DLL — then run
`install.bat` (elevated, machine-wide) or `install_user.bat` (per-user;
both register via regasm and NGEN the assembly — 3.4.5 fixed NGEN in these scripts).
`restart_explorer.bat` reloads the shell. Uninstall with the matching `uninstall*.bat`.
Because the local snk differs from upstream's, never mix DLLs from a local build with an
official package in the same GAC/registration state.

## 9. Verifying the build

1. **Satellite directories:** `bin\Release` must contain all 19 culture folders
   (`ar-SA de-DE el-GR es-ES fa-IR fr-FR he-IL it-IT ja-JP ko-KR my-MM nl-NL pl-PL pt-PT
   ru-RU tr-TR uk-UA zh-CN zh-TW`), each with `ApkShellext2.resources.dll`.
2. **Identity via reflection** — run under **Windows PowerShell 5.1**
   (`C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`); PowerShell 7 (.NET
   Core) cannot reflect these .NET Framework assemblies properly. Format the token bytes
   with `{0:x2}` — `-join`-ing the raw byte array yields decimal digits, not the hex
   token:

   ```powershell
   $ma = [Reflection.Assembly]::LoadFrom('E:\src\apkshellext\ApkShellext2\bin\Release\ApkShellext2.dll')
   $ma.GetName() | Format-List Name, Version   # ApkShellext2  3.4.5.0
   (($ma.GetName().GetPublicKeyToken() | % { '{0:x2}' -f $_ }) -join '')   # fb82661d1c53098c  (local key)
   $sat = [Reflection.Assembly]::LoadFrom('E:\src\apkshellext\ApkShellext2\bin\Release\zh-CN\ApkShellext2.resources.dll')
   $sat.GetName() | Format-List Version, CultureName   # 3.4.5.0, zh-CN — same Pkt as main
   ```

3. **ResourceManager round-trip** (proves satellites are discoverable, not just present):

   ```powershell
   $rm = New-Object System.Resources.ResourceManager('ApkShellext2.Properties.Resources', $ma)
   $rm.GetString('btnOK', [cultureinfo]'zh-CN')              # → 确定   (from satellite)
   $rm.GetString('btnOK', [cultureinfo]::InvariantCulture)   # → OK     (neutral, inside main dll)
   ```

4. **Complete publish layout in `bin\Release`:** the twelve manifest files —
   `ApkShellext2.dll(.pdb)`, the five bats, `libwebp_x86/x64.dll`,
   `System.Memory/System.Buffers/System.Runtime.CompilerServices.Unsafe/System.Numerics.Vectors` —
   plus the nineteen culture folders and — when 7-Zip was found —
   `ApkShellext2-3.5.1.7z` (~1 MB). `7z l` must list **exactly 31 files on both routes**:
   the 12 root entries (no `System.Resources.Extensions.dll` — packaging
   exclusion even when a dotnet-CLI build left it in the folder) plus 19
   `xx-XX\ApkShellext2.resources.dll`
   satellite entries; no `Resources\`/`LibWebP\` subfolders may exist in
   `bin\Release`.
5. **Debug layout:** `bin\Debug` root has `debug.bat` + bats + libwebp and **no `.7z`**.

## 10. Gotchas checklist

1. **NU1100 on restore** → the machine has **zero NuGet sources** (an empty
   `<packageSources>` in user `NuGet.Config` kills the implicit default).
   `dotnet nuget list source` must show nuget.org; add it per §4.1.
2. **`MSB4236 / MSB4276: 无法解析 SDK“Microsoft.NET.Sdk”` from VS's MSBuild** (often with
   a silent `vswhere`) = broken/partial VS install — `MSBuild\Sdks` is empty. Proper fix:
   VS Installer → **Repair**, or reinstall with the *.NET desktop development* workload.
   After Repair or a clean reinstall, vswhere resolves and restore/build run with zero
   extra parameters.
3. **Classic MSBuild switches take a colon**: `-v:m`, `-p:Configuration=Release`;
   `-v m` (space form, fine for `dotnet build`) is rejected by `MSBuild.exe`.
4. **Do not re-implement the packaging hook as a `<PostBuildEvent>` property** — in
   SDK-style projects its macros expand at evaluation time (empty `$(TargetDir)` etc.)
   and the script silently receives `"" ""`. Keep the `AfterBuild`-style `Exec` target.
5. **`Content` + `CopyToOutputDirectory` recreates source subfolders** unless every item
   sets `TargetPath=<bare filename>`; and the 7z wildcard *silently ignores* a pattern
   matching nothing — the historical `*.bat` bug built an archive without bats and
   exited 0. That is why the pack step now uses an explicit file manifest and fails on
   a missing entry (§8): do not reintroduce wildcards, and still verify `7z l` after
   touching layout items.
6. **Never install dotnet-CLI output into Explorer**: its preserialized object
   resources fail to load inside shell host processes (file-type icons keep working,
   the context menu disappears silently). Ship and deploy VS-route builds only;
   packaging excludes `System.Resources.Extensions.dll` on both routes (§8).
7. **snk**: gitignored; generate locally (§4.2). Public key ≠ official builds.
8. **Legacy leftovers are intentional**: the `packages\` folder is unused now; the
   NativeBridge native DLLs are missing upstream by design (`4f30b7c`) and their dead
   consumer code has been deleted from the tree (§5.1). Do not “fix” either.
9. **SVG is in-tree now** (absorbed at `ApkShellext2\SVG\Source\`, §3) — the generator's
   `bin\`/`obj\` are covered by the root `.gitignore`; the one remaining submodule
   (PNGDecrush) still produces its own `bin\`/`obj\` if built standalone.
10. **Renaming/moving the SDK folder fails with “Access denied”** while MSBuild
    node-reuse workers are alive (`MSBuild.exe … /nodemode:2 /nodereuse:True` started
    from the SDK dir; `dotnet build-server shutdown` does not always catch them).
    Find the holder with `Get-CimInstance Win32_Process | ? CommandLine -match '<sdk-root>'`
    and stop it first.
11. **`dotnet` on PATH may be runtime-only.** Verify with `dotnet --version`; if it
    reports no SDK, use the installed SDK's full path (as all commands here do).
12. **Four files are intentionally not upstream** (§5): `.sln`, both csproj
    files, `postbuild.cmd`. Reverting any of them to its upstream version re-introduces
    the legacy workarounds (flags, shim, pregen, manual packages).
13. **`ALINK : error AL1078 对程序集签名时出错 - 未知错误(8013141e)` on the VS route** =
    the snk is a *key-exchange* blob (`aiKeyAlg=0xa400`, what a bare
    `RSACryptoServiceProvider 1024` produces). csc ignores the field, `al.exe` refuses it.
    Regenerate with `sn -k` or the §4.2 PowerShell snippet (`KeyNumber=Signature`,
    header `… 00 24 00 00 "RSA2"`). Not a VS-install problem, not a repo problem.
14. **VS's command-line `Bin\MSBuild.exe` is 32-bit**, so its Exec children see the
    WOW64-redirected registry (`HKLM\SOFTWARE\7-Zip` “missing”) and `%ProgramFiles%`
    pointing at `Program Files (x86)` — a bare `reg query` in postbuild then silently
    skips packaging while the build stays green. `postbuild.cmd` therefore queries both
    views explicitly (`/reg:64`, `/reg:32`); keep those flags if you touch the script.
    (The VS IDE itself builds in a 64-bit engine, which is why this only bites
    command-line builds of the x86 launcher.)

## 11. Releasing

Releases are published **only** by `.github/workflows/release.yml`; a local build is a
development artifact and must never be attached to a GitHub release by hand. The
workflow builds the FullFramework route (§6 Route B), runs both gates and the unit
tests, and publishes an existing annotated tag.

```bash
git tag -a v3.5.1 -m "ApkShellext2 3.5.1"   # annotated, never lightweight
git push origin v3.5.1                      # the lead pushes; contributors do not
```

The tag must be `v` + `version.txt` without its trailing `.0`, the same rule
`eng/package-name.ps1` uses for the package name. Every release is signed: the
workflow restores `ApkShellext2\apkshellext2_hy.snk` from the
`APKSHELLEXT2_SNK_B64` secret and fails when the secret is missing.

To rehearse a release without publishing anything, dispatch the workflow with the
tag and `dry_run: true` (the default for dispatches). The job prints version, dll and
7z sizes with their SHA-256, the previous tag and the test count to the job summary,
but creates no GitHub release. The checks live in `eng/release-checks.ps1`, so the
same verdict can be reproduced locally:

```powershell
pwsh -File eng\release-checks.ps1 -Tag v3.5.1
```

The script rejects a tag whose numeric part disagrees with `version.txt`, a tag that
is not newer than the latest published tag, a lightweight tag, a tag commit that is
not an ancestor of `origin/main`, and a missing package or language directory.

A dry run builds the tag's tree, because that tree is the release content. To
rehearse a tag that is still being prepared against the current `main`, add
`source_ref=main` (only allowed together with `dry_run: true`): the tag stays the
release target and every check still runs against it, only the build input changes.

A real run creates the release with `gh release create <tag>`: the title is the
version without `v`, notes come from `release-notes/<tag>.md` when that file exists
and are generated from the commits otherwise, and a tag containing `-` is marked as
a prerelease. The asset name and size are asserted afterwards, so a failed upload
fails the job instead of leaving a broken release behind; re-run the dispatch after
fixing the cause (the version and ordering checks refuse to republish an already
published version from a tag push).

## Appendix A — one-shot reference script (Route A)

```powershell
$R = 'E:\src\apkshellext'
$DOTNET = 'E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe'

# --- 0. prerequisites (once): submodules §3, nuget source §4.1, snk §4.2 ---
& $DOTNET nuget list source | Select-String nuget.org -Quiet | % { if (-not $_) { throw 'no nuget.org source - see §4.1' } }
if (-not (Test-Path "$R\ApkShellext2\apkshellext2_hy.snk")) { throw 'generate the snk first - see §4.2' }

# --- 1. main + test (restore implicit; analyzer generator runs inside compile) ---
Set-Location $R
& $DOTNET build ApkShellext2\ApkShellext2.csproj     -c Release --nologo; if ($LASTEXITCODE) { throw 'main build failed' }
& $DOTNET build apkshellextTest\apkshellextTest.csproj -c Release --nologo; if ($LASTEXITCODE) { throw 'test build failed' }

# --- 2. verify (must run under Windows PowerShell 5.1, see §9) ---
$sats = Get-ChildItem "$R\ApkShellext2\bin\Release" -Directory | Where-Object Name -cmatch '^[a-z]{2}-'
"satellites: $($sats.Count) (expect 19)"
$check = @'
$ma = [Reflection.Assembly]::LoadFrom('E:\src\apkshellext\ApkShellext2\bin\Release\ApkShellext2.dll')
$n = $ma.GetName()
"MAIN: $($n.Name) $($n.Version) pkt=$((($n.GetPublicKeyToken() | % { '{0:x2}' -f $_ }) -join ''))"
$rm = New-Object System.Resources.ResourceManager('ApkShellext2.Properties.Resources', $ma)
"btnOK zh-CN = " + $rm.GetString('btnOK', [cultureinfo]'zh-CN')      # 确定
"btnOK inv   = " + $rm.GetString('btnOK', [cultureinfo]::InvariantCulture)  # OK
'@
$tmp = "$env:TEMP\apkcheck.ps1"; Set-Content $tmp $check -Encoding UTF8
& C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -ExecutionPolicy Bypass -File $tmp
```

## Appendix B — one-shot reference script (Route B)

```powershell
$R = 'E:\src\apkshellext'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msb = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msb) { throw 'MSBuild not found - repair VS / install the .NET desktop workload (§10 gotcha 2)' }

Set-Location $R
& $msb ApkShellext2.sln -t:restore            -p:Configuration=Release -nologo -v:m; if ($LASTEXITCODE) { throw 'restore failed' }
& $msb ApkShellext2.sln -p:Configuration=Release              -nologo -v:m; if ($LASTEXITCODE) { throw 'build failed' }

# verify the VS-route specifics: NO System.Resources.Extensions.dll in the output,
# archive = 31 files (12 manifest items + 19 satellites)
Test-Path "$R\ApkShellext2\bin\Release\System.Resources.Extensions.dll"   # expect False
& 'D:\Software\7-Zip\7z.exe' l "$R\ApkShellext2\bin\Release\ApkShellext2-3.5.1.7z"   # expect 31 files (adjust 7z path)
```

---

*Companion document: [中文对照版 compile_guidance.zh-CN.md](compile_guidance.zh-CN.md).*
