using System.Text;
using System.Text.RegularExpressions;

namespace HWZCodeBridge.Convert;

/// <summary>
/// 花括号语言（JS / TS / C# / Java / C++）→ Python。
/// 关键动作：花括号层级还原为缩进；容器类（Program / Main）与入口方法视为"透明块"剥离，
/// 避免生成 Python 中毫无意义的多余缩进层级。
/// </summary>
internal sealed class BraceToPythonConverter
{
    private readonly LanguageInfo _source;
    private readonly NoteBag _notes;

    private readonly StringBuilder _sb = new();
    private readonly Stack<bool> _blockStack = new();   // true = 透明块（不产生缩进）
    private int _depth;
    private int _transparent;
    private int _classBlockDepth;   // 当前类体所在的深度，用于给成员方法补 self 参数

    public BraceToPythonConverter(LanguageInfo source, NoteBag notes)
    {
        _source = source;
        _notes = notes;
    }

    private static string Pad(int n) => new(' ', Math.Max(0, n) * 4);

    private void Emit(string? text)
    {
        if (text is null) return;

        var visual = Math.Max(0, _depth - _transparent);
        _sb.AppendLine(Pad(visual) + text);
    }

    public string Convert(string source)
    {
        if (System.Text.RegularExpressions.Regex.IsMatch(source, @"\.(Add|add)\s*\("))
        {
            _notes.Info(1, "检测到 .Add( ) / .add( ) 调用：若意图是集合追加元素，Python 中应改为 .append( )，此处未自动替换以免误伤自定义方法。");
        }

        var parts = SplitBraces(NormalizeComments(source));

        var i = 0;
        var blankRun = 0;

        while (i < parts.Count)
        {
            var (raw, lineNo) = parts[i];
            var t = raw.Trim();

            if (t.Length == 0)
            {
                if (++blankRun <= 1 && _sb.Length > 0) _sb.AppendLine();
                i++;
                continue;
            }

            blankRun = 0;

            if (t.TrimEnd(';') == "}")
            {
                if (_blockStack.Count > 0)
                {
                    var transparent = _blockStack.Pop();
                    _depth--;
                    if (transparent) _transparent--;
                    if (_depth < _classBlockDepth) _classBlockDepth = 0;
                }
                i++;
                continue;
            }

            if (t == "{")
            {
                _blockStack.Push(false);
                _depth++;
                i++;
                continue;
            }

            var nextIsBrace = PeekToken(parts, i + 1) == "{";

            if (nextIsBrace)
            {
                if (IsContainerClass(t) || (IsEntryMethod(t) && _transparent > 0))
                {
                    // 透明块：内容回到父级缩进，Python 不需要这些容器
                    _blockStack.Push(true);
                    _depth++;
                    _transparent++;
                    i += 2;
                    continue;
                }

                Emit(TranslateBlockHeader(t, lineNo));
                _blockStack.Push(false);
                _depth++;
                i += 2;
                continue;
            }

            Emit(TranslateStatement(t, lineNo));
            i++;
        }

        var result = _sb.ToString().TrimEnd();
        return result.Length == 0 ? "" : result + Environment.NewLine;
    }

    private static string? PeekToken(List<(string Text, int Line)> parts, int index)
    {
        for (var i = index; i < parts.Count; i++)
        {
            var t = parts[i].Text.Trim();
            if (t.Length > 0) return t;
        }
        return null;
    }

    private static bool IsContainerClass(string t)
    {
        var cleaned = StripModifiers(t);
        return Regex.IsMatch(cleaned, @"^(?:class|struct|namespace)\s+(Program|Main|App|Application)$")
            || cleaned.StartsWith("namespace ", StringComparison.Ordinal);
    }

    private static bool IsEntryMethod(string t)
    {
        var cleaned = StripModifiers(t);
        return Regex.IsMatch(cleaned, @"^(?:void|int|static\s+void|dynamic)\s+(Main|main)\s*\(")
            || Regex.IsMatch(cleaned, @"^(Main|main)\s*\(")
            || Regex.IsMatch(cleaned, @"^int\s+main\s*\(");
    }

