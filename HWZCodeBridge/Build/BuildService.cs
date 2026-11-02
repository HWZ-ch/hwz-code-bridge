using System.Diagnostics;
using System.IO;
using System.Text;
using HWZCodeBridge.Convert;
using HWZCodeBridge.Core;

namespace HWZCodeBridge.Build;

public sealed class BuildRequest
{
    public required string SourcePath { get; init; }
    public required LanguageInfo Language { get; init; }
    public required string ToolId { get; init; }
    public required string OutputDirectory { get; init; }
    public string OutputName { get; init; } = "";
    public string Architecture { get; init; } = "win-x64";
    public bool CompressWithUpx { get; init; }
}

public sealed class BuildResult
{
    public bool Success { get; init; }
    public string OutputPath { get; init; } = "";
    public string Message { get; init; } = "";
    public long ElapsedMs { get; init; }
    public int ExitCode { get; init; }
}

/// <summary>
/// 源码 → 可执行文件的编排层。不内嵌任何编译器，全部通过命令行调用本机已安装的第三方工具，
/// 调用前会在日志中打印完整命令，便于用户审计与复现。
/// </summary>
public sealed class BuildService
{
    private sealed record Step(string FileName, string Arguments, string WorkingDirectory, string Description);

    public async Task<BuildResult> BuildAsync(BuildRequest req, IProgress<string> log, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();

        if (!File.Exists(req.SourcePath))
        {
            return new BuildResult { Success = false, Message = "源文件不存在，请重新选择。", ElapsedMs = sw.ElapsedMilliseconds };
        }

        var tool = ToolRegistry.Find(req.ToolId);
        if (tool is null)
        {
            return new BuildResult { Success = false, Message = $"未找到工具 '{req.ToolId}'。", ElapsedMs = sw.ElapsedMilliseconds };
        }

        var name = SanitizeName(req.OutputName);
        if (name.Length == 0) name = Path.GetFileNameWithoutExtension(req.SourcePath);

        var outDir = Path.Combine(req.OutputDirectory, name);
        var workDir = Path.Combine(Path.GetTempPath(), "hwz-build-" + Guid.NewGuid().ToString("N")[..8]);

        try
        {
            Directory.CreateDirectory(outDir);
            Directory.CreateDirectory(workDir);

            log.Report($"▶ 目标工具：{tool.Name}（来源：{tool.SourceNote}）");
            log.Report($"▶ 源文件：{req.SourcePath}");
            log.Report($"▶ 输出目录：{outDir}");
            log.Report("");

            var steps = BuildSteps(req, tool, name, workDir, outDir);
            if (steps.Count == 0)
            {
                return new BuildResult
                {
                    Success = false,
                    Message = $"当前版本尚未内置「{tool.Name}」的打包流程，请在工具中心查看该工具的官方用法。",
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }

            foreach (var step in steps)
            {
                ct.ThrowIfCancellationRequested();

                log.Report($"$ {step.FileName} {step.Arguments}");
                var code = await RunAsync(step, log, ct);

                if (code != 0)
                {
                    sw.Stop();
                    log.Report("");
                    log.Report($"✗ 步骤失败（退出码 {code}）：{step.Description}");
                    return new BuildResult
                    {
                        Success = false,
                        Message = $"{step.Description} 失败，退出码 {code}。请检查上方输出。",
                        ExitCode = code,
                        ElapsedMs = sw.ElapsedMilliseconds
                    };
                }

                log.Report($"✓ {step.Description} 完成");
                log.Report("");
            }

            var exe = FindProducedExe(outDir);

            if (req.CompressWithUpx && exe.Length > 0)
            {
                var upx = ToolRegistry.Find("upx");
                if (upx is { Detected: true })
                {
                    log.Report("$ upx --best " + exe);
                    await RunAsync(new Step("upx", $"--best \"{exe}\"", outDir, "UPX 压缩"), log, ct);
                    log.Report("");
                }
                else
                {
                    log.Report("· 已跳过 UPX 压缩：未检测到 UPX，可在工具中心查看获取方式。");
                }
            }

            sw.Stop();

            if (exe.Length == 0)
            {
                log.Report("· 命令执行成功，但未在输出目录找到 .exe。若工具输出的是应用目录，请查看上方日志中的实际产物路径。");
                return new BuildResult
                {
                    Success = true,
                    Message = "打包命令已执行完成，但未定位到 exe 产物，请查看日志。",
                    ElapsedMs = sw.ElapsedMilliseconds
                };
            }

            var size = new FileInfo(exe).Length / 1024.0 / 1024.0;
            log.Report($"✓ 已生成：{exe}（{size:0.00} MB）");

            return new BuildResult
            {
                Success = true,
                OutputPath = exe,
                Message = $"打包成功：{Path.GetFileName(exe)}（{size:0.00} MB）",
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
        catch (OperationCanceledException)
        {
            sw.Stop();
            log.Report("· 用户已取消打包。");
            return new BuildResult { Success = false, Message = "已取消。", ElapsedMs = sw.ElapsedMilliseconds };
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.Error("打包", "打包过程异常", ex.ToString());
            log.Report("✗ 异常：" + ex.Message);
            return new BuildResult { Success = false, Message = "打包异常：" + ex.Message, ElapsedMs = sw.ElapsedMilliseconds };
        }
        finally
        {
            TryCleanup(workDir);
        }
    }

    private static void TryCleanup(string dir)
    {
        try
        {
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch
        {
            // 临时目录清理失败不影响主流程
        }
    }

    private static string SanitizeName(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "";

        var invalid = Path.GetInvalidFileNameChars();
        var cleaned = new string(raw.Where(c => !invalid.Contains(c)).ToArray()).Trim();
        return cleaned.Length > 60 ? cleaned[..60] : cleaned;
    }

    // ─────────────────────────────────────────────────────────
    // 各语言的打包步骤
    // ─────────────────────────────────────────────────────────

    private List<Step> BuildSteps(BuildRequest req, ToolDefinition tool, string name, string workDir, string outDir)
    {
        var src = req.SourcePath;
        var steps = new List<Step>();

        switch (req.Language.Id)
        {
            case "python" when tool.Id == "pyinstaller":
                steps.Add(new Step(
                    "python",
                    $"-m PyInstaller --onefile --clean --noconfirm --distpath \"{outDir}\" --workpath \"{Path.Combine(workDir, "build")}\" --specpath \"{workDir}\" \"{src}\"",
                    workDir,
                    "PyInstaller 打包"));
                break;

            case "python" when tool.Id == "nuitka":
                steps.Add(new Step(
                    "python",
                    $"-m nuitka --onefile --assume-yes-for-downloads --output-dir=\"{outDir}\" --output-filename=\"{name}.exe\" \"{src}\"",
                    workDir,
                    "Nuitka 编译"));
                break;

            case "csharp" when tool.Id == "dotnet":
                {
                    var rid = string.IsNullOrWhiteSpace(req.Architecture) ? "win-x64" : req.Architecture;

                    // 单个 .cs 文件无法直接 publish，先生成一个临时控制台项目承载它。
                    var projectPath = src.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                        ? src
                        : BuildTempCSharpProject(src, name, workDir, rid);

                    steps.Add(new Step(
                        "dotnet",
                        $"publish \"{projectPath}\" -c Release -r {rid} --self-contained true " +
                        $"-p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:DebugType=none -o \"{outDir}\"",
                        workDir,
                        "dotnet publish"));
                    break;
                }

            case "javascript" when tool.Id == "pkg":
                steps.Add(new Step(
                    "pkg",
                    $"\"{src}\" --targets node18-win-x64 --output \"{Path.Combine(outDir, name + ".exe")}\"",
                    workDir,
                    "pkg 打包"));
                break;

            case "typescript" when tool.Id == "pkg":
                steps.Add(new Step(
                    "npx",
                    $"--yes esbuild \"{src}\" --bundle --platform=node --target=node18 --outfile=\"{Path.Combine(workDir, "bundle.js")}\"",
                    workDir,
                    "esbuild 编译 TypeScript"));
                steps.Add(new Step(
                    "pkg",
                    $"\"{Path.Combine(workDir, "bundle.js")}\" --targets node18-win-x64 --output \"{Path.Combine(outDir, name + ".exe")}\"",
                    workDir,
                    "pkg 打包"));
                break;

            case "cpp" when tool.Id == "gcc":
                steps.Add(new Step(
                    "g++",
                    $"-O2 -std=c++20 -static -o \"{Path.Combine(outDir, name + ".exe")}\" \"{src}\"",
                    workDir,
                    "g++ 编译"));
                break;

            case "cpp" when tool.Id == "clang":
                steps.Add(new Step(
                    "clang++",
                    $"-O2 -std=c++20 -static -o \"{Path.Combine(outDir, name + ".exe")}\" \"{src}\"",
                    workDir,
                    "clang++ 编译"));
                break;

            case "go" when tool.Id == "go":
                steps.Add(new Step(
                    "go",
                    $"build -trimpath -ldflags=\"-s -w\" -o \"{Path.Combine(outDir, name + ".exe")}\" \"{src}\"",
                    workDir,
                    "go build"));
                break;

            case "java" when tool.Id == "jpackage":
                {
                    var classesDir = Path.Combine(workDir, "classes");
                    Directory.CreateDirectory(classesDir);

                    var mainClass = GuessJavaMainClass(src, name);

                    steps.Add(new Step(
                        "javac",
                        $"-encoding UTF-8 -d \"{classesDir}\" \"{src}\"",
                        workDir,
                        "javac 编译"));

                    steps.Add(new Step(
                        "jar",
                        $"--create --file \"{Path.Combine(workDir, "app.jar")}\" --main-class {mainClass} -C \"{classesDir}\" .",
                        workDir,
                        "jar 打包"));

                    steps.Add(new Step(
                        "jpackage",
                        $"--type app-image --input \"{workDir}\" --main-jar app.jar --name {name} --dest \"{outDir}\" --win-console",
                        workDir,
                        "jpackage 生成应用"));
                    break;
                }
        }

        return steps;
    }

    /// <summary>从 Java 源文件中猜测主类名，找不到时退回文件名。</summary>
    private static string GuessJavaMainClass(string sourcePath, string fallback)
    {
        try
        {
            var text = File.ReadAllText(sourcePath);
            var m = System.Text.RegularExpressions.Regex.Match(
                text, @"(?:public\s+)?class\s+([A-Za-z_]\w*)[^{]*\{[^}]*static\s+void\s+main",
                System.Text.RegularExpressions.RegexOptions.Singleline);

            if (m.Success) return m.Groups[1].Value;

            var anyClass = System.Text.RegularExpressions.Regex.Match(text, @"(?:public\s+)?class\s+([A-Za-z_]\w*)");
            return anyClass.Success ? anyClass.Groups[1].Value : fallback;
        }
        catch
        {
            return fallback;
        }
    }

    /// <summary>C# 单文件源码无法直接 publish，这里生成一个临时控制台项目承载它。</summary>
    private static string BuildTempCSharpProject(string sourcePath, string name, string workDir, string architecture)
    {
        var projectDir = Path.Combine(workDir, "csapp");
        Directory.CreateDirectory(projectDir);

        var projectPath = Path.Combine(projectDir, name + ".csproj");
        var csPath = Path.Combine(projectDir, "Program.cs");

        File.Copy(sourcePath, csPath, overwrite: true);

        var rid = string.IsNullOrWhiteSpace(architecture) ? "win-x64" : architecture;
        var safeName = System.Security.SecurityElement.Escape(name) ?? "App";

        var csproj = $"""
                      <Project Sdk="Microsoft.NET.Sdk">
                        <PropertyGroup>
                          <OutputType>Exe</OutputType>
                          <TargetFramework>net8.0</TargetFramework>
                          <AssemblyName>{safeName}</AssemblyName>
                          <RootNamespace>{safeName}</RootNamespace>
                          <Nullable>disable</Nullable>
                          <ImplicitUsings>enable</ImplicitUsings>
                          <RuntimeIdentifier>{rid}</RuntimeIdentifier>
                          <SelfContained>true</SelfContained>
                          <PublishSingleFile>true</PublishSingleFile>
                          <InvariantGlobalization>false</InvariantGlobalization>
                        </PropertyGroup>
                      </Project>
                      """;

        File.WriteAllText(projectPath, csproj, new UTF8Encoding(false));
        return projectPath;
    }

    // ─────────────────────────────────────────────────────────
    // 进程执行
    // ─────────────────────────────────────────────────────────

    private static async Task<int> RunAsync(Step step, IProgress<string> log, CancellationToken ct)
    {
        var psi = new ProcessStartInfo
        {
            FileName = step.FileName,
            Arguments = step.Arguments,
            WorkingDirectory = Directory.Exists(step.WorkingDirectory) ? step.WorkingDirectory : Environment.CurrentDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = new Process { StartInfo = psi, EnableRaisingEvents = true };

        process.OutputDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) log.Report("  " + e.Data);
        };

        process.ErrorDataReceived += (_, e) =>
        {
            if (!string.IsNullOrWhiteSpace(e.Data)) log.Report("  ! " + e.Data);
        };

        if (!process.Start())
        {
            log.Report("✗ 无法启动进程：" + step.FileName);
            return -1;
        }

        process.BeginOutputReadLine();
        process.BeginErrorReadLine();

        try
        {
            await process.WaitForExitAsync(ct);
        }
        catch (OperationCanceledException)
        {
            try { process.Kill(entireProcessTree: true); } catch { }
            throw;
        }

        return process.ExitCode;
    }

    private static string FindProducedExe(string outDir)
    {
        try
        {
            return Directory.EnumerateFiles(outDir, "*.exe", SearchOption.AllDirectories)
                .Select(p => new FileInfo(p))
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Select(f => f.FullName)
                .FirstOrDefault() ?? "";
        }
        catch
        {
            return "";
        }
    }
}
