# ApkShellExt 编译指南(compile_guidance.zh-CN.md)

> 本文是**完整、实际执行过**的流程:从全新克隆到产出可用 `ApkShellext2.dll`(含 19 个语言
> 卫星程序集)。下面每条命令都是构建实际在用的命令;可直接复制,只需调整盘符/路径。
>
> 构建体系是 **SDK 式 csproj**(§5),这是两条编译路线都能
> **零附加 MSBuild 参数**的原因。对照文档:[English compile_guidance.md](compile_guidance.md)。

## 0. 速览

| 项目 | 值 |
|---|---|
| 主产物 | `ApkShellext2\bin\Release\ApkShellext2.dll`(3.4.5.0,强名 `Pkt=fb82661d1c53098c`,本地密钥) |
| 卫星程序集 | `bin\Release\{ar-SA,…,zh-CN,zh-TW}\ApkShellext2.resources.dll` × 19 |
| 单元测试 | `tests\ApkShellext2.Tests` —— `dotnet test`,见 §7 |
| 分发包 | `bin\Release\ApkShellext2-4.0.0.7z` —— 完整安装包,内含全部 19 个语言目录(需 7-Zip;postbuild 自动打包,包名 = `ApkShellext2-<版本>.7z`,版本取自 `version.txt`、末尾 `.0` 去掉) |
| 原生依赖 | `ApkShellext2\LibWebP\libwebp_x86.dll` + `libwebp_x64.dll` —— 预编译 libwebp 1.6.0,已入库;重建为可选项(§4.5) |
| 路线 A | 独立 .NET SDK **10.0.404**:`dotnet build ApkShellext2\ApkShellext2.csproj -c Release` —— 一行命令,零参数 |
| 路线 B | 装了*.NET 桌面开发*负载的 Visual Studio 2022/2026:`msbuild -t:restore` + `msbuild -p:Configuration=Release` —— 同样零参数 |
| 关键坑 | ① 签名密钥 `apkshellext2_hy.snk` 不在仓库里,要自己生成(§4.2);② 机器可能**一个 NuGet 源都没配**,restore 直接 NU1100(§4.1);③ dotnet CLI 产物用的是预序列化资源,**被 explorer 承载时静默失败**(右键菜单消失)—— 凡装进 shell 的必须是 VS 路线产物(§8);打包清单在两条路线上都排除 `System.Resources.Extensions.dll` |

## 1. 环境与两条编译路线

工程目标框架为 **.NET Framework 4.8**(`net48`),两个工程都已改为 **SDK 式 csproj**
(`<Project Sdk="Microsoft.NET.Sdk">`),`LangVersion=10.0`。因此任何足够新的 MSBuild
都能编译,**不依赖 Visual Studio**。

- **路线 A:** 独立 .NET SDK(≥ 8;在 10.0.404 上验证过),直接 `dotnet build`。
  无附加参数、无 shim、无预生成步骤。
- **路线 B:** Visual Studio(装「*.NET 桌面开发*」负载,自带 MSBuild + .NET SDK +
  Roslyn + NuGet)或 Build Tools。直接 `msbuild`,同样零附加参数。VS 2026(18)与
  VS 2022(17.x)皆适用。.NET Framework 4.8 目标包有帮助,但并非每台机器都严格
  必需(见 §4.4)。

两条路线编译出的 IL 相同,但资源序列化格式因路线而异,这不是表面差异:只有 VS
路线的产物才能安全装进 Explorer。出货用路线 B,路线 A 只用于开发 —— 两条路线的
打包最终收敛为同一个 28 文件的包(§8)。

