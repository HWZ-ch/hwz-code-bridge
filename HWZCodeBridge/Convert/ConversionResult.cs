namespace HWZCodeBridge.Convert;

public enum NoteLevel
{
    Info,
    Warn
}

/// <summary>转换过程中的提示/告警，用于告知用户哪些行需要人工复核。</summary>
public sealed record ConversionNote(int Line, NoteLevel Level, string Message)
{
    public override string ToString() =>
        Line > 0 ? $"第 {Line} 行 · {Message}" : Message;
}

public sealed class ConversionResult
{
    public bool Success { get; init; }
    public string Code { get; init; } = "";
    public string Message { get; init; } = "";
    public IReadOnlyList<ConversionNote> Notes { get; init; } = Array.Empty<ConversionNote>();
    public int SourceLines { get; init; }
    public int TargetLines { get; init; }
    public long ElapsedMs { get; init; }

    public int WarnCount => Notes.Count(n => n.Level == NoteLevel.Warn);

    public static ConversionResult Fail(string message) => new()
    {
        Success = false,
        Message = message
    };
}

/// <summary>转换过程中的告警收集器。</summary>
internal sealed class NoteBag
{
    private readonly List<ConversionNote> _notes = new();

    public void Info(int line, string message) => _notes.Add(new ConversionNote(line, NoteLevel.Info, message));
    public void Warn(int line, string message) => _notes.Add(new ConversionNote(line, NoteLevel.Warn, message));

    public IReadOnlyList<ConversionNote> Notes => _notes;
}
