using System.IO;

namespace HWZCodeBridge.Convert;

/// <summary>
/// 受支持语言的静态元数据。转换引擎按 Id 分派规则，此处只保存跨语言通用的事实性信息。
/// </summary>
public sealed class LanguageInfo
{
    public required string Id { get; init; }
    public required string Name { get; init; }
    public required string Extension { get; init; }
    public required string Moniker { get; init; }
    public string Sample { get; init; } = "";

    /// <summary>true = 使用花括号分块（C 系语法）；false = 使用缩进分块。</summary>
    public bool BraceBlocks { get; init; }

    /// <summary>生成目标代码时自动追加的文件头（using / import / #include）。</summary>
    public string Header { get; init; } = "";

    public override string ToString() => Name;
}

public static class LanguageCatalog
{
    public static readonly IReadOnlyList<LanguageInfo> All = new List<LanguageInfo>
    {
        new()
        {
            Id = "python", Name = "Python", Extension = ".py", Moniker = "Python", BraceBlocks = false,
            Sample = """
                     # 计算斐波那契数列
                     def fib(n):
                         a = 0
                         b = 1
                         for i in range(n):
                             print(a)
                             a = b
                             b = a + b
                         return a

                     if __name__ == "__main__":
                         fib(10)
                     """
        },
        new()
        {
            Id = "javascript", Name = "JavaScript", Extension = ".js", Moniker = "JavaScript", BraceBlocks = true,
            Sample = """
                     // 计算斐波那契数列
                     function fib(n) {
                         let a = 0;
                         let b = 1;
                         for (let i = 0; i < n; i++) {
                             console.log(a);
                             let temp = a + b;
                             a = b;
                             b = temp;
                         }
                         return a;
                     }
                     console.log(fib(10));
                     """
        },
        new()
        {
            Id = "typescript", Name = "TypeScript", Extension = ".ts", Moniker = "TypeScript", BraceBlocks = true,
            Sample = """
                     // 计算斐波那契数列
                     function fib(n: number): number {
                         let a: number = 0;
                         let b: number = 1;
                         for (let i = 0; i < n; i++) {
                             console.log(a);
                             let temp: number = a + b;
                             a = b;
                             b = temp;
                         }
                         return a;
                     }
                     console.log(fib(10));
                     """
        },
        new()
        {
            Id = "csharp", Name = "C#", Extension = ".cs", Moniker = "C#", BraceBlocks = true,
            Header = "using System;\nusing System.Collections.Generic;\nusing System.Linq;\n",
            Sample = """
                     // 计算斐波那契数列
                     using System;

                     class Program
                     {
                         static void Main(string[] args)
                         {
                             int a = 0, b = 1;
                             for (int i = 0; i < 10; i++)
                             {
                                 Console.WriteLine(a);
                                 int temp = a + b;
                                 a = b;
                                 b = temp;
                             }
                         }
                     }
                     """
        },
        new()
        {
            Id = "java", Name = "Java", Extension = ".java", Moniker = "Java", BraceBlocks = true,
            Header = "import java.util.*;\n",
            Sample = """
                     // 计算斐波那契数列
                     public class Main
                     {
                         public static void main(String[] args)
                         {
                             int a = 0, b = 1;
                             for (int i = 0; i < 10; i++)
                             {
                                 System.out.println(a);
                                 int temp = a + b;
                                 a = b;
                                 b = temp;
                             }
                         }
                     }
                     """
        },
        new()
        {
            Id = "cpp", Name = "C++", Extension = ".cpp", Moniker = "C++", BraceBlocks = true,
            Header = "#include <iostream>\n#include <string>\n#include <vector>\n#include <cmath>\n\nusing namespace std;\n",
            Sample = """
                     // 计算斐波那契数列
                     #include <iostream>

                     int main()
                     {
                         int a = 0, b = 1;
                         for (int i = 0; i < 10; i++)
                         {
                             std::cout << a << std::endl;
                             int temp = a + b;
                             a = b;
                             b = temp;
                         }
                         return 0;
                     }
                     """
        }
    };

    public static LanguageInfo Get(string id) =>
        All.FirstOrDefault(l => l.Id.Equals(id, StringComparison.OrdinalIgnoreCase)) ?? All[0];

    /// <summary>按扩展名猜测语言，用于拖入文件时自动识别。</summary>
    public static LanguageInfo? GuessFromPath(string path)
    {
        var ext = Path.GetExtension(path).ToLowerInvariant();
        return ext switch
        {
            ".py" or ".pyw" => Get("python"),
            ".js" or ".mjs" or ".cjs" or ".jsx" => Get("javascript"),
            ".ts" or ".tsx" or ".mts" => Get("typescript"),
            ".cs" => Get("csharp"),
            ".java" => Get("java"),
            ".cpp" or ".cc" or ".cxx" or ".hpp" or ".h" => Get("cpp"),
            ".c" => Get("cpp"),
            _ => null
        };
    }

    /// <summary>可作为编译输入的语言（面向生成可执行文件）。</summary>
    public static IReadOnlyList<LanguageInfo> Buildable => All;
}
