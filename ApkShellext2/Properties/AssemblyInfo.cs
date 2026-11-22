using System.Resources;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

// 有关程序集的常规信息通过以下
// 特性集控制。更改这些特性值可修改
// 与程序集关联的信息。
[assembly: AssemblyTitle("ApkShellext2")]
[assembly: AssemblyDescription("Windows shell plugin enabling showing app package file icon on windows.")]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany("Haoyu Cloris")]
[assembly: AssemblyProduct("ApkShellext2")]
[assembly: AssemblyCopyright("Copyright © 2015 Jingang Guo, KK; © 2023 1595901624; © 2026 nkh0472")]
[assembly: AssemblyTrademark("ApkShellExt")]

// 将 ComVisible 设置为 false 使此程序集中的类型
// 对 COM 组件不可见。  如果需要从 COM 访问此程序集中的类型，
// 则将该类型上的 ComVisible 特性设置为 true。
// 注意：本程序集以源码方式内嵌了 Svg/SharpShell/PlistCS 等依赖，
// 程序集级 true 会让 regasm 把内嵌依赖的每个公共类型都注册成 COM 组件
// （HKCR 下数百个 Svg.* / SharpShell.* / Record\{...} 键），卸载难以清除。
// 仅 4 个 Shell 处理器类带 [ComVisible(true)]，保持程序集级 false。
[assembly: ComVisible(false)]

// 如果此项目向 COM 公开，则下列 GUID 用于类型库的 ID
[assembly: Guid("1F869CEE-4FDA-35D9-896F-43975A87D1F5")]

// 程序集的版本号不在本文件里写死：AssemblyVersion/AssemblyFileVersion 由
// ApkShellext2.csproj 从仓库根的 version.txt 读取后生成（WriteCodeFragment），
// 保证版本只有一处来源。
[assembly: NeutralResourcesLanguage("en-US")]
