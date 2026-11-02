/*!
 * HWZ 代码互换工坊 — 转换器（一）Python → JS / TS / C# / Java / C++
 * 署名：HWZ工作室
 */
(function (root, factory) {
    if (typeof module === 'object' && module.exports) {
        module.exports = factory(require('./engine.js'));
    } else {
        factory(root.HWZEngine);
    }
})(typeof globalThis !== 'undefined' ? globalThis : this, function (core) {
    'use strict';

    const pad = core._internal.pad;
    const splitTopLevel = core._internal.splitTopLevel;
    const parsePython = core._internal.parsePython;
    const isContinuation = core._internal.isContinuation;

    function replaceAllText(s, find, repl) {
        return s.split(find).join(repl);
    }

    // ═══════════════════════════════════════════════════════
    // Python → 花括号语言
    // ═══════════════════════════════════════════════════════

    function createPythonToBrace(target, notes) {
        let currentClass = '';
        const scopes = [new Set()];

        function convert(nodes) {
            const out = [];
            const imports = nodes.filter(function (n) { return n.kind === 'import'; });
            const body = nodes.filter(function (n) { return n.kind !== 'import'; });

            const header = buildHeader(imports);
            if (header.trim().length > 0) {
                out.push(header.trimEnd());
                out.push('');
            }

            const needsMain = target.id === 'csharp' || target.id === 'java' || target.id === 'cpp';

            // 位于文件开头的注释属于"文件说明"，应留在顶部，而不是被塞进入口方法里
            const leading = [];
            let k = 0;
            while (k < body.length && (body[k].kind === 'comment' || body[k].kind === 'blank')) {
                if (body[k].kind === 'comment') leading.push(body[k]);
                k++;
            }
            const bodyRest = body.slice(k);

            if (!needsMain) {
                renderExecutables(body, 0, out);
                return out.join('\n').trimEnd() + '\n';
            }

            const declarations = bodyRest.filter(function (n) { return n.kind === 'class' || n.kind === 'func'; });
            const executables = bodyRest.filter(function (n) { return n.kind !== 'class' && n.kind !== 'func'; });
            const hasExec = executables.some(function (n) {
                return ['comment', 'decorator', 'import', 'blank'].indexOf(n.kind) < 0;
            });

            if (leading.length > 0) {
                leading.forEach(function (c) { out.push('// ' + c.text); });
                out.push('');
            }

            const topClasses = declarations.filter(function (n) { return n.kind === 'class'; });
            const topFuncs = declarations.filter(function (n) { return n.kind === 'func'; });

            topClasses.forEach(function (cls) {
                renderClass(cls, 0, out);
                out.push('');
            });

            const container = target.id === 'csharp' ? 'Program' : (target.id === 'java' ? 'Main' : '');

            if (target.id === 'cpp') {
                topFuncs.forEach(function (f) {
                    renderFunction(f, 0, out, false);
                    out.push('');
                });

                if (hasExec) {
                    out.push('int main() {');
                    renderExecutables(executables, 1, out);
                    out.push(pad(1) + 'return 0;');
                    out.push('}');
                }                return out.join('\n').trimEnd() + '\n';
            }

            out.push((target.id === 'java' ? 'public ' : '') + 'class ' + container + ' {');

            topFuncs.forEach(function (f) {
                renderFunction(f, 1, out, true);
                out.push('');
            });

            if (hasExec) {
                out.push(pad(1) + (target.id === 'csharp' ? 'static void Main(string[] args)' : 'public static void main(String[] args)') + ' {');
                renderExecutables(executables, 2, out);
                out.push(pad(1) + '}');
            }

            out.push('}');
            return out.join('\n').trimEnd() + '\n';
        }

        function buildHeader(imports) {
            const lines = [];

            imports.forEach(function (imp) {
                const text = imp.text.trim();
                const module = text.indexOf('import ') === 0
                    ? text.slice(7).split(',')[0].trim().split('.')[0]
                    : text.slice(5).split(' ')[0].split('.')[0];

                if (target.id === 'javascript' || target.id === 'typescript') {
                    if (['os', 'sys', 'json', 're', 'time', 'datetime'].indexOf(module) >= 0) {
                        lines.push("const " + module + " = require('" + module + "');");
                        notes.info(imp.line, '`' + module + '` 属于 Python 标准库，Node 对应模块需先 npm 安装并核对 API。');
                    } else if (module === 'math') {
                        lines.push('// Python math 模块：JavaScript 使用内置 Math 对象');
                    } else {
                        notes.warn(imp.line, '第三方模块 `' + module + '` 需替换为对应的 npm 包。');
                    }
                } else if (target.id === 'csharp') {
                    if (module === 'math') lines.push('// Python math 模块：C# 使用 System.Math');
                    else if (module === 'os' || module === 'sys') lines.push('// Python ' + module + '：C# 使用 System.IO / Environment');
                    else notes.warn(imp.line, '模块 `' + module + '` 在 C# 中没有内置对应项，需要引入对应 NuGet 包。');
                } else if (target.id === 'java') {
                    if (module === 'math') lines.push('// Python math 模块：Java 使用 java.lang.Math');
                    else if (module === 'os' || module === 'sys') lines.push('// Python ' + module + '：Java 使用 java.lang.System');
                    else notes.warn(imp.line, '模块 `' + module + '` 在 Java 中没有内置对应项。');
                } else if (target.id === 'cpp') {
                    if (module === 'math') lines.push('#include <cmath>');
                    else if (module === 'iostream') lines.push('#include <iostream>');
                    else notes.warn(imp.line, '模块 `' + module + '` 在 C++ 中需要对应的头文件或第三方库。');
                }
            });

            const out = [];
            if (target.header) out.push(target.header.trimEnd());
            const unique = lines.filter(function (l, i) { return lines.indexOf(l) === i; });
            unique.forEach(function (l) { out.push(l); });
            return out.join('\n');
        }

        function renderExecutables(nodes, level, out) {
            const flat = [];
            nodes.forEach(function (n) {
                if (n.kind === 'if' && n.head.replace(/\s/g, '').indexOf('__name__') >= 0) {
                    n.children.forEach(function (c) { flat.push(c); });
                } else {
                    flat.push(n);
                }
            });
            renderNodes(flat, level, out);
        }

        function renderNodes(nodes, level, out) {
            let i = 0;
            let lastBlank = false;

            while (i < nodes.length) {
                const n = nodes[i];

                if (n.kind === 'blank') {
                    if (!lastBlank && out.length > 0) out.push('');
                    lastBlank = true;
                    i++;
                    continue;
                }
                lastBlank = false;

                switch (n.kind) {
                    case 'comment':
                        out.push(pad(level) + '// ' + n.text);
                        i++;
                        break;

                    case 'import':
                        i++;
                        break;

                    case 'decorator':
                        out.push(pad(level) + '// 装饰器 @' + n.text.replace(/^@/, ''));
                        notes.info(n.line, '装饰器无法自动移植，已保留为注释。');
                        i++;
                        break;

                    case 'func':
                        renderFunction(n, level, out, level > 0);
                        i++;
                        break;

                    case 'class':
                        renderClass(n, level, out);
                        i++;
                        break;

                    case 'if':
                        i = renderIfChain(nodes, i, level, out);
                        break;

                    case 'for':
                        renderFor(n, level, out);
                        i = skipContinuations(nodes, i + 1);
                        break;

                    case 'while':
                        out.push(pad(level) + 'while (' + expr(n.head, n.line) + ') {');
                        renderNodes(n.children, level + 1, out);
                        out.push(pad(level) + '}');
                        i++;
                        break;

                    case 'try':
                        i = renderTryChain(nodes, i, level, out);
                        break;

                    case 'with':
                        out.push(pad(level) + '{');
                        out.push(pad(level + 1) + '// 原 Python with 语句：资源需在此处手动创建与释放');
                        out.push(pad(level + 1) + '// ' + expr(n.head, n.line));
                        renderNodes(n.children, level + 1, out);
                        out.push(pad(level) + '}');
                        notes.warn(n.line, 'with 上下文管理器没有跨语言等价形式，已转换为代码块并保留注释。');
                        i++;
                        break;

                    case 'return': {
                        const ret = n.text.length > 6 ? n.text.slice(6).trim() : '';
                        out.push(pad(level) + (ret.length === 0 ? 'return;' : 'return ' + expr(ret, n.line) + ';'));
                        i++;
                        break;
                    }

                    case 'break':
                        out.push(pad(level) + 'break;');
                        i++;
                        break;

                    case 'continue':
                        out.push(pad(level) + 'continue;');
                        i++;
                        break;

                    case 'pass':
                        out.push(pad(level) + '{ }');
                        i++;
                        break;

                    default:
                        renderSimple(n, level, out);
                        i++;
                        break;
                }
            }
        }

        function skipContinuations(nodes, index) {
            while (index < nodes.length && isContinuation(nodes[index])) index++;
            return index;
        }

        function renderIfChain(nodes, index, level, out) {
            const first = nodes[index];
            out.push(pad(level) + 'if (' + expr(first.head, first.line) + ') {');
            renderNodes(first.children, level + 1, out);

            let i = index + 1;
            while (i < nodes.length && isContinuation(nodes[i])) {
                const c = nodes[i];
                if (c.kind === 'elif') {
                    out.push(pad(level) + '} else if (' + expr(c.head, c.line) + ') {');
                } else if (c.kind === 'else') {
                    out.push(pad(level) + '} else {');
                } else break;

                renderNodes(c.children, level + 1, out);
                i++;
            }

            out.push(pad(level) + '}');
            return i;
        }

        function defaultCatchVar() {
            return (target.id === 'javascript' || target.id === 'typescript' || target.id === 'cpp') ? 'e' : 'ex';
        }

        function parseExcept(head) {
            if (!head || head.trim().length === 0) return ['', defaultCatchVar()];
            const parts = head.split(' as ').map(function (s) { return s.trim(); });
            let type = parts[0].trim();
            const name = parts.length > 1 ? parts[1].trim() : defaultCatchVar();
            type = type.replace(/^\(/, '').replace(/\)$/, '');
            if (type.indexOf(',') >= 0) type = type.split(',')[0].trim();
            return [type, name];
        }

        function catchClause(type, name) {
            switch (target.id) {
                case 'javascript':
                case 'typescript': return 'catch (' + name + ')';
                case 'csharp': return 'catch (Exception ' + name + ')';
                case 'java': return 'catch (Exception ' + name + ')';
                case 'cpp': return 'catch (const std::exception& ' + name + ')';
                default: return 'catch (' + name + ')';
            }
        }

        function renderTryChain(nodes, index, level, out) {
            const first = nodes[index];
            out.push(pad(level) + 'try {');
            renderNodes(first.children, level + 1, out);

            if (target.id === 'csharp' || target.id === 'java') {
                const declares = first.children.some(function (c) { return c.kind === 'simple' && /^[A-Za-z_]\w*\s*=(?!=)/.test(c.text.trim()); });
                if (declares) {
                    notes.warn(first.line, 'try 块内声明的变量在 C#/Java 中作用域仅限该块，若后续继续使用，请把这些声明提到 try 之前。');
                }
            }

            let i = index + 1;
            let hasCatch = false;

            while (i < nodes.length && (nodes[i].kind === 'except' || nodes[i].kind === 'finally')) {
                const c = nodes[i];

                if (c.kind === 'except') {
                    hasCatch = true;
                    const parsed = parseExcept(c.head);
                    out.push(pad(level) + '} ' + catchClause(parsed[0], parsed[1]) + ' {');
                    if (parsed[0]) {
                        notes.warn(c.line, '异常类型 `' + parsed[0] + '` 已映射为通用异常，请按目标语言调整捕获类型。');
                    }
                } else {
                    out.push(pad(level) + '} finally {');
                }

                renderNodes(c.children, level + 1, out);
                i++;
            }

            if (!hasCatch && (target.id === 'csharp' || target.id === 'java')) {
                out.push(pad(level) + '} finally {');
                out.push(pad(level) + '}');
            } else {
                out.push(pad(level) + '}');
            }

            return i;
        }

        function parseParams(rawParams) {
            const result = [];
            if (!rawParams || rawParams.trim().length === 0) return result;

            splitTopLevel(rawParams).forEach(function (part) {
                let p = part.trim();
                if (p.length === 0) return;
                if (p === 'self' || p === 'cls') return;

                const eq = p.indexOf('=');
                if (eq > 0) {
                    const pname = p.slice(0, eq).trim();
                    const def = expr(p.slice(eq + 1), 0);
                    result.push(pname + ' = ' + def);
                } else if (p[0] === '*') {
                    p = p.replace(/^\*+/, '');
                    result.push(p);
                    notes.info(0, '可变参数已按普通参数处理，调用处需要调整。');
                } else {
                    result.push(p);
                }
            });

            return result;
        }

        function renderFunction(node, level, out, isStatic) {
            const m = node.head.match(/^def\s+([A-Za-z_]\w*)\s*\(([\s\S]*)\)\s*$/);
            if (!m) {
                out.push(pad(level) + '// 无法解析的函数定义：' + node.head);
                notes.warn(node.line, '函数定义格式无法识别，已保留为注释。');
                return;
            }

            const name = m[1];
            const paramList = parseParams(m[2]);
            const isConstructor = name === '__init__';

            scopes.push(new Set());

            let signature;

            if (isConstructor) {
                const ctorName = currentClass || 'Constructor';
                if (target.id === 'javascript') {
                    signature = 'constructor(' + paramList.join(', ') + ')';
                } else if (target.id === 'typescript') {
                    signature = 'constructor(' + paramList.map(function (p) { return p + ': any'; }).join(', ') + ')';
                } else if (target.id === 'csharp') {
                    signature = 'public ' + ctorName + '(' + paramList.map(function (p) { return 'dynamic ' + p; }).join(', ') + ')';
                } else if (target.id === 'java') {
                    signature = 'public ' + ctorName + '(' + paramList.map(function (p) { return 'Object ' + p; }).join(', ') + ')';
                } else if (target.id === 'cpp') {
                    signature = ctorName + '(' + paramList.map(function (p) { return 'auto ' + p; }).join(', ') + ')';
                } else {
                    signature = ctorName + '(' + paramList.join(', ') + ')';
                }
            } else {
                if (target.id === 'javascript') {
                    signature = 'function ' + name + '(' + paramList.join(', ') + ')';
                } else if (target.id === 'typescript') {
                    signature = 'function ' + name + '(' + paramList.map(function (p) { return p + ': any'; }).join(', ') + '): any';
                } else if (target.id === 'csharp') {
                    signature = (isStatic ? 'static ' : '') + 'dynamic ' + name + '(' + paramList.map(function (p) { return 'dynamic ' + p; }).join(', ') + ')';
                } else if (target.id === 'java') {
                    signature = (isStatic ? 'static ' : 'public static ') + 'Object ' + name + '(' + paramList.map(function (p) { return 'Object ' + p; }).join(', ') + ')';
                } else if (target.id === 'cpp') {
                    signature = 'auto ' + name + '(' + paramList.map(function (p) { return 'auto ' + p; }).join(', ') + ')';
                } else {
                    signature = name + '(' + paramList.join(', ') + ')';
                }

                if (target.id === 'csharp' || target.id === 'java') {
                    notes.info(node.line, '函数 `' + name + '` 的参数与返回值使用了动态类型占位，建议改为具体类型。');
                }
            }

            out.push(pad(level) + signature + ' {');

            const body = [];
            renderNodes(node.children, level + 1, body);
            const bodyText = body.join('\n').trimEnd();
            out.push(bodyText.length > 0 ? bodyText : pad(level + 1) + '// 空函数体');

            out.push(pad(level) + '}');
            scopes.pop();
        }

        function collectSelfFields(nodes) {
            const found = [];
            function walk(list) {
                list.forEach(function (n) {
                    if (n.kind === 'simple') {
                        const m = n.text.match(/^self\.([A-Za-z_]\w*)\s*=(?!=)/);
                        if (m && found.indexOf(m[1]) < 0) found.push(m[1]);
                    }
                    walk(n.children);
                });
            }
            walk(nodes);
            return found;
        }

        function renderClass(node, level, out) {
            const m = node.head.match(/^class\s+([A-Za-z_]\w*)\s*(?:\(([\s\S]*)\))?\s*$/);
            if (!m) {
                out.push(pad(level) + '// 无法解析的类定义：' + node.head);
                return;
            }

            const name = m[1];
            let bases = [];
            if (m[2] && m[2].trim().length > 0) {
                bases = m[2].split(',').map(function (x) { return x.trim(); })
                    .filter(function (x) { return x !== 'object' && x !== ''; });
            }

            let decl;
            switch (target.id) {
                case 'javascript':
                case 'typescript':
                    decl = 'class ' + name + (bases.length > 0 ? ' extends ' + bases[0] : '');
                    break;
                case 'csharp':
                    decl = 'class ' + name + (bases.length > 0 ? ' : ' + bases[0] : '');
                    break;
                case 'java':
                    decl = 'class ' + name + (bases.length > 0 ? ' extends ' + bases[0] : '');
                    break;
                case 'cpp':
                    decl = 'class ' + name + (bases.length > 0 ? ' : public ' + bases[0] : '');
                    break;
                default:
                    decl = 'class ' + name;
            }

            out.push(pad(level) + decl + ' {');

            const previous = currentClass;
            currentClass = name;

            const members = [];

            if (target.id === 'csharp' || target.id === 'java') {
                const fields = collectSelfFields(node.children);
                if (fields.length > 0) {
                    fields.forEach(function (f) {
                        members.push(pad(level + 1) + (target.id === 'csharp' ? 'public dynamic ' + f + ';' : 'public Object ' + f + ';'));
                    });
                    members.push('');
                    notes.info(node.line, '类 `' + name + '` 的实例字段已按动态类型自动补全声明，建议改为具体类型。');
                }
            }

            renderNodes(node.children, level + 1, members);

            const membersText = members.join('\n').trimEnd();
            if (membersText.length > 0) out.push(membersText);

            out.push(pad(level) + '}');
            currentClass = previous;
        }

        function findTopLevelIn(text) {
            let depth = 0;
            for (let i = 0; i < text.length - 3; i++) {
                const c = text[i];
                if (c === '(' || c === '[' || c === '{') depth++;
                else if (c === ')' || c === ']' || c === '}') depth--;
                else if (depth === 0 && text.substr(i, 4) === ' in ') return i;
            }
            return -1;
        }

        function renderFor(node, level, out) {
            const rest = node.head.length > 3 ? node.head.slice(3).trim() : '';
            const inIdx = findTopLevelIn(rest);

            if (inIdx < 0) {
                out.push(pad(level) + '// 无法解析的 for 语句：' + node.head);
                notes.warn(node.line, 'for 语句格式无法识别，已保留为注释。');
                return;
            }

            let targetVar = rest.slice(0, inIdx).trim().replace(/^\(/, '').replace(/\)$/, '');
            const iter = rest.slice(inIdx + 4).trim();

            const rangeMatch = iter.match(/^range\(([\s\S]*)\)$/);
            if (rangeMatch) {
                const args = splitTopLevel(rangeMatch[1]).map(function (a) { return expr(a.trim(), node.line); });

                let start = '0', end = '0', step = '1';
                if (args.length === 1) { end = args[0]; }
                else if (args.length === 2) { start = args[0]; end = args[1]; }
                else if (args.length >= 3) { start = args[0]; end = args[1]; step = args[2]; }

                const cmp = step[0] === '-' ? '>' : '<';
                const increment = step === '1' ? targetVar + '++' : targetVar + ' += ' + step;

                let forLine;
                if (target.id === 'javascript' || target.id === 'typescript') {
                    forLine = 'for (let ' + targetVar + ' = ' + start + '; ' + targetVar + ' ' + cmp + ' ' + end + '; ' + increment + ')';
                } else if (target.id === 'cpp') {
                    forLine = 'for (int ' + targetVar + ' = ' + start + '; ' + targetVar + ' ' + cmp + ' ' + end + '; ' + increment + ')';
                } else {
                    forLine = 'for (int ' + targetVar + ' = ' + start + '; ' + targetVar + ' ' + cmp + ' ' + end + '; ' + increment + ')';
                }

                out.push(pad(level) + forLine + ' {');
                renderNodes(node.children, level + 1, out);
                out.push(pad(level) + '}');
                return;
            }

            if (iter.indexOf(',') >= 0 && iter.indexOf('(') < 0) {
                targetVar = targetVar.replace(/,/g, ', ');
                notes.warn(node.line, '多重赋值循环已简化，请核对循环变量。');
            }

            const iterExpr = expr(iter, node.line);

            let each;
            if (target.id === 'javascript' || target.id === 'typescript') {
                each = 'for (const ' + targetVar + ' of ' + iterExpr + ')';
            } else if (target.id === 'csharp') {
                each = 'foreach (var ' + targetVar + ' in ' + iterExpr + ')';
            } else if (target.id === 'java') {
                each = 'for (var ' + targetVar + ' : ' + iterExpr + ')';
            } else if (target.id === 'cpp') {
                each = 'for (auto ' + targetVar + ' : ' + iterExpr + ')';
            } else {
                each = 'foreach (var ' + targetVar + ' in ' + iterExpr + ')';
            }

            out.push(pad(level) + each + ' {');
            renderNodes(node.children, level + 1, out);
            out.push(pad(level) + '}');
        }

        function declareVar(name, rawValue, translated) {
            const isStringLiteral = /^["']/.test(rawValue);
            const isListLiteral = rawValue[0] === '[' && rawValue[rawValue.length - 1] === ']';

            switch (target.id) {
                case 'javascript': return 'let ' + name + ' = ' + translated;
                case 'typescript': return isListLiteral ? 'let ' + name + ': any[] = ' + translated : 'let ' + name + ' = ' + translated;
                case 'csharp': return 'var ' + name + ' = ' + translated;
                case 'java': return isStringLiteral ? 'String ' + name + ' = ' + translated : 'var ' + name + ' = ' + translated;
                case 'cpp': return isStringLiteral ? 'std::string ' + name + ' = ' + translated : 'auto ' + name + ' = ' + translated;
                default: return 'let ' + name + ' = ' + translated;
            }
        }

        function buildPrint(args) {
            if (args.length === 0) {
                if (target.id === 'csharp') return 'Console.WriteLine();';
                if (target.id === 'java') return 'System.out.println();';
                if (target.id === 'cpp') return 'std::cout << std::endl;';
                return 'console.log();';
            }

            const joined = args.join(', ');

            if (args.length === 1) {
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

        function renderSimple(node, level, out) {
            const text = node.text.trim();

            const printMatch = text.match(/^print\s*\(([\s\S]*)\)$/);
            if (printMatch) {
                const inner = printMatch[1];
                if (inner.indexOf('end=') >= 0 || inner.indexOf('sep=') >= 0) {
                    notes.warn(node.line, 'print 的 sep / end 参数在目标语言中没有等价写法，输出分隔与换行行为已简化。');
                }

                const args = splitTopLevel(inner)
                    .map(function (a) { return a.trim(); })
                    .filter(function (a) { return a.length > 0 && a.indexOf('sep=') !== 0 && a.indexOf('end=') !== 0; })
                    .map(function (a) { return expr(a, node.line); });

                out.push(pad(level) + buildPrint(args));
                return;
            }

            if (text.indexOf('input(') === 0) {
                const pm = text.match(/^input\s*\(([\s\S]*)\)$/);
                const promptText = pm && pm[1].trim().length > 0 ? pm[1].trim() : null;

                let read;
                if (target.id === 'csharp') {
                    read = promptText
                        ? 'Console.Write(' + expr(promptText, node.line) + '); var _input = Console.ReadLine();'
                        : 'var _input = Console.ReadLine();';
                } else if (target.id === 'java') {
                    read = promptText
                        ? 'System.out.print(' + expr(promptText, node.line) + '); var _scanner = new Scanner(System.in); var _input = _scanner.nextLine();'
                        : 'var _scanner = new Scanner(System.in); var _input = _scanner.nextLine();';
                } else if (target.id === 'javascript') {
                    read = '// Node 环境无内置输入：请引入 readline 模块\nconst _input = null;';
                } else if (target.id === 'typescript') {
                    read = '// Node 环境无内置输入：请引入 readline 模块\nconst _input: string | null = null;';
                } else if (target.id === 'cpp') {
                    read = promptText
                        ? 'std::cout << ' + expr(promptText, node.line) + '; std::string _input; std::getline(std::cin, _input);'
                        : 'std::string _input; std::getline(std::cin, _input);';
                } else {
                    read = 'var _input = null;';
                }

                read.split('\n').forEach(function (line) { out.push(pad(level) + line); });

                if (target.id === 'javascript' || target.id === 'typescript') {
                    notes.warn(node.line, 'input() 在 Node.js 中没有等价实现，需要手动引入 readline 或 prompt 库。');
                }
                return;
            }

            const assign = text.match(/^([A-Za-z_]\w*)\s*(=(?!=)|\+=|-=|\*=|\/=|%=)\s*([\s\S]+)$/);
            if (assign) {
                const name = assign[1];
                const op = assign[2];
                const value = assign[3].trim();
                const translated = expr(value, node.line);

                const scope = scopes[scopes.length - 1];
                const isNew = op === '=' && !scope.has(name);

                if (isNew) {
                    scope.add(name);
                    out.push(pad(level) + declareVar(name, value, translated) + ';');
                } else {
                    out.push(pad(level) + name + ' ' + op + ' ' + translated + ';');
                }
                return;
            }

            out.push(pad(level) + expr(text, node.line) + ';');
        }

        function normalizeLiteral(literal) {
            if (literal.length < 2) return literal;
            if (literal[0] === "'") {
                let inner = literal.slice(1, -1);
                inner = replaceAllText(inner, "\\'", "'");
                inner = replaceAllText(inner, '"', '\\"');
                return '"' + inner + '"';
            }
            return literal;
        }

        function expr(py, line) {
            if (!py || py.trim().length === 0) return py;

            let s = py;
            const fstrings = [];
            const literals = [];

            s = s.replace(/\bf("|')((?:\\[\s\S]|(?!\1)[\s\S])*)\1/g, function (m, q, body) {
                fstrings.push(buildFString(body, line));
                return '\u0002' + (fstrings.length - 1) + '\u0002';
            });

            s = s.replace(/'(?:[^'\\]|\\.)*'|"(?:[^"\\]|\\.)*"/g, function (m) {
                literals.push(m);
                return '\u0001' + (literals.length - 1) + '\u0001';
            });

            s = replaceKeywords(s);
            s = replaceBuiltins(s, line);

            for (let i = 0; i < literals.length; i++) {
                s = s.split('\u0001' + i + '\u0001').join(normalizeLiteral(literals[i]));
            }
            for (let i = 0; i < fstrings.length; i++) {
                s = s.split('\u0002' + i + '\u0002').join(fstrings[i]);
            }

            return s;
        }

        function replaceKeywords(s) {
            s = s.replace(/\bTrue\b/g, 'true');
            s = s.replace(/\bFalse\b/g, 'false');
            s = s.replace(/\bNone\b/g, target.id === 'cpp' ? 'nullptr' : 'null');
            s = s.replace(/\bself\./g, target.id === 'cpp' ? 'this->' : 'this.');
            s = s.replace(/\bnot\s+/g, '!');
            s = s.replace(/\s+and\s+/g, ' && ');
            s = s.replace(/\s+or\s+/g, ' || ');
            s = replaceAllText(s, ' is not ', ' != ');
            s = replaceAllText(s, ' is ', ' == ');
            s = s.replace(/(\w)\s*\/\/\s*(\w)/g, '$1 / $2');

            if (target.id === 'cpp') s = s.replace(/\bnull\b/g, 'nullptr');
            return s;
        }

        function replaceBuiltins(s, line) {
            // input(...)：必须先于 int()/str() 处理，否则嵌套调用无法识别
            s = s.replace(/\binput\(\s*(?:[^()]*)\s*\)/g, function () {
                if (target.id === 'csharp') return 'Console.ReadLine()';
                if (target.id === 'java') return 'new Scanner(System.in).nextLine()';
                if (target.id === 'cpp') return 'std::string()';
                return 'null';
            });

            s = s.replace(/\blen\(([A-Za-z_]\w*)\)/g, function (m, v) {
                if (target.id === 'csharp') return v + '.Count';
                if (target.id === 'java') return v + '.size()';
                if (target.id === 'cpp') return v + '.size()';
                return v + '.length';
            });

            s = s.replace(/\bstr\(([^()]+)\)/g, function (m, v) {
                if (target.id === 'csharp') return v + '.ToString()';
                if (target.id === 'java') return 'String.valueOf(' + v + ')';
                if (target.id === 'cpp') return 'std::to_string(' + v + ')';
                return 'String(' + v + ')';
            });

            s = s.replace(/\bint\(([^()]+)\)/g, function (m, v) {
                if (target.id === 'csharp') return 'int.Parse(' + v + '.ToString())';
                if (target.id === 'java') return 'Integer.parseInt(' + v + '.ToString())';
                if (target.id === 'cpp') return 'std::stoi(' + v + ')';
                return 'parseInt(' + v + ')';
            });

            s = s.replace(/\bfloat\(([^()]+)\)/g, function (m, v) {
                if (target.id === 'csharp') return 'double.Parse(' + v + '.ToString())';
                if (target.id === 'java') return 'Double.parseDouble(' + v + '.ToString())';
                if (target.id === 'cpp') return 'std::stod(' + v + ')';
                return 'parseFloat(' + v + ')';
            });

            s = s.replace(/\babs\(([^()]+)\)/g, function (m, v) {
                if (target.id === 'csharp') return 'Math.Abs(' + v + ')';
                if (target.id === 'java') return 'Math.abs(' + v + ')';
                if (target.id === 'cpp') return 'std::abs(' + v + ')';
                return 'Math.abs(' + v + ')';
            });

            s = s.replace(/([A-Za-z_]\w*)\s*\*\*\s*([A-Za-z_]\w*)/g, function (m, a, b) {
                if (target.id === 'csharp') return 'Math.Pow(' + a + ', ' + b + ')';
                if (target.id === 'java') return 'Math.pow(' + a + ', ' + b + ')';
                if (target.id === 'cpp') return 'std::pow(' + a + ', ' + b + ')';
                return a + ' ** ' + b;
            });

            s = s.replace(/\.append\(/g, function () {
                if (target.id === 'csharp') return '.Add(';
                if (target.id === 'java') return '.add(';
                if (target.id === 'cpp') return '.push_back(';
                return '.push(';
            });

            s = s.replace(/\.pop\(\)/g, function () {
                if (target.id === 'csharp') return '.RemoveAt(Count - 1)';
                if (target.id === 'java') return '.remove(size() - 1)';
                return '.pop()';
            });

            s = convertListLiteral(s, line);
            return s;
        }

        function convertListLiteral(s, line) {
            if (target.id === 'javascript' || target.id === 'typescript') return s;

            const m = s.match(/^\[([\s\S]*)\]$/);
            if (!m) return s;

            const items = splitTopLevel(m[1]).map(function (x) { return x.trim(); });

            if (target.id === 'csharp') return 'new List<dynamic> { ' + items.join(', ') + ' }';
            if (target.id === 'java') {
                return items.length === 0 ? 'new ArrayList<>()' : 'new ArrayList<>(Arrays.asList(' + items.join(', ') + '))';
            }
            if (target.id === 'cpp') return buildCppVector(items);
            return s;
        }

        function buildCppVector(items) {
            let type = 'int';
            if (items.length > 0) {
                if (items.every(function (i) { return /^-?\d+$/.test(i); })) type = 'int';
                else if (items.every(function (i) { return /^-?\d*\.\d+$/.test(i); })) type = 'double';
                else if (items.some(function (i) { return i[0] === '"'; })) type = 'std::string';
                else type = 'double';
            }
            return 'std::vector<' + type + '>{ ' + items.join(', ') + ' }';
        }

        function buildFString(body, line) {
            if (target.id === 'javascript' || target.id === 'typescript') {
                return '`' + body.replace(/\{([^{}]+)\}/g, '${$1}') + '`';
            }
            if (target.id === 'csharp') {
                return '$"' + body.replace(/"/g, '\\"') + '"';
            }
            if (target.id === 'java' || target.id === 'cpp') {
                const segments = [];
                let last = 0;
                const re = /\{([^{}]+)\}/g;
                let g;
                while ((g = re.exec(body)) !== null) {
                    const literal = body.slice(last, g.index);
                    if (literal.length > 0) segments.push('"' + literal.replace(/"/g, '\\"') + '"');
                    const e = g[1].trim();
                    segments.push(target.id === 'cpp' ? 'std::to_string(' + e + ')' : e);
                    last = g.index + g[0].length;
                }
                const tail = body.slice(last);
                if (tail.length > 0) segments.push('"' + tail.replace(/"/g, '\\"') + '"');

                if (segments.length === 0) return '""';
                if (segments.length === 1) return '(' + segments[0] + ')';

                if (target.id === 'cpp') {
                    notes.warn(line, 'C++ 的字符串插值已用 std::to_string 包裹；若插值本身是字符串，请去掉这次调用。');
                }
                return '(' + segments.join(' + ') + ')';
            }
            return '"' + body + '"';
        }

        return { convert: convert, expr: expr };
    }

    core.converters = core.converters || {};
    core.converters.pythonToBrace = createPythonToBrace;
    core._internal.replaceAllText = replaceAllText;

    return core;
});
