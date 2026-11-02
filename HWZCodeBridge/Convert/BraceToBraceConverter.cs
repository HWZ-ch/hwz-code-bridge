using System.Text;
using System.Text.RegularExpressions;

namespace HWZCodeBridge.Convert;

/// <summary>
/// 花括号语言之间的互换（JS / TS / C# / Java / C++）。
/// 这些语言的分块结构一致，工作集中在：声明关键字、打印语句、容器 API、
/// 类型标注、类继承语法与入口函数形态的映射。
/// </summary>
internal sealed class BraceToBraceConverter
{
    private readonly LanguageInfo _source;
    private readonly LanguageInfo _target;
    private readonly NoteBag _notes;

    private string _currentClass = "";

    public BraceToBraceConverter(LanguageInfo source, LanguageInfo target, NoteBag notes)
    {
        _source = source;
        _target = target;
        _notes = notes;
    }

    public string Convert(string source)
    {
        source = source.Replace("\r\n", "\n").Replace('\r', '\n');
        var lines = source.Split('\n');

        var headerLines = new List<string>();
        var bodyLines = new List<string>();

        foreach (var line in lines)
        {
            var t = line.Trim();
            if (t.StartsWith("using ", StringComparison.Ordinal) ||
                t.StartsWith("import ", StringComparison.Ordinal) ||
                t.StartsWith("#include", StringComparison.Ordinal) ||
                t.StartsWith("package ", StringComparison.Ordinal) ||
                t.StartsWith("\"use strict\"", StringComparison.Ordinal))
            {
                headerLines.Add(line);
                continue;
            }
            bodyLines.Add(line);
        }

        var sb = new StringBuilder();
        var header = _target.Header.TrimEnd();
        if (header.Length > 0) sb.AppendLine(header);

        foreach (var l in MapHeader(headerLines))
        {
            sb.AppendLine(l);
        }

        if (header.Length > 0 || headerLines.Count > 0) sb.AppendLine();

        var translated = bodyLines.Select(TranslateLine).ToList();
        var bodyText = string.Join("\n", translated).TrimEnd();
        var needsWrapper = _target.Id is "csharp" or "java" && !Regex.IsMatch(bodyText, @"\bclass\s+\w+");

        if (!needsWrapper)
        {
            sb.AppendLine(bodyText);
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        // 目标语言要求所有代码都在类中：把函数提到类级别，其余语句收进入口方法。
        var (functions, statements) = SplitMembers(translated);

        sb.AppendLine(_target.Id == "csharp" ? "class Program" : "public class Main");
        sb.AppendLine("{");

        foreach (var f in functions)
        {
            foreach (var l in f.Split('\n'))
            {
                sb.AppendLine(l.Trim().Length == 0 ? "" : "    " + l.TrimEnd());
            }
            sb.AppendLine();
        }

        sb.AppendLine(_target.Id == "csharp"
            ? "    static void Main(string[] args)"
            : "    public static void main(String[] args)");
        sb.AppendLine("    {");

        foreach (var l in statements)
        {
            sb.AppendLine(l.Trim().Length == 0 ? "" : "        " + l.TrimEnd());
        }

        sb.AppendLine("    }");
        sb.AppendLine("}");
        _notes.Info(0, "源码没有类定义，已自动补充程序入口类，使目标代码可直接编译运行。");

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    private List<string> MapHeader(List<string> source)
    {
        var result = new List<string>();

        foreach (var line in source)
        {
            var t = line.Trim();

            if (t.StartsWith("using System;", StringComparison.Ordinal) && _target.Id == "cpp")
            {
                continue;
            }

            if (t.StartsWith("#include <iostream>", StringComparison.Ordinal) && _target.Id is "csharp" or "java")
            {
                continue;
            }

            if (t.StartsWith("using ", StringComparison.Ordinal))
            {
                // using 是 C# 特有的命名空间导入，其他目标语言不需要
                if (_target.Id != "csharp") continue;
                result.Add(t);
                continue;
            }

            if (t.StartsWith("import ", StringComparison.Ordinal))
            {
                if (_target.Id is "javascript" or "typescript")
                {
                    // Java 的 import 转成 ES Module 提示
                    var mod = t.Replace("import", "").Trim().TrimEnd(';').Trim();
                    if (mod.EndsWith(".*"))
                    {
                        continue;
                    }
                    result.Add($"// 原 Java 导入：{mod}（Node 需安装对应 npm 包）");
                    continue;
                }

                if (_target.Id is "csharp" or "cpp") continue;
                result.Add(t);
                continue;
            }

            if (t.StartsWith("#include", StringComparison.Ordinal))
            {
                if (_target.Id is "javascript" or "typescript") continue;
                if (_target.Id is "csharp" or "java") continue;
                result.Add(t);
                continue;
            }

            if (t.StartsWith("package ", StringComparison.Ordinal)) continue;
            if (t.StartsWith("\"use strict\"", StringComparison.Ordinal)) continue;

            result.Add(t);
        }

        return result.Distinct().ToList();
    }

    private string TranslateLine(string line)
    {
        var indent = SniffIndent(line);
        var t = line.Trim();

        if (t.Length == 0) return "";

        // 注释
        if (t.StartsWith("//", StringComparison.Ordinal)) return indent + t;

        // 入口函数
        if (IsEntry(t))
        {
            var signature = EntrySignature();

            // 类体内的入口方法，在 JS/TS 中应写成 ES6 方法简写
            if (_currentClass.Length > 0 && (_target.Id is "javascript" or "typescript"))
            {
                signature = "main()";
            }

            return indent + signature + (t.EndsWith('{') ? " {" : "");
        }

        // 类声明
        var classMatch = Regex.Match(t, @"^(?:public\s+)?class\s+([A-Za-z_]\w*)\s*(?:(?:extends|:|implements)\s*([A-Za-z_]\w*))?\s*\{?$");
        if (classMatch.Success)
        {
            var name = classMatch.Groups[1].Value;
            var baseName = classMatch.Groups[2].Success ? classMatch.Groups[2].Value : "";
            _currentClass = name;
            var opens = t.TrimEnd().EndsWith('{');
            return indent + ClassDecl(name, baseName) + (opens ? " {" : "");
        }

        // 构造函数
        var ctorMatch = Regex.Match(t, @"^(?:public\s+|private\s+|protected\s+)?([A-Za-z_]\w*)\s*\(([^)]*)\)\s*\{?$");
        if (ctorMatch.Success && _currentClass.Length > 0 &&
            (ctorMatch.Groups[1].Value == _currentClass || ctorMatch.Groups[1].Value == "constructor"))
        {
            var ps = MapParams(ctorMatch.Groups[2].Value);
            var opensC = t.EndsWith('{');
            var sig = _target.Id switch
            {
                "javascript" => $"constructor({ps})",
                "typescript" => $"constructor({ps})",
                "csharp" => $"public {_currentClass}({ps})",
                "java" => $"public {_currentClass}({ps})",
                "cpp" => $"{_currentClass}({ps})",
                _ => $"{_currentClass}({ps})"
            };
            return indent + sig + (opensC ? " {" : "");
        }

        // 函数声明
        var fnMatch = Regex.Match(t, @"^(?:(?:public|private|protected|static|async|export|final)\s+)*function\s+([A-Za-z_]\w*)\s*\(([^)]*)\)\s*(?::\s*[\w<>\[\]\|]+)?\s*\{?$");
        if (fnMatch.Success)
        {
            var opensF = t.EndsWith('{');
            var fnName = fnMatch.Groups[1].Value;

            if (_currentClass.Length > 0 && (_target.Id is "javascript" or "typescript"))
            {
                return indent + $"{fnName}({MapParams(fnMatch.Groups[2].Value)})" + (opensF ? " {" : "");
            }

            return indent + FunctionSignature(fnName, fnMatch.Groups[2].Value) + (opensF ? " {" : "");
        }

        var typedFn = Regex.Match(t, @"^(?:(?:public|private|protected|static|final)\s+)+[\w<>\[\]]+\s+([A-Za-z_]\w*)\s*\(([^)]*)\)\s*\{?$");
        if (typedFn.Success && typedFn.Groups[1].Value != _currentClass)
        {
            var opensT = t.EndsWith('{');

            if (_currentClass.Length > 0 && (_target.Id is "javascript" or "typescript"))
            {
                return indent + $"{typedFn.Groups[1].Value}({MapParams(typedFn.Groups[2].Value)})" + (opensT ? " {" : "");
            }

            return indent + FunctionSignature(typedFn.Groups[1].Value, typedFn.Groups[2].Value) + (opensT ? " {" : "");
        }

        // 打印
        var print = TryRewritePrint(t);
        if (print is not null) return indent + print;

        // for (int i = 0; ...) → for (let i = 0; ...)
        if ((_target.Id is "javascript" or "typescript") &&
            Regex.IsMatch(t, @"^for\s*\(\s*(?:int|var|let|long|short|double|float)\s+"))
        {
            return indent + Regex.Replace(t, @"^(for\s*\(\s*)(?:int|var|let|long|short|double|float)\s+", "$1let ");
        }

        // C 系类型声明 → JS / TS 的 let 声明
        if (_target.Id is "javascript" or "typescript")
        {
            var typed = Regex.Match(t, @"^(?:[A-Za-z_][\w\.<>\[\]]*)\s+([A-Za-z_]\w*)\s*=(?!=)");
            if (typed.Success)
            {
                return indent + "let " + t[typed.Groups[1].Index..];
            }
        }

        // 声明关键字
        var decl = Regex.Match(t, @"^(let|const|var|auto|final)\s+(.+)$");
        if (decl.Success)
        {
            var rest = TranslateExpr(decl.Groups[2].Value);
            return indent + DeclarePrefix() + " " + rest;
        }

        // class 外部的访问修饰符
        var stmt = TranslateExpr(t);

        if (_target.Id == "cpp" && _currentClass.Length > 0 &&
            Regex.IsMatch(t, @"^(public|private|protected)\s"))
        {
            return indent + t;
        }

        return indent + stmt;
    }

    private static string SniffIndent(string line)
    {
        var n = 0;
        while (n < line.Length && (line[n] == ' ' || line[n] == '\t')) n++;
        return line[..n];
    }

    /// <summary>按花括号层级把函数块与普通语句分开，便于在目标语言中正确摆放入口类。</summary>
    private static (List<string> Functions, List<string> Statements) SplitMembers(List<string> lines)
    {
        var functions = new List<string>();
        var statements = new List<string>();
        var i = 0;

        while (i < lines.Count)
        {
            var t = lines[i].Trim();
            var isFunctionStart = t.EndsWith('{') && Regex.IsMatch(
                t,
                @"^(?:(?:public|private|protected|static|final|inline|extern)\s+)*(?:dynamic|auto|Object|void|int|string|double|bool)\s+\w+\s*\(");

            if (isFunctionStart)
            {
                var block = new List<string>();
                var depth = 0;

                while (i < lines.Count)
                {
                    var l = lines[i];
                    block.Add(l);
                    depth += l.Count(c => c == '{') - l.Count(c => c == '}');
                    i++;
                    if (depth <= 0) break;
                }

                functions.Add(string.Join("\n", block));
                continue;
            }

            statements.Add(lines[i]);
            i++;
        }

        return (functions, statements);
    }

    private static bool IsEntry(string t) =>
        Regex.IsMatch(t, @"^(?:public\s+)?static\s+void\s+(?:Main|main)\s*\(") ||
        Regex.IsMatch(t, @"^int\s+main\s*\(") ||
        Regex.IsMatch(t, @"^(?:func\s+)?main\s*\(\s*\)\s*\{?$");

    private string EntrySignature() => _target.Id switch
    {
        "csharp" => "static void Main(string[] args)",
        "java" => "public static void main(String[] args)",
        "cpp" => "int main()",
        _ => "function main()"
    };

    private string ClassDecl(string name, string baseName)
    {
        var hasBase = baseName.Length > 0;

        return _target.Id switch
        {
            "javascript" or "typescript" => hasBase ? $"class {name} extends {baseName}" : $"class {name}",
            "csharp" => hasBase ? $"class {name} : {baseName}" : $"class {name}",
            "java" => hasBase ? $"public class {name} extends {baseName}" : $"public class {name}",
            "cpp" => hasBase ? $"class {name} : public {baseName}" : $"class {name}",
            _ => $"class {name}"
        };
    }

    private string FunctionSignature(string name, string rawParams)
    {
        var ps = MapParams(rawParams);

        return _target.Id switch
        {
            "javascript" => $"function {name}({ps})",
            "typescript" => $"function {name}({ps}): any",
            "csharp" => $"static dynamic {name}({ps})",
            "java" => $"public static Object {name}({ps})",
            "cpp" => $"auto {name}({ps})",
            _ => $"function {name}({ps})"
        };
    }

    private string MapParams(string rawParams)
    {
        if (string.IsNullOrWhiteSpace(rawParams)) return "";

        var items = SplitTopLevel(rawParams).Select(p => p.Trim()).Where(p => p.Length > 0).ToList();
        var result = new List<string>();

        foreach (var item in items)
        {
            // 拆出变量名与类型
            var cleaned = Regex.Replace(item, @"^(?:(?:const|final|in|out|ref|params)\s+)*", "");
            var parts = cleaned.Split(' ', StringSplitOptions.RemoveEmptyEntries);

            string type = "", name = "";
            if (parts.Length >= 2)
            {
                type = string.Join(" ", parts[..^1]);
                name = parts[^1];
            }
            else
            {
                name = parts.Length > 0 ? parts[0] : "";
            }

            var hasTsType = name.Contains(':');
            if (hasTsType)
            {
                var seg = name.Split(':');
                name = seg[0].Trim();
                type = seg[1].Trim();
            }

            if (name.Length == 0) continue;

            result.Add(_target.Id switch
            {
                "javascript" => name,
                "typescript" => $"{name}: {MapTsType(type)}",
                "csharp" => $"dynamic {name}",
                "java" => $"Object {name}",
                "cpp" => $"auto {name}",
                _ => name
            });
        }

        return string.Join(", ", result);
    }

    private static string MapTsType(string type)
    {
        if (string.IsNullOrWhiteSpace(type)) return "any";

        return type.Trim() switch
        {
            "number" => "number",
            "string" => "string",
            "boolean" => "boolean",
            "any" or "void" or "object" => "any",
            "int" or "double" or "float" or "long" or "decimal" => "number",
            "Object" => "any",
            "var" or "auto" or "dynamic" => "any",
            _ => "any"
        };
    }

    private string DeclarePrefix() => _target.Id switch
    {
        "javascript" => "let",
        "typescript" => "let",
        "csharp" => "var",
        "java" => "var",
        "cpp" => "auto",
        _ => "let"
    };

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '(' or '[' or '{' or '<') depth++;
            else if (c is ')' or ']' or '}' or '>') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private string? TryRewritePrint(string t)
    {
        var pattern = _source.Id switch
        {
            "javascript" or "typescript" => @"^console\.(?:log|info)\s*\((.+)\)\s*;?$",
            "csharp" => @"^Console\.(?:WriteLine|Write)\s*\((.+)\)\s*;?$",
            "java" => @"^System\.out\.println\s*\((.+)\)\s*;?$",
            "cpp" => @"^(?:std::)?cout\s*<<\s*(.+?)\s*;?$",
            _ => @"^console\.log\s*\((.+)\)\s*;?$"
        };

        var m = Regex.Match(t, pattern, RegexOptions.Singleline);
        if (!m.Success) return null;

        var raw = m.Groups[1].Value;

        var args = _source.Id == "cpp"
            ? raw.Split("<<")
                .Select(x => x.Trim())
                .Where(x => x.Length > 0 && x is not ("std::endl" or "endl" or "'\\n'" or "\"\\n\""))
                .Select(TranslateExpr)
                .ToList()
            : SplitTopLevel(raw).Select(x => TranslateExpr(x.Trim())).ToList();

        if (args.Count == 0) return PrintCall(new List<string>());

        return PrintCall(args);
    }

    private string PrintCall(List<string> args)
    {
        var joined = string.Join(", ", args);

        if (args.Count <= 1)
        {
            return _target.Id switch
            {
                "csharp" => $"Console.WriteLine({joined});",
                "java" => $"System.out.println({joined});",
                "cpp" => $"std::cout << {joined} << std::endl;",
                _ => $"console.log({joined});"
            };
        }

        return _target.Id switch
        {
            "csharp" => $"Console.WriteLine(string.Join(\" \", new object[] {{ {joined} }}));",
            "java" => $"System.out.println({string.Join(" + \" \" + ", args)});",
            "cpp" => $"std::cout << {string.Join(" << \" \" << ", args)} << std::endl;",
            _ => $"console.log({joined});"
        };
    }

    private string TranslateExpr(string expr)
    {
        if (string.IsNullOrWhiteSpace(expr)) return expr;

        var s = expr;

        var literals = new List<string>();
        s = Regex.Replace(s, @"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`", m =>
        {
            literals.Add(m.Value);
            return $"\u0001{literals.Count - 1}\u0001";
        });

        if (_target.Id == "cpp")
        {
            s = Regex.Replace(s, @"\bnull\b", "nullptr");
        }
        else
        {
            s = Regex.Replace(s, @"\bnullptr\b", "null");
        }

        // 长度
        s = Regex.Replace(s, @"([A-Za-z_]\w*(?:\.\w+)*)\.(?:Length|length\(\)|size\(\))", m =>
        {
            var v = m.Groups[1].Value;
            return _target.Id switch
            {
                "javascript" or "typescript" => $"{v}.length",
                "csharp" => $"{v}.Length",
                "java" => $"{v}.length()",
                "cpp" => $"{v}.size()",
                _ => $"{v}.length"
            };
        });

        // 容器追加（只处理语义明确的 push 系列；Add/add 可能是自定义方法，不做自动替换）
        s = Regex.Replace(s, @"\.(?:push|push_back)\(", m => _target.Id switch
        {
            "javascript" or "typescript" => ".push(",
            "csharp" => ".Add(",
            "java" => ".add(",
            "cpp" => ".push_back(",
            _ => ".push("
        });

        // 字符串转换
        s = Regex.Replace(s, @"String\.valueOf\(([^()]+)\)", m => ToStr(m.Groups[1].Value));
        s = Regex.Replace(s, @"([A-Za-z_][\w\.\[\]]*)\.ToString\(\)", m => ToStr(m.Groups[1].Value));
        s = Regex.Replace(s, @"String\(([^()]+)\)", m => ToStr(m.Groups[1].Value));
        s = Regex.Replace(s, @"std::to_string\(([^()]+)\)", m => ToStr(m.Groups[1].Value));

        // 数学
        s = Regex.Replace(s, @"Math\.(?:floor|Floor)\(([^()]+)\)", m => MathCall("floor", m.Groups[1].Value));
        s = Regex.Replace(s, @"std::floor\(([^()]+)\)", m => MathCall("floor", m.Groups[1].Value));
        s = Regex.Replace(s, @"Math\.(?:abs|Abs)\(([^()]+)\)", m => MathCall("abs", m.Groups[1].Value));
        s = Regex.Replace(s, @"std::abs\(([^()]+)\)", m => MathCall("abs", m.Groups[1].Value));
        s = Regex.Replace(s, @"Math\.(?:Pow|pow)\(([^,]+),([^)]+)\)", m => MathCall("pow", $"{m.Groups[1].Value},{m.Groups[2].Value}"));
        s = Regex.Replace(s, @"std::pow\(([^,]+),([^)]+)\)", m => MathCall("pow", $"{m.Groups[1].Value},{m.Groups[2].Value}"));

        // 数组字面量（C# / Java / C++ 需要容器构造）
        s = ConvertArrayLiteral(s);

        for (var i = 0; i < literals.Count; i++)
        {
            s = s.Replace($"\u0001{i}\u0001", RebuildLiteral(literals[i]));
        }

        return s;
    }

    private string RebuildLiteral(string literal)
    {
        // 模板字符串 ↔ 插值字符串
        if (literal.StartsWith('`'))
        {
            var body = literal[1..^1];
            if (_target.Id == "csharp")
            {
                body = Regex.Replace(body, @"\$\{([^{}]+)\}", "{$1}");
                body = body.Replace("\"", "\\\"");
                return "$\"" + body + "\"";
            }
            if (_target.Id is "java" or "cpp")
            {
                var segs = new List<string>();
                var last = 0;
                foreach (Match g in Regex.Matches(body, @"\$\{([^{}]+)\}"))
                {
                    var lit = body[last..g.Index];
                    if (lit.Length > 0) segs.Add("\"" + lit + "\"");
                    segs.Add(g.Groups[1].Value);
                    last = g.Index + g.Length;
                }
                var tail = body[last..];
                if (tail.Length > 0) segs.Add("\"" + tail + "\"");
                return segs.Count == 0 ? "\"\"" : "(" + string.Join(" + ", segs) + ")";
            }
            return literal;
        }

        // C# 插值字符串 → 目标
        if (literal.StartsWith("$\"", StringComparison.Ordinal))
        {
            var body = literal[2..^1];
            if (_target.Id is "javascript" or "typescript")
            {
                return "`" + Regex.Replace(body, @"\{([^{}]+)\}", "${$1}") + "`";
            }
            if (_target.Id is "java" or "cpp")
            {
                var segs = new List<string>();
                var last = 0;
                foreach (Match g in Regex.Matches(body, @"\{([^{}]+)\}"))
                {
                    var lit = body[last..g.Index];
                    if (lit.Length > 0) segs.Add("\"" + lit + "\"");
                    segs.Add(g.Groups[1].Value);
                    last = g.Index + g.Length;
                }
                var tail = body[last..];
                if (tail.Length > 0) segs.Add("\"" + tail + "\"");
                return segs.Count == 0 ? "\"\"" : "(" + string.Join(" + ", segs) + ")";
            }
        }

        return literal;
    }

    private string ToStr(string value) => _target.Id switch
    {
        "javascript" or "typescript" => $"String({value})",
        "csharp" => $"{value}.ToString()",
        "java" => $"String.valueOf({value})",
        "cpp" => $"std::to_string({value})",
        _ => $"String({value})"
    };

    private string MathCall(string fn, string args) => _target.Id switch
    {
        "javascript" or "typescript" => $"Math.{fn}({args})",
        "csharp" => $"Math.{char.ToUpperInvariant(fn[0])}{fn[1..]}({args})",
        "java" => $"Math.{fn}({args})",
        "cpp" => $"std::{(fn == "floor" ? "floor" : fn == "abs" ? "abs" : "pow")}({args})",
        _ => $"Math.{fn}({args})"
    };

    private string ConvertArrayLiteral(string s)
    {
        if (_target.Id is "javascript" or "typescript") return s;

        var m = Regex.Match(s, @"^\[(.*)\]$", RegexOptions.Singleline);
        if (!m.Success) return s;

        var items = SplitTopLevel(m.Groups[1].Value).Select(x => x.Trim()).ToList();

        return _target.Id switch
        {
            "csharp" => $"new List<dynamic> {{ {string.Join(", ", items)} }}",
            "java" => items.Count == 0 ? "new ArrayList<>()" : $"new ArrayList<>(Arrays.asList({string.Join(", ", items)}))",
            "cpp" => $"std::vector<int>{{ {string.Join(", ", items)} }}",
            _ => s
        };
    }
}
