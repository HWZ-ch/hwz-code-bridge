using System.Text;

namespace HWZCodeBridge.Convert;

internal enum PyKind
{
    Blank,
    Comment,
    Import,
    Decorator,
    FuncDef,
    ClassDef,
    If,
    Elif,
    Else,
    For,
    While,
    Try,
    Except,
    Finally,
    With,
    Return,
    Break,
    Continue,
    Pass,
    Simple
}

/// <summary>Python 语句节点。Children 表示该语句所辖的缩进块。</summary>
internal sealed class PyNode
{
    public int Line { get; init; }
    public int Indent { get; init; }
    public PyKind Kind { get; set; }
    public string Text { get; set; } = "";        // 去掉注释后的语句文本
    public string Comment { get; set; } = "";     // 行内注释（不含 #）
    public string Head { get; set; } = "";        // 块头关键信息（条件 / 签名 / 迭代器等）
    public List<PyNode> Children { get; } = new();

    public bool IsBlockStarter => Kind is PyKind.FuncDef or PyKind.ClassDef or PyKind.If
        or PyKind.Elif or PyKind.Else or PyKind.For or PyKind.While
        or PyKind.Try or PyKind.Except or PyKind.Finally or PyKind.With;

    public bool IsContinuation => Kind is PyKind.Elif or PyKind.Else or PyKind.Except or PyKind.Finally;
}

/// <summary>
/// 一个"够用"的 Python 解析器：只识别缩进块结构与语句类别，不构建完整 AST。
/// 目的不是做编译器，而是为跨语言结构改写提供可靠的层级与分类信息。
/// </summary>
internal static class PythonParser
{
    public static List<PyNode> Parse(string source, NoteBag notes)
    {
        var physical = source.Replace("\r\n", "\n").Replace('\r', '\n').Split('\n');
        var logical = JoinLogicalLines(physical);

        var root = new PyNode { Indent = -1 };
        var stack = new Stack<PyNode>();
        stack.Push(root);

        var pendingBlanks = 0;

        foreach (var (text, lineNo) in logical)
        {
            var (code, comment) = SplitInlineComment(text);
            var trimmed = code.TrimEnd();
            var isCommentOnly = trimmed.Trim().Length == 0 && !string.IsNullOrWhiteSpace(comment);

            if (trimmed.Trim().Length == 0 && !isCommentOnly)
            {
                pendingBlanks++;
                continue;
            }

            if (isCommentOnly)
            {
                var cIndent = CountIndent(text);

                // 注释的归属由缩进决定，否则会被错误地挂到上一个块里面
                while (stack.Count > 1 && stack.Peek().Indent >= cIndent)
                {
                    stack.Pop();
                }

                FlushBlanks(stack.Peek(), ref pendingBlanks);

                stack.Peek().Children.Add(new PyNode
                {
                    Kind = PyKind.Comment,
                    Line = lineNo,
                    Indent = cIndent,
                    Text = comment
                });
                continue;
            }

            var indent = CountIndent(trimmed);
            var content = trimmed.Trim();

            var node = new PyNode { Line = lineNo, Indent = indent, Comment = comment };
            Classify(node, content, notes);

            while (stack.Count > 1 && stack.Peek().Indent >= indent)
            {
                stack.Pop();
            }

            // 空行挂在"下一条语句所属的层级"，这样块的尾部不会残留空行
            FlushBlanks(stack.Peek(), ref pendingBlanks);

            stack.Peek().Children.Add(node);

            if (node.IsBlockStarter)
            {
                stack.Push(node);
            }
        }

        DetectComplexSyntax(source, notes);
        return root.Children;

        static void FlushBlanks(PyNode parent, ref int count)
        {
            if (count <= 0) return;
            parent.Children.Add(new PyNode { Kind = PyKind.Blank });
            count = 0;
        }
    }

    private static int CountIndent(string line)
    {
        var n = 0;
        foreach (var ch in line)
        {
            if (ch == ' ') n++;
            else if (ch == '\t') n += 4;
            else break;
        }
        return n;
    }

    private static void Classify(PyNode node, string content, NoteBag notes)
    {
        node.Text = content;
        node.Kind = PyKind.Simple;

        if (content.StartsWith('#'))
        {
            node.Kind = PyKind.Comment;
            node.Text = content.TrimStart('#').Trim();
            return;
        }

        if (content.StartsWith('@'))
        {
            node.Kind = PyKind.Decorator;
            return;
        }

        if (content.StartsWith("import ", StringComparison.Ordinal) ||
            content.StartsWith("from ", StringComparison.Ordinal))
        {
            node.Kind = PyKind.Import;
            return;
        }

        // 块级关键字：必须以 ':' 结尾
        var endsWithColon = content.EndsWith(':');
        var body = endsWithColon ? content[..^1].TrimEnd() : content;

        if (endsWithColon)
        {
            if (body.StartsWith("def ", StringComparison.Ordinal) ||
                body.StartsWith("async def ", StringComparison.Ordinal))
            {
                node.Kind = PyKind.FuncDef;
                node.Head = body.StartsWith("async ") ? body[6..] : body;
                return;
            }

            if (body.StartsWith("class ", StringComparison.Ordinal))
            {
                node.Kind = PyKind.ClassDef;
                node.Head = body;
                return;
            }

            if (body.StartsWith("if ", StringComparison.Ordinal))
            {
                node.Kind = PyKind.If;
                node.Head = body[3..].Trim();
                return;
            }

            if (body.StartsWith("elif ", StringComparison.Ordinal))
            {
                node.Kind = PyKind.Elif;
                node.Head = body[5..].Trim();
                return;
            }

            if (body == "else")
            {
                node.Kind = PyKind.Else;
                return;
            }

            if (body.StartsWith("for ", StringComparison.Ordinal) ||
                body.StartsWith("async for ", StringComparison.Ordinal))
            {
                node.Kind = PyKind.For;
                node.Head = body.StartsWith("async ") ? body[6..] : body;
                return;
            }

            if (body.StartsWith("while ", StringComparison.Ordinal))
            {
                node.Kind = PyKind.While;
                node.Head = body[6..].Trim();
                return;
            }

            if (body == "try")
            {
                node.Kind = PyKind.Try;
                return;
            }

            if (body.StartsWith("except", StringComparison.Ordinal))
            {
                node.Kind = PyKind.Except;
                node.Head = body.Length > 6 ? body[6..].Trim() : "";
                return;
            }

            if (body == "finally")
            {
                node.Kind = PyKind.Finally;
                return;
            }

            if (body.StartsWith("with ", StringComparison.Ordinal) ||
                body.StartsWith("async with ", StringComparison.Ordinal))
            {
                node.Kind = PyKind.With;
                node.Head = body.StartsWith("async ") ? body[6..] : body;
                return;
            }
        }

        if (content == "break") { node.Kind = PyKind.Break; return; }
        if (content == "continue") { node.Kind = PyKind.Continue; return; }
        if (content == "pass") { node.Kind = PyKind.Pass; return; }
        if (content == "return" || content.StartsWith("return ", StringComparison.Ordinal))
        {
            node.Kind = PyKind.Return;
            return;
        }
    }

