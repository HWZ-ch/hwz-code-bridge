using System.Text;
using System.Text.RegularExpressions;

namespace HWZCodeBridge.Convert;

/// <summary>
/// Python → 花括号语言（JS / TS / C# / Java / C++）。
/// 核心工作有两块：① 缩进块 → 花括号块的结构重写；② 表达式级语法映射。
/// 设计原则：宁可生成"可读、语法合法"的代码并给出告警，也不假装语义完全等价。
/// </summary>
internal sealed class PythonToBraceConverter
{
    private readonly LanguageInfo _t;
    private readonly NoteBag _notes;
    private readonly Stack<HashSet<string>> _scopes = new();

    private string _currentClass = "";

    public PythonToBraceConverter(LanguageInfo target, NoteBag notes)
    {
        _t = target;
        _notes = notes;
        _scopes.Push(new HashSet<string>(StringComparer.Ordinal));
    }

    private const int IndentUnit = 4;
    private static string Pad(int level) => new(' ', level * IndentUnit);

    public string Convert(List<PyNode> nodes)
    {
        var sb = new StringBuilder();

        var imports = nodes.Where(n => n.Kind == PyKind.Import).ToList();
        var body = nodes.Where(n => n.Kind != PyKind.Import).ToList();

        var header = BuildHeader(imports);
        if (!string.IsNullOrWhiteSpace(header))
        {
            sb.AppendLine(header.TrimEnd());
            sb.AppendLine();
        }

        var declarations = body.Where(n => n.Kind is PyKind.ClassDef or PyKind.FuncDef).ToList();
        var executables = body.Where(n => n.Kind is not (PyKind.ClassDef or PyKind.FuncDef or PyKind.Blank)).ToList();

        var needsMain = _t.Id is "csharp" or "java" or "cpp";
        var hasExec = executables.Any(n => n.Kind is not (PyKind.Comment or PyKind.Decorator or PyKind.Import or PyKind.Blank));

        if (!needsMain)
        {
            // JS / TS：顶层语句直接执行，`if __name__ == "__main__":` 会被摊平为顶层代码
            RenderExecutables(body, 0, sb);
            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        // ── 需要容器入口的语言 ──────────────────────────────
        var topClasses = declarations.Where(n => n.Kind == PyKind.ClassDef).ToList();
        var topFuncs = declarations.Where(n => n.Kind == PyKind.FuncDef).ToList();

        foreach (var cls in topClasses)
        {
            RenderClass(cls, 0, sb);
            sb.AppendLine();
        }

        var container = _t.Id switch
        {
            "csharp" => "Program",
            "java" => "Main",
            _ => ""
        };

        if (_t.Id == "cpp")
        {
            foreach (var f in topFuncs)
            {
                RenderFunction(f, 0, sb, isStatic: false);
                sb.AppendLine();
            }

            if (hasExec)
            {
                sb.AppendLine("int main() {");
                RenderExecutables(executables, 1, sb);
                sb.AppendLine(Pad(1) + "return 0;");
                sb.AppendLine("}");
            }

            return sb.ToString().TrimEnd() + Environment.NewLine;
        }

        // C# / Java 容器类
        sb.AppendLine($"{(_t.Id == "java" ? "public " : "")}class {container} {{");

        foreach (var f in topFuncs)
        {
            RenderFunction(f, 1, sb, isStatic: true);
            sb.AppendLine();
        }

        if (hasExec)
        {
            if (_t.Id == "csharp")
            {
                sb.AppendLine(Pad(1) + "static void Main(string[] args)");
            }
            else
            {
                sb.AppendLine(Pad(1) + "public static void main(String[] args)");
            }

            sb.AppendLine(Pad(1) + "{");
            RenderExecutables(executables, 2, sb);
            sb.AppendLine(Pad(1) + "}");
        }

        sb.AppendLine("}");

        return sb.ToString().TrimEnd() + Environment.NewLine;
    }

    // ─────────────────────────────────────────────────────────
    // 头部与导入
    // ─────────────────────────────────────────────────────────

    private string BuildHeader(List<PyNode> imports)
    {
        var lines = new List<string>();

        foreach (var imp in imports)
        {
            var text = imp.Text.Trim();
            var module = text.StartsWith("import ")
                ? text[7..].Split(',')[0].Trim().Split('.')[0]
                : text[5..].Split(' ')[0].Split('.')[0];

            switch (_t.Id)
            {
                case "javascript":
                case "typescript":
                    if (module is "os" or "sys" or "json" or "re" or "time" or "datetime")
                    {
                        lines.Add($"const {module} = require('{module}');");
                        _notes.Info(imp.Line, $"`{module}` 属于 Python 标准库，Node 对应模块需先 npm 安装并核对 API。");
                    }
                    else if (module is "math")
                    {
                        lines.Add("// Python math 模块：JavaScript 使用内置 Math 对象");
                    }
                    else
                    {
                        _notes.Warn(imp.Line, $"第三方模块 `{module}` 需替换为对应的 npm 包。");
                    }
                    break;

                case "csharp":
                    if (module is "math") lines.Add("// Python math 模块：C# 使用 System.Math");
                    else if (module is "os" or "sys") lines.Add("// Python " + module + "：C# 使用 System.IO / Environment");
                    else _notes.Warn(imp.Line, $"模块 `{module}` 在 C# 中没有内置对应项，需要引入对应 NuGet 包。");
                    break;

                case "java":
                    if (module is "math") lines.Add("// Python math 模块：Java 使用 java.lang.Math");
                    else if (module is "os" or "sys") lines.Add("// Python " + module + "：Java 使用 java.lang.System");
                    else _notes.Warn(imp.Line, $"模块 `{module}` 在 Java 中没有内置对应项。");
                    break;

                case "cpp":
                    if (module is "math") lines.Add("#include <cmath>");
                    else if (module is "iostream") lines.Add("#include <iostream>");
                    else _notes.Warn(imp.Line, $"模块 `{module}` 在 C++ 中需要对应的头文件或第三方库。");
                    break;
            }
        }

        var header = new StringBuilder();
        var std = _t.Header.TrimEnd();

        if (!string.IsNullOrEmpty(std))
        {
            header.AppendLine(std);
        }

        foreach (var l in lines.Distinct())
        {
            header.AppendLine(l);
        }

        return header.ToString();
    }

    // ─────────────────────────────────────────────────────────
    // 节点渲染
    // ─────────────────────────────────────────────────────────

    private void RenderExecutables(List<PyNode> nodes, int level, StringBuilder sb)
    {
        // `if __name__ == "__main__":` 视为入口标记，其内容直接平铺进 main。
        var flattened = new List<PyNode>();
        foreach (var n in nodes)
        {
            if (n.Kind == PyKind.If && n.Head.Replace(" ", "").Contains("__name__"))
            {
                flattened.AddRange(n.Children);
            }
            else
            {
                flattened.Add(n);
            }
        }

        RenderNodes(flattened, level, sb);
    }

    private void RenderNodes(List<PyNode> nodes, int level, StringBuilder sb)
    {
        var i = 0;
        var lastBlank = false;

        while (i < nodes.Count)
        {
            var n = nodes[i];

            if (n.Kind == PyKind.Blank)
            {
                if (!lastBlank && sb.Length > 0) sb.AppendLine();
                lastBlank = true;
                i++;
                continue;
            }

            lastBlank = false;

            switch (n.Kind)
            {
                case PyKind.Comment:
                    sb.AppendLine(Pad(level) + "// " + n.Text);
                    i++;
                    break;

                case PyKind.Import:
                    i++;
                    break;

                case PyKind.Decorator:
                    sb.AppendLine(Pad(level) + "// 装饰器 @" + n.Text.TrimStart('@'));
                    _notes.Info(n.Line, "装饰器无法自动移植，已保留为注释。");
                    i++;
                    break;

                case PyKind.FuncDef:
                    RenderFunction(n, level, sb, isStatic: level > 0);
                    i++;
                    break;

                case PyKind.ClassDef:
                    RenderClass(n, level, sb);
                    i++;
                    break;

                case PyKind.If:
                    i = RenderIfChain(nodes, i, level, sb);
                    break;

                case PyKind.For:
                    RenderFor(n, level, sb);
                    i = SkipContinuations(nodes, i + 1);
                    break;

                case PyKind.While:
                    sb.AppendLine(Pad(level) + $"while ({Expr(n.Head, n.Line)}) {{");
                    RenderNodes(n.Children, level + 1, sb);
                    sb.AppendLine(Pad(level) + "}");
                    i++;
                    break;

                case PyKind.Try:
                    i = RenderTryChain(nodes, i, level, sb);
                    break;

                case PyKind.With:
                    sb.AppendLine(Pad(level) + "{");
                    sb.AppendLine(Pad(level + 1) + "// 原 Python with 语句：资源需在此处手动创建与释放");
                    sb.AppendLine(Pad(level + 1) + "// " + Expr(n.Head, n.Line));
                    RenderNodes(n.Children, level + 1, sb);
                    sb.AppendLine(Pad(level) + "}");
                    _notes.Warn(n.Line, "with 上下文管理器没有跨语言等价形式，已转换为代码块并保留注释。");
                    i++;
                    break;

                case PyKind.Return:
                    var ret = n.Text.Length > 6 ? n.Text[6..].Trim() : "";
                    sb.AppendLine(Pad(level) + (ret.Length == 0 ? "return;" : $"return {Expr(ret, n.Line)};"));
                    i++;
                    break;

                case PyKind.Break:
                    sb.AppendLine(Pad(level) + "break;");
                    i++;
                    break;

                case PyKind.Continue:
                    sb.AppendLine(Pad(level) + "continue;");
                    i++;
                    break;

                case PyKind.Pass:
                    sb.AppendLine(Pad(level) + "{ }");
                    i++;
                    break;

                default:
                    RenderSimple(n, level, sb);
                    i++;
                    break;
            }
        }
    }

    private static int SkipContinuations(List<PyNode> nodes, int index)
    {
        while (index < nodes.Count && nodes[index].IsContinuation) index++;
        return index;
    }

    private int RenderIfChain(List<PyNode> nodes, int index, int level, StringBuilder sb)
    {
        var first = nodes[index];
        sb.AppendLine(Pad(level) + $"if ({Expr(first.Head, first.Line)}) {{");
        RenderNodes(first.Children, level + 1, sb);

        var i = index + 1;
        while (i < nodes.Count && nodes[i].IsContinuation)
        {
            var c = nodes[i];
            if (c.Kind == PyKind.Elif)
            {
                sb.AppendLine(Pad(level) + $"}} else if ({Expr(c.Head, c.Line)}) {{");
            }
            else if (c.Kind == PyKind.Else)
            {
                sb.AppendLine(Pad(level) + "} else {");
            }
            else break;

            RenderNodes(c.Children, level + 1, sb);
            i++;
        }

        sb.AppendLine(Pad(level) + "}");
        return i;
    }

    private int RenderTryChain(List<PyNode> nodes, int index, int level, StringBuilder sb)
    {
        var first = nodes[index];
        sb.AppendLine(Pad(level) + "try {");
        RenderNodes(first.Children, level + 1, sb);

        var i = index + 1;
        var hasCatch = false;

        while (i < nodes.Count && nodes[i].Kind is PyKind.Except or PyKind.Finally)
        {
            var c = nodes[i];

            if (c.Kind == PyKind.Except)
            {
                hasCatch = true;
                var (type, name) = ParseExcept(c.Head);
                sb.AppendLine(Pad(level) + $"}} {CatchClause(type, name)} {{");
                if (!string.IsNullOrEmpty(type))
                {
                    _notes.Warn(c.Line, $"异常类型 `{type}` 已映射为通用异常，请按目标语言调整捕获类型。");
                }
            }
            else
            {
                sb.AppendLine(Pad(level) + "} finally {");
            }

            RenderNodes(c.Children, level + 1, sb);
            i++;
        }

        if (!hasCatch && _t.Id is "csharp" or "java")
        {
            sb.AppendLine(Pad(level) + "} finally {");
            sb.AppendLine(Pad(level) + "}");
        }
        else
        {
            sb.AppendLine(Pad(level) + "}");
        }

        return i;
    }

    private (string Type, string Name) ParseExcept(string head)
    {
        if (string.IsNullOrWhiteSpace(head)) return ("", DefaultCatchVar());

        var parts = head.Split(" as ", StringSplitOptions.TrimEntries);
        var type = parts[0].Trim();
        var name = parts.Length > 1 ? parts[1].Trim() : DefaultCatchVar();

        // 去掉括号包裹：except (A, B) as e
        type = type.Trim('(', ')');
        if (type.Contains(',')) type = type.Split(',')[0].Trim();

        return (type, name);
    }

    private string DefaultCatchVar() => _t.Id switch
    {
        "javascript" or "typescript" => "e",
        "cpp" => "e",
        _ => "ex"
    };

    private string CatchClause(string type, string name) => _t.Id switch
    {
        "javascript" or "typescript" => $"catch ({name})",
        "csharp" => $"catch (Exception {name})",
        "java" => $"catch (Exception {name})",
        "cpp" => $"catch (const std::exception& {name})",
        _ => $"catch ({name})"
    };

    private void RenderFunction(PyNode node, int level, StringBuilder sb, bool isStatic)
    {
        var head = node.Head; // def name(args)
        var m = Regex.Match(head, @"^def\s+([A-Za-z_]\w*)\s*\((.*)\)\s*$", RegexOptions.Singleline);
        if (!m.Success)
        {
            sb.AppendLine(Pad(level) + "// 无法解析的函数定义：" + head);
            _notes.Warn(node.Line, "函数定义格式无法识别，已保留为注释。");
            return;
        }

        var name = m.Groups[1].Value;
        var rawParams = m.Groups[2].Value;
        var isConstructor = name == "__init__";

        var paramList = ParseParams(rawParams, out _);

        _scopes.Push(new HashSet<string>(StringComparer.Ordinal));

        if (isConstructor)
        {
            var ctorName = string.IsNullOrEmpty(_currentClass) ? "Constructor" : _currentClass;
            var signature = _t.Id switch
            {
                "javascript" => $"constructor({string.Join(", ", paramList)})",
                "typescript" => $"constructor({string.Join(", ", paramList.Select(p => p + ": any"))})",
                "csharp" => $"public {ctorName}({string.Join(", ", paramList.Select(p => "dynamic " + p))})",
                "java" => $"public {ctorName}({string.Join(", ", paramList.Select(p => "Object " + p))})",
                "cpp" => $"public: {ctorName}({string.Join(", ", paramList.Select(p => "auto " + p))})",
                _ => $"{ctorName}({string.Join(", ", paramList)})"
            };
            sb.AppendLine(Pad(level) + signature + " {");
        }
        else
        {
            var signature = _t.Id switch
            {
                "javascript" => $"function {name}({string.Join(", ", paramList)})",
                "typescript" => $"function {name}({string.Join(", ", paramList.Select(p => p + ": any"))}): any",
                "csharp" => $"{(isStatic ? "static " : "")}dynamic {name}({string.Join(", ", paramList.Select(p => "dynamic " + p))})",
                "java" => $"{(isStatic ? "static " : "public static ")}Object {name}({string.Join(", ", paramList.Select(p => "Object " + p))})",
                "cpp" => $"auto {name}({string.Join(", ", paramList.Select(p => "auto " + p))})",
                _ => $"{name}({string.Join(", ", paramList)})"
            };
            sb.AppendLine(Pad(level) + signature + " {");
        }

        var body = new StringBuilder();
        RenderNodes(node.Children, level + 1, body);

        var bodyText = body.ToString().TrimEnd();
        sb.AppendLine(bodyText.Length > 0 ? bodyText : Pad(level + 1) + "// 空函数体");
        sb.AppendLine(Pad(level) + "}");

        _scopes.Pop();

        if (_t.Id is "csharp" or "java")
        {
            _notes.Info(node.Line, $"函数 `{name}` 的参数与返回值使用了动态类型占位，建议改为具体类型。");
        }
    }

    private List<string> ParseParams(string rawParams, out bool hasSelf)
    {
        hasSelf = false;
        var result = new List<string>();
        if (string.IsNullOrWhiteSpace(rawParams)) return result;

        foreach (var part in SplitTopLevel(rawParams))
        {
            var p = part.Trim();
            if (p.Length == 0) continue;

            if (p is "self" or "cls")
            {
                hasSelf = true;
                continue;
            }

            // 保留默认值：a=1 → a = 1
            var eq = p.IndexOf('=');
            if (eq > 0)
            {
                var pname = p[..eq].Trim();
                var def = Expr(p[eq..].TrimStart('='), 0);
                result.Add($"{pname} = {def}");
            }
            else if (p.StartsWith('*'))
            {
                p = p.TrimStart('*');
                result.Add(p);
                _notes.Info(0, "可变参数已按普通参数处理，调用处需要调整。");
            }
            else
            {
                result.Add(p);
            }
        }

        return result;
    }

    private static List<string> SplitTopLevel(string text)
    {
        var parts = new List<string>();
        var depth = 0;
        var start = 0;

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == ',' && depth == 0)
            {
                parts.Add(text[start..i]);
                start = i + 1;
            }
        }