    private static string StripModifiers(string t)
    {
        var s = t.Trim();
        s = Regex.Replace(s, @"^(?:(?:public|private|protected|internal|static|final|abstract|sealed|override|virtual|synchronized|partial|async|export|default)\s+)+", "");
        return s.Trim();
    }

    // ─────────────────────────────────────────────────────────
    // 预处理
    // ─────────────────────────────────────────────────────────

    /// <summary>把 /* */ 块注释改写为逐行 // 形式，简化后续处理。</summary>
    private static string NormalizeComments(string source)
    {
        source = source.Replace("\r\n", "\n").Replace('\r', '\n');

        return Regex.Replace(source, @"/\*(.*?)\*/", m =>
        {
            var body = m.Groups[1].Value;
            var lines = body.Split('\n')
                .Select(l => "// " + l.Trim().TrimStart('*').Trim())
                .Where(l => l.Trim().Length > 2);
            return string.Join("\n", lines);
        }, RegexOptions.Singleline);
    }

    /// <summary>按字符串外的 { } 切分，保证每个花括号独立成行。</summary>
    private static List<(string Text, int Line)> SplitBraces(string source)
    {
        var lines = source.Split('\n');
        var result = new List<(string, int)>();

        for (var ln = 0; ln < lines.Length; ln++)
        {
            var line = lines[ln];
            var current = new StringBuilder();
            var inString = false;
            var inChar = false;
            char quote = '\0';

            for (var i = 0; i < line.Length; i++)
            {
                var c = line[i];

                if (!inString && !inChar && (c == '{' || c == '}'))
                {
                    if (current.ToString().Trim().Length > 0)
                    {
                        result.Add((current.ToString(), ln + 1));
                        current.Clear();
                    }
                    result.Add((c.ToString(), ln + 1));
                    continue;
                }

                if (!inString && !inChar && (c == '"' || c == '\''))
                {
                    inString = c == '"';
                    inChar = c == '\'';
                    quote = c;
                    current.Append(c);
                    continue;
                }

                if (inString || inChar)
                {
                    if (c == '\\' && i + 1 < line.Length)
                    {
                        current.Append(c).Append(line[i + 1]);
                        i++;
                        continue;
                    }
                    if (c == quote)
                    {
                        inString = false;
                        inChar = false;
                    }
                }

                current.Append(c);
            }

            if (current.ToString().Trim().Length > 0)
            {
                result.Add((current.ToString(), ln + 1));
            }
            else if (line.Trim().Length == 0)
            {
                // 空行要保留，否则转换后源码会全部挤在一起
                result.Add((string.Empty, ln + 1));
            }
        }

        return result;
    }

    // ─────────────────────────────────────────────────────────
    // 块头
    // ─────────────────────────────────────────────────────────

    private string TranslateBlockHeader(string raw, int line)
    {
        var t = raw.Trim().TrimEnd(';');

        if (t == "try") return "try:";
        if (t == "finally") return "finally:";
        if (t == "else") return "else:";
        if (t is "do") return "while True:";

        // if / else if
        var ifMatch = Regex.Match(t, @"^(?:else\s+)?if\s*\((.*)\)$", RegexOptions.Singleline);
        if (ifMatch.Success)
        {
            var cond = TranslateExpr(ifMatch.Groups[1].Value, line);
            return t.StartsWith("else") ? $"elif {cond}:" : $"if {cond}:";
        }

        if (t.StartsWith("else if", StringComparison.Ordinal))
        {
            var cond = Regex.Match(t, @"else\s+if\s*\((.*)\)").Groups[1].Value;
            return $"elif {TranslateExpr(cond, line)}:";
        }

        // while
        var whileMatch = Regex.Match(t, @"^while\s*\((.*)\)$", RegexOptions.Singleline);
        if (whileMatch.Success)
        {
            return $"while {TranslateExpr(whileMatch.Groups[1].Value, line)}:";
        }

        // for
        var forMatch = Regex.Match(t, @"^for\s*\((.*)\)$", RegexOptions.Singleline);
        if (forMatch.Success)
        {
            return TranslateForHeader(forMatch.Groups[1].Value, line);
        }

        // foreach
        var eachMatch = Regex.Match(t, @"^foreach\s*\((.+?)\s+(\w+)\s+in\s+(.+)\)$", RegexOptions.Singleline);
        if (eachMatch.Success)
        {
            return $"for {eachMatch.Groups[2].Value} in {TranslateExpr(eachMatch.Groups[3].Value, line)}:";
        }

        // catch
        var catchMatch = Regex.Match(t, @"^catch\s*\((.*)\)$", RegexOptions.Singleline);
        if (catchMatch.Success)
        {
            var inner = catchMatch.Groups[1].Value.Trim();
            if (inner is "..." or "") return "except:";
            var parts = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2) return $"except Exception as {parts[^1]}:";
            return $"except {parts[0]}:";
        }

