using System.Diagnostics;
using System.IO;
using HWZCodeBridge.Build;
using HWZCodeBridge.Convert;

Console.OutputEncoding = System.Text.Encoding.UTF8;

if (args.Length > 0 && args[0] == "build")
{
    await RunBuildTest();
    return;
}

var pySample = """
                 # 计算斐波那契数列
                 def fib(n):
                     a = 0
                     b = 1
                     total = 0
                     for i in range(n):
                         print(a, end=" ")
                         t = a + b
                         a = b
                         b = t
                         total += a
                     return total

                 def classify(x):
                     if x > 100:
                         return "big"
                     elif x > 10:
                         return "medium"
                     else:
                         return "small"

                 # 主程序
                 if __name__ == "__main__":
                     name = "HWZ"
                     result = fib(8)
                     print(f"result of {name} is {result}")
                     print(classify(result))
                 """;

foreach (var target in new[] { "javascript", "typescript", "csharp", "java", "cpp" })
{
    var r = CodeConverter.Convert(pySample, LanguageCatalog.Get("python"), LanguageCatalog.Get(target));
    Console.WriteLine($"################ Python → {target.ToUpperInvariant()} ################");
    Console.WriteLine(r.Code);
    Console.WriteLine($"-- 状态: {(r.Success ? "OK" : "FAIL")} / 提示 {r.Notes.Count} 条 / {r.ElapsedMs} ms");
    foreach (var n in r.Notes.Take(6)) Console.WriteLine("   " + n);
    Console.WriteLine();
}

Console.WriteLine(new string('=', 70));
Console.WriteLine("反向：C# → Python / JavaScript");
Console.WriteLine(new string('=', 70));

var csSample = """
                // 简单计算器
                using System;

                class Calculator
                {
                    public int Add(int a, int b)
                    {
                        return a + b;
                    }

                    static void Main(string[] args)
                    {
                        Calculator calc = new Calculator();
                        int x = 12;
                        int y = 30;
                        int sum = calc.Add(x, y);
                        Console.WriteLine(sum);
                        if (sum > 40)
                        {
                            Console.WriteLine("big");
                        }
                        else
                        {
                            Console.WriteLine("small");
                        }
                        for (int i = 0; i < 3; i++)
                        {
                            Console.WriteLine(i);
                        }
                    }
                }
                """;

foreach (var target in new[] { "python", "javascript", "java" })
{
    var r = CodeConverter.Convert(csSample, LanguageCatalog.Get("csharp"), LanguageCatalog.Get(target));
    Console.WriteLine($"################ C# → {target.ToUpperInvariant()} ################");
    Console.WriteLine(r.Code);
    Console.WriteLine($"-- 状态: {(r.Success ? "OK" : "FAIL")} / {r.ElapsedMs} ms");
    Console.WriteLine();
}

Console.WriteLine(new string('=', 70));
Console.WriteLine("往返一致性：Python → C# → Python");
Console.WriteLine(new string('=', 70));

var forward = CodeConverter.Convert(pySample, LanguageCatalog.Get("python"), LanguageCatalog.Get("csharp"));
var back = CodeConverter.Convert(forward.Code, LanguageCatalog.Get("csharp"), LanguageCatalog.Get("python"));
Console.WriteLine(back.Code);
Console.WriteLine($"-- 状态: {(back.Success ? "OK" : "FAIL")}");

// ─────────────────────────────────────────────────────────────
// 编译打包流程实测
// ─────────────────────────────────────────────────────────────

static async Task RunBuildTest()
{
    var workDir = Path.Combine(Path.GetTempPath(), "hwz-buildtest-" + Guid.NewGuid().ToString("N")[..6]);
    Directory.CreateDirectory(workDir);

    var sourcePath = Path.Combine(workDir, "HelloApp.cs");
    await File.WriteAllTextAsync(sourcePath, """
        using System;
        using System.Collections.Generic;

        class Program
        {
            static void Main(string[] args)
            {
                Console.WriteLine("Hello from HWZ Code Bridge!");
                var sum = 0;
                for (int i = 1; i <= 10; i++)
                {
                    sum += i;
                }
                Console.WriteLine($"1..10 sum = {sum}");
            }
        }
        """);

    var outputDir = Path.Combine(workDir, "out");

    var request = new BuildRequest
    {
        SourcePath = sourcePath,
        Language = LanguageCatalog.Get("csharp"),
        ToolId = "dotnet",
        OutputDirectory = outputDir,
        OutputName = "HwzHello",
        Architecture = "win-x64"
    };

    Console.WriteLine("=== 开始打包测试（C# → exe） ===");
    var service = new BuildService();
    var progress = new Progress<string>(line => Console.WriteLine("   | " + line));

    var result = await service.BuildAsync(request, progress, CancellationToken.None);

    Console.WriteLine();
    Console.WriteLine($"成功 = {result.Success}");
    Console.WriteLine($"耗时 = {result.ElapsedMs} ms");
    Console.WriteLine($"产物 = {result.OutputPath}");
    Console.WriteLine($"说明 = {result.Message}");

    if (result.Success && File.Exists(result.OutputPath))
    {
        Console.WriteLine();
        Console.WriteLine("=== 运行生成的 exe ===");

        var psi = new ProcessStartInfo(result.OutputPath)
        {
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)!;
        var output = await process.StandardOutput.ReadToEndAsync();
        await process.WaitForExitAsync();

        Console.WriteLine(output);
        Console.WriteLine($"进程退出码 = {process.ExitCode}");
    }
}