        parts.Add(text[start..]);
        return parts;
    }

    private void RenderClass(PyNode node, int level, StringBuilder sb)
    {
        var m = Regex.Match(node.Head, @"^class\s+([A-Za-z_]\w*)\s*(?:\((.*)\))?\s*$");
        if (!m.Success)
        {
            sb.AppendLine(Pad(level) + "// 无法解析的类定义：" + node.Head);
            return;
        }

        var name = m.Groups[1].Value;
        var bases = m.Groups[2].Success && m.Groups[2].Value.Trim().Length > 0
            ? m.Groups[2].Value.Split(',').Select(x => x.Trim()).Where(x => x is not ("object" or "")).ToList()
            : new List<string>();

        var decl = _t.Id switch
        {
            "javascript" => $"class {name}" + (bases.Count > 0 ? $" extends {bases[0]}" : ""),
            "typescript" => $"class {name}" + (bases.Count > 0 ? $" extends {bases[0]}" : ""),
            "csharp" => $"class {name}" + (bases.Count > 0 ? $" : {bases[0]}" : ""),
            "java" => $"class {name}" + (bases.Count > 0 ? $" extends {bases[0]}" : ""),
            "cpp" => $"class {name}" + (bases.Count > 0 ? $" : public {bases[0]}" : ""),
            _ => $"class {name}"
        };

        sb.AppendLine(Pad(level) + decl + " {");

        var previous = _currentClass;
        _currentClass = name;

        var members = new StringBuilder();

        // C# / Java 需要在类体内声明 Python 里由 self.x 隐式创建的字段。
        if (_t.Id is "csharp" or "java")
        {
            var fields = CollectSelfFields(node.Children);
            if (fields.Count > 0)
            {
                foreach (var f in fields)
                {
                    members.AppendLine(Pad(level + 1) + (_t.Id == "csharp"
                        ? $"public dynamic {f};"
                        : $"public Object {f};"));
                }
                members.AppendLine();
                _notes.Info(node.Line, $"类 `{name}` 的实例字段已按动态类型自动补全声明，建议改为具体类型。");
            }
        }

        RenderNodes(node.Children, level + 1, members);

        var membersText = members.ToString().TrimEnd();
        if (membersText.Length > 0) sb.AppendLine(membersText);

        sb.AppendLine(Pad(level) + "}");

        _currentClass = previous;
    }

    private static List<string> CollectSelfFields(List<PyNode> nodes)
    {
        var found = new List<string>();

        void Walk(List<PyNode> list)
        {
            foreach (var n in list)
            {
                if (n.Kind == PyKind.Simple)
                {
                    var m = Regex.Match(n.Text, @"^self\.([A-Za-z_]\w*)\s*=(?!=)");
                    if (m.Success && !found.Contains(m.Groups[1].Value))
                    {
                        found.Add(m.Groups[1].Value);
                    }
                }
                Walk(n.Children);
            }
        }

        Walk(nodes);
        return found;
    }

    // ─────────────────────────────────────────────────────────
    // for 循环
    // ─────────────────────────────────────────────────────────

    private void RenderFor(PyNode node, int level, StringBuilder sb)
    {
        var head = node.Head; // for x in y
        var rest = head.Length > 3 ? head[3..].Trim() : "";
        var inIdx = FindTopLevelIn(rest);

        if (inIdx < 0)
        {
            sb.AppendLine(Pad(level) + "// 无法解析的 for 语句：" + head);
            _notes.Warn(node.Line, "for 语句格式无法识别，已保留为注释。");
            return;
        }

        var target = rest[..inIdx].Trim().Trim('(', ')');
        var iter = rest[(inIdx + 4)..].Trim();

        var rangeMatch = Regex.Match(iter, @"^range\((.*)\)$", RegexOptions.Singleline);
        if (rangeMatch.Success)
        {
            var args = SplitTopLevel(rangeMatch.Groups[1].Value).Select(a => Expr(a.Trim(), node.Line)).ToList();

            string start = "0", end = "0", step = "1";
            if (args.Count == 1) { end = args[0]; }
            else if (args.Count == 2) { start = args[0]; end = args[1]; }
            else if (args.Count >= 3) { start = args[0]; end = args[1]; step = args[2]; }

            var cmp = step.StartsWith('-') ? ">" : "<";
            var increment = step == "1" ? $"{target}++" : $"{target} += {step}";

            var forLine = _t.Id switch
            {
                "javascript" or "typescript" => $"for (let {target} = {start}; {target} {cmp} {end}; {increment})",
                "csharp" => $"for (int {target} = {start}; {target} {cmp} {end}; {increment})",
                "java" => $"for (int {target} = {start}; {target} {cmp} {end}; {increment})",
                "cpp" => $"for (int {target} = {start}; {target} {cmp} {end}; {increment.Replace("++", "++")})",
                _ => $"for (int {target} = {start}; {target} {cmp} {end}; {increment})"
            };

            sb.AppendLine(Pad(level) + forLine + " {");
            RenderNodes(node.Children, level + 1, sb);
            sb.AppendLine(Pad(level) + "}");
            return;
        }

        if (iter.Contains(',') && !iter.Contains('('))
        {
            target = target.Replace(",", ", ");
            _notes.Warn(node.Line, "多重赋值循环已简化，请核对循环变量。");
        }

        var iterExpr = Expr(iter, node.Line);

        var each = _t.Id switch
        {
            "javascript" or "typescript" => $"for (const {target} of {iterExpr})",
            "csharp" => $"foreach (var {target} in {iterExpr})",
            "java" => $"for (var {target} : {iterExpr})",
            "cpp" => $"for (auto {target} : {iterExpr})",
            _ => $"foreach (var {target} in {iterExpr})"
        };

        sb.AppendLine(Pad(level) + each + " {");
        RenderNodes(node.Children, level + 1, sb);
        sb.AppendLine(Pad(level) + "}");
    }

    private static int FindTopLevelIn(string text)
    {
        var depth = 0;
        for (var i = 0; i < text.Length - 3; i++)
        {
            var c = text[i];
            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (depth == 0 && text.Substring(i, 4) == " in ") return i;
        }
        return -1;
    }

    // ─────────────────────────────────────────────────────────
    // 普通语句
    // ─────────────────────────────────────────────────────────

    private void RenderSimple(PyNode node, int level, StringBuilder sb)
    {
        var text = node.Text.Trim();

        // print(...)
        var printMatch = Regex.Match(text, @"^print\s*\((.*)\)$", RegexOptions.Singleline);
        if (printMatch.Success)
        {
            if (printMatch.Groups[1].Value.Contains("end=") || printMatch.Groups[1].Value.Contains("sep="))
            {
                _notes.Warn(node.Line, "print 的 sep / end 参数在目标语言中没有等价写法，输出分隔与换行行为已简化。");
            }

            var args = SplitTopLevel(printMatch.Groups[1].Value)
                .Select(a => a.Trim())
                .Where(a => a.Length > 0 && !a.StartsWith("sep=") && !a.StartsWith("end="))
                .Select(a => Expr(a, node.Line))
                .ToList();

            sb.AppendLine(Pad(level) + BuildPrint(args, node.Line));
            return;
        }

        // input(...)
        if (text.StartsWith("input(", StringComparison.Ordinal))
        {
            var prompt = Regex.Match(text, @"^input\s*\((.*)\)$", RegexOptions.Singleline);
            var promptText = prompt.Success && prompt.Groups[1].Value.Trim().Length > 0
                ? prompt.Groups[1].Value.Trim()
                : null;

            var read = _t.Id switch
            {
                "csharp" => promptText is not null
                    ? $"Console.Write({Expr(promptText, node.Line)}); var _input = Console.ReadLine();"
                    : "var _input = Console.ReadLine();",
                "java" => promptText is not null
                    ? $"System.out.print({Expr(promptText, node.Line)}); var _scanner = new Scanner(System.in); var _input = _scanner.nextLine();"
                    : "var _scanner = new Scanner(System.in); var _input = _scanner.nextLine();",
                "javascript" => "// Node 环境无内置输入：请引入 readline 模块\nconst _input = null;",
                "typescript" => "// Node 环境无内置输入：请引入 readline 模块\nconst _input: string | null = null;",
                "cpp" => promptText is not null
                    ? $"std::cout << {Expr(promptText, node.Line)}; std::string _input; std::getline(std::cin, _input);"
                    : "std::string _input; std::getline(std::cin, _input);",
                _ => "var _input = null;"
            };

            sb.AppendLine(Pad(level) + read);
            if (_t.Id is "javascript" or "typescript")
            {
                _notes.Warn(node.Line, "input() 在 Node.js 中没有等价实现，需要手动引入 readline 或 prompt 库。");
            }
            return;
        }

        // 赋值语句
        var assign = Regex.Match(text, @"^([A-Za-z_]\w*)\s*(=(?!=)|\+=|-=|\*=|/=|%=)\s*(.+)$", RegexOptions.Singleline);
        if (assign.Success)
        {
            var name = assign.Groups[1].Value;
            var op = assign.Groups[2].Value;
            var value = assign.Groups[3].Value.Trim();
            var expr = Expr(value, node.Line);

            var scope = _scopes.Peek();
            var isNew = op == "=" && !scope.Contains(name);

            if (isNew)
            {
                scope.Add(name);
                sb.AppendLine(Pad(level) + DeclareVar(name, value, expr) + ";");
            }
            else
            {
                sb.AppendLine(Pad(level) + $"{name} {op} {expr};");
            }
            return;
        }

        // 表达式语句
        var exprText = Expr(text, node.Line);
        sb.AppendLine(Pad(level) + exprText + ";");
    }

    private string DeclareVar(string name, string rawValue, string translated)
    {
        var isStringLiteral = Regex.IsMatch(rawValue, @"^(""|')");
        var isListLiteral = rawValue.StartsWith('[') && rawValue.EndsWith(']');

        return _t.Id switch
        {
            "javascript" => $"let {name} = {translated}",
            "typescript" => isListLiteral ? $"let {name}: any[] = {translated}" : $"let {name} = {translated}",
            "csharp" => $"var {name} = {translated}",
            "java" => isStringLiteral ? $"String {name} = {translated}" : $"var {name} = {translated}",
            "cpp" => isStringLiteral ? $"std::string {name} = {translated}" : $"auto {name} = {translated}",
            _ => $"let {name} = {translated}"
        };
    }

    private string BuildPrint(List<string> args, int line)
    {
        if (args.Count == 0)
        {
            return _t.Id switch
            {
                "csharp" => "Console.WriteLine();",
                "java" => "System.out.println();",
                "cpp" => "std::cout << std::endl;",
                _ => "console.log();"
            };
        }

        var joined = string.Join(", ", args);

        if (args.Count == 1)
        {
            return _t.Id switch
            {
                "csharp" => $"Console.WriteLine({joined});",
                "java" => $"System.out.println({joined});",
                "cpp" => $"std::cout << {joined} << std::endl;",
                _ => $"console.log({joined});"
            };
        }

        return _t.Id switch
        {
            "csharp" => $"Console.WriteLine(string.Join(\" \", new object[] {{ {joined} }}));",
            "java" => $"System.out.println({string.Join(" + \" \" + ", args)});",
            "cpp" => $"std::cout << {string.Join(" << \" \" << ", args)} << std::endl;",
            _ => $"console.log({joined});"
        };
    }

    // ─────────────────────────────────────────────────────────
    // 表达式翻译
    // ─────────────────────────────────────────────────────────

    internal string Expr(string py, int line)
    {
        if (string.IsNullOrWhiteSpace(py)) return py;

        var s = py;
        var fstrings = new List<string>();
        var literals = new List<string>();

        // f-string 必须先提取：它生成的结果已经是目标语言代码，
        // 若继续参与后面的关键字替换，会把字符串里的 " is " 之类误改成运算符。
        s = Regex.Replace(s, @"\bf(""|')((?:\\.|(?!\1).)*)\1", m =>
        {
            fstrings.Add(BuildFString(m.Groups[2].Value, line));
            return $"\u0002{fstrings.Count - 1}\u0002";
        });

        // 保护普通字符串字面量，避免其中的关键字被误替换
        s = Regex.Replace(s, @"'(?:[^'\\]|\\.)*'|""(?:[^""\\]|\\.)*""", m =>
        {
            literals.Add(m.Value);
            return $"\u0001{literals.Count - 1}\u0001";
        });

        s = ReplaceKeywords(s);
        s = ReplaceBuiltins(s, line);

        for (var i = 0; i < literals.Count; i++)
        {
            s = s.Replace($"\u0001{i}\u0001", NormalizeLiteral(literals[i]));
        }

        for (var i = 0; i < fstrings.Count; i++)
        {
            s = s.Replace($"\u0002{i}\u0002", fstrings[i]);
        }

        return s;
    }

    private string ReplaceKeywords(string s)
    {
        s = Regex.Replace(s, @"\bTrue\b", "true");
        s = Regex.Replace(s, @"\bFalse\b", "false");
        s = Regex.Replace(s, @"\bNone\b", _t.Id == "cpp" ? "nullptr" : "null");
        s = Regex.Replace(s, @"\bself\.", _t.Id == "cpp" ? "this->" : "this.");
        s = Regex.Replace(s, @"\bnot\s+", "!");
        s = Regex.Replace(s, @"\s+and\s+", " && ");
        s = Regex.Replace(s, @"\s+or\s+", " || ");
        s = s.Replace(" is not ", " != ").Replace(" is ", " == ");
        s = Regex.Replace(s, @"(\w)\s*//\s*(\w)", "$1 / $2");

        if (_t.Id == "cpp")
        {
            s = Regex.Replace(s, @"\bnull\b", "nullptr");
        }

        return s;
    }

    private string ReplaceBuiltins(string s, int line)
    {
        // len(x)
        s = Regex.Replace(s, @"\blen\(([A-Za-z_]\w*)\)", m =>
        {
            var v = m.Groups[1].Value;
            return _t.Id switch
            {
                "csharp" => $"{v}.Length",
                "java" => $"{v}.length()",
                "cpp" => $"{v}.size()",
                _ => $"{v}.length"
            };
        });

        // str(x) / int(x) / float(x) / abs(x)
        s = Regex.Replace(s, @"\bstr\(([^()]+)\)", m => _t.Id switch
        {
            "csharp" => $"{m.Groups[1].Value}.ToString()",
            "java" => $"String.valueOf({m.Groups[1].Value})",
            "cpp" => $"std::to_string({m.Groups[1].Value})",
            _ => $"String({m.Groups[1].Value})"
        });

        s = Regex.Replace(s, @"\bint\(([^()]+)\)", m => _t.Id switch
        {
            "csharp" => $"int.Parse({m.Groups[1].Value}.ToString())",
            "java" => $"Integer.parseInt({m.Groups[1].Value}.ToString())",
            "cpp" => $"std::stoi({m.Groups[1].Value})",
            _ => $"parseInt({m.Groups[1].Value})"
        });

        s = Regex.Replace(s, @"\bfloat\(([^()]+)\)", m => _t.Id switch
        {
            "csharp" => $"double.Parse({m.Groups[1].Value}.ToString())",
            "java" => $"Double.parseDouble({m.Groups[1].Value}.ToString())",
            "cpp" => $"std::stod({m.Groups[1].Value})",
            _ => $"parseFloat({m.Groups[1].Value})"
        });

        s = Regex.Replace(s, @"\babs\(([^()]+)\)", m => _t.Id switch
        {
            "csharp" => $"Math.Abs({m.Groups[1].Value})",
            "java" => $"Math.abs({m.Groups[1].Value})",
            "cpp" => $"std::abs({m.Groups[1].Value})",
            _ => $"Math.abs({m.Groups[1].Value})"
        });

        // 幂运算
        s = Regex.Replace(s, @"([A-Za-z_]\w*)\s*\*\*\s*([A-Za-z_]\w*)", m => _t.Id switch
        {
            "csharp" => $"Math.Pow({m.Groups[1].Value}, {m.Groups[2].Value})",
            "java" => $"Math.pow({m.Groups[1].Value}, {m.Groups[2].Value})",
            "cpp" => $"std::pow({m.Groups[1].Value}, {m.Groups[2].Value})",
            _ => $"{m.Groups[1].Value} ** {m.Groups[2].Value}"
        });

        // 列表方法
        s = Regex.Replace(s, @"\.append\(", _t.Id switch
        {
            "csharp" => ".Add(",
            "java" => ".add(",
            "cpp" => ".push_back(",
            _ => ".push("
        });

        s = Regex.Replace(s, @"\.pop\(\)", _t.Id switch
        {
            "csharp" => ".RemoveAt(Count - 1)",
            "java" => ".remove(size() - 1)",
            _ => ".pop()"
        });

        // 列表字面量（仅整体为列表时转换）
        s = ConvertListLiteral(s, line);

        return s;
    }

    private string ConvertListLiteral(string s, int line)
    {
        if (_t.Id is "javascript" or "typescript") return s;

        var m = Regex.Match(s, @"^\[(.*)\]$", RegexOptions.Singleline);
        if (!m.Success) return s;

        var items = SplitTopLevel(m.Groups[1].Value).Select(x => x.Trim()).ToList();

        return _t.Id switch
        {
            "csharp" => $"new List<dynamic> {{ {string.Join(", ", items)} }}",
            "java" => items.Count == 0
                ? "new ArrayList<>()"
                : $"new ArrayList<>(Arrays.asList({string.Join(", ", items)}))",
            "cpp" => BuildCppVector(items),
            _ => s
        };
    }

    private string BuildCppVector(List<string> items)
    {
        var type = "int";
        if (items.Count > 0)
        {
            if (items.All(i => Regex.IsMatch(i, @"^-?\d+$"))) type = "int";
            else if (items.All(i => Regex.IsMatch(i, @"^-?\d*\.\d+$"))) type = "double";
            else if (items.Any(i => i.StartsWith('"'))) type = "std::string";
            else type = "double";
        }

        return $"std::vector<{type}>{{ {string.Join(", ", items)} }}";
    }

    /// <summary>把 Python f-string 的内容转成目标语言的字符串插值写法。</summary>
    private string BuildFString(string body, int line)
    {
        switch (_t.Id)
        {
            case "javascript":
            case "typescript":
                return "`" + Regex.Replace(body, @"\{([^{}]+)\}", "${$1}") + "`";

            case "csharp":
                // C# 插值语法与 f-string 同为 {expr}，只需处理引号
                return "$\"" + body.Replace("\"", "\\\"") + "\"";

            case "java":
            case "cpp":
                {
                    var segments = new List<string>();
                    var last = 0;

                    foreach (Match g in Regex.Matches(body, @"\{([^{}]+)\}"))
                    {
                        var literal = body[last..g.Index];
                        if (literal.Length > 0) segments.Add("\"" + literal.Replace("\"", "\\\"") + "\"");

                        var expr = g.Groups[1].Value.Trim();
                        segments.Add(_t.Id == "cpp" ? $"std::to_string({expr})" : expr);
                        last = g.Index + g.Length;
                    }

                    var tail = body[last..];
                    if (tail.Length > 0) segments.Add("\"" + tail.Replace("\"", "\\\"") + "\"");

                    if (segments.Count == 0) return "\"\"";
                    if (segments.Count == 1) return "(" + segments[0] + ")";

                    if (_t.Id == "cpp")
                    {
                        _notes.Warn(line, "C++ 的字符串插值已用 std::to_string 包裹；若插值本身是字符串，请去掉这次调用。");
                    }

                    return "(" + string.Join(" + ", segments) + ")";
                }

            default:
                return "\"" + body + "\"";
        }
    }

    private string NormalizeLiteral(string literal)
    {
        if (literal.Length < 2) return literal;

        // Python 单引号字符串 → 目标语言双引号字符串
        if (literal[0] == '\'')
        {
            var inner = literal[1..^1];
            inner = inner.Replace("\\'", "'").Replace("\"", "\\\"");
            return "\"" + inner + "\"";
        }

        return literal;
    }
}
