/*!
 * HWZ 代码互换工坊 — 转换器（二）C 系 → Python
 * 署名：HWZ工作室
 */
(function (root, factory) {
    if (typeof module === 'object' && module.exports) {
        module.exports = factory(require('./engine.js'), require('./engine-py2brace.js'));
    } else {
        factory(root.HWZEngine, root.HWZEngine);
    }
})(typeof globalThis !== 'undefined' ? globalThis : this, function (core) {
    'use strict';

    const pad = core._internal.pad;
    const splitTopLevel = core._internal.splitTopLevel;
    const replaceAllText = core._internal.replaceAllText;

    function createBraceToPython(source, notes) {
        let depth = 0;
        let transparent = 0;
        let classBlockDepth = 0;
        const blockStack = [];
        const out = [];

        function emit(text) {
            if (text === null || text === undefined) return;
            const visual = Math.max(0, depth - transparent);
            out.push(pad(visual) + text);
        }

        function convert(sourceText) {
            if (/\.(Add|add)\s*\(/.test(sourceText)) {
                notes.info(1, '检测到 .Add( ) / .add( ) 调用：若意图是集合追加元素，Python 中应改为 .append( )，此处未自动替换以免误伤自定义方法。');
            }

            const parts = splitBraces(normalizeComments(sourceText));
            let i = 0;
            let blankRun = 0;

            while (i < parts.length) {
                const raw = parts[i].text;
                const lineNo = parts[i].line;
                const t = raw.trim();

                if (t.length === 0) {
                    blankRun++;
                    if (blankRun <= 1 && out.length > 0) out.push('');
                    i++;
                    continue;
                }

                blankRun = 0;

                if (t.replace(/;+$/, '') === '}') {
                    if (blockStack.length > 0) {
                        const tr = blockStack.pop();
                        depth--;
                        if (tr) transparent--;
                        if (depth < classBlockDepth) classBlockDepth = 0;
                    }
                    i++;
                    continue;
                }

                if (t === '{') {
                    blockStack.push(false);
                    depth++;
                    i++;
                    continue;
                }

                const nextIsBrace = peekToken(parts, i + 1) === '{';

                if (nextIsBrace) {
                    if (isContainerClass(t) || (isEntryMethod(t) && transparent > 0)) {
                        blockStack.push(true);
                        depth++;
                        transparent++;
                        i += 2;
                        continue;
                    }

                    emit(translateBlockHeader(t, lineNo));
                    blockStack.push(false);
                    depth++;
                    i += 2;
                    continue;
                }

                emit(translateStatement(t, lineNo));
                i++;
            }

            const result = out.join('\n').trimEnd();
            return result.length === 0 ? '' : result + '\n';
        }

        function peekToken(parts, index) {
            for (let i = index; i < parts.length; i++) {
                const t = parts[i].text.trim();
                if (t.length > 0) return t;
            }
            return null;
        }

        function stripModifiers(t) {
            let s = t.trim();
            s = s.replace(/^(?:(?:public|private|protected|internal|static|final|abstract|sealed|override|virtual|synchronized|partial|async|export|default)\s+)+/, '');
            return s.trim();
        }

        function isContainerClass(t) {
            const cleaned = stripModifiers(t);
            if (/^(?:class|struct|namespace)\s+(Program|Main|App|Application)$/.test(cleaned)) return true;
            return cleaned.indexOf('namespace ') === 0;
        }

        function isEntryMethod(t) {
            const cleaned = stripModifiers(t);
            return /^(?:void|int|static\s+void|dynamic)\s+(Main|main)\s*\(/.test(cleaned)
                || /^(Main|main)\s*\(/.test(cleaned)
                || /^int\s+main\s*\(/.test(cleaned);
        }

        function normalizeComments(text) {
            text = text.replace(/\r\n/g, '\n').replace(/\r/g, '\n');
            return text.replace(/\/\*([\s\S]*?)\*\//g, function (m, body) {
                const lines = body.split('\n')
                    .map(function (l) { return '// ' + l.trim().replace(/^\*+/, '').trim(); })
                    .filter(function (l) { return l.trim().length > 2; });
                return lines.join('\n');
            });
        }

        /** 按字符串外的花括号切分，保证每个花括号独立成行，同时保留空行 */
        function splitBraces(text) {
            const lines = text.split('\n');
            const result = [];

            for (let ln = 0; ln < lines.length; ln++) {
                const line = lines[ln];
                let current = '';
                let inString = false, inChar = false, quote = '';

                for (let i = 0; i < line.length; i++) {
                    const c = line[i];

                    if (!inString && !inChar && (c === '{' || c === '}')) {
                        if (current.trim().length > 0) { result.push({ text: current, line: ln + 1 }); current = ''; }
                        result.push({ text: c, line: ln + 1 });
                        continue;
                    }

                    if (!inString && !inChar && (c === '"' || c === "'")) {
                        inString = c === '"';
                        inChar = c === "'";
                        quote = c;
                        current += c;
                        continue;
                    }

                    if (inString || inChar) {
                        if (c === '\\' && i + 1 < line.length) { current += c + line[i + 1]; i++; continue; }
                        if (c === quote) { inString = false; inChar = false; }
                    }

                    current += c;
                }

                if (current.trim().length > 0) result.push({ text: current, line: ln + 1 });
                else if (line.trim().length === 0) result.push({ text: '', line: ln + 1 });
            }

            return result;
        }

        function translateForHeader(inner, line) {
            const colon = inner.match(/^([\s\S]+?)\s*:\s*([\s\S]+)$/);
            if (colon && inner.indexOf(';') < 0) {
                const name = colon[1].trim().split(' ').pop();
                return 'for ' + name + ' in ' + translateExpr(colon[2], line) + ':';
            }

            const ofMatch = inner.match(/^(?:const|let|var)\s+(\w+)\s+of\s+([\s\S]+)$/);
            if (ofMatch) {
                return 'for ' + ofMatch[1] + ' in ' + translateExpr(ofMatch[2], line) + ':';
            }

            const segments = inner.split(';').map(function (x) { return x.trim(); });
            if (segments.length === 3) {
                const init = segments[0].match(/(\w+)\s*=\s*([\s\S]+)/);
                const cond = segments[1].match(/(\w+)\s*(<=|<|>=|>)\s*([\s\S]+)/);
                const step = segments[2].trim();

                if (init && cond) {
                    const v = init[1];
                    const start = init[2].trim();
                    let end = cond[3].trim();
                    const cmp = cond[2];

                    let stepValue = '1';
                    if (/(\w+)\s*\+\+/.test(step)) stepValue = '1';
                    else if (/(\w+)\s*--/.test(step)) stepValue = '-1';
                    else {
                        const add = step.match(/(\w+)\s*\+=\s*([\s\S]+)/);
                        const sub = step.match(/(\w+)\s*-=\s*([\s\S]+)/);
                        if (add) stepValue = add[2].trim();
                        else if (sub) stepValue = '-' + sub[2].trim();
                    }

                    if (cmp === '<=') end = end + ' + 1';
                    if (cmp === '>') end = end + ' - 1';

                    notes.info(line, 'C 风格 for 循环已还原为 range()，请核对边界条件是否等价。');

                    if (start === '0' && stepValue === '1') {
                        return 'for ' + v + ' in range(' + translateExpr(end, line) + '):';
                    }

                    if (stepValue === '1') {
                        return 'for ' + v + ' in range(' + translateExpr(start, line) + ', ' + translateExpr(end, line) + '):';
                    }

                    return 'for ' + v + ' in range(' + translateExpr(start, line) + ', ' + translateExpr(end, line) + ', ' + stepValue + '):';
                }
            }

            notes.warn(line, '该 for 循环结构无法等价还原，已按 while 处理。');
            return 'for _ in ' + translateExpr(inner, line) + ':';
        }

        function parseFunctionSignature(t) {
            let cleaned = stripModifiers(t);
            cleaned = cleaned.replace(/^(?:void|int|string|bool|double|float|char|object|dynamic|number|any|boolean|String|Object|long|short|byte)\s+/, '');

            const m = cleaned.match(/^([A-Za-z_]\w*)\s*\(([\s\S]*)\)$/);
            if (!m) return [null, [], false];

            let name = m[1];
            const isCtor = name === 'constructor' || name === 'init';
            if (name === 'constructor') name = '__init__';

            const list = [];
            splitTopLevel(m[2]).forEach(function (p) {
                let item = p.trim();
                if (item.length === 0) return;

                item = item.split('=')[0].trim();
                item = item.replace(/^(?:(?:const|final|in|out|ref|params)\s+)*[\w<>[\],.?]+\s+/, '');
                item = item.replace(/^[*&]+/, '');
                if (/^[A-Za-z_]\w*$/.test(item)) list.push(item);
            });

            return [name, list, isCtor];
        }

        function translateBlockHeader(raw, line) {
            const t = raw.trim().replace(/;+$/, '');

            if (t === 'try') return 'try:';
            if (t === 'finally') return 'finally:';
            if (t === 'else') return 'else:';
            if (t === 'do') return 'while True:';

            let m = t.match(/^(?:else\s+)?if\s*\(([\s\S]*)\)$/);
            if (m) {
                const cond = translateExpr(m[1], line);
                return t.indexOf('else') === 0 ? 'elif ' + cond + ':' : 'if ' + cond + ':';
            }

            if (t.indexOf('else if') === 0) {
                const cm = t.match(/else\s+if\s*\(([\s\S]*)\)/);
                return 'elif ' + translateExpr(cm ? cm[1] : '', line) + ':';
            }

            m = t.match(/^while\s*\(([\s\S]*)\)$/);
            if (m) return 'while ' + translateExpr(m[1], line) + ':';

            m = t.match(/^for\s*\(([\s\S]*)\)$/);
            if (m) return translateForHeader(m[1], line);

            m = t.match(/^foreach\s*\((.+?)\s+(\w+)\s+in\s+([\s\S]+)\)$/);
            if (m) return 'for ' + m[2] + ' in ' + translateExpr(m[3], line) + ':';

            m = t.match(/^catch\s*\(([\s\S]*)\)$/);
            if (m) {
                const inner = m[1].trim();
                if (inner === '...' || inner === '') return 'except:';
                const parts = inner.split(/\s+/).filter(function (x) { return x.length > 0; });
                if (parts.length >= 2) return 'except Exception as ' + parts[parts.length - 1] + ':';
                return 'except ' + parts[0] + ':';
            }

            if (t === 'catch') return 'except:';

            m = t.match(/^switch\s*\(([\s\S]*)\)$/);
            if (m) {
                notes.info(line, 'switch 已转换为 Python 3.10+ 的 match 语句，需要 Python 3.10 及以上版本。');
                return 'match ' + translateExpr(m[1], line) + ':';
            }

            m = t.match(/^case\s+([\s\S]+?):?$/);
            if (m) return 'case ' + translateExpr(m[1], line) + ':';

            m = t.match(/^(?:class|struct|interface)\s+([A-Za-z_]\w*)\s*([\s\S]*)$/);
            if (m) {
                const name = m[1];
                classBlockDepth = depth + 1;
                const tail = m[2].trim();
                const baseMatch = tail.match(/(?::|extends|implements)\s*([A-Za-z_][\w<>,.\s]*)$/);
                if (baseMatch) {
                    const bases = baseMatch[1].split(',')
                        .map(function (b) { return b.trim().split('<')[0]; })
                        .filter(function (b) { return b.length > 0; });
                    if (bases.length > 0) return 'class ' + name + '(' + bases.join(', ') + '):';
                }
                return 'class ' + name + ':';
            }

            const fn = parseFunctionSignature(t);
            if (fn[0] !== null) {
                let params = fn[1].join(', ');
                const inClass = classBlockDepth > 0 && depth >= classBlockDepth;

                if (fn[2] || inClass) {
                    params = params.length === 0 ? 'self' : 'self, ' + params;
                }

                return 'def ' + fn[0] + '(' + params + '):';
            }

            notes.warn(line, '未能识别的块头，已保留为注释：' + t);
            return '# 待人工处理：' + t;
        }

        function stripTypeDeclaration(t) {
            let s = t.replace(/^(?:(?:const|let|var|auto|final|readonly|static|unsigned|signed|long|short|volatile)\s+)+/, '').trim();

            const eq = findTopLevelAssign(s);
            const left = (eq >= 0 ? s.slice(0, eq) : s).trim();
            const right = eq >= 0 ? s.slice(eq) : '';

            if (/^[A-Za-z_]\w*$/.test(left)) return left + (right.length > 0 ? ' ' : '') + right;

            const m = left.match(/^[\w:<>[\],.?]+(?:\[\])?\s+([A-Za-z_]\w*)$/);
            if (m) return m[1] + (right.length > 0 ? ' ' : '') + right;

            return s;
        }

        function findTopLevelAssign(s) {
            let depth = 0, inStr = false, quote = '';
            for (let i = 0; i < s.length; i++) {
                const c = s[i];
                if (inStr) {
                    if (c === '\\') { i++; continue; }
                    if (c === quote) inStr = false;
                    continue;
                }
                if (c === '"' || c === "'") { inStr = true; quote = c; continue; }

                if (c === '(' || c === '[' || c === '{') depth++;
                else if (c === ')' || c === ']' || c === '}') depth--;
                else if (c === '=' && depth === 0) {
                    const prev = i > 0 ? s[i - 1] : ' ';
                    const next = i + 1 < s.length ? s[i + 1] : ' ';
                    if (next === '=' || '=!<>+-*/%'.indexOf(prev) >= 0) {
                        if (next === '=') i++;
                        continue;
                    }
                    return i;
                }
            }
            return -1;
        }

        function tryTranslatePrint(t, line) {
            let m = t.match(/^(?:console\.log|Console\.WriteLine|System\.out\.println|System\.println|println|printf)\s*\(([\s\S]*)\)$/);
            if (m) {
                const args = splitTopLevel(m[1])
                    .map(function (a) { return translateExpr(a.trim(), line); })
                    .filter(function (a) { return a.trim().length > 0; });
                return args.length === 0 ? 'print()' : 'print(' + args.join(', ') + ')';
            }

            m = t.match(/^(?:Console\.Write|System\.out\.print|print|cout)\s*\(([\s\S]*)\)$/);
            if (m) return 'print(' + translateExpr(m[1].trim(), line) + ', end="")';

            m = t.match(/^(?:std::)?cout\s*<<\s*([\s\S]+)$/);
            if (m) {
                const items = m[1].split('<<')
                    .map(function (x) { return x.trim(); })
                    .filter(function (x) {
                        return x.length > 0 && ['std::endl', 'endl', "'\\n'", '"\\n"'].indexOf(x) < 0;
                    })
                    .map(function (x) { return translateExpr(x, line); });
                return items.length === 0 ? 'print()' : 'print(' + items.join(', ') + ')';
            }

            return null;
        }

        function translateStatement(raw, line) {
            const t = raw.trim().replace(/;+$/, '');
            if (t.length === 0) return null;

            if (t.indexOf('#include') === 0 || t.indexOf('using System') === 0 ||
                t.indexOf('using static') === 0 || t.indexOf('package ') === 0 ||
                t.indexOf('"use strict"') === 0) {
                return null;
            }

            if (/^(import|from)\s+/.test(t) && t.indexOf('(') < 0) return null;

            if (t.indexOf('//') === 0) {
                if (t.indexOf('入口函数') >= 0) return null;
                return '#' + t.slice(2);
            }

            const print = tryTranslatePrint(t, line);
            if (print !== null) return print;

            if (t === 'return') return 'return';
            if (t.indexOf('return ') === 0) return 'return ' + translateExpr(t.slice(7), line);
            if (t === 'break' || t === 'continue') return t;

            const inc = t.match(/^([A-Za-z_]\w*)(\+\+|--)$/);
            if (inc) return inc[1] + ' ' + (inc[2] === '++' ? '+=' : '-=') + ' 1';

            return translateExpr(stripTypeDeclaration(t), line);
        }

        function translateExpr(expr, line) {
            if (!expr || expr.trim().length === 0) return expr;

            let s = expr;
            const literals = [];

            s = s.replace(/"(?:[^"\\]|\\.)*"|'(?:[^'\\]|\\.)*'/g, function (m) {
                literals.push(m);
                return '\u0001' + (literals.length - 1) + '\u0001';
            });

            s = s.replace(/\btrue\b/g, 'True');
            s = s.replace(/\bfalse\b/g, 'False');
            s = s.replace(/\b(?:null|nullptr|NULL)\b/g, 'None');
            s = s.replace(/\bthis\b/g, 'self');
            s = replaceAllText(s, '->', '.');

            s = replaceAllText(s, '&&', ' and ');
            s = replaceAllText(s, '||', ' or ');
            s = s.replace(/(?<![=!<>])!(?!=)/g, ' not ');

            s = s.replace(/([A-Za-z_]\w*(?:\.\w+)*)\.(?:Length|length\(\)|size\(\))/g, 'len($1)');

            s = s.replace(/(?:String|Convert\.ToString)\(([\s\S]*?)\)/g, 'str($1)');
            s = s.replace(/(?:Integer\.parseInt|int\.Parse|std::stoi)\(([\s\S]*?)\)/g, 'int($1)');
            s = s.replace(/(?:Double\.parseDouble|double\.Parse|std::stod)\(([\s\S]*?)\)/g, 'float($1)');
            s = s.replace(/Math\.(?:abs|Abs)\(([\s\S]*?)\)/g, 'abs($1)');
            s = s.replace(/(?:Math\.(?:Pow|pow)|std::pow)\(([\s\S]*?),([\s\S]*?)\)/g, '$1 ** $2');

            s = s.replace(/\.(?:push|push_back)\(/g, '.append(');
            s = s.replace(/([A-Za-z_]\w*(?:\.\w+)*)\.(?:Count|size\(\))/g, 'len($1)');

            s = s.replace(/\bnew\s+([A-Za-z_][\w.]*)\s*\(/g, '$1(');
            s = s.replace(/\bnew\s+([A-Za-z_][\w.]*)\s*\[\s*\]/g, '$1()');

            s = s.replace(/(\w+)\s*==\s*None/g, '$1 is None');
            s = s.replace(/(\w+)\s*!=\s*None/g, '$1 is not None');

            for (let i = 0; i < literals.length; i++) {
                s = s.split('\u0001' + i + '\u0001').join(literals[i]);
            }

            s = s.replace(/\$("(?:[^"\\]|\\.)*")/g, 'f$1');

            return s.trim();
        }

        return { convert: convert };
    }

    core.converters = core.converters || {};
    core.converters.braceToPython = createBraceToPython;

    return core;
});
