using System.Diagnostics;
using System.IO;
using System.Text;
using HWZCodeBridge.Core;

namespace HWZCodeBridge.Build;

/// <summary>
/// 第三方工具注册表。
///
/// 设计约定：任何被集成的外部工具都必须在 <see cref="CreateBuiltIn"/> 中登记
/// 名称 / 用途 / 官方主页 / 许可证 / 来源组织，界面会一并展示，满足"标明来源"的要求。
/// 用户自行添加的工具同样强制填写来源，否则拒绝保存。
/// </summary>
public static class ToolRegistry
{
    public const string LicenseDisclaimer = "许可证信息以各项目官方页面为准，分发前请自行核对合规性。";

    public static List<ToolDefinition> BuiltIn { get; } = CreateBuiltIn();

    private static List<ToolDefinition>? _custom;
    public static List<ToolDefinition> Custom => _custom ??= JsonStore.Load(AppPaths.ToolsFile, () => new List<ToolDefinition>());

    public static IEnumerable<ToolDefinition> All => BuiltIn.Concat(Custom);

    /// <summary>
    /// 内置工具清单。所有条目均为第三方开源 / 免费工具，本软件不修改其代码，
    /// 仅通过命令行调用，来源与许可证在此集中声明。
    /// </summary>
    private static List<ToolDefinition> CreateBuiltIn() => new()
    {
        new ToolDefinition
        {
            Id = "pyinstaller",
            Name = "PyInstaller",
            Purpose = "把 Python 脚本打包成单文件 exe，自动收集依赖解释器",
            Category = "打包",
            Homepage = "https://pyinstaller.org",
            License = "GPL-2.0-or-later（含 bootloader 例外条款）",
            SourceNote = "PyInstaller Development Team",
            DetectCommand = "python",
            VersionArgs = "-m PyInstaller --version"
        },
        new ToolDefinition
        {
            Id = "nuitka",
            Name = "Nuitka",
            Purpose = "把 Python 编译为 C 代码再生成原生 exe，运行效率更高",
            Category = "打包",
            Homepage = "https://nuitka.net",
            License = "Apache-2.0",
            SourceNote = "Kay Hayen 及 Nuitka 贡献者",
            DetectCommand = "python",
            VersionArgs = "-m nuitka --version"
        },
        new ToolDefinition
        {
            Id = "dotnet",
            Name = ".NET SDK",
            Purpose = "编译 C# 源码并发布为独立 exe（PublishSingleFile）",
            Category = "编译",
            Homepage = "https://dotnet.microsoft.com/download",
            License = "MIT",
            SourceNote = "Microsoft / .NET Foundation",
            DetectCommand = "dotnet",
            VersionArgs = "--version"
        },
        new ToolDefinition
        {
            Id = "node",
            Name = "Node.js",
            Purpose = "JavaScript 运行时，同时作为 pkg / esbuild 的执行基础",
            Category = "运行时",
            Homepage = "https://nodejs.org",
            License = "MIT",
            SourceNote = "OpenJS Foundation",
            DetectCommand = "node",
            VersionArgs = "--version"
        },
        new ToolDefinition
        {
            Id = "pkg",
            Name = "pkg",
            Purpose = "把 Node.js 项目打包为 Windows exe（原 vercel/pkg 已归档，社区继任为 @yao-pkg/pkg）",
            Category = "打包",
            Homepage = "https://github.com/yao-pkg/pkg",
            License = "MIT",
            SourceNote = "vercel/pkg 原作者，现由 yao-pkg 社区维护",
            DetectCommand = "pkg",
            VersionArgs = "--version"
        },
        new ToolDefinition
        {
            Id = "gcc",
            Name = "GCC / MinGW-w64",
            Purpose = "编译 C / C++ 源码为 exe，使用 -static 生成免依赖可执行文件",
            Category = "编译",
            Homepage = "https://www.mingw-w64.org",
            License = "GPL-3.0-or-later（GCC 运行时库有例外条款）",
            SourceNote = "GNU 项目 / MinGW-w64 团队",
            DetectCommand = "g++",
            VersionArgs = "--version"
        },
        new ToolDefinition
        {
            Id = "clang",
            Name = "LLVM Clang",
            Purpose = "C / C++ 的替代编译器，错误提示更友好",
            Category = "编译",
            Homepage = "https://llvm.org",
            License = "Apache-2.0 WITH LLVM-exception",
            SourceNote = "LLVM 项目 / LLVM Foundation",
            DetectCommand = "clang++",
            VersionArgs = "--version"
        },
        new ToolDefinition
        {
            Id = "go",
            Name = "Go 工具链",
            Purpose = "编译 Go 源码为独立 exe（天然静态链接）",
            Category = "编译",
            Homepage = "https://go.dev",
            License = "BSD-3-Clause",
            SourceNote = "Google / Go 项目贡献者",
            DetectCommand = "go",
            VersionArgs = "version"
        },
        new ToolDefinition
        {
            Id = "rustc",
            Name = "Rust 编译器",
            Purpose = "用 rustc 直接把单个 .rs 源文件编译为 exe",
            Category = "编译",
            Homepage = "https://www.rust-lang.org",
            License = "MIT 或 Apache-2.0（双许可）",
            SourceNote = "Rust 项目 / Rust Foundation",
            DetectCommand = "rustc",
            VersionArgs = "--version"
        },
        new ToolDefinition
        {
            Id = "jpackage",
            Name = "JDK / jpackage",
            Purpose = "把 Java 程序打包为自带运行时的 Windows 应用目录",
            Category = "打包",
            Homepage = "https://openjdk.org",
            License = "GPL-2.0 WITH Classpath-exception-2.0",
            SourceNote = "OpenJDK 社区（亦可使用 Eclipse Temurin 发行版）",
            DetectCommand = "jpackage",
            VersionArgs = "--version"
        },
        new ToolDefinition
        {
            Id = "upx",
            Name = "UPX",
            Purpose = "对已生成的 exe 做无损压缩，显著减小体积",
            Category = "辅助",
            Homepage = "https://upx.github.io",
            License = "GPL-2.0-or-later（含特殊例外）",
            SourceNote = "Markus Oberhumer, Laszlo Molnar & John Reiser",
            DetectCommand = "upx",
            VersionArgs = "--version"
        },
        new ToolDefinition
        {
            Id = "ilspy",
            Name = "ILSpy",
            Purpose = "反编译 .NET 程序集，用于核对编译产物的类型与结构",
            Category = "辅助",
            Homepage = "https://github.com/icsharpcode/ILSpy",
            License = "MIT",
            SourceNote = "ICSharpCode 团队",
            DetectCommand = "ilspycmd",
            VersionArgs = "--version"
        },
        new ToolDefinition
        {
            Id = "pe-bear",
            Name = "PE-bear",
            Purpose = "查看 PE 文件结构（节区、导入表、资源），排查打包异常",
            Category = "辅助",
            Homepage = "https://github.com/hasherezade/pe-bear",
            License = "GPL-3.0",
            SourceNote = "hasherezade",
            DetectCommand = "pe-bear",
            VersionArgs = "--version"
        }
    };