    /// <summary>把跨物理行的逻辑行（括号未闭合、三引号字符串）合并成一条。</summary>
    private static List<(string Text, int Line)> JoinLogicalLines(string[] physical)
    {
        var result = new List<(string, int)>();
        var buffer = new StringBuilder();
        var depth = 0;
        var inTriple = false;
        var startLine = 0;

        for (var i = 0; i < physical.Length; i++)
        {
            var line = physical[i];

            if (buffer.Length == 0)
            {
                startLine = i + 1;
                buffer.Append(line);
            }
            else
            {
                buffer.Append('\n').Append(line);
            }

            (depth, inTriple) = ScanLine(line, depth, inTriple);

            if (depth <= 0 && !inTriple)
            {
                result.Add((buffer.ToString(), startLine));
                buffer.Clear();
                depth = 0;
            }
        }

        if (buffer.Length > 0) result.Add((buffer.ToString(), startLine));
        return result;
    }

    private static (int Depth, bool InTriple) ScanLine(string line, int depth, bool inTriple)
    {
        var i = 0;
        var triple = "\"\"\"";
        while (i < line.Length)
        {
            if (inTriple)
            {
                if (i + 2 < line.Length && line.Substring(i, 3) is "\"\"\"" or "'''")
                {
                    inTriple = false;
                    i += 3;
                }
                else i++;
                continue;
            }

            var c = line[i];

            if (c == '#') break;

            if (c is '"' or '\'')
            {
                if (i + 2 < line.Length && line[i + 1] == c && line[i + 2] == c)
                {
                    inTriple = true;
                    i += 3;
                    continue;
                }

                i++;
                while (i < line.Length && line[i] != c)
                {
                    if (line[i] == '\\') i++;
                    i++;
                }
                i++;
                continue;
            }

            if (c is '(' or '[' or '{') depth++;
            else if (c is ')' or ']' or '}') depth--;

            i++;
        }

        _ = triple;
        return (depth, inTriple);
    }

    private static (string Code, string Comment) SplitInlineComment(string text)
    {
        var i = 0;
        var inStr = false;
        var inTriple = false;
        char quote = '\0';

        while (i < text.Length)
        {
            var c = text[i];

            if (inTriple)
            {
                if (i + 2 < text.Length && text[i + 1] == quote && text[i + 2] == quote)
                {
                    inTriple = false;
                    i += 3;
                    continue;
                }
                i++;
                continue;
            }

            if (inStr)
            {
                if (c == '\\') { i += 2; continue; }
                if (c == quote) inStr = false;
                i++;
                continue;
            }

            if (c is '"' or '\'')
            {
                if (i + 2 < text.Length && text[i + 1] == c && text[i + 2] == c)
                {
                    inTriple = true;
                    quote = c;
                    i += 3;
                    continue;
                }
                inStr = true;
                quote = c;
                i++;
                continue;
            }

            if (c == '#')
            {
                return (text[..i], text[(i + 1)..].Trim());
            }

            i++;
        }

        return (text, "");
    }

    /// <summary>扫描 Python 中目标语言难以等价表达的高级语法，提前给出告警。</summary>
    private static void DetectComplexSyntax(string source, NoteBag notes)
    {
        var lines = source.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var t = lines[i].Trim();
            if (t.StartsWith("import ") || t.StartsWith("from ")) continue;

            var ln = i + 1;
            if (t.Contains("lambda")) notes.Warn(ln, "lambda 已按匿名函数尽力改写，请核对参数与返回值。");
            if (t.Contains("yield")) notes.Warn(ln, "生成器语义在目标语言中没有直接对应，需要改写为数组或迭代器。");
            if (t.StartsWith("with ")) notes.Warn(ln, "with 上下文管理器已降级为普通语句，资源释放需要手动处理。");
            if (t.Contains("**kwargs") || t.Contains("def ") && t.Contains("*args")) notes.Warn(ln, "可变参数在目标语言中需要显式改写。");
            if (t.StartsWith('@')) notes.Warn(ln, "装饰器无法自动移植，已保留为注释。");
            if (t.Contains("async ") || t.Contains("await ")) notes.Warn(ln, "异步语法仅保留关键字，运行时行为需要按目标语言调整。");
        }
    }
}