下文示例路径的参考环境(按你的机器调整):Windows x64;仓库在
`E:\src\apkshellext`,工具链在 `E:\src\apkshellext-dep`;7-Zip 装在非标准路径
`D:\Software\7-Zip\`(注册表
`HKLM:\SOFTWARE\7-Zip` → `Path` 可查到,postbuild 会自动找到);v4.8 参考程序集位于
`C:\Program Files (x86)\Reference Assemblies\Microsoft\Framework\.NETFramework\v4.8`。

## 2. 克隆仓库

默认直接从 GitHub 克隆:

```powershell
git clone https://github.com/nkh0472/apkshellext.git E:\src\apkshellext
```

只有 github.com 连不通或太慢时,才改用 githubfast 镜像(§3 的子模块同理):

```powershell
git clone https://githubfast.com/nkh0472/apkshellext.git E:\src\apkshellext
```

## 3. 子模块

`.gitmodules` 里写的是 github.com,上面的克隆走直连时,直接:

```powershell
git -C E:\src\apkshellext submodule update --init --recursive --jobs 4
```

网络到不了 github.com 时,用一次性的镜像改写做同一件事(`-c` 让 `insteadOf` 只作用于
这一条命令,不落盘):

```powershell
git -C E:\src\apkshellext -c url."https://githubfast.com/".insteadOf="https://github.com/" submodule update --init --recursive --jobs 4
```

只剩 1 个子模块,且真实参与编译:

| 子模块 | 上游 | 说明 |
|---|---|---|
| `ApkShellext2\PNGDecrush` | nkh0472/PNGDecrush @ `e926128` | 源码级引用 |

注意:
- **PlistCS 已收编进仓库** —— 上游(kkguo/PlistCS @ `0fc69e7`,属 animetrics/PlistCS 的 fork)已无人维护,
  其唯一源文件就在树内 `ApkShellext2\PlistCS\PlistCS\Src\Plist.cs`,并带有本地加固(深度/索引上限、
  每次调用独立解析状态,见 `ApkShellext2\PlistCS\NOTICE.txt`),与 `git submodule update` 无关。
- **WebP-wrapper 已收编进仓库** —— 上游(JosePineiro/WebP-wrapper @ `79e4234`)只剩一个
  收编文件 `ApkShellext2\WebP-wrapper\WebPWrapper.cs`(MIT,来源与许可见
  `ApkShellext2\WebP-wrapper\NOTICE.txt`),因为本仓库在该文件里带有本地加固(解码尺寸上限、
  从安装目录预加载原生库)。与 `git submodule update` 无关;运行时仍需仓库内已提交的
  `LibWebP\libwebp_x86/x64.dll`。
- **SharpZipLib 完全没有收编** —— 上游(icsharpcode/SharpZipLib v1.4.2 @ `33f64eb`)在这里只承担
  只读容器读取,别无他用,因此 zip 容器由 BCL
  `System.IO.Compression.ZipArchive` 在 `ApkShellext2\Imaging\SafeArchive.cs` 之后读取(条目 ≤ 65535、
  单条目字节/压缩比率上限、旧编码条目名、精确查找)。固定版本的 PNGDecrush 源码仍按全名引用
  三个 SharpZipLib 类型,由 `ApkShellext2\Vendor\SharpZipLibShim.cs` 只提供这三个
  (基于 `System.IO.Compression` 的 zlib/CRC-32);SharpZipLib 其余部分一律不编译。
- **不编译任何外壳扩展框架** —— 四个处理器自己实现外壳接口(`ApkShellext2\Shell\Native\`,§5.5),
  注册器自己写类键、ProgID 与关联键(`ApkShellext2\Registration\`),代码与外壳 ABI 之间没有中间层。
- **SVG 已收编进仓库** —— 上游(vvvv-github/SVG **v3.5.0** @ `9d564f4`)位于
  `ApkShellext2\SVG\Source\`,其 Roslyn 生成器在 `SVG\Generators\`,作为编译器
  analyzer 自动运行(§5)。来源、许可证与本地裁剪(ExCSS、未使用的 SVG 子树)记录在
  `ApkShellext2\SVG\NOTICE.txt`;其 `bin\`/`obj\` 位于仓库树内,由根 `.gitignore` 覆盖。

## 4. 工具链与一次性准备

### 4.1 安装 .NET SDK(仅路线 A)——并确认 NuGet 有源

```powershell
Invoke-WebRequest 'https://dot.net/v1/dotnet-install.ps1' -OutFile 'E:\src\apkshellext-dep\dotnet-install.ps1' -UseBasicParsing
& 'E:\src\apkshellext-dep\dotnet-install.ps1' -Channel 10.0 -InstallDir 'E:\src\apkshellext-dep\dotnet-sdk-10' -NoPath
# 10.0 频道当前解析到 10.0.404(本文写作时)。任何 SDK >= 8 都行(analyzer 需要 Roslyn >= 4.8)。
```

**一次性检查 NuGet 源。** 用户级 `NuGet.Config` 里如果存在 `<packageSources>` 节但
内容为**空**,会静默禁用隐式的 nuget.org 默认源,restore 直接报
`error NU1100: 无法解析 net48 的 "System.Resources.Extensions (>= 6.0.0)"`(或第一个
PackageReference):

```powershell
& E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe nuget list source   # 必须能看到 nuget.org
# 如果什么都没列出来:
& E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe nuget add source https://api.nuget.org/v3/index.json -n nuget.org
```

两条路线都会被咬(VS 共用同一份用户配置):coreclr 下的构建要从 nuget.org restore
System.Resources.Extensions(§5.2),所以第一次编译前源清单就得是对的。

### 4.2 签名密钥 snk(本地生成 —— 生成方式不对会弄坏 VS 路线)

csproj 里 `SignAssembly=true`、`AssemblyOriginatorKeyFile=apkshellext2_hy.snk`。
官方私钥不随仓库发布,且 `*.snk` 被 gitignore。README 里写:*「Release builds sign
with `ApkShellext2\apkshellext2_hy.snk` — generate your own key with `sn -k`」*。
**自己生成一个就能编**(公钥与官方发布不同 —— 产物仅限本地使用,不可能冒充官方
签名版)。

`sn -k`(Windows SDK 工具)可以用,但其实不需要任何 SDK —— 系统自带的 Windows
PowerShell 5.1 就能产出同一种密钥,请用这个确切写法:

```powershell
& C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe -NoProfile -Command "
  `$cp = New-Object Security.Cryptography.CspParameters
  `$cp.ProviderType = 1   # PROV_RSA_FULL
  `$cp.KeyNumber    = 2   # AT_SIGNATURE  <-- 关键就在这行
  `$rsa = New-Object Security.Cryptography.RSACryptoServiceProvider(`$cp)
  [IO.File]::WriteAllBytes('E:\src\apkshellext\ApkShellext2\apkshellext2_hy.snk', `$rsa.ExportCspBlob(`$true))"
# 596 字节;文件头应为 07 02 00 00 00 24 00 00 "RSA2"(aiKeyAlg=0x2400 CALG_RSA_SIGN)
```

**陷阱:** 想当然的写法
`New-Object System.Security.Cryptography.RSACryptoServiceProvider 1024` 默认生成
*密钥交换(key-exchange)*密钥(`aiKeyAlg=0xa400`)。Roslyn/csc 毫不挑剔(dotnet 路线
一路绿灯),但 Visual Studio 的 MSBuild 下给**卫星程序集**签名的经典 `al.exe` 会拒绝:
`ALINK : error AL1078 … 未知错误(8013141e)`。VS 路线见到 AL1078,先按上面的写法重新
生成 snk(坑 13),再考虑怀疑 VS 安装。

### 4.3 7-Zip(可选;只影响 postbuild 打包步骤)

`postbuild.cmd` 的查找顺序:`%ProgramFiles%\7-Zip` 与 `%ProgramFiles(x86)%\7-Zip` →
`HKLM:\SOFTWARE\7-Zip`(InstallPath、Path 两个值,**用 `reg /reg:64`、`/reg:32` 把
64 位和 32 位注册表视图都查一遍** —— 经典 `Bin\MSBuild.exe` 是 32 位,只查默认视图会
漏掉 64 位安装,坑 14)→ `where 7z`。找不到只打一条
**WARNING** 并跳过打包 —— DLL 产物不受影响。要出 `.7z` 包就装一个,例如:

```powershell
winget install 7zip.7zip
```

### 4.4 .NET Framework 4.8 参考程序集

所有 net48 编译都需要。当前 Windows 上一般已在
`C:\Program Files (x86)\Reference Assemblies\...\.NETFramework\v4.8`;也可通过 VS 组件
「.NET Framework 4.8 SDK/目标包」获得(*.NET 桌面开发*负载自带)。若都没有,部分 SDK
版本会自动回退到 `Microsoft.NETFramework.ReferenceAssemblies` NuGet 包 —— 别指望这个,
直接确认目录存在。

### 4.5 LibWebP 原生依赖(可选重建)

WebP 支持是原生代码。`ApkShellext2\WebP-wrapper\WebPWrapper.cs`(已收编进仓库,直接编进主工程)
P/Invoke 的 DLL 名 **`libwebp_x86.dll` / `libwebp_x64.dll`** 硬编码在其 `[DllImport]`
特性里,运行时按 `switch (IntPtr.Size)` 二选一。外壳扩展寄宿在宿主进程内运行:64 位
`explorer.exe` 加载 x64 DLL,32 位宿主进程(32 位文件对话框、老应用)加载 x86 那个。
因此这两个文件必须以此文件名同放一个安装目录。

两个 DLL 已**预编译入库**,位于 `ApkShellext2\LibWebP\`:**libwebp v1.6.0** 官方源码
加一个本地补丁 —— `build\libwebp\sharpyuv-static.patch` 把 libsharpyuv *静态*链进
libwebp,因为官方 Windows 构建会额外要求一个 `libsharpyuv.dll`,而它只有一个固定文件名,
无法在同一目录同时服务两种位数。仓库里两个 DLL 的导入表都只有 `KERNEL32.dll`
(静态 CRT、静态 libsharpyuv)。它们不带 VERSIONINFO 资源 —— `FileVersion` 为空是设计
使然;`WebPGetDecoderVersion()` 返回 `0x010600`。csproj 以 `Content` 项把它们复制到
输出根目录,postbuild 自动打包(§8)。

**正常编译不需要本节的任何东西。** 只有要更换 libwebp 版本(功能或安全修复)、或预编译
产物不覆盖你的目标平台时,才需要重建。

重建套件在 `build\libwebp\`:先读那里的 `README.md`(动机 + 钉死的 ABI 事实),干活的是
`build-libwebp.ps1`。

| 步骤 | 命令(在 `build\libwebp\` 里运行) | 前置 |
|---|---|---|
| 拉取并打补丁 v1.6.0 源码 | `.\build-libwebp.ps1 -Fetch` | Git;`-Mirror` 可指定 GitHub 镜像 |
| 编 x64 DLL —— **gcc、MSVC 二选一** | gcc:`.\build-libwebp.ps1 -Gcc64 -GccRoot <root>`;MSVC:`.\build-libwebp.ps1 -Msvc64 -GccRoot <root>` | gcc:MSYS2 ucrt64(gcc+CMake+Ninja),无需 Visual Studio。MSVC:装了**「使用 C++ 的桌面开发」**负载的 VS —— §1 的 *.NET 桌面开发* 负载**不含 C++ 编译器** |
| 编 x86 DLL —— **仅 MSVC** | `.\build-libwebp.ps1 -Msvc32 -GccRoot <root>` | 同 MSVC x64;脚本没有 gcc-x86 模式 |
| 解码冒烟测试 | `.\build-libwebp.ps1 -Smoke <build-dir>` | `-GccRoot` 里的 gcc;`<build-dir>` 指向 `-Gcc64` 的输出 |
| 替换仓库内 DLL | `.\build-libwebp.ps1 -Deploy` | 把 `lw-build-{x64,x86}-msvc\libwebp.dll` 复制为 `ApkShellext2\LibWebP\libwebp_x{64,86}.dll` |

实际上要紧的几点:

- **`-GccRoot` 指定工具根目录**:任意 `bin\` 下同时有 `gcc.exe`、`cmake.exe`、
  `ninja.exe` 的目录(MSYS2 ucrt64 是最常见来源)——**MSVC 模式也从这里取
  CMake/Ninja**,两条路线都要传它。gcc 路线完全不需要 Visual Studio;MSVC 模式在
  `VsDevCmd -arch=…` 环境里调 `cl`。
- **MSVC 路线必须静态 CRT**(`/MT`,经 `CMAKE_MSVC_RUNTIME_LIBRARY=MultiThreaded`
  设置);脚本会检查产物的导入表,出现 `VCRUNTIME*/MSVCP*` 即判失败 —— 发布的 DLL
  不能依赖系统装没装运行库。
- **`-Deploy` 只收割 MSVC 产物**(`lw-build-x64-msvc`、`lw-build-x86-msvc`)。用 gcc
  编的 x64 要自己把 dll 复制成 `ApkShellext2\LibWebP\libwebp_x64.dll`。另注意 MSYS2
  gcc 产物链接 UCRT api-set(Windows 10+ 默认自带),而在库的两个 DLL(MSVC `/MT`
  产物)只导入 `KERNEL32.dll`;要真正发布,优先走 MSVC 路线。
- **冒烟测试**(`smoke.c`):合成图像先编码再解码回来,走的全是 wrapper 实际调用的导出
  (`WebPGetInfo`、`WebPDecodeBGRInto`、`WebPDecodeBGRAInto`、`WebPGetFeatures`)。
  `-Smoke` 用 gcc 对着构建目录的导入库编译它,把 `smoke.exe` 写到新编出的
  `libwebp.dll` 旁边运行,通过打印 `SMOKE PASS`。ABI 的依据是解码器 ABI 主版本字节 ——
  wrapper 传 `0x0208`,libwebp 1.6.0 是 `0x0210`,主字节相同即被接受。细节见
  `build\libwebp\README.md`。
- **文件名是硬约束。** 重建产物就是覆盖 `ApkShellext2\LibWebP\` 下的
  `libwebp_x86.dll` / `libwebp_x64.dll` 两个文件 —— wrapper 里的 P/Invoke 名是硬编码的。
- 上游 WebP-wrapper 自带的 `BUILDING.md` / `Compile dll.txt`(未收编)是上游老口径(nmake +
  `Makefile.vc`,钉在 libwebp 1.2.1,那还没有 libsharpyuv)。该口径与本仓库实际发布方式
  不一致;重建一律以 `build\libwebp\` 为准。

## 5. 构建体系

两个 csproj 都是 **SDK 式**,这就是 §6–§7 所有命令零参数的原因。老式工程编这棵树需要的
三个绕行方案(SVG 源生成器手工预生成、`MSB4064` 卫星程序集 shim、手工补 NuGet 包)在这里
没有对应物:工程文件本身已经承担了它们。

### 5.1 3.5.0 的树在非 SDK 构建下需要什么 —— 现在由工程文件提供什么

左列是上游 3.5.0 那棵树当初的构建方式,右列是这里替代它的做法。

| 老式工程需要什么 | 由谁提供 |
|---|---|
| SVG 元素元数据要编译前手工生成(`pregen.ps1` → `SVG\Source\Generated\*.g.cs`) | `<ProjectReference Include="SVG\Generators\Svg.Generators.csproj" OutputItemType="Analyzer" ReferenceOutputAssembly="false" />` —— 该生成器**在 Roslyn 内**每次编译自动跑,两条工具链都行 |
| coreclr MSBuild 下卫星程序集崩(`MSB4064` AL/ToolPath)→ 外部 `dotnet-satellites.targets` shim + 2 个命令行参数 | SDK 式工程在 coreclr 下自动 `GenerateSatelliteAssembliesForCore=true`(用 Csc 生成);FullFramework MSBuild 用真 `al.exe`。什么都不用传 |
| 手工塞进工程的 System.Memory facade 一族(`System.Memory` / `System.Buffers` / `System.Runtime.CompilerServices.Unsafe` / `System.Numerics.Vectors`) | 完全不引用:SVG 的解析走纯字符串(`TextSlice`),见 §8。coreclr 下 `System.Resources.Extensions` 仍会把它作为开发产物的运行时依赖拉进来 |
| 用户手敲 `-p:GenerateResourceUsePreserializedResources=true` | 写进 csproj:`Condition="'$(MSBuildRuntimeType)' == 'Core'"` —— dotnet CLI 自动用预序列化资源,VS 用经典资源,自动分岔(唯一运行时后果见 §8) |
| 右键菜单属性页宿主及其所需的原生桥 DLL | 不属于构建:没有任何被编译的文件引用这些命名空间,使用它们的目录(`NativeBridge\`、`SharpPropertySheet\`、`SharpDeskBand\`)不在树里 |

### 5.2 其他构建设置

- 两个工程都不按路径引用程序集:编译期依赖一律走 `PackageReference` restore。不要加回
  HintPath 引用。
- `AppendTargetFrameworkToOutputPath=false` 保持扁平 `bin\Release` 布局 —— 安装脚本、
  `postbuild.cmd` 和本指南都按这个布局说话(SDK 默认是 `bin\Release\net48\`)。
- `GenerateAssemblyInfo=false`:`Properties\AssemblyInfo.cs` 仍是版本/Guid/ComVisible
  特性的唯一来源。
- `EnableDefaultCompileItems/EmbeddedResourceItems/NoneItems=false` + 显式 glob ——
  vendored 树里有大量**不该**被编译的文件(它们自己的 AssemblyInfo、示例、
  工具、WinUI/WPF 变体)。
- `Debug2` 是保留的配置(`Configurations=Debug;Debug2;Release`),行为等同 Debug。
  生成器 ProjectReference 通过 `SetConfiguration` 把它映射到生成器自身的 Debug 输出。

### 5.3 零警告基线

两条路线均 `0 警告 / 0 错误`。下表每一行都是这棵树**能**产生的警告,以及当前代码为什么
不会触发它:

| 警告 | 位置 | 为什么不会触发 |
|---|---|---|
| CS0219 ×4(`height`、`units`) | `AabReader` / `ApkQuickReader` 的矢量回退 | 画布尺寸来自钳制后的 viewport,`android:width`/`android:height` 根本不解析 |
| CS0618 `SvgDocument.Open` 过时重载 | vendored 的 `SVG\Source\Web\SvgHandler.cs` | 该 ASP.NET 请求处理器从编译面剔除(`Compile Remove`):它是全树唯一的 `System.Web` 使用者,而主 dll 不得引用 `System.Web`。SVG 树其余部分原样编译 |
| CS0618 `SvgPathSegment.Start` | `VectorDrawableRender.Convert2Path` | Svg 3.5.0 标记过时且无替代 API;唯一调用点局部禁用 CS0618 并注释说明 |

### 5.4 源码目录结构

源码按职责分组;工程根只留每一层都用得上的东西。其余文件保持原路径:vendored 树、
`Properties\`、`Resources\`、`Preferences.resx`、`LibWebP\` 原生库与签名密钥。
`eng\check-layout.ps1`(发布布局 9 个根文件 + 19 个语言目录)未改;`eng\check-size.ps1`
只按参考预算出报告(不拦 CI)—— 发布目录依旧是平铺的。

```
ApkShellext2\
  Shell\          四个 COM 处理器(右键菜单、图标、信息提示、缩略图)
  Formats\        IPackageReader + PackageReaderFactory、六个 reader、AXML/ARSC 解析器
  Imaging\        IArchive/SafeArchive、IIconSource 链与解码上限、矢量图光栅化
  Diagnostics\    ErrorLog、ModuleInitializer、DbgLog
  Preferences\    WinForms 设置窗体与 designer(Preferences.resx 仍留在根目录)
  Vendor\         net48 polyfill(SharpZipLibShim、ModuleInitializerAttribute)
  Utility.cs      留在根目录:每一层都用它
  Interop\        COM/PInvoke 声明(§5.5)
  Registration\   注册/注销(§5.5)
  PlistCS\ PNGDecrush\ WebP-wrapper\ SVG\ LibWebP\ Properties\ Resources\
```

**命名空间一律不改**(`ApkShellext2`、`ApkShellext2.Compression`、`ApkQuickReader` 等),
原因有三:把 `Preferences` 挪进 `ApkShellext2.Preferences`
命名空间会与同名的 `Preferences` 类型(以及该命名空间名本身)冲突;其余重命名要动
COM ServerRegistration/ProgID 契约;而行为收益为零。
`Preferences.resx` 的路径与 `ApkShellext2.Preferences.resources` 资源名都不变;窗体移走后
必须去掉它的 `DependentUpon` 元数据,否则 MSBuild 取名时会以 MSB3041 失败。

### 5.5 Interop 与注册器

四个注册到 shell 的 CLSID 由 `ApkShellext2\Shell\Native` 里的处理器服务,它们直接实现外壳接口。
`install*.bat` / `update.bat` 仍用 `regasm` 注册它们,而 `ShellRegistrar` 从托管代码写同一批键:
`Interop\` 声明外壳调用的接口,`Registration\` 负责键。

#### 5.5.1 声明规范(`Interop\`)

全部手写,不用 CsWin32、不用源生成器(只有 `[ComImport]` + `[PreserveSig]` + 显式 `DllImport`),
所以文件可 grep、净构建只依赖 net48。

- **一个 COM 接口 = 一个扁平接口,接口之间不继承。** 每个接口都隐式继承 `IUnknown`,然后把
  自己需要的**全部**槽按 vtable 顺序重新声明一遍。CLR 会把继承来的方法排在声明的方法之前,
  `IContextMenu3 : IContextMenu2` 这种链式写法会让 `HandleMenuMsg2` 落到错误的槽上
  (实测崩溃 `0xC0000005`)。因此 `IContextMenuFlat`、`IContextMenu2Flat`、`IContextMenu3Flat`
  是三个**互不继承**的接口,各带自己的 IID —— 服务端可以同时实现三个,`QueryInterface` 沿
  任一路径都能命中正确的槽位。
- **每个槽都必须 `[PreserveSig]`。** 没有它,CLR 会把失败的 `HRESULT` 翻译成 `COMException`
  并吞掉返回值;而外壳回调靠返回值通信(`QueryContextMenu` 还要把条目数放在低 16 位里)。
- **IID 必须是真 IID。** 扁平的 `IContextMenu3` 要声明
  `bcfce0a0-ec17-11d0-8d10-00a0c90f2719`;`000214e4-…` 是 `IContextMenu`,`QueryInterface`
  拿着它会返回形状不对的那段。
- **结构体走自然对齐,一律不加 `Pack`。** Win32 头文件用的是默认对齐;照抄 spike 的 `Pack = 4`
  会在 x64 上把 `FORMATETC`、`STGMEDIUM`、`MENUITEMINFO` 翻译错。尺寸改由测试钉住
  (x64 / x86):`FORMATETC` 32/20、`STGMEDIUM` 24/12、`CMINVOKECOMMANDINFO` 64/40、
  `…INFOEX` 104/64、`MENUITEMINFO` 80/48、`PROPERTYKEY` 20/20。
- **调用方直接用裸 vtable**(`ComObject`:读 `vtable + slot * IntPtr.Size`,再
  `Marshal.GetDelegateForFunctionPointer`)。对 `IShellExtInit.Initialize` 送进来的指针
  **绝不能** `Marshal.ReleaseComObject` —— 那会把外壳自己的引用减掉。
- **build 号只信 `RtlGetVersion`。** 没有 manifest 时 `Environment.OSVersion` 在
  Windows 10/11 上会谎报 6.2;`ntdll!RtlGetVersion` 返回真实 build(本机:19045)。

`Interop\ComLayout.cs` 保存契约表(13 个接口 × vtable 槽名、8 个结构 × 尺寸/偏移),
`Verify()` 返回人可读的违规清单;`InteropLayoutTests` 断言清单为空,并额外钉住每个 IID、
每个槽的顺序和 `PreserveSig`。

#### 5.5.2 注册器(`Registration\`)

`ShellRegistrar.Register(RegistrationScope, string dllPath, RegistrationOptions)` 与
`Unregister(scope, options)` 写的是 `regasm /codebase` 那套键(CLSID 根默认值 = ProgID、
`InprocServer32` 里的 `mscoree.dll` + `Class` + `Assembly` + `RuntimeVersion` +
`ThreadingModel=Both` + `CodeBase`、版本子键、`ProgId`、以及类的 `ProgID` 键和它的 `CLSID`
子键),然后按 `ShellGuids.AppliesTo` 写关联槽(图标 8 个扩展全写、信息提示与右键菜单除
`.aab`、缩略图仅 `.apk`),最后写 `DefaultIcon` 占位并遵循 `install_user.bat` 的
`SharpShell_Backup_DefaultIcon` 备份语义(只备份一次、已有备份不覆盖、`%1` 本身不备份)。
`ShellGuids.cs` 是四个 handler CLSID、命令 CLSID、四个 ProgID、八个扩展和全部键名/值名的唯一
出处;`.bat` 里的同值副本仍在。

- **作用域。** `Machine` = `HKLM\SOFTWARE\Classes`(外加
  `SOFTWARE\Microsoft\Windows\CurrentVersion\Shell Extensions\Approved`,只有真正的机器级安装
  才写);`User` = `HKCU\Software\Classes`。
- **per-user 必须双视图。** 所有写入都要镜像进 `Wow6432Node` 树
  (`HKCU\Software\Classes\Wow6432Node\Classes\…`、`HKLM\SOFTWARE\WOW6432Node\Classes\…`),
  因为 32 位宿主解析 per-user CLSID 时只读 32 位视图 —— 没有镜像,`CoCreateInstance` 会以
  `0x80040154` 失败(已实测;`D:` 上那份真实 per-user 安装缺的正是这个)。注册器一律用
  `RegistryView.Registry64` 打开,32 位树通过显式 `Wow6432Node` 路径写,因此在 32 位和 64 位
  宿主里行为一致。
- **Win11 命令按真实 build 门控。** 22621 及以后,注册器写动词键
  `{类}\shell\ApkShellext2`,其中 `ExplorerCommandHandler` =
  `{0B6C1F2A-7D34-4E5B-9A18-3C2D4E5F6071}`、`MultiSelectModel` = `Player`(整个选中集只激活
  一次),并把该 CLSID 按与四个 handler 相同的 `InprocServer32` 形状注册成 COM 服务器,但没有
  `ProgID` —— 动词靠类 id 指名。两处写入都在 `RtlGetVersion().BuildNumber >= 22621` 之后;
  `RegistrationOptions.BuildNumber` / `ExplorerCommandBuildThreshold` 让测试在任何机器上都能
  走门控两侧。四个经典槽完全不动,所以 Windows 10 和 Win11 的「显示更多选项」仍走老路径。
  更早的版本把同一个值写在 `{类}\ShellEx` 下;`Register` 与 `Unregister` 只在这个值属于我们时
  才删它。`regasm /regfile` 会遍历所有 `ComVisible` 类,因此任何 build 都会写这个 CLSID 块,
  但单有它不起作用 —— 没有动词键,没有任何东西指向它。
- **Win11 菜单本身未经验证。** 写这份文档的机器是 Windows 10 22H2,只验证了注册表形状、
  vtable 和命令的返回值,没有验证 Explorer 实际画出来的菜单。发布前必须在 Windows 11 机器上
  装一次并人工确认:`.apk` 的主菜单里出现该动词、子命令能展开、选中项里没有受支持文件时它
  隐藏、多选时仍只激活一次。已知风险是子命令里再套子菜单:Explorer 不支持这种结构,而
  提取/商店两组正是这样的子菜单。
- **幂等且对称。** 第二次 `Register` 产出的树逐字节一致(只报告仍需创建/删除的键,
  见 `RegistrationReport.KeysWritten`);`Unregister` 只删 `Register` 写过的东西,并把它顶掉过
  的 `DefaultIcon` 回灌。每次 `Register` 都会清掉 `InprocServer32` 下非当前版本的子键(只留
  当前版,与现有 `CustomRegisterFunction` 一致);五个服务器类 id(四个 handler 加命令)也会从
  三处 `Shell Extensions\Blocked` 名单里删除,per-user 无权限打开的机器级名单静默跳过。
- **`RegistrationTarget` 把注册表隔离出来。** `RegistryTarget.ForScope` 给真实根;
  `RegistryTarget.Sandbox(rootPath)` 在 `HKCU\Software\<root>` 下给出同样形状的根,
  `ShellRegistrarTests` 与等价性 harness 靠它跑,不碰任何真实安装。

等价性验证方式:在一个沙盒根里把 `regasm /codebase /regfile` 的产物落地,再把
`install_user.bat` 的关联循环重放一遍,与第二个沙盒根里 `ShellRegistrar.Register(User, dll)`
的结果逐键逐值比对:两边各 103 条值记录,归一化 `CodeBase`/`Assembly` 后**零差异**,
`Wow6432Node` 镜像与 64 位树完全一致(参照树根本没有镜像)。

#### 5.5.3 与 `.bat` 安装器的关系

`install.bat`、`install_user.bat`、`update.bat` **不改**,仍自己做 `regasm` + 关联那套;注册器是
`[ComRegisterFunction]` 钩子调用的实现,所以 `regasm` 安装与注册器安装写出的键相同。Win11 动词
只有注册器会写,安装器因此不检查它:在批处理里按 build 判一遍等于把门控抄两遍。
`uninstall.bat` 与 `uninstall_user.bat` 则加上了动词:两者的清扫都删命令 CLSID,并有
`:dropverb` 子过程,只在键里的类 id 是我们的时候才删 `{类}\shell\ApkShellext2`——别的产品接管
过这个动词的机器不会被连坐。这也覆盖了不跑 `regasm` 的按扩展名回退路径(那里类可能还得按
安装器自建类来删)。相对
`uninstall_user.bat` 的清扫有两点刻意差异,前面已说明:`Unregister` 不做全局
`sweepzoo`/`:cleancls` 清扫(只反向 `Register`),并且只在安装器自建的 `\<ext>.1` 类下
只剩默认值时才会删掉它。

#### 5.5.4 处理器与注册钩子

四个处理器就是 `Shell\Native\` 里的类(`IconHandlerNative`、`InfoTipHandlerNative`、
`ThumbnailHandlerNative`、`ContextMenuHandler`)。它们直接实现外壳接口,并调用 `Shell\` 下的
渲染器(`IconRenderer.cs`、`InfoTipRenderer.cs`、`ThumbnailRenderer.cs`),因此图标、提示文本和
缩略图各来自同一条管线。

每个原生类都带一对 `[ComRegisterFunction]`/`[ComUnregisterFunction]`,转调
`ShellRegistrar.RegisterAssembly`/`UnregisterAssembly` 的机器作用域:regasm 写的是 `HKCR`
(机器 hive),而 `/regfile` 从不执行该钩子——按用户安装器正是靠这一点才安全。

四个已注册 CLSID 由处理器服务,注册器写的也是那些类名,所以注册器安装的机器解析到同一个类。
同一份构建与 `regasm /codebase /regfile` 逐键对比:两侧各 56 条服务器记录,没有任一侧独有的键,
也没有任何值不同。

### 5.6 右键菜单:一份模型,一个前端

菜单先被描述成数据、再被渲染,因此外壳看到的菜单就是这份模型的渲染结果,不是另一份平行清单。

**模型(`Shell\Commands\`)。** `CommandCatalog.Build(selectedPaths)` 读取 HKCU 设置、本地化、为
单选打开唯一需要的 reader,返回 `CommandModel`:一棵前序排列的 `CommandItem` 树,每项带
`Index`(相对 `idCmdFirst` 的偏移)、`Text`、`Verb`、`HelpText`、`Action`、`Icon`、`Enabled`、
`IsSeparator` 和 `Children`。该目录里没有任何代码碰 WinForms;与 UI 相邻的只有
`CommandActions`(执行重命名、导出 CSV、复制、提取文件、商店与首选项命令)、`CommandIconResolver`(把图标标记变成
`Bitmap`)和 `Shell\StaUiHost.cs`(在带消息泵的专用 STA 线程上打开首选项窗口、CSV 保存/进度窗口
和提取文件提示框,因此查询菜单不会加载 UI 栈)。

**导出所选文件。** 只要 `PackageReaderFactory.GetAppType` 能识别至少一个选中文件,
`Shell\Commands\CommandCatalog.cs` 就在重命名项之后加一个 `Export` 项;只选中不受支持的文件时
没有导出项。执行后在 STA 线程上打开 `SaveFileDialog`(默认 `apk-info.csv`),随后在线程池线程
写文件,上百个文件也不会卡住菜单。`Formats\PackageCsvWriter.cs` 负责写表:UTF-8 带 BOM、CRLF
(主要读者是 Excel)、RFC 4180 引号规则、每个选中路径一行且保持选择顺序,列固定为 `File`、
`Folder`、`AppName`、`Package`、`Version`、`Revision`、`Publisher`、`AppID`、`Type`、
`SizeBytes`、`Modified`、`Status`。`Status` 为 `ok`、`unsupported type`、`unreadable` 或
`cancelled`;混合选择里每个路径都保留一行,CSV 自己说明跳过了什么。进度窗口显示当前计数,
并通过 `CancellationToken` 取消;写失败记入 `ErrorLog` 并弹出消息框,绝不越过 COM 边界抛出。

**复制所选文件。** 导出项之后是 `Copy info` 子菜单,由同一次单选 reader 解析构建,包含
`Copy File Path`、`Copy App Name`、`Copy Package Name`、`Copy Version`;某个值只有在至少一个选中
文件能填时才出现,因此只选中不受支持的文件时仍能复制路径,而一个值都填不出的选择没有子菜单。
`Formats\PackageClipboardText.cs` 负责生成文本——每个文件一行、保持选择顺序,行间为 CRLF、末尾
不留换行——版本行沿用 CSV 的口径 `Version (Revision)`。写入经 `StaUiHost.SetClipboardText` 在
STA 线程上执行,因为剪贴板只能由 STA 线程持有;reader 或剪贴板失败记入 `ErrorLog`,绝不越过
COM 边界抛出。

**id 与 verb。** `CommandIds` 固定区间(`First = 0`,`MaxItems = 0x100`);菜单写入
`wID = idCmdFirst + Index`,分隔符完全不携带 id,与改造前的输出一致。`CommandVerb` 把每条命令
命名为 `ApkShellext2.<Name>`;对于命令 id,`GCS_VERBW` 返回该字符串,对于父项、分隔符或菜单未
发放的 id 返回 `E_FAIL`。`InvokeCommand` 同时接受 verb 字符串与整数 verb(相对 `idCmdFirst`
的偏移,也容忍绝对 id),执行与 WinForms 适配器相同的 `Action`,把父项、分隔符和禁用项当作
成功的空操作,对无法识别的输入返回 `E_FAIL`。verb 指针为空表示"默认项",这里一律拒绝:本菜单
没有默认项。

**为什么用原生 `HMENU`。** shell 把 `HMENU` 交给右键菜单处理器,由它把菜单项插进去。直接用
`CreatePopupMenu` / `InsertMenuItemW` / `DestroyMenu` 构造,可以让 COM 入口不碰 WinForms(实测:
一次菜单查询加载 8 个程序集,没有 `System.Windows.Forms`),让 id 与 verb 由我们自己控制,也避开
了改造前那条转换路径的缺陷(0 基 id 计数器,以及 `IContextMenu2/3` 重新声明时漏掉
`[PreserveSig]`,导致处理器返回 `E_FAIL` 时调用方看到 `S_OK`、并且拿不到项数)。菜单位图按图标
标记与尺寸缓存到进程结束,因为交给 `InsertMenuItemW` 的 `HBITMAP` 必须活得比菜单更久。
`QueryContextMenu` 在低字返回项数,比改造前包装器对同一份菜单报的数少一。

每个原生处理器自己声明它服务的那个 CLSID:那是该 CLSID 的唯一声明,所以 harness 激活的就是 shell 激活的同一个类。

## 6. 编译主工程

### 路线 A —— dotnet CLI(仅开发产物 —— 不要把该产物装进 Explorer,§8)

```powershell
Set-Location E:\src\apkshellext
E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe build ApkShellext2\ApkShellext2.csproj -c Release --nologo
```

就一行。restore 隐式执行。预期:`EXIT=0` 且**编译器零警告**(仓库警告基线已清零,见 §5.3),输出
`ApkShellext2 -> …\bin\Release\ApkShellext2.dll`,随后是 7z 打包步骤(§8)。

### 路线 B —— Visual Studio / Build Tools 的 MSBuild

```powershell
# 定位 MSBuild(知道路径就跳过):
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msb = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1

Set-Location E:\src\apkshellext
& $msb ApkShellext2\ApkShellext2.csproj -t:restore -p:Configuration=Release -nologo -v:m
& $msb ApkShellext2\ApkShellext2.csproj -p:Configuration=Release -nologo -v:m
```

两条命令(经典 MSBuild 的 restore **不**隐式),依然零自定义参数。注意冒号:经典
MSBuild 要 `-v:m` 而不是 `-v m`(坑 3)。restore/编译若报
`MSB4236 找不到指定的 SDK“Microsoft.NET.Sdk”`,说明 VS 安装坏了 —— 坑 2。
`Bin\MSBuild.exe` 是 **32 位**启动器(64 位在 `Bin\amd64\`);在这里两者都能用 ——
postbuild 的 7-Zip 定位就是专门做成位宽无关的(坑 14)。VS 18 还在
`C:\Program Files\dotnet\sdk\` 自带一份 SDK,路线 B 完全不依赖路线 A 装的东西。

整个解决方案(主工程+测试)两条路线也都能一把梭:
`dotnet build ApkShellext2.sln -c Release` / `msbuild ApkShellext2.sln -t:restore` +
`msbuild ApkShellext2.sln -p:Configuration=Release`。

CI(GitHub Actions,`.github/workflows/ci.yml`)对两个 job 故意用不同 SDK:
`dotnet-build` 固定 10.0.404(`latestPatch`);`vs-build` 固定 9.0.100
(`latestFeature`),因为 runner 的 Visual Studio 2022 带的是 MSBuild 17.14,
载不动 SDK 10+(那要求 MSBuild ≥ 18.0.0)。VS 路线要的是 FullFramework 资源管线,
不是编译器版本。

### 体积报告(指导性,不拦 CI)

```powershell
& .\eng\check-size.ps1
```

脚本**总是 exit 0**,只打印 `ApkShellext2.dll`、7z 包与 28 文件部署树相对参考预算的位置
(参考值 1,260,000 / 1,000,000 / 2,500,000 B;参考点 1,181,184 / ~752,500 / 2,360,097 B)。
超过参考值的那一行标 `OVER (guidance only)` —— 体积只是指导,不是合并契约,升依赖或加功能
都不必改预算,也永远不会让 CI 变红;真正的功能契约是 `check-layout.ps1`(缺文件/多文件才失败)。
部署树只按 9 个清单文件 + 19 个卫星资源集计,不扫目录,所以 `bin\Release` 里多出的
pdb/日志不影响数字。预算都是参数(`-MaxDllBytes` 等),调小只是渲染一份更紧的报告。

## 7. 单元测试(NUnit)

`tests\ApkShellext2.Tests\ApkShellext2.Tests.csproj` 存放无人值守的测试,钉住解析器的
安全闸。覆盖范围:版本单一来源;arsc 的 config / package /
entry / reference 上限;binary 与 XML plist 的深度限制、索引边界和每次调用独立状态;
archive 的条目数、压缩比和累计字节预算;WebP 与 PNG 解码尺寸上限;Appx / Appxbundle / Ipa
reader 的边界(manifest 大小、包数、文件名长度、嵌套容器读取);ComStream 的 seek/dispose;
以及 ErrorLog 的滚动、4 KiB 单行和跨进程互斥契约。net48 + NUnit 3(NUnit3TestAdapter +
Microsoft.NET.Test.Sdk)。它和产品唯一的联系是一条 ProjectReference,并传了
`Skip7zPackage=true`,因此跑测试会重新编译产品,但绝不触发 postbuild 的 7-Zip 打包 ——
没装 7-Zip 的机器也能跑。

```powershell
# 全量(测试工程目标框架是 net48,所以必须在 Windows 上跑):
E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe test tests\ApkShellext2.Tests\ApkShellext2.Tests.csproj -c Release --nologo

# 只跑一个测试类,或一个用例:
#   ... --filter "FullyQualifiedName~SafeArchiveTests"
#   ... --filter "Name~Rejects33NestedArrays"
```

任何一个用例失败,`dotnet test` 就返回非零 —— CI 那一步要的就是这个。本机没有 snk(§4.2)
时加 `-p:SignAssembly=false`,和编译那一步用的是同一个开关(ProjectReference 会重编产品)。

测试用反射而不是 `InternalsVisibleTo` 触达内部守卫:产品用本机私钥强签名,仓库里并不带这把
钥匙,而缺公钥的 `InternalsVisibleTo` 是编译错误(CS1726);反射既不动产品源码,也让测试在
签名机和未签名机上都能跑。测试产物落在 `tests\ApkShellext2.Tests\bin\<Configuration>\net48`,
不会混进 §8 和 `eng\check-layout.ps1` 所描述的 `bin\Release` 发布布局。

所有夹具要么在测试内部现造(zip 用 `System.IO.Compression`,binary plist 用
`TestSupport\BinaryPlistBuilder.cs` 里的小写入器,appx/appxbundle 包用
`TestSupport\AppxFixtures.cs`),要么是 `tests\ApkShellext2.Tests\fixtures\` 下提交的真实
字节切片(Spectroid 的 `resources.arsc` / `AndroidManifest.xml`,由
`tests\ApkShellext2.Tests\fixtures\make-fixtures.ps1` 重新生成)。因此测试是自足的:不读
`F:\Backups\apks\` 样本库,也不联网。

CI:`.github\workflows\ci.yml` 的 `dotnet-build` job,**Run unit tests** 一步 —— 位于
**Build Release (warnings as errors)** 和 **Check publish layout** 之后、上传产物之前。
`vs-build` job 不跑测试。`-warnaserror` 只作用于产品编译;测试工程是
`TreatWarningsAsErrors=false`,测试侧的 analyzer 建议不会把构建搞红。

## 8. 发布布局、postbuild 与部署说明

**布局由 MSBuild 组装 —— postbuild 只负责打包。**

- **MSBuild 侧**(`ApkShellext2.csproj`):`install.bat`、`install_user.bat`、
  `uninstall.bat`、`uninstall_user.bat`、`restart_explorer.bat`、`update.bat` 和两个
  `libwebp_*.dll`
  都是 `CopyToOutputDirectory=PreserveNewest` 的 `Content` 项;`debug.bat` 同理但带
  `Condition="'$(Configuration)'=='Debug'"`。`CopyFilesToOutputDirectory` 在任何打包
  之前就把它们复制到位,所以 `bin\Release` 本身就是完整发布目录。每个项都带
  `TargetPath=<裸文件名>` —— 不带的话复制会在输出里重建 `Resources\`/`LibWebP\` 源
  子目录(坑 5)。两个 `libwebp_*.dll` 是仓库内预编译产物(来源与可选重建见 §4.5);
  正常编译不需要任何原生工具链。
- **打包钩子**:实现为 `<Target Name="PackageWith7z" AfterTargets="PostBuildEvent">`
  用 `Exec` 调脚本,**不是**老的 `<PostBuildEvent>` 属性 —— SDK 式工程里属性赋值里的
  宏在**求值期**展开,那时 `$(ProjectDir)`/`$(TargetDir)` 还没有值,老一行式会被烤成
  `call "Resources\postbuild.cmd" "" ""`(坑 4)。
- **`Resources\postbuild.cmd`**(仅 Release 干活;Debug 直接退出):删残留 `debug.bat`,
  定位 `7z.exe`(§4.3),用 `eng\package-name.ps1` 从 `version.txt` 算包名
  (`4.0.0.0` → `ApkShellext2-4.0.0.7z`;`check-layout.ps1`/`check-size.ps1` dot-source
  的是同一条规则),删残留 `*.7z`(旧无版本名 + 上一版本留下的包),然后按**显式清单**打新包 —— 9 个
  根文件(`ApkShellext2.dll`、六个 bat、两个 `libwebp_*` 二进制)加上磁盘上存在的
  全部语言目录(`ar-SA` … `zh-TW`)。
  清单条目缺失会**让编译失败**,短包不可能静默出货。
  四个 System.Memory facade dll(`System.Memory.dll`、`System.Buffers.dll`、
  `System.Runtime.CompilerServices.Unsafe.dll`、`System.Numerics.Vectors.dll`)一个都不在
  清单里,也不能在清单里:COM 宿主(explorer/dllhost)里 Fusion 的 Load 上下文只探测宿主
  app base(`C:\Windows`)与 GAC,而 `TypeDescriptor` 要绑定 Svg.NET 的数值类型转换器,
  只有该文件躺在 `ApkShellext2.dll` 旁边才加载得到 —— 缺失时每个数值 SVG 属性
  (渐变坐标、offset……)都抛 `FileNotFoundException`,自适应图标静默渲染成空白/碎片。
  SVG 里的解析全部走纯字符串,编译器不绑定这些程序集,部署它们这件事也就不存在。
  只有一个 dll 被注为*排除项*,无论输出目录里躺着什么都不打包:
  `System.Resources.Extensions.dll`(见下)。主包就是完整安装包:不做按语言分包 ——
  只装主包会让界面静默变英文。没有 7-Zip ⇒ WARNING + 跳过,编译照样成功。

**为什么 VS 路线是出货路线 —— 预序列化资源。** coreclr MSBuild(dotnet CLI)下
csproj 启用预序列化资源生成,把对象资源内嵌成经 `System.Resources.Extensions`
编码的 blob。独立进程里这没问题(测试 exe 能正常解析该 dll),但 **explorer
这样的外壳扩展宿主**只在自己的模块目录和 GAC 里探测程序集依赖 —— 而
`System.Resources.Extensions` 两者都不是,于是解析失败,而且是*静默*失败:
右键菜单处理器在获取自身条目图标时死掉,explorer 照画菜单其余部分,扩展的条目
消失。把 dotnet CLI 产物装进外壳后实测到的失败特征:**自定义文件类型图标照常
工作(图标处理器从不触碰 resx 里的对象资源),右键菜单则整个消失** —— 对两种
产物都复现过,且用 VS 路线编译的同一程序集原位替换即修复(同路径、同强名,
无需重新注册)。Visual Studio(FullFramework)编译走经典 BinaryFormatter 风格
资源,从不引用该包,与上游发布包完全一致。因此:**出货用 VS 路线程序集;
dotnet CLI 产物只留作开发。** `postbuild.cmd` 把该包 dll 列为注名排除项,
两条路线的包都夹带不了它 —— 也别手工把它拷进部署目录,在宿主进程里它救不了
预序列化产物。

部署(目标机,以要用这个外壳扩展的用户身份):解压 `ApkShellext2-4.0.0.7z`(语言目录已在
包内、与 DLL 同级),跑 `install.bat`(管理员,全机)或
`install_user.bat`(按用户;两者都 regasm 注册并 NGEN 预编译 —— 3.4.5 修好了脚本里的
NGEN)。`restart_explorer.bat` 重载外壳。卸载用对应的 `uninstall*.bat`。本地 snk 与
上游不同,别把本地编译的 DLL 和官方包混在同一注册/GAC 状态里。

**正在被占用的 dll 怎么换。** Explorer 和 `dllhost.exe` 都会加载处理器,先停这两个。
若文件仍被占用,`Rename-Item <dll> <dll>.old` 一定成功(Explorer 以非 delete 共享打开
它),然后把新文件拷进去、删掉 `.old`。重启资源管理器要用预建的计划任务,不要在会话里
`Start-Process explorer`:那样起出来的是没有桌面的实例。

**部署后的旧缓存。** 资源管理器停着的时候,删掉 `%LOCALAPPDATA%\Microsoft\Windows\
Explorer` 下的 `thumbcache_*.db` 与 `iconcache_*.db`;否则 dll 换了之后图标和缩略图
仍在用旧缓存。

**已有安装的升级:** 把新包解压到任意位置,在包目录里运行 `update.bat`(可选 `/quiet`
供脚本调用、`/ngen` 刷新原生映像)。它从 COM 的 `CodeBase` 读已注册目录(HKLM 64 位
视图 → WOW6432Node → HKCU,机器级优先),先把新文件暂存,再把 8 个产品文件连同本版本
已不再出货的文件(3.5.1 安装里还有 `ExCSS.dll` 和四个 Span facade 程序集)一起移进
`backup-<旧版本>-<时间戳>\`,复制新文件并**就地修复同一注册作用域**。包目录就是已注册
目录时就地换文件;否则复制进已注册目录,`CodeBase` 保持指向它。它不会覆盖已有的
`SharpShell_Backup_DefaultIcon`,并清掉五个服务器类 id 在 Windows 阻止列表里的项。注册写完后
它会把每个处理器槽位回读一遍——八个扩展名的文件类在作用域两个类根下,`ShellEx\IconHandler`、
`ShellEx\{00021500-0000-0000-C000-000000000046}`、`ShellEx\{e357fccd-a995-4576-b01f-234630154e96}`
与 `ShellEx\ContextMenuHandlers\ApkContextMenu` 的默认值——逐个和本包的类 id 比对,对不上就打印
注册表路径并以退出码 1 结束。`/rollback` 还原最近一份备份并重启资源管理器;任何一步失败也会把
备份移回并重启。迁移式升级(包目录不是已注册目录)会先把已装的 `update.bat` 移进备份再复制新版,
所以回滚能精确还原旧文件集——旧安装本来没有升级器时,回滚后也没有。就地升级无法移动正在运行的
脚本,其回滚在备份里没有 `update.bat` 时会把它删掉。

## 9. 编译结果验证

1. **卫星目录:** `bin\Release` 必须有全部 19 个语言目录
   (`ar-SA de-DE el-GR es-ES fa-IR fr-FR he-IL it-IT ja-JP ko-KR my-MM nl-NL pl-PL pt-PT
   ru-RU tr-TR uk-UA zh-CN zh-TW`),各含 `ApkShellext2.resources.dll`。
2. **反射验身份** —— 必须用 **Windows PowerShell 5.1**
   (`C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe`);PowerShell 7(.NET Core)
   无法正确反射这些 .NET Framework 程序集。公钥令牌字节要用 `{0:x2}` 格式化 ——
   直接对字节数组 `-join` 得到的是十进制数字串,不是十六进制令牌:

   ```powershell
   $ma = [Reflection.Assembly]::LoadFrom('E:\src\apkshellext\ApkShellext2\bin\Release\ApkShellext2.dll')
   $ma.GetName() | Format-List Name, Version   # ApkShellext2  3.4.5.0
   (($ma.GetName().GetPublicKeyToken() | % { '{0:x2}' -f $_ }) -join '')   # fb82661d1c53098c  (本地密钥)
   $sat = [Reflection.Assembly]::LoadFrom('E:\src\apkshellext\ApkShellext2\bin\Release\zh-CN\ApkShellext2.resources.dll')
   $sat.GetName() | Format-List Version, CultureName   # 3.4.5.0, zh-CN — Pkt 与主 DLL 相同
   ```

3. **ResourceManager 往返**(证明卫星可被检索,而不只是存在):

   ```powershell
   $rm = New-Object System.Resources.ResourceManager('ApkShellext2.Properties.Resources', $ma)
   $rm.GetString('btnOK', [cultureinfo]'zh-CN')              # → 确定   (来自卫星)
   $rm.GetString('btnOK', [cultureinfo]::InvariantCulture)   # → OK     (中性资源,在主 DLL 内)
   ```

4. **`bin\Release` 完整发布布局:** 9 个清单项 —— `ApkShellext2.dll(.pdb)`、六个 bat、
   `libwebp_x86/x64.dll`,
   再加上 19 个语言目录,以及 —— 找到 7-Zip 时 —— `ApkShellext2-4.0.0.7z`(约 700 KB)。
   `7z l` 必须在**两条路线上都恰好共 28 个文件**:9 个根条目(没有
   `System.Resources.Extensions.dll` —— 即便 dotnet CLI 编译把它留在了目录里,
   打包也照常排除;也没有
   `System.Memory/System.Buffers/System.Runtime.CompilerServices.Unsafe/System.Numerics.Vectors`
   —— VS 路线根本不复制它们,dotnet CLI 因传递依赖复制了也从不打包),外加 19 条
   `xx-XX\ApkShellext2.resources.dll` 卫星条目;且 `bin\Release` 里不得有
   `Resources\`/`LibWebP\` 子目录。
5. **Debug 布局:** `bin\Debug` 根有 `debug.bat` + bat + libwebp,且**没有 `.7z`**。

### 可测性门(Testability gate)

`eng\check-layers.ps1` 保证解析层不依赖外壳宿主。两个 CI job 都在 "Check publish layout"
之后、较慢的测试之前运行它;本地也用同一个脚本。它扫描 `ApkShellext2\Formats\` 与
`ApkShellext2\Imaging\` 下的全部 `.cs`(整行注释跳过),命中以下任何一项即失败:

- `System.Windows.*` —— WinForms / WPF 命名空间(`System.Windows.Forms`、
  `System.Windows.Media` 等);
- 注册层(`ShellRegistrar`、`RegistryWriter`);
- 外壳 COM 接口 `IContextMenu*`、`IShellExtInit`、`IQueryInfo`、`IThumbnailProvider`、
  `IExtractIconW`、`IExplorerCommand`;
- `[ComImport]`、`ComVisible`、`[Guid(...)]`。

允许的是 BCL、`System.Drawing`、根 `Utility.cs`、`Diagnostics\`、`Vendor\`,以及这些文件
本就使用的工程内解析/图标辅助类型。COM 与外壳注册代码属于 `Shell\`、`Registration\`、
`Preferences\` —— 它们可以引用解析层,反向不允许。只有这样,NUnit 或普通控制台进程才能
在不启动 explorer 的情况下加载 reader、parser、`SafeArchive`、图标链;`tests\ApkShellext2.Tests`
的测试正依赖这一点。

负例验证(预期 `exit 1`,还原后重新变绿 —— 门禁本身也要被验证):

```powershell
Add-Content ApkShellext2\Formats\AppxReader.cs 'using System.Windows.Forms;'
pwsh -File eng\check-layers.ps1    # 打印 "Formats\AppxReader.cs:<行号>: ...",exit 1
git checkout -- ApkShellext2\Formats\AppxReader.cs
pwsh -File eng\check-layers.ps1    # "layers OK: <count> C# files ...",exit 0
```

这个数字是脚本在当前工作树上实际扫描到的文件数,不是固定断言;在 `Formats\` 下新增
parser 或 writer 会让它变化,但不需要改门禁脚本。

门禁要求 `Formats\` 与 `Imaging\` 既没有 COM/注册依赖,也没有
`using Media = System.Windows.Media;` —— 这些在这里都只会是死引用。

这道门和 §7 的单元测试是一对:那些测试在普通 NUnit 进程里直接加载 parser、`SafeArchive`
和图标链,而这只在门禁成立时才行。若 `Formats\` 或 `Imaging\` 里需要用 shell 类型,门禁会
先拦住,而不是等测试(以及打包后的 handler)崩掉。

## 10. 坑清单

1. **restore 报 NU1100** → 机器**一个 NuGet 源都没有**(用户 `NuGet.Config` 里的空
   `<packageSources>` 节会干掉隐式默认源)。`dotnet nuget list source` 必须列出
   nuget.org;按 §4.1 添加。
2. **VS 的 MSBuild 报 `MSB4236 / MSB4276: 无法解析 SDK“Microsoft.NET.Sdk”`**(常伴随
   `vswhere` 静默无输出)= VS 安装损坏/不完整 —— `MSBuild\Sdks` 是空的。正解:
   VS Installer → **修复**,或带「*.NET 桌面开发*」负载重装。修复或干净重装后,
   vswhere 能解析,restore/build 零额外参数即可跑通。
3. **经典 MSBuild 开关带冒号**:`-v:m`、`-p:Configuration=Release`;`-v m`(空格形式,
   `dotnet build` 可用)会被 `MSBuild.exe` 拒收。
4. **不要把打包钩子改回 `<PostBuildEvent>` 属性** —— SDK 式工程里该属性的宏在**求值期**
   展开(那时 `$(TargetDir)` 等还是空),脚本会静默收到 `"" ""`。保持
   `AfterBuild` 式的 `Exec` target。
5. **`Content` + `CopyToOutputDirectory` 会在输出里重建源子目录**,除非每项都写
   `TargetPath=<裸文件名>`;并且 7z 通配符对**零匹配静默忽略** —— 一个 `*.bat` 通配符
   就能打出没有 bat 的包还退出 0。所以打包步骤用显式文件清单、缺条目即失败(§8):
   不要引入通配符,动过布局项后仍要查 `7z l`。
6. **绝不要把 dotnet CLI 产物装进 Explorer**:其预序列化对象资源在宿主进程里
   静默失败 —— 自定义文件类型图标照常工作,右键菜单整个消失。出货用 VS 路线
   产物;两条路线的打包都排除 `System.Resources.Extensions.dll`(§8)。
7. **snk**:被 gitignore;本地生成(§4.2)。公钥 ≠ 官方构建。
8. **原生桥 DLL 是不存在,不是漏了**:没有任何文件针对属性页宿主编译(§5.1),因此不附带桥二进制。
   不要"修"它。
9. **SVG 在仓内**(`ApkShellext2\SVG\Source\`,§3)—— 生成器的 `bin\`/`obj\`
   由根 `.gitignore` 覆盖;唯一剩下的子模块(PNGDecrush)独立编译时仍会
   在各自目录产生 `bin\`/`obj\`。
10. **MSBuild node-reuse 工作进程活着时改名/移动 SDK 目录会「Access denied」**
    (`MSBuild.exe … /nodemode:2 /nodereuse:True`,从 SDK 目录启动;
    `dotnet build-server shutdown` 不一定停得掉)。用
    `Get-CimInstance Win32_Process | ? CommandLine -match '<sdk-root>'` 找到持有者先停掉。
11. **PATH 上的 `dotnet` 可能只有运行时。** 用 `dotnet --version` 验证;报无 SDK 就用
    已装 SDK 的完整路径(本文所有命令都这么写)。
12. **四个文件是有意不跟随上游的**(§5):`.sln`、两个 csproj、`postbuild.cmd`。
    把任何一个回退成上游版本,就等于把老绕行方案(参数、shim、pregen)
    请回来。
13. **VS 路线报 `ALINK : error AL1078 对程序集签名时出错 - 未知错误(8013141e)`** =
    snk 是*密钥交换(key-exchange)*blob(`aiKeyAlg=0xa400`,裸
    `RSACryptoServiceProvider 1024` 的默认产物)。csc 无视该字段,`al.exe` 拒收。
    用 `sn -k` 或 §4.2 的 PowerShell 片段(`KeyNumber=Signature`,头 `… 00 24 00 00
    "RSA2"`)重新生成。不是 VS 安装问题,也不是仓库问题。
14. **VS 命令行的 `Bin\MSBuild.exe` 是 32 位**,它的 Exec 子进程看到的是 WOW64 重定向后
    的注册表(`HKLM\SOFTWARE\7-Zip`"不存在")和指向 `Program Files (x86)` 的
    `%ProgramFiles%` —— postbuild 里裸的 `reg query` 会静默跳过打包而编译照样绿。
    `postbuild.cmd` 因此显式查两个视图(`/reg:64`、`/reg:32`);改脚本时保留这两个开关。
    (VS IDE 本身用 64 位引擎构建,所以这个坑只咬 x86 启动器的命令行构建。)

## 11. 发布流程

发布**只能**走 `.github/workflows/release.yml`;本地编译产物是开发用的,绝不能手工挂到
GitHub Release 上。工作流在 `windows-2022` 上编译 FullFramework 路线(§6 路线 B),跑两个门禁
和单元测试,然后发布一个已存在的 annotated tag。

```bash
git tag -a v4.0.0 -m "ApkShellext2 4.0.0"   # 必须是 annotated,不能是轻量 tag
git push origin v4.0.0                      # push 只由 lead 执行,贡献者不要 push
```

tag 必须是 `v` + `version.txt` 去掉末尾 `.0`,与 `eng/package-name.ps1` 的包名规则一致。
发布必须签名:工作流从 `APKSHELLEXT2_SNK_B64` secret 还原
`ApkShellext2\apkshellext2_hy.snk`,secret 缺失时直接失败。

想空跑一次发布而不产生任何东西,就手动 dispatch 工作流并填 tag、`dry_run: true`(dispatch 的
默认值)。job 会把版本、dll 与 7z 的字节数和 SHA-256、上一个 tag、测试数写进 job summary,
但不创建 GitHub Release。检查逻辑都在 `eng/release-checks.ps1`,本地可以复现同样的结论:

```powershell
pwsh -File eng\release-checks.ps1 -Tag v4.0.0
```

以下情况脚本一律拒绝:tag 的数字部分与 `version.txt` 不一致、tag 不高于最近发布的 tag、
tag 是轻量 tag、tag 所在提交不是 `origin/main` 的祖先、缺包或缺语言目录。

dry run 默认编译 tag 的树 —— 那棵树就是将来的发布内容。如果 tag 还没准备好、想先拿当前
`main` 演练,就再加 `source_ref=main`(只允许和 `dry_run: true` 一起用):发布的仍然是 tag,
所有校验也仍然对着 tag 做,只有编译输入换了。

真正发布时用 `gh release create <tag>` 创建 Release:标题去掉 `v`,仓库有
`release-notes/<tag>.md` 就用它、没有就用提交记录生成 notes,tag 里带 `-` 就标记为
prerelease。随后断言上传资产的名称与大小,断言失败则整个 job 失败,不会留下坏的 Release;
修好原因后重新 dispatch 即可(版本检查与递增检查会拒绝通过 tag push 重发已发布的版本)。

## 附录 A —— 一键参考脚本(路线 A)

```powershell
$R = 'E:\src\apkshellext'
$DOTNET = 'E:\src\apkshellext-dep\dotnet-sdk-10\dotnet.exe'

# --- 0. 前置(一次性):子模块 §3、NuGet 源 §4.1、snk §4.2 ---
& $DOTNET nuget list source | Select-String nuget.org -Quiet | % { if (-not $_) { throw '没有 nuget.org 源 - 见 §4.1' } }
if (-not (Test-Path "$R\ApkShellext2\apkshellext2_hy.snk")) { throw '先生成 snk - 见 §4.2' }

# --- 1. 主工程 + 单元测试(restore 隐式;analyzer 生成器在编译内自动跑) ---
Set-Location $R
& $DOTNET build ApkShellext2\ApkShellext2.csproj     -c Release --nologo; if ($LASTEXITCODE) { throw '主工程编译失败' }
& $DOTNET test tests\ApkShellext2.Tests\ApkShellext2.Tests.csproj -c Release --nologo; if ($LASTEXITCODE) { throw '测试失败' }

# --- 2. 验证(必须在 Windows PowerShell 5.1 下跑,见 §9) ---
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

## 附录 B —— 一键参考脚本(路线 B)

```powershell
$R = 'E:\src\apkshellext'
$vswhere = "${env:ProgramFiles(x86)}\Microsoft Visual Studio\Installer\vswhere.exe"
$msb = & $vswhere -latest -requires Microsoft.Component.MSBuild -find 'MSBuild\**\Bin\MSBuild.exe' | Select-Object -First 1
if (-not $msb) { throw '找不到 MSBuild - 修复 VS / 安装 .NET 桌面开发负载(§10 坑 2)' }

Set-Location $R
& $msb ApkShellext2.sln -t:restore            -p:Configuration=Release -nologo -v:m; if ($LASTEXITCODE) { throw 'restore 失败' }
& $msb ApkShellext2.sln -p:Configuration=Release              -nologo -v:m; if ($LASTEXITCODE) { throw '编译失败' }

# 验证 VS 路线特有项:输出里没有 facade dll、没有 System.Resources.Extensions.dll;
# 包应为 28 文件(9 根文件 + 19 条卫星资源),不含
#  System.Resources.Extensions.dll(§8 打包清单排除)
Test-Path "$R\ApkShellext2\bin\Release\System.Resources.Extensions.dll"   # 应为 False
Test-Path "$R\ApkShellext2\bin\Release\System.Memory.dll"                 # 应为 False
& 'D:\Software\7-Zip\7z.exe' l "$R\ApkShellext2\bin\Release\ApkShellext2-4.0.0.7z"   # 应为 28 文件(7z 路径按机器调整)
```

---

*对照文档:[English version compile_guidance.md](compile_guidance.md)。*
