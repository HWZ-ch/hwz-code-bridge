/* 网页版引擎验证脚本：node test.js */
'use strict';

const engine = require('./assets/engine-convert.js');

const pySample = [
    '# 计算斐波那契数列',
    'def fib(n):',
    '    a = 0',
    '    b = 1',
    '    total = 0',
    '    for i in range(n):',
    '        print(a, end=" ")',
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
    '# 主程序',
    'if __name__ == "__main__":',
    '    name = "HWZ"',
    '    result = fib(8)',
    '    print(f"result of {name} is {result}")',
    '    print(classify(result))'
].join('\n');

const csSample = [
    '// 简单计算器',
    'using System;',
    '',
    'class Calculator',
    '{',
    '    public int Add(int a, int b)',
    '    {',
    '        return a + b;',
    '    }',
    '',
    '    static void Main(string[] args)',
    '    {',
    '        Calculator calc = new Calculator();',
    '        int x = 12;',
    '        int y = 30;',
    '        int sum = calc.Add(x, y);',
    '        Console.WriteLine(sum);',
    '        if (sum > 40)',
    '        {',
    '            Console.WriteLine("big");',
    '        }',
    '        else',
    '        {',
    '            Console.WriteLine("small");',
    '        }',
    '        for (int i = 0; i < 3; i++)',
    '        {',
    '            Console.WriteLine(i);',
    '        }',
    '    }',
    '}'
].join('\n');

function show(title, result) {
    console.log('#'.repeat(20) + ' ' + title + ' ' + '#'.repeat(20));
    if (!result.success) {
        console.log('!! 失败：' + result.message);
    } else {
        console.log(result.code);
    }
    console.log('-- ' + (result.success ? 'OK' : 'FAIL') + ' / 提示 ' + result.notes.length + ' 条 / ' + result.elapsedMs + ' ms');
    result.notes.slice(0, 6).forEach(function (n) {
        console.log('   ' + engine.noteText(n));
    });
    console.log();
}

['javascript', 'typescript', 'csharp', 'java', 'cpp'].forEach(function (t) {
    show('Python → ' + t.toUpperCase(), engine.convert(pySample, 'python', t));
});

['python', 'javascript', 'java'].forEach(function (t) {
    show('C# → ' + t.toUpperCase(), engine.convert(csSample, 'csharp', t));
});

console.log('='.repeat(70));
console.log('往返：Python → C# → Python');
console.log('='.repeat(70));
const forward = engine.convert(pySample, 'python', 'csharp');
const back = engine.convert(forward.code, 'csharp', 'python');
console.log(back.code);
console.log('-- ' + (back.success ? 'OK' : 'FAIL'));

console.log('='.repeat(70));
console.log('边界：空输入 / 同语言 / 复杂语法');
console.log('='.repeat(70));

console.log('空输入 ->', JSON.stringify(engine.convert('', 'python', 'csharp').message));
console.log('同语言 ->', JSON.stringify(engine.convert('x = 1', 'python', 'python').message));

const tricky = engine.convert([
    'try:',
    '    value = int(input("数字: "))',
    'except ValueError as e:',
    '    print(e)',
    'finally:',
    '    print("done")',
    '',
    'items = [1, 2, 3]',
    'items.append(4)',
    'print(len(items))',
    'while value > 0:',
    '    value -= 1'
].join('\n'), 'python', 'csharp');
console.log(tricky.code);
tricky.notes.forEach(function (n) { console.log('   ' + engine.noteText(n)); });
