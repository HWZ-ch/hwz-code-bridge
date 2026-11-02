/*!
 * HWZ 代码互换工坊 — 网页版转换引擎
 * 署名：HWZ工作室
 *
 * 与桌面版（.NET 8 / WPF）共用同一套转换规则：
 * 决定语义的是"缩进块 ↔ 花括号块"的结构重写，而非逐词替换。
 * 引擎不依赖任何三方库，可在浏览器与 Node.js 中同时运行。
 */
(function (root, factory) {
    if (typeof module === 'object' && module.exports) module.exports = factory();
    else root.HWZEngine = factory();
})(typeof globalThis !== 'undefined' ? globalThis : this, function () {
    'use strict';

    // ═══════════════════════════════════════════════════════
    // 语言目录
    // ═══════════════════════════════════════════════════════

    const LANGUAGES = [
        {
            id: 'python', name: 'Python', ext: '.py', brace: false, header: '',
            sample: [
                '# 计算斐波那契数列',
                'def fib(n):',
                '    a = 0',
                '    b = 1',
                '    total = 0',
                '    for i in range(n):',
                '        print(a)',
                '        t = a + b',
                '        a = b',
                '        b = t',
                '        total += a',
                '    return total',
                '',
                'def classify(x):',
                '    if x > 100:',
                '        return "big"',
                '    elif x > 10:',
                '        return "medium"',
                '    else:',
                '        return "small"',
                '',
                'if __name__ == "__main__":',
                '    name = "HWZ"',
                '    result = fib(8)',
                '    print(f"result of {name} is {result}")',
                '    print(classify(result))'
            ].join('\n')
        },
        {
            id: 'javascript', name: 'JavaScript', ext: '.js', brace: true, header: '',
            sample: [
                '// 计算斐波那契数列',
                'function fib(n) {',
                '    let a = 0;',
                '    let b = 1;',
                '    for (let i = 0; i < n; i++) {',
                '        console.log(a);',
                '        let t = a + b;',
                '        a = b;',
                '        b = t;',
                '    }',
                '    return a;',
                '}',
                '',
                'console.log(fib(10));'
            ].join('\n')
        },
        {
            id: 'typescript', name: 'TypeScript', ext: '.ts', brace: true, header: '',
            sample: [
                '// 计算斐波那契数列',
                'function fib(n: number): number {',
                '    let a: number = 0;',
                '    let b: number = 1;',
                '    for (let i = 0; i < n; i++) {',
                '        console.log(a);',
                '        let t: number = a + b;',
                '        a = b;',
                '        b = t;',
                '    }',
                '    return a;',
                '}',
                '',
                'console.log(fib(10));'
            ].join('\n')
        },
        {
            id: 'csharp', name: 'C#', ext: '.cs', brace: true,
            header: 'using System;\nusing System.Collections.Generic;\nusing System.Linq;',
            sample: [
                '// 计算斐波那契数列',
                'using System;',
                '',
                'class Program',
                '{',
                '    static void Main(string[] args)',
                '    {',
                '        int a = 0, b = 1;',
                '        for (int i = 0; i < 10; i++)',
                '        {',
                '            Console.WriteLine(a);',
                '            int temp = a + b;',
                '            a = b;',
                '            b = temp;',
                '        }',
                '    }',
                '}'
            ].join('\n')
        },
        {
            id: 'java', name: 'Java', ext: '.java', brace: true,
            header: 'import java.util.*;',
            sample: [
                '// 计算斐波那契数列',
                '',
                'public class Main',
                '{',
                '    public static void main(String[] args)',
                '    {',
                '        int a = 0, b = 1;',
                '        for (int i = 0; i < 10; i++)',
                '        {',
                '            System.out.println(a);',
                '            int temp = a + b;',
                '            a = b;',
                '            b = temp;',
                '        }',
                '    }',
                '}'
            ].join('\n')
        },
        {
            id: 'cpp', name: 'C++', ext: '.cpp', brace: true,
            header: '#include <iostream>\n#include <string>\n#include <vector>\n#include <cmath>\n\nusing namespace std;',
            sample: [
                '// 计算斐波那契数列',
                '#include <iostream>',
                '',
                'int main()',
                '{',
                '    int a = 0, b = 1;',
                '    for (int i = 0; i < 10; i++)',
                '    {',
                '        std::cout << a << std::endl;',
                '        int temp = a + b;',
                '        a = b;',
                '        b = temp;',
                '    }',
                '    return 0;',
                '}'
            ].join('\n')
        }
    ];

    const LANGUAGE_MAP = {};
    LANGUAGES.forEach(function (l) { LANGUAGE_MAP[l.id] = l; });

    function getLanguage(id) {
        return LANGUAGE_MAP[String(id || '').toLowerCase()] || LANGUAGES[0];
    }

    function guessFromPath(path) {
        const p = String(path || '').toLowerCase();
        const m = p.match(/\.[a-z0-9]+$/);
        const ext = m ? m[0] : '';
        const table = {
            '.py': 'python', '.pyw': 'python',
            '.js': 'javascript', '.mjs': 'javascript', '.cjs': 'javascript', '.jsx': 'javascript',
            '.ts': 'typescript', '.tsx': 'typescript', '.mts': 'typescript',
            '.cs': 'csharp',
            '.java': 'java',
            '.cpp': 'cpp', '.cc': 'cpp', '.cxx': 'cpp', '.h': 'cpp', '.hpp': 'cpp', '.c': 'cpp'
        };
        return table[ext] ? getLanguage(table[ext]) : null;
    }

    // ═══════════════════════════════════════════════════════
    // 通用工具
    // ═══════════════════════════════════════════════════════

    function createNotes() {
        const list = [];
        return {
            list: list,
            info: function (line, msg) { list.push({ line: line, level: 'info', message: msg }); },
            warn: function (line, msg) { list.push({ line: line, level: 'warn', message: msg }); }
        };
    }

    function noteText(n) {
        return n.line > 0 ? ('第 ' + n.line + ' 行 · ' + n.message) : n.message;
    }

    function countLines(text) {
        if (!text) return 0;
        return String(text).replace(/\r\n/g, '\n').split('\n').length;
    }

    const INDENT = 4;

    function pad(level) {
        return ' '.repeat(Math.max(0, level) * INDENT);
    }

    /** 按顶层逗号切分，忽略括号与字符串内部的逗号 */
    function splitTopLevel(text) {
        const parts = [];
        let depth = 0, start = 0;
        for (let i = 0; i < text.length; i++) {
            const c = text[i];
            if (c === '(' || c === '[' || c === '{') depth++;
            else if (c === ')' || c === ']' || c === '}') depth--;
            else if (c === ',' && depth === 0) {
                parts.push(text.slice(start, i));
                start = i + 1;
            }
        }
        parts.push(text.slice(start));
        return parts;
    }

    // ═══════════════════════════════════════════════════════
    // Python 解析器：只识别缩进块结构与语句类别
    // ═══════════════════════════════════════════════════════

    function mkNode(indent) {
        return { line: 0, indent: indent, kind: 'simple', text: '', comment: '', head: '', children: [] };
    }

    const BLOCK_KINDS = { func: 1, 'class': 1, 'if': 1, 'elif': 1, 'else': 1, 'for': 1, 'while': 1, 'try': 1, 'except': 1, 'finally': 1, 'with': 1 };
    const CONTINUATION_KINDS = { 'elif': 1, 'else': 1, 'except': 1, 'finally': 1 };

    function isBlockStarter(n) { return !!BLOCK_KINDS[n.kind]; }
    function isContinuation(n) { return !!CONTINUATION_KINDS[n.kind]; }

    function countIndent(line) {
        let n = 0;
        for (const ch of line) {
            if (ch === ' ') n++;
            else if (ch === '\t') n += 4;
            else break;
        }
        return n;
    }

    /** 扫描一行，返回括号深度与三引号字符串状态 */
    function scanLine(line, depth, inTriple) {
        let i = 0;
        while (i < line.length) {
            if (inTriple) {
                if (i + 2 < line.length) {
                    const three = line.substr(i, 3);
                    if (three === '"""' || three === "'''") { inTriple = false; i += 3; continue; }
                }
                i++;
                continue;
            }
            const c = line[i];
            if (c === '#') break;
            if (c === '"' || c === "'") {
                if (i + 2 < line.length && line[i + 1] === c && line[i + 2] === c) {
                    inTriple = true; i += 3; continue;
                }
                i++;
                while (i < line.length && line[i] !== c) {
                    if (line[i] === '\\') i++;
                    i++;
                }
                i++;
                continue;
            }
            if (c === '(' || c === '[' || c === '{') depth++;
            else if (c === ')' || c === ']' || c === '}') depth--;
            i++;
        }
        return [depth, inTriple];
    }

    /** 把跨物理行的逻辑行（括号未闭合、三引号）合并 */
    function joinLogicalLines(physical) {
        const result = [];
        let buffer = '';
        let depth = 0;
        let inTriple = false;
        let startLine = 0;

        for (let i = 0; i < physical.length; i++) {
            const line = physical[i];
            if (buffer.length === 0) { startLine = i + 1; buffer = line; }
            else buffer += '\n' + line;

            const r = scanLine(line, depth, inTriple);
            depth = r[0]; inTriple = r[1];

            if (depth <= 0 && !inTriple) {
                result.push({ text: buffer, line: startLine });
                buffer = '';
                depth = 0;
            }
        }
        if (buffer.length > 0) result.push({ text: buffer, line: startLine });
        return result;
    }

    /** 拆出行内注释，返回 [代码, 注释] */
    function splitInlineComment(text) {
        let i = 0, inStr = false, inTriple = false, quote = '';
        while (i < text.length) {
            const c = text[i];
            if (inTriple) {
                if (i + 2 < text.length && text[i + 1] === quote && text[i + 2] === quote) {
                    inTriple = false; i += 3; continue;
                }
                i++; continue;
            }
            if (inStr) {
                if (c === '\\') { i += 2; continue; }
                if (c === quote) inStr = false;
                i++; continue;
            }
            if (c === '"' || c === "'") {
                if (i + 2 < text.length && text[i + 1] === c && text[i + 2] === c) {
                    inTriple = true; quote = c; i += 3; continue;
                }
                inStr = true; quote = c; i++; continue;
            }
            if (c === '#') return [text.slice(0, i), text.slice(i + 1).trim()];
            i++;
        }
        return [text, ''];
    }

    function classifyNode(node, content) {
        node.text = content;
        node.kind = 'simple';

        if (content[0] === '#') {
            node.kind = 'comment';
            node.text = content.replace(/^#+/, '').trim();
            return;
        }
        if (content[0] === '@') { node.kind = 'decorator'; return; }
        if (/^import\s/.test(content) || /^from\s/.test(content)) { node.kind = 'import'; return; }

        const endsWithColon = content.endsWith(':');
        const body = endsWithColon ? content.slice(0, -1).trimEnd() : content;

        if (endsWithColon) {
            if (/^def\s/.test(body) || /^async\s+def\s/.test(body)) {
                node.kind = 'func';
                node.head = body.replace(/^async\s+/, '');
                return;
            }
            if (/^class\s/.test(body)) { node.kind = 'class'; node.head = body; return; }
            if (/^if\s/.test(body)) { node.kind = 'if'; node.head = body.slice(3).trim(); return; }
            if (/^elif\s/.test(body)) { node.kind = 'elif'; node.head = body.slice(5).trim(); return; }
            if (body === 'else') { node.kind = 'else'; return; }
            if (/^for\s/.test(body) || /^async\s+for\s/.test(body)) {
                node.kind = 'for';
                node.head = body.replace(/^async\s+/, '');
                return;
            }
            if (/^while\s/.test(body)) { node.kind = 'while'; node.head = body.slice(6).trim(); return; }
            if (body === 'try') { node.kind = 'try'; return; }
            if (/^except/.test(body)) { node.kind = 'except'; node.head = body.length > 6 ? body.slice(6).trim() : ''; return; }
            if (body === 'finally') { node.kind = 'finally'; return; }
            if (/^with\s/.test(body) || /^async\s+with\s/.test(body)) {
                node.kind = 'with';
                node.head = body.replace(/^async\s+/, '');
                return;
            }
        }

        if (content === 'break') { node.kind = 'break'; return; }
        if (content === 'continue') { node.kind = 'continue'; return; }
        if (content === 'pass') { node.kind = 'pass'; return; }
        if (content === 'return' || /^return\s/.test(content)) { node.kind = 'return'; return; }
    }

    function detectComplexSyntax(source, notes) {
        const lines = source.replace(/\r\n/g, '\n').split('\n');
        for (let i = 0; i < lines.length; i++) {
            const t = lines[i].trim();
            if (/^import\s/.test(t) || /^from\s/.test(t)) continue;
            const ln = i + 1;
            if (t.indexOf('lambda') >= 0) notes.warn(ln, 'lambda 已按匿名函数尽力改写，请核对参数与返回值。');
            if (t.indexOf('yield') >= 0) notes.warn(ln, '生成器语义在目标语言中没有直接对应，需要改写为数组或迭代器。');
            if (/^with\s/.test(t)) notes.warn(ln, 'with 上下文管理器已降级为普通语句，资源释放需要手动处理。');
            if (t.indexOf('**kwargs') >= 0 || (/^def\s/.test(t) && t.indexOf('*args') >= 0)) {
                notes.warn(ln, '可变参数在目标语言中需要显式改写。');
            }
            if (t[0] === '@') notes.warn(ln, '装饰器无法自动移植，已保留为注释。');
            if (t.indexOf('async ') >= 0 || t.indexOf('await ') >= 0) {
                notes.warn(ln, '异步语法仅保留关键字，运行时行为需要按目标语言调整。');
            }
        }
    }

    function parsePython(source, notes) {
        const physical = source.replace(/\r\n/g, '\n').replace(/\r/g, '\n').split('\n');
        const logical = joinLogicalLines(physical);

        const root = mkNode(-1);
        const stack = [root];
        let pendingBlanks = 0;

        function flushBlanks(parent) {
            if (pendingBlanks <= 0) return;
            parent.children.push(mkNode(0));
            parent.children[parent.children.length - 1].kind = 'blank';
            pendingBlanks = 0;
        }

        for (let idx = 0; idx < logical.length; idx++) {
            const item = logical[idx];
            const text = item.text;
            const lineNo = item.line;

            const sp = splitInlineComment(text);
            const code = sp[0];
            const comment = sp[1];
            const trimmed = code.trimEnd();
            const isCommentOnly = trimmed.trim().length === 0 && comment.length > 0;

            if (trimmed.trim().length === 0 && !isCommentOnly) {
                pendingBlanks++;
                continue;
            }

            if (isCommentOnly) {
                const cIndent = countIndent(text);
                while (stack.length > 1 && stack[stack.length - 1].indent >= cIndent) stack.pop();
                flushBlanks(stack[stack.length - 1]);

                const cn = mkNode(cIndent);
                cn.kind = 'comment';
                cn.line = lineNo;
                cn.text = comment;
                stack[stack.length - 1].children.push(cn);
                continue;
            }

            const indent = countIndent(trimmed);
            const content = trimmed.trim();

            const node = mkNode(indent);
            node.line = lineNo;
            node.comment = comment;
            classifyNode(node, content);

            while (stack.length > 1 && stack[stack.length - 1].indent >= indent) stack.pop();
            flushBlanks(stack[stack.length - 1]);
            stack[stack.length - 1].children.push(node);

            if (isBlockStarter(node)) stack.push(node);
        }

        detectComplexSyntax(source, notes);
        return root.children;
    }

    // 供后续模块使用
    const internal = {
        LANGUAGES: LANGUAGES,
        getLanguage: getLanguage,
        guessFromPath: guessFromPath,
        createNotes: createNotes,
        noteText: noteText,
        countLines: countLines,
        pad: pad,
        splitTopLevel: splitTopLevel,
        parsePython: parsePython,
        isBlockStarter: isBlockStarter,
        isContinuation: isContinuation
    };

    // 占位，后续追加具体转换器
    return {
        languages: LANGUAGES,
        getLanguage: getLanguage,
        guessFromPath: guessFromPath,
        countLines: countLines,
        noteText: noteText,
        _internal: internal
    };
});
