/*!
 * HWZ 代码互换工坊 — 转换器（三）C 系语言互相转换 + 统一入口
 * 署名：HWZ工作室
 */
(function (root, factory) {
    if (typeof module === 'object' && module.exports) {
        module.exports = factory(
            require('./engine.js'),
            require('./engine-py2brace.js'),
            require('./engine-brace2py.js')
        );
    } else {
        factory(root.HWZEngine, root.HWZEngine, root.HWZEngine);
    }
})(typeof globalThis !== 'undefined' ? globalThis : this, function (core) {
    'use strict';

    const pad = core._internal.pad;
    const splitTopLevel = core._internal.splitTopLevel;
    const replaceAllText = core._internal.replaceAllText;

    function createBraceToBrace(source, target, notes) {
        let currentClass = '';

        function convert(text) {
            text = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
            const lines = text.split('\n');

            const headerLines = [];
            const bodyLines = [];

            lines.forEach(function (line) {
                const t = line.trim();
                if (t.indexOf('using ') === 0 || t.indexOf('import ') === 0 ||
                    t.indexOf('#include') === 0 || t.indexOf('package ') === 0 ||
                    t.indexOf('"use strict"') === 0) {
                    headerLines.push(line);
                    return;
                }
                bodyLines.push(line);
            });

            const out = [];

            if (target.header) out.push(target.header.trimEnd());

            mapHeader(headerLines).forEach(function (l) { out.push(l); });

            if ((target.header && target.header.trim().length > 0) || headerLines.length > 0) out.push('');

            const translated = bodyLines.map(function (l) { return translateLine(l); });
            const bodyText = translated.join('\n').trimEnd();

            const needsWrapper = (target.id === 'csharp' || target.id === 'java') && !/class\s+\w+/.test(bodyText);

            if (!needsWrapper) {
                out.push(bodyText);
                return out.join('\n').trimEnd() + '\n';
            }

            const split = splitMembers(translated);

            out.push(target.id === 'csharp' ? 'class Program' : 'public class Main');
            out.push('{');

            split.functions.forEach(function (f) {
                f.split('\n').forEach(function (l) {
                    out.push(l.trim().length === 0 ? '' : '    ' + l.trimEnd());
                });
                out.push('');
            });

            out.push(target.id === 'csharp' ? '    static void Main(string[] args)' : '    public static void main(String[] args)');
            out.push('    {');

            split.statements.forEach(function (l) {
                out.push(l.trim().length === 0 ? '' : '        ' + l.trimEnd());
            });

            out.push('    }');
            out.push('}');

            notes.info(0, '源码没有类定义，已自动补充程序入口类，使目标代码可直接编译运行。');

            return out.join('\n').trimEnd() + '\n';
        }

        function mapHeader(source) {
            const result = [];

            source.forEach(function (line) {
                const t = line.trim();

                if (t.indexOf('using System;') === 0 && target.id === 'cpp') return;
                if (t.indexOf('#include <iostream>') === 0 && (target.id === 'csharp' || target.id === 'java')) return;

                if (t.indexOf('using ') === 0) {
                    if (target.id !== 'csharp') return;
                    result.push(t);
                    return;
                }

                if (t.indexOf('import ') === 0) {
                    if (target.id === 'javascript' || target.id === 'typescript') {
                        const mod = t.replace('import', '').trim().replace(/;+$/, '').trim();
                        if (mod.slice(-2) === '.*') return;
                        result.push('// 原 Java 导入：' + mod + '（Node 需安装对应 npm 包）');
                        return;
                    }
                    if (target.id === 'csharp' || target.id === 'cpp') return;
                    result.push(t);
                    return;
                }

                if (t.indexOf('#include') === 0) {
                    if (target.id === 'javascript' || target.id === 'typescript') return;
                    if (target.id === 'csharp' || target.id === 'java') return;
                    result.push(t);
                    return;
                }

                if (t.indexOf('package ') === 0) return;
                if (t.indexOf('"use strict"') === 0) return;

                result.push(t);
            });

            return result.filter(function (l, i) { return result.indexOf(l) === i; });
        }

        function sniffIndent(line) {
            const m = line.match(/^[ \t]*/);
            return m ? m[0] : '';
        }

        function isEntry(t) {
            return /^(?:public\s+)?static\s+void\s+(?:Main|main)\s*\(/.test(t)
                || /^int\s+main\s*\(/.test(t)
                || /^(?:func\s+)?main\s*\(\s*\)\s*\{?$/.test(t);
        }

        function entrySignature() {
            if (target.id === 'csharp') return 'static void Main(string[] args)';
            if (target.id === 'java') return 'public static void main(String[] args)';
            if (target.id === 'cpp') return 'int main()';
            return 'function main()';
        }

        function classDecl(name, baseName) {
            const hasBase = baseName && baseName.length > 0;
            if (target.id === 'javascript' || target.id === 'typescript') {
                return hasBase ? 'class ' + name + ' extends ' + baseName : 'class ' + name;
            }
            if (target.id === 'csharp') return hasBase ? 'class ' + name + ' : ' + baseName : 'class ' + name;
            if (target.id === 'java') return hasBase ? 'public class ' + name + ' extends ' + baseName : 'public class ' + name;
            if (target.id === 'cpp') return hasBase ? 'class ' + name + ' : public ' + baseName : 'class ' + name;
            return 'class ' + name;
        }

        function mapTsType(type) {
            if (!type || type.trim().length === 0) return 'any';
            const t = type.trim();
            if (t === 'number' || t === 'string' || t === 'boolean') return t;
            if (t === 'int' || t === 'double' || t === 'float' || t === 'long' || t === 'decimal') return 'number';
            return 'any';
        }

        function mapParams(rawParams) {
            if (!rawParams || rawParams.trim().length === 0) return '';

            const result = [];

            splitTopLevel(rawParams).forEach(function (raw) {
                const item = raw.trim();
                if (item.length === 0) return;

                const cleaned = item.replace(/^(?:(?:const|final|in|out|ref|params)\s+)+/, '');
                const parts = cleaned.split(/\s+/).filter(function (x) { return x.length > 0; });

                let type = '', name = '';
                if (parts.length >= 2) {
                    type = parts.slice(0, parts.length - 1).join(' ');
                    name = parts[parts.length - 1];
                } else {
                    name = parts.length > 0 ? parts[0] : '';
                }

                if (name.indexOf(':') >= 0) {
                    const seg = name.split(':');
                    name = seg[0].trim();
                    type = seg[1].trim();
                }

                if (name.length === 0) return;

                if (target.id === 'javascript') result.push(name);
                else if (target.id === 'typescript') result.push(name + ': ' + mapTsType(type));
                else if (target.id === 'csharp') result.push('dynamic ' + name);
                else if (target.id === 'java') result.push('Object ' + name);
                else if (target.id === 'cpp') result.push('auto ' + name);
                else result.push(name);
            });

            return result.join(', ');
        }

        function functionSignature(name, rawParams) {
            const ps = mapParams(rawParams);
            if (target.id === 'javascript') return 'function ' + name + '(' + ps + ')';
            if (target.id === 'typescript') return 'function ' + name + '(' + ps + '): any';
            if (target.id === 'csharp') return 'static dynamic ' + name + '(' + ps + ')';
            if (target.id === 'java') return 'public static Object ' + name + '(' + ps + ')';
            if (target.id === 'cpp') return 'auto ' + name + '(' + ps + ')';
            return 'function ' + name + '(' + ps + ')';
        }

        function declarePrefix() {
            if (target.id === 'javascript' || target.id === 'typescript') return 'let';
            if (target.id === 'csharp') return 'var';
            if (target.id === 'java') return 'var';
            if (target.id === 'cpp') return 'auto';
            return 'let';
        }

        function toStr(value) {
            if (target.id === 'javascript' || target.id === 'typescript') return 'String(' + value + ')';
            if (target.id === 'csharp') return value + '.ToString()';
            if (target.id === 'java') return 'String.valueOf(' + value + ')';
            if (target.id === 'cpp') return 'std::to_string(' + value + ')';
            return 'String(' + value + ')';
        }

        function mathCall(fn, args) {
            if (target.id === 'javascript' || target.id === 'typescript') return 'Math.' + fn + '(' + args + ')';
            if (target.id === 'csharp') return 'Math.' + fn.charAt(0).toUpperCase() + fn.slice(1) + '(' + args + ')';
            if (target.id === 'java') return 'Math.' + fn + '(' + args + ')';
            if (target.id === 'cpp') return 'std::' + fn + '(' + args + ')';
            return 'Math.' + fn + '(' + args + ')';
        }

        function convertArrayLiteral(s) {
            if (target.id === 'javascript' || target.id === 'typescript') return s;

            const m = s.match(/^\[([\s\S]*)\]$/);
            if (!m) return s;

            const items = splitTopLevel(m[1]).map(function (x) { return x.trim(); });

            if (target.id === 'csharp') return 'new List<dynamic> { ' + items.join(', ') + ' }';
            if (target.id === 'java') {
                return items.length === 0 ? 'new ArrayList<>()' : 'new ArrayList<>(Arrays.asList(' + items.join(', ') + '))';
            }
            if (target.id === 'cpp') return 'std::vector<int>{ ' + items.join(', ') + ' }';
            return s;
        }

        function rebuildLiteral(literal) {
            if (literal[0] === '`') {
                let body = literal.slice(1, -1);
                if (target.id === 'csharp') {
                    body = body.replace(/\$\{([^{}]+)\}/g, '{$1}').replace(/"/g, '\\"');
                    return '$"' + body + '"';
                }
                if (target.id === 'java' || target.id === 'cpp') {
                    const segments = [];
                    let last = 0;
                    const re = /\$\{([^{}]+)\}/g;
                    let g;
                    while ((g = re.exec(body)) !== null) {
                        const lit = body.slice(last, g.index);
                        if (lit.length > 0) segments.push('"' + lit + '"');
                        segments.push(g[1]);
                        last = g.index + g[0].length;
                    }
                    const tail = body.slice(last);
                    if (tail.length > 0) segments.push('"' + tail + '"');
                    return segments.length === 0 ? '""' : '(' + segments.join(' + ') + ')';
                }
                return literal;
            }

            if (literal.indexOf('$"') === 0) {
                const body = literal.slice(2, -1);
                if (target.id === 'javascript' || target.id === 'typescript') {
                    return '`' + body.replace(/\{([^{}]+)\}/g, '${$1}') + '`';
                }
                if (target.id === 'java' || target.id === 'cpp') {
                    const segments = [];
                    let last = 0;
                    const re = /\{([^{}]+)\}/g;
                    let g;
                    while ((g = re.exec(body)) !== null) {
                        const lit = body.slice(last, g.index);
                        if (lit.length > 0) segments.push('"' + lit + '"');
                        segments.push(g[1]);
                        last = g.index + g[0].length;
                    }
                    const tail = body.slice(last);
                    if (tail.length > 0) segments.push('"' + tail + '"');
                    return segments.length === 0 ? '""' : '(' + segments.join(' + ') + ')';
                }
            }

            return literal;
        }

        function translateExpr(expr) {
            if (!expr || expr.trim().length === 0) return expr;

            let s = expr;
            const literals = [];

            s = s.replace(/"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'|`(?:[^`\\]|\\.)*`/g, function (m) {
                literals.push(m);
                return '\u0001' + (literals.length - 1) + '\u0001';
            });

            if (target.id === 'cpp') s = s.replace(/\bnull\b/g, 'nullptr');
            else s = s.replace(/\bnullptr\b/g, 'null');

            s = s.replace(/([A-Za-z_]\w*(?:\.\w+)*)\.(?:Length|length\(\)|size\(\))/g, function (m, v) {
                if (target.id === 'javascript' || target.id === 'typescript') return v + '.length';
                if (target.id === 'csharp') return v + '.Length';
                if (target.id === 'java') return v + '.length()';
                if (target.id === 'cpp') return v + '.size()';
                return v + '.length';
            });

            s = s.replace(/\.(?:push|push_back)\(/g, function () {
                if (target.id === 'javascript' || target.id === 'typescript') return '.push(';
                if (target.id === 'csharp') return '.Add(';
                if (target.id === 'java') return '.add(';
                if (target.id === 'cpp') return '.push_back(';
                return '.push(';
            });

            s = s.replace(/String\.valueOf\(([^()]+)\)/g, function (m, v) { return toStr(v); });
            s = s.replace(/([A-Za-z_][\w.[\]]*)\.ToString\(\)/g, function (m, v) { return toStr(v); });
            s = s.replace(/String\(([^()]+)\)/g, function (m, v) { return toStr(v); });
            s = s.replace(/std::to_string\(([^()]+)\)/g, function (m, v) { return toStr(v); });

            s = s.replace(/Math\.(?:floor|Floor)\(([^()]+)\)/g, function (m, v) { return mathCall('floor', v); });
            s = s.replace(/std::floor\(([^()]+)\)/g, function (m, v) { return mathCall('floor', v); });
            s = s.replace(/Math\.(?:abs|Abs)\(([^()]+)\)/g, function (m, v) { return mathCall('abs', v); });
            s = s.replace(/std::abs\(([^()]+)\)/g, function (m, v) { return mathCall('abs', v); });
            s = s.replace(/Math\.(?:Pow|pow)\(([^,]+),([^)]+)\)/g, function (m, a, b) { return mathCall('pow', a + ',' + b); });
            s = s.replace(/std::pow\(([^,]+),([^)]+)\)/g, function (m, a, b) { return mathCall('pow', a + ',' + b); });

            s = convertArrayLiteral(s);

            for (let i = 0; i < literals.length; i++) {
                s = s.split('\u0001' + i + '\u0001').join(rebuildLiteral(literals[i]));
            }

            return s;
        }

        function tryRewritePrint(t) {
            let pattern;
            if (source.id === 'javascript' || source.id === 'typescript') pattern = /^console\.(?:log|info)\s*\((.+)\)\s*;?$/;
            else if (source.id === 'csharp') pattern = /^Console\.(?:WriteLine|Write)\s*\((.+)\)\s*;?$/;
            else if (source.id === 'java') pattern = /^System\.out\.println\s*\((.+)\)\s*;?$/;
            else if (source.id === 'cpp') pattern = /^(?:std::)?cout\s*<<\s*(.+?)\s*;?$/;
            else pattern = /^console\.log\s*\((.+)\)\s*;?$/;

            const m = t.match(pattern);
            if (!m) return null;

            const raw = m[1];

            let args;
            if (source.id === 'cpp') {
                args = raw.split('<<')
                    .map(function (x) { return x.trim(); })
                    .filter(function (x) {
                        return x.length > 0 && ['std::endl', 'endl', "'\\n'", '"\\n"'].indexOf(x) < 0;
                    })
                    .map(translateExpr);
            } else {
                args = splitTopLevel(raw).map(function (x) { return translateExpr(x.trim()); });
            }

            return printCall(args);
        }

        function printCall(args) {
            const joined = args.join(', ');

            if (args.length <= 1) {
                if (target.id === 'csharp') return 'Console.WriteLine(' + joined + ');';
                if (target.id === 'java') return 'System.out.println(' + joined + ');';
                if (target.id === 'cpp') return 'std::cout << ' + joined + ' << std::endl;';
                return 'console.log(' + joined + ');';
            }

            if (target.id === 'csharp') return 'Console.WriteLine(string.Join(" ", new object[] { ' + joined + ' }));';
            if (target.id === 'java') return 'System.out.println(' + args.join(' + " " + ') + ');';
            if (target.id === 'cpp') return 'std::cout << ' + args.join(' << " " << ') + ' << std::endl;';
            return 'console.log(' + joined + ');';
        }

        function translateLine(line) {
            const indent = sniffIndent(line);
            const t = line.trim();

            if (t.length === 0) return '';

            if (t.indexOf('//') === 0) return indent + t;

            if (isEntry(t)) {
                let signature = entrySignature();
                if (currentClass.length > 0 && (target.id === 'javascript' || target.id === 'typescript')) {
                    signature = 'main()';
                }
                return indent + signature + (t.slice(-1) === '{' ? ' {' : '');
            }

            const classMatch = t.match(/^(?:public\s+)?class\s+([A-Za-z_]\w*)\s*(?:(?:extends|:|implements)\s*([A-Za-z_]\w*))?\s*\{?$/);
            if (classMatch) {
                currentClass = classMatch[1];
                const opens = t.replace(/\s+$/, '').slice(-1) === '{';
                return indent + classDecl(classMatch[1], classMatch[2] || '') + (opens ? ' {' : '');
            }

            const ctorMatch = t.match(/^(?:public\s+|private\s+|protected\s+)?([A-Za-z_]\w*)\s*\(([^)]*)\)\s*\{?$/);
            if (ctorMatch && currentClass.length > 0 &&
                (ctorMatch[1] === currentClass || ctorMatch[1] === 'constructor')) {
                const ps = mapParams(ctorMatch[2]);
                const opensC = t.slice(-1) === '{';
                let sig;
                if (target.id === 'javascript' || target.id === 'typescript') sig = 'constructor(' + ps + ')';
                else if (target.id === 'csharp') sig = 'public ' + currentClass + '(' + ps + ')';
                else if (target.id === 'java') sig = 'public ' + currentClass + '(' + ps + ')';
                else if (target.id === 'cpp') sig = currentClass + '(' + ps + ')';
                else sig = currentClass + '(' + ps + ')';
                return indent + sig + (opensC ? ' {' : '');
            }

            const fnMatch = t.match(/^(?:(?:public|private|protected|static|async|export|final)\s+)*function\s+([A-Za-z_]\w*)\s*\(([^)]*)\)\s*(?::\s*[\w<>[\]|]+)?\s*\{?$/);
            if (fnMatch) {
                const opensF = t.slice(-1) === '{';
                if (currentClass.length > 0 && (target.id === 'javascript' || target.id === 'typescript')) {
                    return indent + fnMatch[1] + '(' + mapParams(fnMatch[2]) + ')' + (opensF ? ' {' : '');
                }
                return indent + functionSignature(fnMatch[1], fnMatch[2]) + (opensF ? ' {' : '');
            }

            const typedFn = t.match(/^(?:(?:public|private|protected|static|final)\s+)+[\w<>[\]]+\s+([A-Za-z_]\w*)\s*\(([^)]*)\)\s*\{?$/);
            if (typedFn && typedFn[1] !== currentClass) {
                const opensT = t.slice(-1) === '{';
                if (currentClass.length > 0 && (target.id === 'javascript' || target.id === 'typescript')) {
                    return indent + typedFn[1] + '(' + mapParams(typedFn[2]) + ')' + (opensT ? ' {' : '');
                }
                return indent + functionSignature(typedFn[1], typedFn[2]) + (opensT ? ' {' : '');
            }

            const print = tryRewritePrint(t);
            if (print !== null) return indent + print;

            if ((target.id === 'javascript' || target.id === 'typescript') &&
                /^for\s*\(\s*(?:int|var|let|long|short|double|float)\s+/.test(t)) {
                return indent + t.replace(/^(for\s*\(\s*)(?:int|var|let|long|short|double|float)\s+/, '$1let ');
            }

            if (target.id === 'javascript' || target.id === 'typescript') {
                // 去掉 C 系类型前缀，改为 let 声明
                if (/^(?:[A-Za-z_][\w.<>[\]]*)\s+[A-Za-z_]\w*\s*=(?!=)/.test(t)) {
                    return indent + 'let ' + t.replace(/^[A-Za-z_][\w.<>[\]]*\s+/, '');
                }
            }

            const decl = t.match(/^(let|const|var|auto|final)\s+([\s\S]+)$/);
            if (decl) return indent + declarePrefix() + ' ' + translateExpr(decl[2]);

            return indent + translateExpr(t);
        }

        function splitMembers(lines) {
            const functions = [];
            const statements = [];
            let i = 0;

            while (i < lines.length) {
                const t = lines[i].trim();
                const isFunctionStart = t.slice(-1) === '{' &&
                    /^(?:(?:public|private|protected|static|final|inline|extern)\s+)*(?:dynamic|auto|Object|void|int|string|double|bool)\s+\w+\s*\(/.test(t);

                if (isFunctionStart) {
                    const block = [];
                    let dep = 0;
                    while (i < lines.length) {
                        const l = lines[i];
                        block.push(l);
                        dep += (l.split('{').length - 1) - (l.split('}').length - 1);
                        i++;
                        if (dep <= 0) break;
                    }
                    functions.push(block.join('\n'));
                    continue;
                }

                statements.push(lines[i]);
                i++;
            }

            return { functions: functions, statements: statements };
        }

        return { convert: convert };
    }

    core.converters = core.converters || {};
    core.converters.braceToBrace = createBraceToBrace;

    // ═══════════════════════════════════════════════════════
    // 统一入口
    // ═══════════════════════════════════════════════════════

    function convertCode(sourceText, fromId, toId) {
        const started = (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now();

        const from = core.getLanguage(fromId);
        const to = core.getLanguage(toId);

        function elapsed() {
            const now = (typeof performance !== 'undefined' && performance.now) ? performance.now() : Date.now();
            return Math.round(now - started);
        }

        if (!sourceText || sourceText.trim().length === 0) {
            return {
                success: false, code: '', message: '请先输入代码，或把代码文件拖入窗口。',
                notes: [], sourceLines: 0, targetLines: 0, elapsedMs: elapsed(), warnCount: 0
            };
        }

        if (from.id === to.id) {
            return {
                success: true,
                code: sourceText,
                message: '源语言与目标语言相同，内容原样返回。',
                notes: [],
                sourceLines: core.countLines(sourceText),
                targetLines: core.countLines(sourceText),
                elapsedMs: elapsed(),
                warnCount: 0
            };
        }

        const notes = core._internal.createNotes();

        try {
            let code;

            if (from.id === 'python') {
                const nodes = core._internal.parsePython(sourceText, notes);
                code = core.converters.pythonToBrace(to, notes).convert(nodes);
            } else if (to.id === 'python') {
                code = core.converters.braceToPython(from, notes).convert(sourceText);
            } else {
                code = core.converters.braceToBrace(from, to, notes).convert(sourceText);
            }

            if (!code || code.trim().length === 0) {
                return {
                    success: false, code: '', message: '转换结果为空，可能是源文件结构与目标语言差异过大，请检查输入内容。',
                    notes: notes.list, sourceLines: core.countLines(sourceText), targetLines: 0,
                    elapsedMs: elapsed(), warnCount: 0
                };
            }

            return {
                success: true,
                code: code,
                message: '转换完成：' + from.name + ' → ' + to.name,
                notes: notes.list,
                sourceLines: core.countLines(sourceText),
                targetLines: core.countLines(code),
                elapsedMs: elapsed(),
                warnCount: notes.list.filter(function (n) { return n.level === 'warn'; }).length
            };
        } catch (err) {
            return {
                success: false, code: '', message: '转换过程中出现异常：' + (err && err.message ? err.message : String(err)),
                notes: notes.list, sourceLines: core.countLines(sourceText), targetLines: 0,
                elapsedMs: elapsed(), warnCount: 0
            };
        }
    }

    core.convert = convertCode;

    return core;
});