        if (t == "catch") return "except:";

        // switch → match
        var switchMatch = Regex.Match(t, @"^switch\s*\((.*)\)$", RegexOptions.Singleline);
        if (switchMatch.Success)
        {
            _notes.Info(line, "switch 已转换为 Python 3.10+ 的 match 语句，需要 Python 3.10 及以上版本。");
            return $"match {TranslateExpr(switchMatch.Groups[1].Value, line)}:";
        }

        var caseMatch = Regex.Match(t, @"^case\s+(.+?):?$", RegexOptions.Singleline);
        if (caseMatch.Success)
        {
            return $"case {TranslateExpr(caseMatch.Groups[1].Value, line)}:";
        }

        // class / struct / interface
        var classMatch = Regex.Match(t, @"^(?:class|struct|interface)\s+([A-Za-z_]\w*)\s*(.*)$", RegexOptions.Singleline);
        if (classMatch.Success)
        {
            var name = classMatch.Groups[1].Value;
            _classBlockDepth = _depth + 1;
            var tail = classMatch.Groups[2].Value.Trim();
            var baseMatch = Regex.Match(tail, @"(?::|extends|implements)\s*([A-Za-z_][\w<>,\.\s]*)$");
            if (baseMatch.Success)
            {
                var bases = baseMatch.Groups[1].Value
                    .Split(',')
                    .Select(b => b.Trim().Split('<')[0])
                    .Where(b => b.Length > 0)
                    .ToList();
                if (bases.Count > 0) return $"class {name}({string.Join(", ", bases)}):";
            }
            return $"class {name}:";
        }

        // 函数
        var (fnName, fnParams, isCtor) = ParseFunctionSignature(t);
        if (fnName is not null)
        {
            var ps = string.Join(", ", fnParams);
            var inClass = _classBlockDepth > 0 && _depth >= _classBlockDepth;

            if (isCtor || inClass)
            {
                ps = ps.Length == 0 ? "self" : "self, " + ps;
            }

            return $"def {fnName}({ps}):";
        }

