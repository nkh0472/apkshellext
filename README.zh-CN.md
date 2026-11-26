# ApkShellext2

[![release](https://img.shields.io/badge/release_4.2.0-blue)](https://github.com/nkh0472/apkshellext/releases)
[![.NET](https://img.shields.io/badge/.NET_Framework-4.8-512BD4)](#从源码构建)
[![license](https://img.shields.io/badge/license-MIT-green)](LICENSE)

[English](README.md) | **简体中文**

在资源管理器中为手机应用安装包显示真实图标、悬浮信息与右键管理菜单的 Windows 外壳扩展。支持 Windows 10 / 11，运行于 .NET Framework 4.8。

## 支持的文件类型

| 扩展名 | 图标 | 悬浮信息 | 右键菜单 | 缩略图 |
|--------|:----:|:--------:|:--------:|:------:|
| `.apk` | ✔ | ✔ | ✔ | ✔ |
| `.apks` `.apkm` `.xapk`（拆分容器，3.4.0 起） | ✔ | ✔ | ✔ | — |
| `.aab` | ✔ | — | — | — |
| `.ipa` | ✔ | ✔ | ✔ | — |
| `.appx` `.appxbundle` | ✔ | ✔ | ✔ | — |

不支持 `.xap`。

## 功能特性

下面这些都在资源管理器进程里完成——一个 DLL，没有服务，也没有常驻后台进程。

| 功能 | 做什么 | 说明 |
|------|--------|------|
| **图标** | 任意缩放级别都显示真实应用图标 | Android 自适应图标按 Android 自己的 1.5x 中心裁切绘制；某个变体画不出来时回退传统图标 |
| **缩略图** | `.apk` 在文件夹视图里的预览 | 按请求尺寸填充；大图标视图为 256 px |
| **悬浮信息** | 版本、包名、体积、Min/Max SDK、打包器 | 包里取不到的事实，连标签一起整行省略 |
| **右键菜单** | Win10 与 Win11 用同一套条目 | Windows 11 22H2 及以上是现代 `IExplorerCommand` 菜单；Windows 10 与 Win11 的「显示更多选项」仍是经典 `IContextMenu` 菜单 |
| **重命名** | 按模板批量重命名 | 支持 `%AppName%`、`%Version%`、`%Package%`、`%Attr(tag,attr)` 等；首选项在输入框旁列出变量，点击插入到光标处 |
| **提取信息** | 导出 CSV，或提取某个 XML 条目 | CSV：UTF-8 带 BOM，每个选中文件一行，固定 20 列——MinSdk、TargetSdk、Debuggable、Packer 四列位于 Type 与 SizeBytes 之间，Verdict、Evidence 两列位于 Packer 与 SizeBytes 之间——另有 SHA-256、XXH3 与状态列，读不出的文件也占一行。列顺序属于对外契约：请按表头取列，不要写死列号。XML：写进你选的文件夹，文件名带重命名模板前缀 |
| **复制信息** | 文件路径、应用名、包名、版本 | 多选时一行一个值，按选择顺序 |
| **商店页面** | Google Play、Amazon Appstore、VirusTotal | VirusTotal 条目只在点击时才后台算哈希，随后打开 `virustotal.com/gui/file/<sha256>` |
| **首选项** | 所有开关集中在一处 | 界面语言、应用名语言、角标、各商店菜单项、缩略图与自适应图标开关、错误日志 |

| 实现基础 | 说明 |
|----------|------|
| **外壳集成自己实现** | 图标、悬浮信息、缩略图与右键菜单四个处理器都在本仓库里直接基于 Windows 外壳接口实现——不依赖第三方外壳框架，也不依赖菜单助手 |
| **19 种语言** | 所有文案都有 19 种语言；守卫测试保证每份翻译与中性资源同步，半翻译的菜单会直接构建失败 |
| **解析加固** | 每条读取路径都有体积与解压比率上限，恶意输入在分配内存之前就被拦住；WebP 图标由 libwebp 1.6.0 绘制 |
| **体积小** | 一个约 0.9 MB 的 DLL，加两个 `libwebp` 二进制、安装脚本与语言目录 |
## 安装

1. 下载最新 Release 压缩包，解压到固定目录（如 `C:\Program Files\ApkShellext2\`）。
2. **以管理员身份**运行 `install.bat`——它会为 64 位与 32 位 Explorer 宿主注册扩展，并生成 NGEN 原生映像（开机后首次显示图标更快）。没有管理员权限？改用 `install_user.bat`：`HKCU` 下的每用户注册，无需 UAC（用 `uninstall_user.bat` 移除）；设计上跳过 NGEN，仅每次开机后的第一批图标 JIT 编译稍慢。机器级注册始终优先于每用户注册。注意：`install.bat` 会拒绝普通用户可写的目录（否则任何本地账户都能替换所有用户 Explorer 加载的 DLL）——请把解压目录移到 `%ProgramFiles%`，或直接用 `install_user.bat`。
3. 图标没有刷新时，运行 `restart_explorer.bat`。

**卸载：** 以管理员身份运行 `uninstall.bat`——同时反注册 64/32 位两个视图，即使 DLL 已被删除也能清扫注册表（可从本版本任意安装目录运行），并清理 3.4.2 之前版本遗留的旧 COM 项。若卸载后文件图标仍在，残留的是 Windows 图标缓存（`IconCache.db` / `thumbcache_*.db`）而非扩展本身，删除后重启资源管理器即可。

## 设置

右键安装包 → **ApkShellext2 → 首选项**（非模态，绝不阻塞 Explorer）。设置项保存在 `HKCU\SOFTWARE\ApkShellext2`，包括 `SupportAdaptiveIcon`（支持自适应图标）、`UseAppNameLanguage` / `AppNameLanguage`、`CheckForUpdates`（设为 `False` 关闭每日后台版本检查）以及悬浮信息自定义。

## 从源码构建

完整构建与部署指南——工具链选择（VS 与 dotnet CLI，以及为何只有其一能产出可部署的产物）、打包、libwebp 重编、常见坑——见 [compile_guidance.zh-CN.md](compile_guidance.zh-CN.md)（[English](compile_guidance.md)）。

速成版：`--recursive` 克隆后执行 `msbuild ApkShellext2.sln /p:Configuration=Release /p:Platform="Any CPU"`。需要 Visual Studio 2019+ 与 .NET Framework 4.8 targeting pack；发行构建使用自己的 `sn -k` 密钥签名。

## 项目历史与维护者

| 时段 | 维护者 | 贡献 |
|------|--------|------|
| 2009 – | [kkguo](https://github.com/kkguo) | ApkShellext2 原作者 |
| – 2023 | [1595901624](https://github.com/1595901624) | 维护 v3.x，创建 `ApkShellext2_hy` 分支（.aab 支持等） |
| 2026 – | [nkh0472](https://github.com/nkh0472) | 现任维护者：依赖现代化（libwebp 1.6.0）、Windows 11 图标修复、本地化应用名、拆分 APK 容器、归档加固 |

## 更新日志

完整的双语发行说明见 [Releases 页面](https://github.com/nkh0472/apkshellext/releases)。

| 版本 | 摘要 |
|------|------|
| 4.2.0 | 右键菜单告诉你这个包是什么：CSV 增加 `MinSdk`、`TargetSdk`、`Debuggable`、`Packer`、`Verdict`、`Evidence` 六列；`.apks` 从 `toc.pb` 读出最低 SDK；base 包把图标放在配置 split 里时也能显示；语言名走 ASCII 守卫并有测试。图标引用没有资源表的包现在判为 `Tampered`，不再显示 `unreadable` — dll 943,104 B，7z 717,249 B |
| 4.1.2 | 设置：所有功能开关改为按布尔值读取，手工写入或旧版本留下的 DWORD（`1`/`0`）、`yes`/`on`/`1` 不会再静默把功能关掉（角标、缩略图拉伸、自适应图标、商店条目、调试日志）；新增 19 条测试钉住可接受的写法，README 功能特性改为两张表格 — dll 935,424 B，7z 714,456 B |
| 4.1.1 | 翻译：3.x/4.x 新增命令用到的 24 条文案（复制菜单、VirusTotal 项、文件夹选择器、重命名变量说明）补齐到全部 19 种语言，不再回退英文；新增测试保证每种语言的资源文件与中性文件保持同步 — dll 935,936 B（尺寸不变），7z 708,473 B → 714,213 B |
| 4.1.0 | 右键菜单：新增「复制信息」子菜单（文件路径、应用名、包名、版本，多选时每个文件一行），「提取信息」子菜单改为收纳 CSV 导出与两项 XML 提取（现代文件夹选择器、文件名带重命名前缀），新增「在 VirusTotal 查看哈希」项，CSV 增加 SHA-256 与 XXH3-64 两列，信息提示可显示 min/target SDK 与打包器，菜单图标改为与系统小图标同尺寸——dll 909,312 B → 935,936 B，7z 698,828 B → 708,473 B | 
| 4.0.0 | 外壳层收归仓库：四个处理器与右键菜单直接基于外壳接口实现，不再依赖内置的第三方外壳框架；Windows 11 22H2+ 由 `IExplorerCommand` 提供现代菜单，Windows 10 与「显示更多选项」仍为经典菜单；新增「导出信息 (CSV)…」命令；首选项可点选插入重命名变量；`update.bat` 回读全部处理器槽位，`/rollback` 精确还原升级前的文件集——dll 1,165,312 B → 909,312 B，7z 746,997 B → 698,946 B |
| 3.5.0 | 自适应图标转正：Android 一致的 1.5x 缩放、框架颜色引用、区分大小写条目解析、DPI 无关矢量渲染；支持 `<bitmap>` |
| 3.4.5 | 修复安装脚本 NGEN 步骤（`/quiet` 并非 ngen.exe 合法参数，原生映像其实从未生成） |
| 3.4.4 | 安全/性能审计加固；19 种语言全部补齐；AssemblyVersion 终于跟随版本号 |
| 3.4.3 | NGEN 原生映像；可选免管理员的每用户安装；首选项深色主题；更新检查可关闭 |
| 3.4.2 | 安装/卸载脚本现代化：双位数注册、离线注册表清扫、旧 COM 项清理；COM 暴露面收敛为四个 shell 处理器 |
| 3.4.1 | VectorDrawable 渲染迁移到内置 SVG 引擎（渐变含多停靠点/角度/径向、嵌套 group、裁剪路径）；解析间接颜色引用 |
| 3.4.0 | 拆分 APK 容器（`.apks`/`.apkm`/`.xapk`）；全部读取路径的压缩炸弹与长度溢出防护；MSIX bundle 成员名解码 |
| 3.3.1 | FacadeResolver——修复 Explorer 内矢量图标静默失败；矢量 group 变换；自适应图标回退链 |
| 3.3.0 | 本地化应用名；SVG/PNG 图标回归修复；非模态首选项 |
| 3.2.0 | 图标扩展名大小写不敏感（#99）；FileShare.Delete（#103）；WOW64 安全提权（#116） |
| 3.1.0 | 依赖现代化：libwebp 1.6.0（CVE-2023-4863 等）、SharpZipLib 1.4.2、QRCoder 1.4.2、SVG.NET 3.5.0 —— *存在 SVG 回归，3.3.1 已修复* |
| 3.0.0 | 修复 Chrome/Edge 下载崩溃；升级到 .NET Framework 4.8 |

## 致谢

本项目建立在以下开源作品之上：

| 组件 | 用途 |
|------|------|
| [libwebp](https://chromium.googlesource.com/webm/libwebp) | WebP 编解码（1.6.0，源码构建） |
| [WebP-wrapper](https://github.com/JosePineiro/WebP-wrapper) | WebP C# 绑定 |
| [SVG](https://github.com/vvvv/SVG) | SVG / 矢量 drawable 渲染 |
| [PlistCS](https://github.com/animetrics/PlistCS) | iOS plist 读取 |
| [PNGDecrush](https://github.com/MikeWeller/PNGDecrush) | PNG zlib 重编码 |
| [apk-info](https://github.com/delvinru/apk-info) | 拆分 APK 容器结构与防 zip-bomb 准则 |
| [apkspec](https://apkspec.readthedocs.io/) | 非官方拆分 APK 容器规范 |
| [Ultimate Social](https://www.iconfinder.com/iconsets/ultimate-social) | 角标图标集 |

## 许可证

[MIT](LICENSE) — Copyright (c) 2015 Jingang Guo, KK；Copyright (c) 2023 1595901624；Copyright (c) 2026 nkh0472。
