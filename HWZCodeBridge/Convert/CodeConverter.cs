using System.Diagnostics;
using HWZCodeBridge.Core;

namespace HWZCodeBridge.Convert;

/// <summary>代码互换的统一入口，负责路线分派、计时与错误兜底。</summary>
public static class CodeConverter
{
    public static ConversionResult Convert(string? source, LanguageInfo from, LanguageInfo to)
    {
        var sw = Stopwatch.StartNew();

        if (string.IsNullOrWhiteSpace(source))
        {
            return ConversionResult.Fail("请先输入代码，或把代码文件拖入窗口。");
        }

        if (string.Equals(from.Id, to.Id, StringComparison.OrdinalIgnoreCase))
        {
            sw.Stop();
            return new ConversionResult
            {
                Success = true,
                Code = source,
                Message = "源语言与目标语言相同，内容原样返回。",
                SourceLines = CountLines(source),
                TargetLines = CountLines(source),
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }

        var notes = new NoteBag();

        try
        {
            string code;

            if (from.Id == "python")
            {
                var nodes = PythonParser.Parse(source, notes);
                code = new PythonToBraceConverter(to, notes).Convert(nodes);
            }
            else if (to.Id == "python")
            {
                code = new BraceToPythonConverter(from, notes).Convert(source);
            }
            else
            {
                code = new BraceToBraceConverter(from, to, notes).Convert(source);
            }

            sw.Stop();

            if (string.IsNullOrWhiteSpace(code))
            {
                return ConversionResult.Fail("转换结果为空，可能是源文件结构与目标语言差异过大，请检查输入内容。");
            }

            Logger.Info("转换", $"{from.Name} → {to.Name} 完成，{CountLines(source)} 行 → {CountLines(code)} 行，{notes.Notes.Count} 条提示");

            return new ConversionResult
            {
                Success = true,
                Code = code,
                Message = $"转换完成：{from.Name} → {to.Name}",
                Notes = notes.Notes,
                SourceLines = CountLines(source),
                TargetLines = CountLines(code),
                ElapsedMs = sw.ElapsedMilliseconds
            };
        }
        catch (Exception ex)
        {
            sw.Stop();
            Logger.Error("转换", $"转换 {from.Name} → {to.Name} 失败", ex.ToString());
            return ConversionResult.Fail($"转换过程中出现异常：{ex.Message}");
        }
    }

    public static int CountLines(string? text) =>
        string.IsNullOrEmpty(text) ? 0 : text.Replace("\r\n", "\n").Split('\n').Length;
}
