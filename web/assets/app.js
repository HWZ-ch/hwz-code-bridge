/*!
 * HWZ 代码互换工坊 · 网页版交互
 * 署名：HWZ工作室
 */
(function () {
    'use strict';

    const E = window.HWZEngine;

    const KEY_SETTINGS = 'hwz.settings.v1';
    const KEY_RECORDS = 'hwz.records.v1';
    const MAX_RECORDS = 200;

    const DEFAULT_SETTINGS = {
        theme: 'light',
        fontSize: 13,
        wrap: false,
        saveHistory: true,
        remember: true,
        autoCopy: false,
        lastSrc: 'python',
        lastDst: 'csharp'
    };

    // ═══════════════════════════════════════════════
    // 第三方工具清单（来源为必填项）
    // ═══════════════════════════════════════════════

    const TOOLS = [
        { id: 'pyinstaller', name: 'PyInstaller', category: '打包', langs: ['python'],
          purpose: '把 Python 脚本打包成单文件 exe，自动收集依赖解释器',
          source: 'PyInstaller Development Team', homepage: 'https://pyinstaller.org',
          license: 'GPL-2.0-or-later（含 bootloader 例外条款）' },
        { id: 'nuitka', name: 'Nuitka', category: '打包', langs: ['python'],
          purpose: '把 Python 编译为 C 代码再生成原生 exe，运行效率更高',
          source: 'Kay Hayen 及 Nuitka 贡献者', homepage: 'https://nuitka.net',
          license: 'Apache-2.0' },
        { id: 'dotnet', name: '.NET SDK', category: '编译', langs: ['csharp'],
          purpose: '编译 C# 源码并发布为独立 exe（PublishSingleFile）',
          source: 'Microsoft / .NET Foundation', homepage: 'https://dotnet.microsoft.com/download',
          license: 'MIT' },
        { id: 'pkg', name: 'pkg', category: '打包', langs: ['javascript', 'typescript'],
          purpose: '把 Node.js 项目打包为 Windows exe（原 vercel/pkg 已归档，社区继任为 @yao-pkg/pkg）',
          source: 'vercel/pkg 原作者，现由 yao-pkg 社区维护', homepage: 'https://github.com/yao-pkg/pkg',
          license: 'MIT' },
        { id: 'gcc', name: 'GCC / MinGW-w64', category: '编译', langs: ['cpp'],
          purpose: '编译 C / C++ 源码为 exe，使用 -static 生成免依赖可执行文件',
          source: 'GNU 项目 / MinGW-w64 团队', homepage: 'https://www.mingw-w64.org',
          license: 'GPL-3.0-or-later（GCC 运行时库有例外条款）' },
        { id: 'clang', name: 'LLVM Clang', category: '编译', langs: ['cpp'],
          purpose: 'C / C++ 的替代编译器，错误提示更友好',
          source: 'LLVM 项目 / LLVM Foundation', homepage: 'https://llvm.org',
          license: 'Apache-2.0 WITH LLVM-exception' },
        { id: 'java', name: 'JDK / jpackage', category: '打包', langs: ['java'],
          purpose: '把 Java 程序打包为自带运行时的 Windows 应用目录',
          source: 'OpenJDK 社区（亦可使用 Eclipse Temurin 发行版）', homepage: 'https://openjdk.org',
          license: 'GPL-2.0 WITH Classpath-exception-2.0' },
        { id: 'node', name: 'Node.js', category: '运行时', langs: ['javascript', 'typescript'],
          purpose: 'JavaScript 运行时，同时作为 pkg / esbuild 的执行基础',
          source: 'OpenJS Foundation', homepage: 'https://nodejs.org',
          license: 'MIT' },
        { id: 'go', name: 'Go 工具链', category: '编译', langs: [],
          purpose: '编译 Go 源码为独立 exe（天然静态链接）',
          source: 'Google / Go 项目贡献者', homepage: 'https://go.dev',
          license: 'BSD-3-Clause' },
        { id: 'rustc', name: 'Rust 编译器', category: '编译', langs: [],
          purpose: '用 rustc 直接把单个 .rs 源文件编译为 exe',
          source: 'Rust 项目 / Rust Foundation', homepage: 'https://www.rust-lang.org',
          license: 'MIT 或 Apache-2.0（双许可）' },
        { id: 'upx', name: 'UPX', category: '辅助', langs: [],
          purpose: '对已生成的 exe 做无损压缩，显著减小体积',
          source: 'Markus Oberhumer, Laszlo Molnar & John Reiser', homepage: 'https://upx.github.io',
          license: 'GPL-2.0-or-later（含特殊例外）' },
        { id: 'ilspy', name: 'ILSpy', category: '辅助', langs: [],
          purpose: '反编译 .NET 程序集，用于核对编译产物的类型与结构',
          source: 'ICSharpCode 团队', homepage: 'https://github.com/icsharpcode/ILSpy',
          license: 'MIT' },
        { id: 'pebear', name: 'PE-bear', category: '辅助', langs: [],
          purpose: '查看 PE 文件结构（节区、导入表、资源），排查打包异常',
          source: 'hasherezade', homepage: 'https://github.com/hasherezade/pe-bear',
          license: 'GPL-3.0' }
    ];

    const TOOL_BY_ID = {};
    TOOLS.forEach(function (t) { TOOL_BY_ID[t.id] = t; });

    const TOOLS_BY_LANG = {
        python: ['pyinstaller', 'nuitka'],
        javascript: ['pkg'],
        typescript: ['pkg'],
        csharp: ['dotnet'],
        cpp: ['gcc', 'clang'],
        java: ['java']
    };

    // ═══════════════════════════════════════════════

    const $ = function (id) { return document.getElementById(id); };

    let settings = Object.assign({}, DEFAULT_SETTINGS, loadJSON(KEY_SETTINGS, {}));
    let records = loadJSON(KEY_RECORDS, []);
    const logs = [];
    let currentFileName = '';
    let lastBuildInfo = null;

    function loadJSON(key, fallback) {
        try {
            const raw = localStorage.getItem(key);
            if (!raw) return fallback;
            const parsed = JSON.parse(raw);
            return parsed === null || parsed === undefined ? fallback : parsed;
        } catch (err) {
            return fallback;
        }
    }

    function saveSettings() {
        try { localStorage.setItem(KEY_SETTINGS, JSON.stringify(settings)); } catch (err) { /* 隐私模式下忽略 */ }
    }

    function saveRecords() {
        try { localStorage.setItem(KEY_RECORDS, JSON.stringify(records)); } catch (err) { /* 忽略 */ }
    }

    // ═══════════ 提示与日志 ═══════════

    function toast(message, isError) {
        const box = $('toast');
        const el = document.createElement('div');
        el.className = 'toast-item' + (isError ? ' err' : '');
        el.textContent = message;
        box.appendChild(el);
        setTimeout(function () {
            el.style.opacity = '0';
            el.style.transform = 'translateX(10px)';
            el.style.transition = 'all .25s ease';
            setTimeout(function () { el.remove(); }, 260);
        }, 2400);
    }

    function log(level, category, message) {
        const entry = { time: new Date(), level: level, category: category, message: message };
        logs.push(entry);
        if (logs.length > 500) logs.shift();
        appendLogLine(entry);
    }

    function pad2(n) { return n < 10 ? '0' + n : '' + n; }

    function timeText(d) {
        return pad2(d.getHours()) + ':' + pad2(d.getMinutes()) + ':' + pad2(d.getSeconds()) + '.' + ('00' + d.getMilliseconds()).slice(-3);
    }

    function appendLogLine(entry) {
        const box = $('logBox');
        if (!box) return;

        const filter = $('logLevel').value;
        if (filter === 'warn' && entry.level === 'info') return;
        if (filter === 'error' && entry.level !== 'error') return;

        const line = document.createElement('span');
        line.className = 'log-line';
        line.innerHTML = '<span class="lv-time">' + timeText(entry.time) + '</span> ' +
            '<span class="lv-' + entry.level + '">[' + entry.level.toUpperCase() + ']</span> ' +
            '[' + escapeHtml(entry.category) + '] ' + escapeHtml(entry.message);
        box.appendChild(line);
        box.scrollTop = box.scrollHeight;
    }

    function escapeHtml(s) {
        return String(s).replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;').replace(/"/g, '&quot;');
    }

    function renderLogs() {
        const box = $('logBox');
        box.innerHTML = '';
        logs.forEach(function (e) { appendLogLine(e); });
    }

    // ═══════════ 页面切换 ═══════════

    function switchPage(name) {
        document.querySelectorAll('.page').forEach(function (p) {
            p.classList.toggle('active', p.id === 'page-' + name);
        });
        document.querySelectorAll('.nav-btn').forEach(function (b) {
            b.classList.toggle('active', b.dataset.page === name);
        });
        if (location.hash !== '#' + name) {
            history.replaceState(null, '', '#' + name);
        }

        document.querySelector('.main').scrollTop = 0;
        log('info', '界面', '切换到页面：' + name);
    }

    // ═══════════ 代码互换 ═══════════

    function initLanguages() {
        const srcSel = $('srcLang');
        const dstSel = $('dstLang');
        const buildSel = $('buildLang');

        E.languages.forEach(function (l) {
            [srcSel, dstSel, buildSel].forEach(function (sel) {
                const opt = document.createElement('option');
                opt.value = l.id;
                opt.textContent = l.name;
                sel.appendChild(opt);
            });
        });

        const src = settings.remember ? settings.lastSrc : 'python';
        const dst = settings.remember ? settings.lastDst : 'csharp';

        srcSel.value = E.getLanguage(src).id;
        dstSel.value = E.getLanguage(dst).id;
        buildSel.value = srcSel.value;
    }

    function applyEditorStyle() {
        const size = clamp(settings.fontSize, 11, 20);
        ['srcCode', 'dstCode'].forEach(function (id) {
            const el = $(id);
            el.style.fontSize = size + 'px';
            el.style.whiteSpace = settings.wrap ? 'pre-wrap' : 'pre';
            el.style.overflowX = settings.wrap ? 'hidden' : 'auto';
        });
        $('setFont').value = size;
        $('setWrap').checked = !!settings.wrap;
    }

    function clamp(v, min, max) {
        v = parseInt(v, 10);
        if (isNaN(v)) return min;
        return Math.max(min, Math.min(max, v));
    }

    function updateMeta() {
        const srcLines = E.countLines($('srcCode').value);
        $('srcMeta').textContent = srcLines + ' 行';
    }

    function doConvert() {
        const src = E.getLanguage($('srcLang').value);
        const dst = E.getLanguage($('dstLang').value);
        const code = $('srcCode').value;

        if (!code.trim()) {
            toast('请先输入代码，或把代码文件拖入页面', true);
            return;
        }

        const result = E.convert(code, src.id, dst.id);

        if (!result.success) {
            $('dstCode').value = '';
            renderNotes([{ line: 0, level: 'warn', message: result.message }], '转换未完成');
            toast(result.message, true);
            log('warn', '转换', src.name + ' → ' + dst.name + ' 失败：' + result.message);
            return;
        }

        $('dstCode').value = result.code;
        $('dstMeta').textContent = result.targetLines + ' 行 · ' + result.elapsedMs + ' ms';
        renderNotes(result.notes, result.notes.length + ' 条，其中需人工复核 ' + result.warnCount + ' 条');

        addRecord({
            kind: 'convert',
            name: currentFileName || '编辑器输入',
            route: src.name + ' → ' + dst.name,
            status: result.warnCount > 0 ? ('成功 · ' + result.warnCount + ' 条提示') : '成功',
            duration: result.elapsedMs,
            summary: result.message
        });

        log('info', '转换', src.name + ' → ' + dst.name + ' 完成，' + result.sourceLines + ' 行 → ' + result.targetLines + ' 行，' + result.notes.length + ' 条提示');

        if (settings.remember) {
            settings.lastSrc = src.id;
            settings.lastDst = dst.id;
            saveSettings();
        }

        if (settings.autoCopy) copyText(result.code, true);
    }

    function renderNotes(notes, countText) {
        const panel = $('notesPanel');
        const list = $('notesList');

        if (!notes || notes.length === 0) {
            panel.classList.remove('show');
            return;
        }

        panel.classList.add('show');
        $('notesCount').textContent = countText || (notes.length + ' 条');

        list.innerHTML = '';
        notes.slice(0, 80).forEach(function (n) {
            const row = document.createElement('div');
            row.className = 'note ' + (n.level === 'warn' ? 'warn' : 'info');
            row.innerHTML = '<span class="dot">' + (n.level === 'warn' ? '▲' : '·') + '</span>' +
                '<span>' + escapeHtml(E.noteText(n)) + '</span>';
            list.appendChild(row);
        });
    }

    function copyText(text, silent) {
        if (!text) return;

        function done() { if (!silent) toast('已复制到剪贴板'); }

        if (navigator.clipboard && navigator.clipboard.writeText) {
            navigator.clipboard.writeText(text).then(done).catch(function () { fallbackCopy(text, done); });
        } else {
            fallbackCopy(text, done);
        }
    }

    function fallbackCopy(text, done) {
        const ta = document.createElement('textarea');
        ta.value = text;
        ta.style.position = 'fixed';
        ta.style.opacity = '0';
        document.body.appendChild(ta);
        ta.select();
        try { document.execCommand('copy'); done(); } catch (err) { toast('复制失败，请手动选择', true); }
        ta.remove();
    }

    function downloadText(filename, content, mime) {
        const blob = new Blob([content], { type: (mime || 'text/plain') + ';charset=utf-8' });
        const url = URL.createObjectURL(blob);
        const a = document.createElement('a');
        a.href = url;
        a.download = filename;
        document.body.appendChild(a);
        a.click();
        a.remove();
        setTimeout(function () { URL.revokeObjectURL(url); }, 1500);
    }

    function readFileSmart(file) {
        return new Promise(function (resolve, reject) {
            const fr = new FileReader();
            fr.onerror = function () { reject(new Error('读取失败')); };
            fr.onload = function () {
                const text = String(fr.result || '');
                if (text.indexOf('\uFFFD') < 0) { resolve(text); return; }

                const fr2 = new FileReader();
                fr2.onload = function () { resolve(String(fr2.result || '')); };
                fr2.onerror = function () { resolve(text); };
                fr2.readAsText(file, 'gbk');
            };
            fr.readAsText(file, 'utf-8');
        });
    }

    function loadFile(file) {
        readFileSmart(file).then(function (text) {
            $('srcCode').value = text;
            currentFileName = file.name;

            const guessed = E.guessFromPath(file.name);
            if (guessed) {
                $('srcLang').value = guessed.id;
                if ($('srcLang').value === $('dstLang').value) {
                    const other = E.languages.filter(function (l) { return l.id !== guessed.id; })[0];
                    $('dstLang').value = other.id;
                }
            }

            updateMeta();
            toast('已载入 ' + file.name);
            log('info', '转换', '已载入文件：' + file.name + '（' + (text.length) + ' 字符）');
        }).catch(function (err) {
            toast('无法读取该文件：' + err.message, true);
            log('error', '转换', '读取文件失败：' + err.message);
        });
    }

    // ═══════════ 打包命令生成 ═══════════

    function initBuild() {
        refreshToolOptions();
        renderToolInfo();
    }

    function refreshToolOptions() {
        const lang = $('buildLang').value;
        const sel = $('buildTool');
        sel.innerHTML = '';

        const ids = TOOLS_BY_LANG[lang] || [];
        ids.forEach(function (id) {
            const t = TOOL_BY_ID[id];
            if (!t) return;
            const opt = document.createElement('option');
            opt.value = id;
            opt.textContent = t.name;
            sel.appendChild(opt);
        });

        const langInfo = E.getLanguage(lang);
        const base = $('buildFile').value || '';
        const ext = langInfo.ext;
        if (!base || !/\.(\w+)$/.test(base)) {
            $('buildFile').value = 'app' + ext;
        }

        renderToolInfo();
    }

    function renderToolInfo() {
        const tool = TOOL_BY_ID[$('buildTool').value];
        const el = $('buildToolInfo');
        if (!tool) {
            el.textContent = '该语言暂未内置打包流程。可以在「工具中心」查看对应工具的官方用法。';
            return;
        }
        el.innerHTML = '<strong>' + escapeHtml(tool.name) + '</strong> — ' + escapeHtml(tool.purpose) +
            '<br>来源：' + escapeHtml(tool.source) + ' · <a href="' + escapeHtml(tool.homepage) + '" target="_blank" rel="noopener">' + escapeHtml(tool.homepage) + '</a>' +
            ' · 许可证：' + escapeHtml(tool.license) +
            '<br>状态：需在本机安装该工具后才能执行命令。';
    }

    function buildCommands() {
        const lang = $('buildLang').value;
        const toolId = $('buildTool').value;
        const file = ($('buildFile').value || '').trim() || 'app';
        const name = ($('buildName').value || '').trim() || 'MyApp';
        const arch = $('buildArch').value;
        const upx = $('buildUpx').checked;
        const tool = TOOL_BY_ID[toolId];

        if (!tool) return null;

        const q = '"' + file + '"';
        let commands = [];

        if (toolId === 'pyinstaller') {
            commands = ['python -m PyInstaller --onefile --clean --noconfirm --name ' + name + ' ' + q];
        } else if (toolId === 'nuitka') {
            commands = ['python -m nuitka --onefile --assume-yes-for-downloads --output-filename=' + name + '.exe ' + q];
        } else if (toolId === 'dotnet') {
            if (/\.csproj$/i.test(file)) {
                commands = ['dotnet publish ' + q + ' -c Release -r ' + arch + ' --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -o dist'];
            } else {
                commands = [
                    'dotnet new console -o build_temp --force >nul',
                    'copy /Y ' + q + ' build_temp\\Program.cs >nul',
                    'dotnet publish build_temp\\build_temp.csproj -c Release -r ' + arch + ' --self-contained true -p:PublishSingleFile=true -p:EnableCompressionInSingleFile=true -p:AssemblyName=' + name + ' -o dist'
                ];
            }
        } else if (toolId === 'pkg') {
            if (lang === 'typescript') {
                commands = [
                    'npx --yes esbuild ' + q + ' --bundle --platform=node --target=node18 --outfile=bundle.js',
                    'pkg bundle.js --targets node18-win-x64 --output dist\\' + name + '.exe'
                ];
            } else {
                commands = ['pkg ' + q + ' --targets node18-win-x64 --output dist\\' + name + '.exe'];
            }
        } else if (toolId === 'gcc') {
            commands = ['g++ -O2 -std=c++20 -static -o dist\\' + name + '.exe ' + q];
        } else if (toolId === 'clang') {
            commands = ['clang++ -O2 -std=c++20 -static -o dist\\' + name + '.exe ' + q];
        } else if (toolId === 'java') {
            commands = [
                'javac -encoding UTF-8 -d classes ' + q,
                'jar --create --file app.jar --main-class Main -C classes .',
                'jpackage --type app-image --input . --main-jar app.jar --name ' + name + ' --dest dist --win-console'
            ];
        } else if (toolId === 'go') {
            commands = ['go build -trimpath -ldflags="-s -w" -o dist\\' + name + '.exe ' + q];
        } else if (toolId === 'rustc') {
            commands = ['rustc -O -o dist\\' + name + '.exe ' + q];
        }

        if (upx) commands.push('upx --best dist\\' + name + '.exe');

        return { commands: commands, tool: tool, name: name, file: file };
    }

    function generateCommand() {
        const info = buildCommands();
        if (!info) {
            toast('当前语言暂未内置打包流程', true);
            return;
        }

        lastBuildInfo = info;

        const header = [
            'mkdir dist 2>nul',
            ''
        ];

        const full = header.concat(info.commands);
        $('buildCmd').textContent = full.join('\n');
        $('cmdMeta').textContent = '已生成 · ' + info.tool.name + ' · ' + info.commands.length + ' 个步骤';
        $('downloadBatBtn').disabled = false;
        $('copyCmdBtn').disabled = false;

        addRecord({
            kind: 'build',
            name: info.file,
            route: (E.getLanguage($('buildLang').value).name) + ' → EXE',
            status: '命令已生成',
            duration: 0,
            summary: info.tool.name + '：' + info.commands[0].slice(0, 80)
        });

        log('info', '打包', '已生成 ' + info.tool.name + ' 打包命令（' + info.commands.length + ' 步）');
        toast('命令已生成');
    }

    function makeBat(info) {
        const lines = [];
        lines.push('@echo off');
        lines.push('chcp 65001 >nul');
        lines.push('setlocal');
        lines.push('cd /d "%~dp0"');
        lines.push('');
        lines.push('echo ============================================================');
        lines.push('echo  HWZ 代码互换工坊 - 打包脚本');
        lines.push('echo  署名：HWZ工作室');
        lines.push('echo ------------------------------------------------------------');
        lines.push('echo  工具：' + info.tool.name);
        lines.push('echo  来源：' + info.tool.source);
        lines.push('echo  主页：' + info.tool.homepage);
        lines.push('echo  许可：' + info.tool.license);
        lines.push('echo ============================================================');
        lines.push('echo.');

        info.commands.forEach(function (c, i) {
            lines.push('echo [' + (i + 1) + '/' + info.commands.length + '] ' + c);
            lines.push(c);
            lines.push('if errorlevel 1 goto failed');
            lines.push('echo.');
        });

        lines.push('echo 打包完成，产物位于 dist 目录。');
        lines.push('pause');
        lines.push('exit /b 0');
        lines.push('');
        lines.push(':failed');
        lines.push('echo.');
        lines.push('echo 步骤执行失败。请确认已安装 ' + info.tool.name + '。');
        lines.push('echo 获取方式：' + info.tool.homepage);
        lines.push('pause');
        lines.push('exit /b 1');

        return lines.join('\r\n');
    }

    // ═══════════ 工具中心 ═══════════

    function renderTools() {
        const box = $('toolList');
        box.innerHTML = '';

        TOOLS.forEach(function (t) {
            const item = document.createElement('div');
            item.className = 'tool-item';
            item.innerHTML =
                '<div>' +
                    '<div class="tool-name">' + escapeHtml(t.name) +
                        '<span class="tag">' + escapeHtml(t.category) + '</span>' +
                    '</div>' +
                    '<div class="tool-purpose">' + escapeHtml(t.purpose) + '</div>' +
                    '<div class="tool-src">来源：' + escapeHtml(t.source) +
                        ' · <a href="' + escapeHtml(t.homepage) + '" target="_blank" rel="noopener">' + escapeHtml(t.homepage) + '</a>' +
                        '<br>许可证：' + escapeHtml(t.license) + '</div>' +
                '</div>' +
                '<div class="tool-actions">' +
                    '<a class="btn btn-sm" href="' + escapeHtml(t.homepage) + '" target="_blank" rel="noopener">官网</a>' +
                '</div>';
            box.appendChild(item);
        });
    }

    function renderAboutTools() {
        const box = $('aboutTools');
        box.innerHTML = '';

        TOOLS.forEach(function (t) {
            const row = document.createElement('div');
            row.style.cssText = 'display:flex;gap:14px;padding:8px 0;border-bottom:1px solid var(--line);font-size:12px';
            row.innerHTML =
                '<div style="width:150px;flex:none;font-weight:600">' + escapeHtml(t.name) + '</div>' +
                '<div style="color:var(--text-2)">' + escapeHtml(t.source) + ' · ' + escapeHtml(t.license) +
                '<br><span style="color:var(--accent)">' + escapeHtml(t.homepage) + '</span></div>';
            box.appendChild(row);
        });
    }

    // ═══════════ 记录 ═══════════

    function addRecord(rec) {
        if (!settings.saveHistory) return;

        const now = new Date();
        records.unshift({
            id: String(Date.now()) + Math.random().toString(16).slice(2, 6),
            time: now.toISOString(),
            kind: rec.kind,
            name: rec.name || '',
            route: rec.route || '',
            status: rec.status || '',
            duration: rec.duration || 0,
            summary: rec.summary || ''
        });

        if (records.length > MAX_RECORDS) records.length = MAX_RECORDS;

        saveRecords();
        if ($('page-history').classList.contains('active')) renderRecords();
    }

    function kindText(k) { return k === 'convert' ? '代码互换' : '打包命令'; }

    function durationText(ms) {
        if (!ms) return '—';
        return ms < 1000 ? ms + ' ms' : (ms / 1000).toFixed(1) + ' s';
    }

    function timeShort(iso) {
        const d = new Date(iso);
        if (isNaN(d.getTime())) return '';
        return pad2(d.getMonth() + 1) + '-' + pad2(d.getDate()) + ' ' + pad2(d.getHours()) + ':' + pad2(d.getMinutes());
    }

    function renderRecords() {
        const box = $('recordList');
        const keyword = ($('recordSearch').value || '').trim().toLowerCase();
        const kind = $('recordKind').value;

        const list = records.filter(function (r) {
            if (kind !== 'all' && r.kind !== kind) return false;
            if (!keyword) return true;
            return (r.name + ' ' + r.route + ' ' + r.status + ' ' + r.summary).toLowerCase().indexOf(keyword) >= 0;
        });

        if (list.length === 0) {
            box.innerHTML = '<div class="empty">' +
                (records.length === 0
                    ? '还没有记录。完成一次代码互换或生成一次打包命令后会出现在这里。'
                    : '没有符合筛选条件的记录。') +
                '</div>';
            return;
        }

        box.innerHTML = '';
        list.forEach(function (r) {
            const el = document.createElement('div');
            el.className = 'record';
            el.innerHTML =
                '<div class="record-bar"></div>' +
                '<div class="record-body">' +
                    '<div class="record-top"><b>' + escapeHtml(kindText(r.kind)) + '</b>' +
                        '<span class="record-route">' + escapeHtml(r.route) + '</span>' +
                        '<span class="record-time">' + escapeHtml(timeShort(r.time)) + '</span></div>' +
                    '<div class="record-sub">' + escapeHtml(r.name) + ' · ' + escapeHtml(r.summary) + '</div>' +
                '</div>' +
                '<div class="record-right">' + escapeHtml(r.status) + '<br>' + durationText(r.duration) +
                    '<br><button class="btn btn-sm" style="margin-top:6px" data-del="' + r.id + '">删除</button></div>';
            box.appendChild(el);
        });

        box.querySelectorAll('[data-del]').forEach(function (btn) {
            btn.addEventListener('click', function () {
                records = records.filter(function (r) { return r.id !== btn.dataset.del; });
                saveRecords();
                renderRecords();
            });
        });
    }

    function exportRecords() {
        if (records.length === 0) { toast('当前没有可导出的记录', true); return; }

        const rows = [['时间', '类型', '来源', '转换路径', '状态', '耗时(ms)', '说明']];
        records.forEach(function (r) {
            rows.push([
                new Date(r.time).toLocaleString('zh-CN'),
                kindText(r.kind),
                r.name,
                r.route,
                r.status,
                String(r.duration || 0),
                r.summary
            ]);
        });

        const csv = '\uFEFF' + rows.map(function (row) {
            return row.map(function (c) { return '"' + String(c).replace(/"/g, '""') + '"'; }).join(',');
        }).join('\r\n');

        downloadText('HWZ转换记录-' + stamp() + '.csv', csv, 'text/csv');
        log('info', '记录', '已导出 ' + records.length + ' 条记录');
        toast('已导出 ' + records.length + ' 条记录');
    }

    function stamp() {
        const d = new Date();
        return d.getFullYear() + pad2(d.getMonth() + 1) + pad2(d.getDate()) + '-' + pad2(d.getHours()) + pad2(d.getMinutes()) + pad2(d.getSeconds());
    }

    // ═══════════ 设置 ═══════════

    function applyTheme() {
        document.documentElement.setAttribute('data-theme', settings.theme === 'dark' ? 'dark' : 'light');
    }

    function bindSettings() {
        $('setTheme').value = settings.theme;
        $('setHistory').checked = !!settings.saveHistory;
        $('setRemember').checked = !!settings.remember;
        $('setCopy').checked = !!settings.autoCopy;

        $('setTheme').addEventListener('change', function () {
            settings.theme = this.value;
            saveSettings();
            applyTheme();
            log('info', '设置', '主题切换为 ' + (this.value === 'dark' ? '深色' : '浅色'));
        });

        $('setFont').addEventListener('change', function () {
            settings.fontSize = clamp(this.value, 11, 20);
            this.value = settings.fontSize;
            saveSettings();
            applyEditorStyle();
        });

        $('setWrap').addEventListener('change', function () {
            settings.wrap = this.checked;
            saveSettings();
            applyEditorStyle();
        });

        $('setHistory').addEventListener('change', function () {
            settings.saveHistory = this.checked;
            saveSettings();
        });

        $('setRemember').addEventListener('change', function () {
            settings.remember = this.checked;
            saveSettings();
        });

        $('setCopy').addEventListener('change', function () {
            settings.autoCopy = this.checked;
            saveSettings();
        });

        $('resetSettingsBtn').addEventListener('click', function () {
            if (!confirm('将恢复所有设置为默认值（不会删除转换记录）。继续吗？')) return;
            settings = Object.assign({}, DEFAULT_SETTINGS);
            saveSettings();
            applyTheme();
            applyEditorStyle();
            $('setTheme').value = settings.theme;
            $('setHistory').checked = true;
            $('setRemember').checked = true;
            $('setCopy').checked = false;
            log('info', '设置', '设置已恢复默认值');
            toast('已恢复默认设置');
        });

        $('wipeAllBtn').addEventListener('click', function () {
            if (!confirm('将清除本机保存的设置、转换记录与日志，此操作不可撤销。继续吗？')) return;
            try {
                localStorage.removeItem(KEY_SETTINGS);
                localStorage.removeItem(KEY_RECORDS);
            } catch (err) { /* 忽略 */ }
            records = [];
            logs.length = 0;
            settings = Object.assign({}, DEFAULT_SETTINGS);
            applyTheme();
            applyEditorStyle();
            renderLogs();
            renderRecords();
            $('dataInfo').textContent = '本地数据已清空。';
            toast('本地数据已清空');
        });

        const used = (function () {
            try {
                let bytes = 0;
                for (let i = 0; i < localStorage.length; i++) {
                    const k = localStorage.key(i);
                    if (k && k.indexOf('hwz.') === 0) bytes += (localStorage.getItem(k) || '').length;
                }
                return (bytes / 1024).toFixed(1);
            } catch (err) { return '0.0'; }
        })();

        $('dataInfo').textContent = '当前保存在本机浏览器的数据：设置与 ' + records.length + ' 条记录，约 ' + used + ' KB。清理浏览器数据会一并删除。';
    }

    // ═══════════ 事件绑定 ═══════════

    function bindEvents() {
        document.querySelectorAll('.nav-btn').forEach(function (btn) {
            btn.addEventListener('click', function () { switchPage(btn.dataset.page); });
        });

        $('convertBtn').addEventListener('click', doConvert);

        $('swapBtn').addEventListener('click', function () {
            const s = $('srcLang').value;
            $('srcLang').value = $('dstLang').value;
            $('dstLang').value = s;

            const result = $('dstCode').value;
            if (result) {
                $('srcCode').value = result;
                $('dstCode').value = '';
                $('dstMeta').textContent = '等待转换';
                updateMeta();
            }
        });

        $('clearBtn').addEventListener('click', function () {
            $('srcCode').value = '';
            $('dstCode').value = '';
            $('dstMeta').textContent = '等待转换';
            currentFileName = '';
            $('notesPanel').classList.remove('show');
            updateMeta();
        });

        $('sampleBtn').addEventListener('click', function () {
            const lang = E.getLanguage($('srcLang').value);
            $('srcCode').value = lang.sample;
            currentFileName = '';
            updateMeta();
            toast('已填入 ' + lang.name + ' 示例');
        });

        $('openBtn').addEventListener('click', function () {
            const input = document.createElement('input');
            input.type = 'file';
            input.accept = '.py,.js,.mjs,.ts,.cs,.java,.cpp,.cc,.c,.h';
            input.addEventListener('change', function () {
                if (input.files && input.files[0]) loadFile(input.files[0]);
            });
            input.click();
        });

        $('srcCode').addEventListener('input', updateMeta);

        $('srcCode').addEventListener('keydown', function (e) {
            if (e.key !== 'Tab') return;
            e.preventDefault();
            const el = this;
            const start = el.selectionStart;
            const end = el.selectionEnd;
            el.value = el.value.slice(0, start) + '    ' + el.value.slice(end);
            el.selectionStart = el.selectionEnd = start + 4;
        });

        $('copyBtn').addEventListener('click', function () {
            const text = $('dstCode').value;
            if (!text) { toast('还没有可复制的结果', true); return; }
            copyText(text, false);
        });

        $('saveBtn').addEventListener('click', function () {
            const text = $('dstCode').value;
            if (!text) { toast('还没有可下载的结果', true); return; }

            const dst = E.getLanguage($('dstLang').value);
            const base = currentFileName ? currentFileName.replace(/\.[^.]+$/, '') : 'converted';
            downloadText(base + '_' + dst.id + dst.ext, text, 'text/plain');
            log('info', '转换', '结果已下载：' + base + '_' + dst.id + dst.ext);
        });

        $('buildLang').addEventListener('change', refreshToolOptions);
        $('buildTool').addEventListener('change', renderToolInfo);
        $('genCmdBtn').addEventListener('click', generateCommand);

        $('downloadBatBtn').addEventListener('click', function () {
            if (!lastBuildInfo) { toast('请先生成命令', true); return; }
            downloadText('HWZ打包-' + lastBuildInfo.name + '.bat', makeBat(lastBuildInfo), 'application/octet-stream');
            log('info', '打包', '已下载打包脚本：HWZ打包-' + lastBuildInfo.name + '.bat');
            toast('打包脚本已下载');
        });

        $('copyCmdBtn').addEventListener('click', function () {
            const text = $('buildCmd').textContent;
            if (!text || text.indexOf('尚未生成') >= 0) { toast('请先生成命令', true); return; }
            copyText(text, false);
        });

        $('recordSearch').addEventListener('input', renderRecords);
        $('recordKind').addEventListener('change', renderRecords);
        $('exportRecordsBtn').addEventListener('click', exportRecords);

        $('clearRecordsBtn').addEventListener('click', function () {
            if (records.length === 0) { toast('当前没有记录', true); return; }
            if (!confirm('确定清空全部 ' + records.length + ' 条记录吗？此操作不可撤销。')) return;
            records = [];
            saveRecords();
            renderRecords();
            log('info', '记录', '全部转换记录已清空');
        });

        $('logLevel').addEventListener('change', renderLogs);
        $('clearLogBtn').addEventListener('click', function () {
            logs.length = 0;
            renderLogs();
        });

        $('exportLogBtn').addEventListener('click', function () {
            const lines = [];
            lines.push('HWZ 代码互换工坊 · 网页版诊断报告');
            lines.push('署名：HWZ工作室');
            lines.push('='.repeat(56));
            lines.push('生成时间 : ' + new Date().toLocaleString('zh-CN'));
            lines.push('页面地址 : ' + location.href);
            lines.push('浏览器   : ' + navigator.userAgent);
            lines.push('语言     : ' + navigator.language);
            lines.push('='.repeat(56));
            lines.push('');
            logs.forEach(function (e) {
                lines.push(timeText(e.time) + ' [' + e.level.toUpperCase() + '] [' + e.category + '] ' + e.message);
            });
            downloadText('HWZ诊断报告-' + stamp() + '.txt', lines.join('\r\n'), 'text/plain');
            toast('诊断报告已导出');
        });

        // 拖入文件
        let dragDepth = 0;
        document.addEventListener('dragenter', function (e) {
            if (!e.dataTransfer || Array.prototype.indexOf.call(e.dataTransfer.types, 'Files') < 0) return;
            dragDepth++;
            $('drop-mask').classList.add('show');
        });

        document.addEventListener('dragover', function (e) {
            if (e.dataTransfer) e.dataTransfer.dropEffect = 'copy';
            e.preventDefault();
        });

        document.addEventListener('dragleave', function () {
            dragDepth--;
            if (dragDepth <= 0) { dragDepth = 0; $('drop-mask').classList.remove('show'); }
        });

        document.addEventListener('drop', function (e) {
            e.preventDefault();
            dragDepth = 0;
            $('drop-mask').classList.remove('show');

            if (!e.dataTransfer || !e.dataTransfer.files || e.dataTransfer.files.length === 0) return;

            const file = e.dataTransfer.files[0];

            if ($('page-build').classList.contains('active')) {
                $('buildFile').value = file.name;
                $('buildName').value = file.name.replace(/\.[^.]+$/, '');
                const guessed = E.guessFromPath(file.name);
                if (guessed) { $('buildLang').value = guessed.id; refreshToolOptions(); }
                toast('已填入文件名：' + file.name);
                return;
            }

            switchPage('convert');
            loadFile(file);
        });

        window.addEventListener('hashchange', function () {
            const name = (location.hash || '#convert').slice(1);
            if ($('page-' + name)) switchPage(name);
        });

        window.addEventListener('error', function (e) {
            log('error', '脚本', (e.message || '未知错误') + ' @ ' + (e.filename || '') + ':' + (e.lineno || 0));
        });

        window.addEventListener('unhandledrejection', function (e) {
            log('error', '脚本', '未处理的 Promise 异常：' + (e.reason && e.reason.message ? e.reason.message : String(e.reason)));
        });

        $('ann-close').addEventListener('click', closeAnnouncement);

        $('notice-close').addEventListener('click', closeNoticeBar);

        $('notice-detail').addEventListener('click', function () {
            if (!currentNotice) return;
            closeNoticeBar();
            showAnnouncement(currentNotice);
        });

        $('announcement-modal').addEventListener('click', function (e) {
            if (e.target === this) closeAnnouncement();
        });

        document.addEventListener('keydown', function (e) {
            if (e.key === 'Escape') closeAnnouncement();

            if ((e.ctrlKey || e.metaKey) && e.key === 'Enter') {
                if ($('page-convert').classList.contains('active')) {
                    e.preventDefault();
                    doConvert();
                }
            }
        });
    }

    // ═══════════ 公告 ═══════════

    const ANNOUNCEMENT_URL = 'https://api.github.com/repos/HWZ-ch/hwz-code-bridge/contents/announcement.json?ref=main';

    async function loadAnnouncement() {
        try {
            const resp = await fetch(ANNOUNCEMENT_URL, {
                headers: { 'Accept': 'application/vnd.github+json' }
            });

            if (!resp.ok) {
                log('info', '公告', '未获取到公告（HTTP ' + resp.status + '）');
                return;
            }

            const data = await resp.json();
            if (!data || !data.content) return;

            // GitHub 返回的 content 是带换行的 base64，需先清理再按 UTF-8 解码
            const base64 = String(data.content).replace(/\s/g, '');
            const bytes = Uint8Array.from(atob(base64), function (c) { return c.charCodeAt(0); });
            const text = new TextDecoder('utf-8').decode(bytes);

            const ann = JSON.parse(text);

            if (!ann || ann.enabled === false) return;
            if (!ann.title && !ann.content) return;

            if (ann.level === 'important') {
                // 重要级：模态弹窗，确保用户完整看到
                showAnnouncement(ann);
                log('info', '公告', '已展示重要公告（弹窗）：' + (ann.title || '（无标题）'));
            } else {
                // 普通 / 注意级：顶部提示条，不打断操作
                showNoticeBar(ann);
                log('info', '公告', '已展示提示条（' + ann.level + '）：' + (ann.title || '（无标题）'));
            }
        } catch (err) {
            log('info', '公告', '公告加载失败：' + (err && err.message ? err.message : String(err)));
        }
    }

    let currentNotice = null;

    /** 顶部提示条：普通级只显示标题，注意级额外展示部分正文 */
    function showNoticeBar(ann) {
        const isWarn = ann.level === 'warn';

        currentNotice = ann;

        $('notice-dot').style.background = isWarn ? 'var(--warn)' : 'var(--accent)';
        $('notice-title').textContent = ann.title || '公告';

        const summary = $('notice-summary');
        const detail = $('notice-detail');

        if (isWarn && ann.content) {
            summary.textContent = ann.content;
            summary.style.display = '-webkit-box';
            detail.style.display = '';
        } else {
            summary.textContent = '';
            summary.style.display = 'none';
            detail.style.display = 'none';
        }

        $('notice-bar').classList.add('show');
    }

    function closeNoticeBar() {
        $('notice-bar').classList.remove('show');
    }

    function showAnnouncement(ann) {
        const color = ann.level === 'warn' ? 'var(--warn)'
            : ann.level === 'important' ? 'var(--danger)'
                : 'var(--accent)';

        const bar = $('ann-bar');
        const badge = $('ann-badge');

        bar.style.background = color;
        badge.style.borderColor = color;
        badge.style.color = color;
        badge.textContent = ann.level === 'warn' ? '注意'
            : ann.level === 'important' ? '重要'
                : '公告';

        $('ann-title').textContent = ann.title || '公告';
        $('ann-content').textContent = ann.content || '';

        const time = (function () {
            if (!ann.publishedAt) return '';
            const d = new Date(ann.publishedAt);
            if (isNaN(d.getTime())) return '';
            return '发布于 ' + d.getFullYear() + '-' + pad2(d.getMonth() + 1) + '-' + pad2(d.getDate()) +
                ' ' + pad2(d.getHours()) + ':' + pad2(d.getMinutes());
        })();

        $('ann-time').textContent = time;

        const action = $('ann-action');
        if (ann.actionText && /^https?:\/\//i.test(ann.actionUrl || '')) {
            action.textContent = ann.actionText;
            action.href = ann.actionUrl;
            action.style.display = '';
        } else {
            action.style.display = 'none';
        }

        $('announcement-modal').classList.add('show');
    }

    function closeAnnouncement() {
        $('announcement-modal').classList.remove('show');
    }

    // ═══════════ 启动 ═══════════

    function boot() {
        applyTheme();
        initLanguages();
        applyEditorStyle();
        bindSettings();
        bindEvents();
        renderTools();
        renderAboutTools();
        initBuild();
        renderRecords();
        renderLogs();
        updateMeta();

        $('downloadBatBtn').disabled = true;
        $('copyCmdBtn').disabled = true;

        document.querySelectorAll('.page').forEach(function (p) { p.classList.remove('active'); });

        const initial = (location.hash || '#convert').slice(1);
        switchPage($('page-' + initial) ? initial : 'convert');

        log('info', '启动', 'HWZ 代码互换工坊 网页版 v1.0.2 已就绪');
        log('info', '环境', '浏览器：' + navigator.userAgent);
        log('info', '引擎', '已加载 ' + E.languages.length + ' 种语言，支持 ' + (E.languages.length * (E.languages.length - 1)) + ' 种转换组合');

        // 公告稍后加载，避免与首屏渲染争抢
        setTimeout(loadAnnouncement, 900);
    }

    if (document.readyState === 'loading') {
        document.addEventListener('DOMContentLoaded', boot);
    } else {
        boot();
    }
})();