        _notes.Warn(line, $"未能识别的块头，已保留为注释：{t}");
        return $"# 待人工处理：{t}";
    }

    private string TranslateForHeader(string inner, int line)
    {
        // for (var x : arr)
        var colon = Regex.Match(inner, @"^(.+?)\s*:\s*(.+)$", RegexOptions.Singleline);
        if (colon.Success && !inner.Contains(';'))
        {
            var name = colon.Groups[1].Value.Trim().Split(' ')[^1];
            return $"for {name} in {TranslateExpr(colon.Groups[2].Value, line)}:";
        }

        // for (const x of arr)
        var ofMatch = Regex.Match(inner, @"^(?:const|let|var)\s+(\w+)\s+of\s+(.+)$", RegexOptions.Singleline);
        if (ofMatch.Success)
        {
            return $"for {ofMatch.Groups[1].Value} in {TranslateExpr(ofMatch.Groups[2].Value, line)}:";
        }

        // for (let i = 0; i < n; i++)
        var segments = inner.Split(';').Select(x => x.Trim()).ToList();
        if (segments.Count == 3)
        {
            var init = Regex.Match(segments[0], @"(\w+)\s*=\s*(.+)");
            var cond = Regex.Match(segments[1], @"(\w+)\s*(<=|<|>=|>)\s*(.+)");
            var step = segments[2].Trim();

            if (init.Success && cond.Success)
            {
                var v = init.Groups[1].Value;
                var start = init.Groups[2].Value.Trim();
                var cmp = cond.Groups[2].Value;
                var end = cond.Groups[3].Value.Trim();

                var stepValue = "1";
                var inc = Regex.Match(step, @"(\w+)\s*\+\+");
                var dec = Regex.Match(step, @"(\w+)\s*--");
                var add = Regex.Match(step, @"(\w+)\s*\+=\s*(.+)");
                var sub = Regex.Match(step, @"(\w+)\s*-=\s*(.+)");

                if (inc.Success) stepValue = "1";
                else if (dec.Success) stepValue = "-1";
                else if (add.Success) stepValue = add.Groups[2].Value.Trim();
                else if (sub.Success) stepValue = "-" + sub.Groups[2].Value.Trim();

                if (cmp == "<=") end = $"{end} + 1";
                if (cmp == ">") end = $"{end} - 1";

                _notes.Info(line, "C 风格 for 循环已还原为 range()，请核对边界条件是否等价。");

                if (start == "0" && stepValue == "1")
                {
                    return $"for {v} in range({TranslateExpr(end, line)}):";
                }

                return stepValue == "1"
                    ? $"for {v} in range({TranslateExpr(start, line)}, {TranslateExpr(end, line)}):"
                    : $"for {v} in range({TranslateExpr(start, line)}, {TranslateExpr(end, line)}, {stepValue}):";
            }
        }

        _notes.Warn(line, "该 for 循环结构无法等价还原，已按 while 处理。");
        return $"for _ in {TranslateExpr(inner, line)}:";
    }

    private (string? Name, List<string> Params, bool IsCtor) ParseFunctionSignature(string t)
    {
        var cleaned = StripModifiers(t);
        cleaned = Regex.Replace(cleaned, @"^(?:void|int|string|bool|double|float|char|object|dynamic|number|any|boolean|String|Object|long|short|byte)\s+", "");

        var m = Regex.Match(cleaned, @"^([A-Za-z_]\w*)\s*\((.*)\)$", RegexOptions.Singleline);
        if (!m.Success) return (null, new List<string>(), false);

        var name = m.Groups[1].Value;
        var isCtor = name is "constructor" or "init";
        if (name == "constructor") name = "__init__";

        var rawParams = m.Groups[2].Value;
        var list = new List<string>();

        foreach (var p in SplitTopLevel(rawParams))
        {
            var item = p.Trim();
            if (item.Length == 0) continue;

            // 去掉类型前缀与默认值
            item = item.Split('=')[0].Trim();
            item = Regex.Replace(item, @"^(?:(?:const|final|in|out|ref|params)\s+)*[\w<>\[\],\.\?]+\s+", "");
            item = item.TrimStart('*', '&');
            if (item.Length > 0 && Regex.IsMatch(item, @"^[A-Za-z_]\w*$")) list.Add(item);
        }

        return (name, list, isCtor);
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
    // ─────────────────────────────────────────────────────────
    // 普通语句
    // ─────────────────────────────────────────────────────────

    private string? TranslateStatement(string raw, int line)
    {
        var t = raw.Trim().TrimEnd(';');

        if (t.Length == 0) return null;

        // 头文件 / 导入 / 包声明
        if (t.StartsWith("#include", StringComparison.Ordinal) ||
            t.StartsWith("using System", StringComparison.Ordinal) ||
            t.StartsWith("using static", StringComparison.Ordinal) ||
            t.StartsWith("package ", StringComparison.Ordinal) ||
            t.StartsWith("\"use strict\"", StringComparison.Ordinal))
        {
            return null;
        }

        if (Regex.IsMatch(t, @"^(import|from)\s+") && !t.Contains('('))
        {
            return null;
        }

        if (t.StartsWith("//", StringComparison.Ordinal))
        {
            if (t.Contains("入口函数")) return null;
            return "#" + t[2..];
        }

        // 打印
        var print = TryTranslatePrint(t, line);
        if (print is not null) return print;

        // return / break / continue
        if (t == "return") return "return";
        if (t.StartsWith("return ", StringComparison.Ordinal))
        {
            return "return " + TranslateExpr(t[7..], line);
        }
        if (t is "break" or "continue") return t;

        // 自增自减
        var inc = Regex.Match(t, @"^([A-Za-z_]\w*)(\+\+|--)$");
        if (inc.Success)
        {
            return $"{inc.Groups[1].Value} {(inc.Groups[2].Value == "++" ? "+=" : "-=")} 1";
        }

        // 变量声明去类型
        var stripped = StripTypeDeclaration(t);
        var expr = TranslateExpr(stripped, line);

        return expr;
    }

    private string? TryTranslatePrint(string t, int line)
    {
        var m = Regex.Match(t, @"^(?:console\.log|Console\.WriteLine|System\.out\.println|System\.println|println|printf)\s*\((.*)\)$", RegexOptions.Singleline);
        if (m.Success)
        {
            var args = SplitTopLevel(m.Groups[1].Value)
                .Select(a => TranslateExpr(a.Trim(), line))
                .Where(a => a.Trim().Length > 0)
                .ToList();
            return args.Count == 0 ? "print()" : $"print({string.Join(", ", args)})";
        }

        var write = Regex.Match(t, @"^(?:Console\.Write|System\.out\.print|print|cout)\s*\((.*)\)$", RegexOptions.Singleline);
        if (write.Success)
        {
            return $"print({TranslateExpr(write.Groups[1].Value.Trim(), line)}, end=\"\")";
        }

        var cout = Regex.Match(t, @"^(?:std::)?cout\s*<<\s*(.+)$", RegexOptions.Singleline);
        if (cout.Success)
        {
            var items = cout.Groups[1].Value
                .Split("<<")
                .Select(x => x.Trim())
                .Where(x => x.Length > 0 && x is not ("std::endl" or "endl" or "'\\n'" or "\"\\n\""))
                .Select(x => TranslateExpr(x, line))
                .ToList();

            return items.Count == 0 ? "print()" : $"print({string.Join(", ", items)})";
        }

        return null;
    }

    private static string StripTypeDeclaration(string t)
    {
        // 1) 去掉声明关键字前缀：let / const / var / auto / final / readonly ...
        var s = Regex.Replace(t, @"^(?:(?:const|let|var|auto|final|readonly|static|unsigned|signed|long|short|volatile)\s+)+", "");
        s = s.Trim();

        // 2) 找到顶层赋值号，把左侧拆出来单独判断
        var eq = FindTopLevelAssign(s);
        var left = (eq >= 0 ? s[..eq] : s).Trim();
        var right = eq >= 0 ? s[eq..] : "";

        // 左侧已经是纯变量名，说明没有类型前缀
        if (Regex.IsMatch(left, @"^[A-Za-z_]\w*$")) return left + (right.Length > 0 ? " " : "") + right;

        // 3) 左侧形如 "类型 变量名"（可带命名空间、泛型、数组），去掉类型
        var m = Regex.Match(left, @"^[\w:<>\[\],\.\?]+(?:\[\])?\s+([A-Za-z_]\w*)$");
        if (m.Success) return m.Groups[1].Value + (right.Length > 0 ? " " : "") + right;

        return s;
    }

    /// <summary>返回首个位于顶层（不在括号与字符串内）的赋值号位置；排除 ==、!=、&lt;=、&gt;=。</summary>
    private static int FindTopLevelAssign(string s)
    {
        var depth = 0;
        var inStr = false;
        char quote = '\0';

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];

            if (inStr)
            {
                if (c == '\\') { i++; continue; }
                if (c == quote) inStr = false;
                continue;
            }

            if (c is '"' or '\'') { inStr = true; quote = c; continue; }

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;
            else if (c == '=' && depth == 0)
            {
                var prev = i > 0 ? s[i - 1] : ' ';
                var next = i + 1 < s.Length ? s[i + 1] : ' ';
                if (next == '=' || prev is '=' or '!' or '<' or '>' or '+' or '-' or '*' or '/' or '%')
                {
                    if (next == '=') i++;   // 跳过 == 的第二个等号
                    continue;
                }
                return i;
            }
        }

        return -1;
    }

    // ─────────────────────────────────────────────────────────
    // 表达式
    // ─────────────────────────────────────────────────────────

    private string TranslateExpr(string expr, int line)
    {
        if (string.IsNullOrWhiteSpace(expr)) return expr;

        var s = expr;

        // 保护字符串
        var literals = new List<string>();
        s = Regex.Replace(s, @"""(?:[^""\\]|\\.)*""|'(?:[^'\\]|\\.)*'", m =>
        {
            literals.Add(m.Value);
            return $"\u0001{literals.Count - 1}\u0001";
        });

        // 字面量
        s = Regex.Replace(s, @"\btrue\b", "True");
        s = Regex.Replace(s, @"\bfalse\b", "False");
        s = Regex.Replace(s, @"\b(?:null|nullptr|NULL)\b", "None");
        s = Regex.Replace(s, @"\bthis\b", "self");
        s = s.Replace("->", ".");

        // 逻辑运算
        s = s.Replace("&&", " and ").Replace("||", " or ");
        s = Regex.Replace(s, @"(?<![=!<>])!(?!=)", " not ");

        // 长度
        s = Regex.Replace(s, @"([A-Za-z_]\w*(?:\.\w+)*)\.(?:Length|length\(\)|size\(\))", "len($1)");

        // 类型转换
        s = Regex.Replace(s, @"(?:String|Convert\.ToString)\((.*?)\)", "str($1)");
        s = Regex.Replace(s, @"(?:Integer\.parseInt|int\.Parse|std::stoi)\((.*?)\)", "int($1)");
        s = Regex.Replace(s, @"(?:Double\.parseDouble|double\.Parse|std::stod)\((.*?)\)", "float($1)");
        s = Regex.Replace(s, @"Math\.(?:abs|Abs)\((.*?)\)", "abs($1)");
        s = Regex.Replace(s, @"(?:Math\.(?:Pow|pow)|std::pow)\((.*?),(.*?)\)", "$1 ** $2");

        // 容器方法（只处理语义明确的 push 系列；Add/add 见转换开头的提示）
        s = Regex.Replace(s, @"\.(?:push|push_back)\(", ".append(");
        s = Regex.Replace(s, @"([A-Za-z_]\w*(?:\.\w+)*)\.(?:Count|size\(\))", "len($1)");

        // new 关键字 / 数组构造
        s = Regex.Replace(s, @"\bnew\s+([A-Za-z_][\w\.]*)\s*\(", "$1(");
        s = Regex.Replace(s, @"\bnew\s+([A-Za-z_][\w\.]*)\s*\[\s*\]", "$1()");

        // 空值判断
        s = Regex.Replace(s, @"(\w+)\s*==\s*None", "$1 is None");
        s = Regex.Replace(s, @"(\w+)\s*!=\s*None", "$1 is not None");

        for (var i = 0; i < literals.Count; i++)
        {
            s = s.Replace($"\u0001{i}\u0001", literals[i]);
        }

        // C# 插值字符串 → Python f-string
        s = Regex.Replace(s, @"\$(""(?:[^""\\]|\\.)*"")", "f$1");

        return s.Trim();
    }
}
