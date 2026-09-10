using System;
using System.Text;

namespace NoesisToolkit.CodeGen;

/// <summary>
/// Indent- and brace-owning source emitter. A brace can only be opened through a scope, so an emitter
/// cannot produce unbalanced output; <see cref="ToString"/> throws if a scope was left open.
/// </summary>
sealed class CodeWriter
{
    private const string Step = "    ";

    private readonly StringBuilder _sb = new();
    private int _depth;

    public CodeWriter Line()
    {
        _sb.Append('\n');
        return this;
    }

    public CodeWriter Line(string text)
    {
        if (text.IndexOf('\n') < 0)
        {
            AppendIndented(text);
            return this;
        }

        foreach (var slice in text.Split('\n'))
            AppendIndented(slice.TrimEnd('\r'));
        return this;
    }

    public Scope Block(string header) => Block(header, "}");

    /// <summary>A braced scope closed by <paramref name="close"/> — <c>};</c> for an initializer.</summary>
    public Scope Block(string header, string close)
    {
        Line(header);
        return Braces(close);
    }

    /// <summary>A braced scope with no header, for a declaration whose signature spans several lines.</summary>
    public Scope Braces(string close = "}")
    {
        Line("{");
        _depth++;
        return new Scope(this, close);
    }

    /// <summary>Indentation without braces, for a wrapped expression or a continuation line.</summary>
    public Scope Indented()
    {
        _depth++;
        return new Scope(this, null);
    }

    public override string ToString()
    {
        if (_depth != 0)
            throw new InvalidOperationException(
                $"CodeWriter has {_depth} unclosed scope(s); the emitted source would be unbalanced."
            );
        return _sb.ToString();
    }

    private void AppendIndented(string slice)
    {
        if (slice.Length > 0)
        {
            for (var i = 0; i < _depth; i++)
                _sb.Append(Step);
            _sb.Append(slice);
        }
        _sb.Append('\n');
    }

    private void Close(string? close)
    {
        _depth--;
        if (close != null)
            Line(close);
    }

    public readonly struct Scope : IDisposable
    {
        private readonly CodeWriter _writer;
        private readonly string? _close;

        internal Scope(CodeWriter writer, string? close)
        {
            _writer = writer;
            _close = close;
        }

        /// <summary>A <c>default</c> scope closes nothing, so an emitter can make a wrapper conditional.</summary>
        public void Dispose() => _writer?.Close(_close);
    }
}