    public static ToolDefinition? Find(string id) =>
        All.FirstOrDefault(t => t.Id.Equals(id, StringComparison.OrdinalIgnoreCase));

    // ───────────────── 可执行文件路径覆盖 ─────────────────

    private static string PathsFile => Path.Combine(AppPaths.RootDir, "tool-paths.json");
    private static Dictionary<string, string>? _pathOverrides;

    private static Dictionary<string, string> PathOverrides =>
        _pathOverrides ??= JsonStore.Load(PathsFile, () => new Dictionary<string, string>());

    /// <summary>把用户手动指定的工具路径应用到当前清单。</summary>
    public static void LoadPathOverrides()
    {
        foreach (var tool in All)
        {
            if (PathOverrides.TryGetValue(tool.Id, out var path) && !string.IsNullOrWhiteSpace(path))
            {
                tool.CustomPath = path;
            }
        }
    }

    public static void SavePathOverride(string id, string path)
    {
        PathOverrides[id] = path;
        JsonStore.Save(PathsFile, PathOverrides);

        var tool = Find(id);
        if (tool is not null) tool.CustomPath = path;
    }

    /// <summary>为某语言推荐默认打包工具。</summary>
    public static string DefaultToolFor(string languageId) => languageId switch
    {
        "python" => "pyinstaller",
        "csharp" => "dotnet",
        "javascript" or "typescript" => "pkg",
        "cpp" => "gcc",
        "java" => "jpackage",
        _ => "dotnet"
    };

