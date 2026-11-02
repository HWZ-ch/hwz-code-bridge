/* 静态一致性校验：确认 app.js 引用的元素 ID 都存在于 index.html */
'use strict';

const fs = require('fs');
const path = require('path');

const root = __dirname;
const html = fs.readFileSync(path.join(root, 'index.html'), 'utf8');
const app = fs.readFileSync(path.join(root, 'assets/app.js'), 'utf8');

const htmlIds = new Set();
const idRe = /\bid="([^"]+)"/g;
let m;
while ((m = idRe.exec(html)) !== null) htmlIds.add(m[1]);

const usedIds = new Set();
const useRe = /\$\('([A-Za-z0-9_-]+)'\)/g;
while ((m = useRe.exec(app)) !== null) usedIds.add(m[1]);

const missing = [];
usedIds.forEach(function (id) {
    if (!htmlIds.has(id)) missing.push(id);
});

// 页面容器
const pages = ['convert', 'build', 'tools', 'history', 'log', 'settings', 'about'];
const missingPages = pages.filter(function (p) { return !htmlIds.has('page-' + p); });

// 导航按钮与页面是否一一对应
const navPages = [];
const navRe = /data-page="([a-z]+)"/g;
while ((m = navRe.exec(html)) !== null) navPages.push(m[1]);

console.log('HTML 中 id 数量      :', htmlIds.size);
console.log('app.js 引用的 id 数量 :', usedIds.size);
console.log('缺失的页面容器        :', missingPages.length ? missingPages.join(', ') : '无');
console.log('导航项                :', navPages.join(', '));

if (missing.length > 0) {
    console.log('!! app.js 引用了不存在的 id:', missing.join(', '));
    process.exitCode = 1;
} else {
    console.log('✓ 所有引用的元素 id 均存在');
}

const badNav = navPages.filter(function (p) { return pages.indexOf(p) < 0; });
console.log(badNav.length ? '!! 导航指向不存在的页面: ' + badNav.join(', ') : '✓ 导航页面映射正确');

// 资源引用检查
const srcRe = /(?:src|href)="((?:assets\/)[^"]+)"/g;
const assets = [];
while ((m = srcRe.exec(html)) !== null) assets.push(m[1]);

const missingAssets = assets.filter(function (a) { return !fs.existsSync(path.join(root, a)); });
console.log('引用的资源            :', assets.join(', '));
console.log(missingAssets.length ? '!! 缺失资源: ' + missingAssets.join(', ') : '✓ 所有资源文件存在');

if (missingAssets.length) process.exitCode = 1;