    public static bool AddCustom(ToolDefinition tool, out string error)
    {
        error = "";

        if (string.IsNullOrWhiteSpace(tool.Name))
        {
            error = "工具名称不能为空。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(tool.Homepage) && string.IsNullOrWhiteSpace(tool.SourceNote))
        {
            error = "必须填写来源：至少提供官方主页或来源组织，以便追溯第三方工具出处。";
            return false;
        }

        if (string.IsNullOrWhiteSpace(tool.DetectCommand))
        {
            error = "必须填写可执行文件名，否则无法调用该工具。";
            return false;
        }

        tool.Id = "custom-" + Guid.NewGuid().ToString("N")[..8];
        tool.IsBuiltIn = false;

        if (string.IsNullOrWhiteSpace(tool.License)) tool.License = "未标注";

        Custom.Add(tool);
        JsonStore.Save(AppPaths.ToolsFile, Custom);
        Logger.Info("工具", $"已添加自定义工具：{tool.Name}（来源：{tool.SourceNote} {tool.Homepage}）");
        return true;
    }

    public static void RemoveCustom(string id)
    {
        var hit = Custom.FirstOrDefault(t => t.Id == id);
        if (hit is null) return;

        Custom.Remove(hit);
        JsonStore.Save(AppPaths.ToolsFile, Custom);
        Logger.Info("工具", $"已移除自定义工具：{hit.Name}");
    }

    /// <summary>并行检测所有工具的安装状态。</summary>
    public static async Task DetectAllAsync()
    {
        var tasks = All.Select(DetectAsync).ToArray();
        await Task.WhenAll(tasks);
    }

    private static async Task DetectAsync(ToolDefinition tool)
    {
        try
        {
            var (ok, output) = await RunDetectAsync(tool);
            tool.Detected = ok;
            tool.Version = ok ? FirstLine(output) : "";
        }
        catch
        {
            tool.Detected = false;
            tool.Version = "";
        }
    }

    private static async Task<(bool Ok, string Output)> RunDetectAsync(ToolDefinition tool)
    {
        var fileName = string.IsNullOrWhiteSpace(tool.CustomPath) ? tool.DetectCommand : tool.CustomPath;

        var psi = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = tool.VersionArgs,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi };

        if (!process.Start()) return (false, "");

        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(4));
        try
        {
            await process.WaitForExitAsync(cts.Token);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            return (false, "");
        }

        var text = (await stdout) + (await stderr);

        // 退出码非 0，或明确提示模块缺失，都视为未就绪；
        // 否则会把 "No module named PyInstaller" 之类的报错误判成已安装。
        var missing = text.Contains("No module named", StringComparison.OrdinalIgnoreCase)
                      || text.Contains("不是内部或外部命令", StringComparison.Ordinal)
                      || text.Contains("command not found", StringComparison.OrdinalIgnoreCase);

        return (process.ExitCode == 0 && !missing, text);
    }

    /// <summary>从工具输出里提炼版本号；取不到版本号时退回首行摘要。</summary>
    private static string FirstLine(string text)
    {
        var version = System.Text.RegularExpressions.Regex.Match(text, @"v?\d+\.\d+(?:\.\d+)?");
        if (version.Success) return version.Value;

        var line = text.Replace("\r", "").Split('\n')
            .Select(l => l.Trim())
            .FirstOrDefault(l => l.Length > 0) ?? "";

        return line.Length > 60 ? line[..60] : line;
    }
}
