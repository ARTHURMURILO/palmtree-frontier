using System;
using System.Collections.Generic;
using System.Numerics;
using System.Text;
using System.Threading.Tasks;
using Content.Client.Resources;
using Content.Shared.Input;
using Robust.Client.Graphics;
using Robust.Client.ResourceManagement;
using Robust.Client.UserInterface;
using Robust.Shared.Input;
using Robust.Shared.IoC;
using Robust.Shared.Maths;
using Robust.Shared.Utility;

namespace Content.Client._PS.Circuitry.UI;

/// <summary>
/// Small content-side Lua editor. It exists because the engine TextEdit control cannot render
/// per-token colors, line numbers and a code-focused input model.
/// </summary>
public sealed class LuaCodeEditor : Control
{
    private const int FontSize = 12;
    private const int TabSize = 4;
    private const int MaxUndo = 100;

    private static readonly Color DefaultColor = Color.FromHex("#D4D4D4");
    private static readonly Color CommentColor = Color.FromHex("#6A9955");
    private static readonly Color StringColor = Color.FromHex("#CE9178");
    private static readonly Color NumberColor = Color.FromHex("#B5CEA8");
    private static readonly Color KeywordColor = Color.FromHex("#569CD6");
    private static readonly Color BuiltinColor = Color.FromHex("#4FC1FF");
    private static readonly Color FunctionColor = Color.FromHex("#DCDCAA");
    private static readonly Color GutterColor = Color.FromHex("#0F0F14");
    private static readonly Color GutterTextColor = Color.FromHex("#858585");
    private static readonly Color CursorColor = Color.FromHex("#FFFFFF");
    private static readonly Color SelectionColor = Color.FromHex("#264F78A0");

    private static readonly HashSet<string> Keywords = new()
    {
        "and", "break", "do", "else", "elseif", "end", "false", "for", "function", "goto",
        "if", "in", "local", "nil", "not", "or", "repeat", "return", "then", "true",
        "until", "while",
    };

    private static readonly HashSet<string> Builtins = new()
    {
        "robot", "self", "print", "log", "ipairs", "pairs", "type", "tostring", "tonumber",
        "math", "string", "table",
    };
    private static readonly HashSet<string> ApiFunctions = new()
    {
        // robot.*
        "self", "position", "power", "health", "has_module", "move", "walk_to", "walk_to_async",
        "stop",
        "walk_status", "scan", "scan_mobs", "scan_items", "scan_structures", "scan_doors",
        "find", "inspect", "can_see", "can_hear", "nearest_mob", "nearest_person",
        "nearest_item", "nearest_structure", "nearest_door", "distance", "position_of",
        "can_reach", "get_tile_relative", "tile", "entities_on_tile", "decals", "scan_puddles",
        "has_job", "has_access", "wanted_status", "solution", "item_status", "door_status",
        "can_open", "poll_event", "wait_event", "say", "radio", "radio_channels", "pickup",
        "drop", "held", "use", "use_async", "use_item", "use_item_async", "attack", "shoot",
        "interact", "interact_async", "harvest", "harvest_async", "plant", "plant_async",
        "water", "trigger", "trigger_async", "send_signal", "inject", "inject_async",
        "action_busy", "action_marker", "action_status",
        "analyze_plant", "analyze_chemicals",
        "analyze_medical", "analyze_gas", "chem_master_status", "chem_master_set_mode",
        "chem_master_set_pill_type", "chem_master_transfer", "chem_master_create_pills",
        "chem_master_bottle", "dispense", "beakers", "reagents", "transfer_to", "transfer_from",
        "transfer",

        // GLua-style table/string helpers and type shorthands.
        "HasValue", "Contains", "IsEmpty", "Count", "GetKeys", "KeyFromValue", "Random",
        "Add", "RemoveByValue", "Trim", "StartsWith", "StartWith", "EndsWith", "Split",
        "istable", "isstring", "isnumber", "isbool", "isfunction",
    };

    [Dependency] private readonly IResourceCache _cache = default!;
    [Dependency] private readonly IClipboardManager _clipboard = default!;

    private readonly List<string> _lines = new() { string.Empty };
    private readonly List<string> _undo = new();
    private readonly List<string> _redo = new();
    private Font _font = default!;

    private int _cursorLine;
    private int _cursorColumn;
    private int _anchorLine;
    private int _anchorColumn;
    private int _scrollLine;
    private bool _editable = true;
    private bool _dragging;
    private bool _suppressChanged;

    public event Action<string>? TextChanged;
    public event Action? SaveRequested;
    public event Action? RunRequested;

    public LuaCodeEditor()
    {
        IoCManager.InjectDependencies(this);

        _font = _cache.GetFont("/EngineFonts/NotoSans/NotoSansMono-Regular.ttf", FontSize);

        CanKeyboardFocus = true;
        KeyboardFocusOnClick = true;
        MouseFilter = MouseFilterMode.Stop;
        RectClipContent = true;
        MinSize = new Vector2(120, 80);
    }

    public bool Editable
    {
        get => _editable;
        set
        {
            if (_editable == value)
                return;

            _editable = value;

            if (!HasKeyboardFocus())
                return;

            if (value)
                Root?.Window?.TextInputStart();
            else
                Root?.Window?.TextInputStop();
        }
    }

    public string Text => string.Join('\n', _lines);

    public int CursorLine => _cursorLine;
    public int CursorColumn => _cursorColumn;

    public static string NormalizeText(string text)
    {
        return text
            .Replace("\r\n", "\n")
            .Replace('\r', '\n')
            .Replace("\t", new string(' ', TabSize));
    }

    public void SetText(string text)
    {
        text = NormalizeText(text);
        _suppressChanged = true;
        SetLinesFromText(text);
        _suppressChanged = false;
        _cursorLine = _cursorColumn = 0;
        _anchorLine = _anchorColumn = 0;
        _scrollLine = 0;
        _undo.Clear();
        _redo.Clear();
        InvalidateMeasure();
    }

    public void ClearHistory()
    {
        _undo.Clear();
        _redo.Clear();
    }

    protected override Vector2 MeasureOverride(Vector2 availableSize)
    {
        return MinSize;
    }

    protected override void Draw(DrawingHandleScreen handle)
    {
        var box = PixelSizeBox;
        var lineHeight = _font.GetLineHeight(UIScale);
        var gutterWidth = GetGutterWidth();

        handle.DrawRect(new UIBox2(box.Left, box.Top, box.Left + gutterWidth, box.Bottom), GutterColor);
        handle.DrawLine(
            new Vector2(box.Left + gutterWidth, box.Top),
            new Vector2(box.Left + gutterWidth, box.Bottom),
            Color.FromHex("#2B2B33"));

        var visibleLines = Math.Max(1, (int)(box.Height / lineHeight));
        ClampScroll(visibleLines);
        var selection = GetSelectionRange();
        var textX = box.Left + gutterWidth + (4f * UIScale);

        for (var offset = 0; offset < visibleLines; offset++)
        {
            var lineIndex = _scrollLine + offset;
            if (lineIndex >= _lines.Count)
                break;

            // DrawString treats Y as the top of the line and adds the font ascent itself.
            var y = box.Top + (offset * lineHeight);
            var line = _lines[lineIndex];

            var number = (lineIndex + 1).ToString();
            var numberWidth = MeasureText(number);
            handle.DrawString(
                _font,
                new Vector2(box.Left + gutterWidth - numberWidth - (4f * UIScale), y),
                number,
                UIScale,
                GutterTextColor);

            if (selection != null)
            {
                var (startLine, startColumn, endLine, endColumn) = selection.Value;
                if (lineIndex >= startLine && lineIndex <= endLine)
                {
                    var from = lineIndex == startLine ? startColumn : 0;
                    var to = lineIndex == endLine ? endColumn : line.Length;
                    if (to > from)
                    {
                        handle.DrawRect(
                            new UIBox2(
                                textX + MeasureText(line[..from]),
                                y,
                                textX + MeasureText(line[..to]),
                                y + lineHeight),
                            SelectionColor);
                    }
                }
            }

            var x = textX;
            foreach (var token in Tokenize(line))
            {
                x += handle.DrawString(_font, new Vector2(x, y), token.Text, UIScale, token.Color).X;
            }
        }

        if (lineIndexVisible(_cursorLine, visibleLines))
        {
            var cursorX = textX + MeasureText(_lines[_cursorLine][.._cursorColumn]);
            var cursorY = box.Top + ((_cursorLine - _scrollLine) * lineHeight);
            handle.DrawRect(
                new UIBox2(cursorX, cursorY + 2f, cursorX + MathF.Max(1f, UIScale), cursorY + lineHeight - 2f),
                CursorColor);
        }
    }

    protected override void TextEntered(GUITextEnteredEventArgs args)
    {
        base.TextEntered(args);
        if (!_editable || string.IsNullOrEmpty(args.Text))
            return;

        InsertText(args.Text.Replace("\t", new string(' ', TabSize)));
    }

    protected override void KeyBindDown(GUIBoundKeyEventArgs args)
    {
        base.KeyBindDown(args);
        if (args.Handled)
            return;

        if (args.Function == EngineKeyFunctions.UIClick)
        {
            GrabKeyboardFocus();
            _dragging = true;
            SetCursorFromPosition(args.RelativePixelPosition, select: false);
            _anchorLine = _cursorLine;
            _anchorColumn = _cursorColumn;
            args.Handle();
            return;
        }

        if (!_editable)
            return;

        if (args.Function == EngineKeyFunctions.TextCursorLeft)
            MoveCursor(0, -1, false);
        else if (args.Function == EngineKeyFunctions.TextCursorRight)
            MoveCursor(0, 1, false);
        else if (args.Function == EngineKeyFunctions.TextCursorUp)
            MoveCursor(-1, 0, false);
        else if (args.Function == EngineKeyFunctions.TextCursorDown)
            MoveCursor(1, 0, false);
        else if (args.Function == EngineKeyFunctions.TextCursorSelectLeft)
            MoveCursor(0, -1, true);
        else if (args.Function == EngineKeyFunctions.TextCursorSelectRight)
            MoveCursor(0, 1, true);
        else if (args.Function == EngineKeyFunctions.TextCursorSelectUp)
            MoveCursor(-1, 0, true);
        else if (args.Function == EngineKeyFunctions.TextCursorSelectDown)
            MoveCursor(1, 0, true);
        else if (args.Function == EngineKeyFunctions.TextCursorBegin)
            SetCursor(_cursorLine, 0, false);
        else if (args.Function == EngineKeyFunctions.TextCursorEnd)
            SetCursor(_cursorLine, _lines[_cursorLine].Length, false);
        else if (args.Function == EngineKeyFunctions.TextCursorSelectBegin)
            SetCursor(_cursorLine, 0, true);
        else if (args.Function == EngineKeyFunctions.TextCursorSelectEnd)
            SetCursor(_cursorLine, _lines[_cursorLine].Length, true);
        else if (args.Function == EngineKeyFunctions.TextBackspace)
            Backspace();
        else if (args.Function == EngineKeyFunctions.TextDelete)
            Delete();
        else if (args.Function == EngineKeyFunctions.TextNewline)
            InsertText("\n" + GetIndent(_cursorLine));
        else if (args.Function == EngineKeyFunctions.TextTabComplete)
            InsertText(new string(' ', TabSize));
        else if (args.Function == EngineKeyFunctions.TextSelectAll)
            SelectAll();
        else if (args.Function == EngineKeyFunctions.TextCopy)
            CopySelection();
        else if (args.Function == EngineKeyFunctions.TextCut)
            CutSelection();
        else if (args.Function == EngineKeyFunctions.TextPaste)
            _ = PasteNormalized();
        else if (args.Function == ContentKeyFunctions.CircuitEditorSave)
            SaveRequested?.Invoke();
        else if (args.Function == EngineKeyFunctions.MultilineTextSubmit)
            RunRequested?.Invoke();
        else if (args.Function == ContentKeyFunctions.CircuitEditorUndo)
            Undo();
        else if (args.Function == ContentKeyFunctions.CircuitEditorRedo)
            Redo();
        else
            return;

        args.Handle();
        EnsureCursorVisible();
    }

    protected override void KeyBindUp(GUIBoundKeyEventArgs args)
    {
        base.KeyBindUp(args);

        if (args.Function == EngineKeyFunctions.UIClick)
            _dragging = false;
    }

    protected override void KeyboardFocusEntered()
    {
        base.KeyboardFocusEntered();

        if (_editable)
            Root?.Window?.TextInputStart();
    }

    protected override void KeyboardFocusExited()
    {
        base.KeyboardFocusExited();

        Root?.Window?.TextInputStop();
    }

    protected override void MouseMove(GUIMouseMoveEventArgs args)
    {
        base.MouseMove(args);

        if (_dragging)
            SetCursorFromPosition(args.RelativePixelPosition, select: true);
    }

    protected override void MouseWheel(GUIMouseWheelEventArgs args)
    {
        base.MouseWheel(args);
        _scrollLine = Math.Max(0, _scrollLine - (int)MathF.Sign(args.Delta.Y));
        args.Handle();
    }

    private void InsertText(string text)
    {
        if (text.Length == 0)
            return;

        PushUndo();
        ReplaceSelection(text);
    }

    private void ReplaceSelection(string replacement)
    {
        var all = Text;
        var start = GetIndex(SelectionStart());
        var end = GetIndex(SelectionEnd());
        var updated = all[..start] + replacement + all[end..];
        SetLinesFromText(updated);
        var newCursor = start + replacement.Length;
        (_cursorLine, _cursorColumn) = PositionFromIndex(newCursor);
        (_anchorLine, _anchorColumn) = (_cursorLine, _cursorColumn);
        Changed();
    }

    private void Backspace()
    {
        var start = SelectionStart();
        var end = SelectionEnd();
        if (start != end)
        {
            PushUndo();
            ReplaceSelection(string.Empty);
            return;
        }

        var index = GetIndex((_cursorLine, _cursorColumn));
        if (index <= 0)
            return;

        PushUndo();
        var all = Text;
        var removed = all[..(index - 1)] + all[index..];
        SetLinesFromText(removed);
        (_cursorLine, _cursorColumn) = PositionFromIndex(index - 1);
        (_anchorLine, _anchorColumn) = (_cursorLine, _cursorColumn);
        Changed();
    }

    private void Delete()
    {
        var start = SelectionStart();
        var end = SelectionEnd();
        if (start != end)
        {
            PushUndo();
            ReplaceSelection(string.Empty);
            return;
        }

        var index = GetIndex((_cursorLine, _cursorColumn));
        var all = Text;
        if (index >= all.Length)
            return;

        PushUndo();
        var removed = all[..index] + all[(index + 1)..];
        SetLinesFromText(removed);
        (_cursorLine, _cursorColumn) = PositionFromIndex(index);
        (_anchorLine, _anchorColumn) = (_cursorLine, _cursorColumn);
        Changed();
    }

    private void SelectAll()
    {
        _anchorLine = 0;
        _anchorColumn = 0;
        _cursorLine = _lines.Count - 1;
        _cursorColumn = _lines[^1].Length;
    }

    private void CopySelection()
    {
        var text = GetSelectedText();
        if (text.Length > 0)
            _clipboard.SetText(text);
    }

    private void CutSelection()
    {
        var text = GetSelectedText();
        if (text.Length == 0)
            return;

        _clipboard.SetText(text);
        PushUndo();
        ReplaceSelection(string.Empty);
    }

    private async Task PasteNormalized()
    {
        var text = await _clipboard.GetText();
        if (text == null)
            return;

        text = NormalizeText(text);
        InsertText(text);
    }

    private void Undo()
    {
        if (_undo.Count == 0)
            return;

        _redo.Add(Text);
        SetLinesFromText(_undo[^1]);
        _undo.RemoveAt(_undo.Count - 1);
        ClampCursor();
        Changed();
    }

    private void Redo()
    {
        if (_redo.Count == 0)
            return;

        _undo.Add(Text);
        SetLinesFromText(_redo[^1]);
        _redo.RemoveAt(_redo.Count - 1);
        ClampCursor();
        Changed();
    }

    private void PushUndo()
    {
        _undo.Add(Text);
        if (_undo.Count > MaxUndo)
            _undo.RemoveAt(0);

        _redo.Clear();
    }

    private void Changed()
    {
        if (_suppressChanged)
            return;

        TextChanged?.Invoke(Text);
        InvalidateMeasure();
    }

    private void SetCursor(int line, int column, bool select)
    {
        _cursorLine = Math.Clamp(line, 0, _lines.Count - 1);
        _cursorColumn = Math.Clamp(column, 0, _lines[_cursorLine].Length);
        if (!select)
        {
            _anchorLine = _cursorLine;
            _anchorColumn = _cursorColumn;
        }
    }

    private void MoveCursor(int lineDelta, int columnDelta, bool select)
    {
        if (lineDelta != 0)
        {
            var line = Math.Clamp(_cursorLine + lineDelta, 0, _lines.Count - 1);
            SetCursor(line, Math.Min(_cursorColumn, _lines[line].Length), select);
            return;
        }

        var column = _cursorColumn + columnDelta;
        var currentLine = _cursorLine;
        if (column < 0)
        {
            if (currentLine == 0)
                return;

            currentLine--;
            column = _lines[currentLine].Length;
        }
        else if (column > _lines[currentLine].Length)
        {
            if (currentLine >= _lines.Count - 1)
                return;

            currentLine++;
            column = 0;
        }

        SetCursor(currentLine, column, select);
    }

    private void SetCursorFromPosition(Vector2 position, bool select)
    {
        var box = PixelSizeBox;
        var lineHeight = _font.GetLineHeight(UIScale);
        var textX = box.Left + GetGutterWidth() + (4f * UIScale);

        var line = (int)((position.Y - box.Top) / lineHeight) + _scrollLine;
        line = Math.Clamp(line, 0, _lines.Count - 1);

        var column = GetColumnAtX(_lines[line], position.X - textX);

        SetCursor(line, column, select);
        EnsureCursorVisible();
    }

    private int GetColumnAtX(string line, float localX)
    {
        if (localX <= 0f)
            return 0;

        var index = 0;
        var position = 0f;
        foreach (var rune in line.EnumerateRunes())
        {
            if (!_font.TryGetCharMetrics(rune, UIScale, out var metrics))
            {
                index += rune.Utf16SequenceLength;
                continue;
            }

            var advance = metrics.Advance;
            if (localX < position + advance)
            {
                return localX < position + (advance / 2f)
                    ? index
                    : index + rune.Utf16SequenceLength;
            }

            position += advance;
            index += rune.Utf16SequenceLength;
        }

        return line.Length;
    }

    private float MeasureText(string text)
    {
        var width = 0f;
        foreach (var rune in text.EnumerateRunes())
        {
            if (_font.TryGetCharMetrics(rune, UIScale, out var metrics))
                width += metrics.Advance;
        }

        return width;
    }

    private float GetGutterWidth()
    {
        var digits = Math.Max(2, _lines.Count.ToString().Length);
        return MeasureText(new string('0', digits)) + (10f * UIScale);
    }

    private void EnsureCursorVisible()
    {
        var lineHeight = _font.GetLineHeight(UIScale);
        var visibleLines = Math.Max(1, (int)(PixelSize.Y / lineHeight));
        if (_cursorLine < _scrollLine)
            _scrollLine = _cursorLine;
        else if (_cursorLine >= _scrollLine + visibleLines)
            _scrollLine = _cursorLine - visibleLines + 1;
    }

    private void ClampScroll(int visibleLines)
    {
        _scrollLine = Math.Clamp(_scrollLine, 0, Math.Max(0, _lines.Count - visibleLines));
    }

    private void ClampCursor()
    {
        _cursorLine = Math.Clamp(_cursorLine, 0, _lines.Count - 1);
        _cursorColumn = Math.Clamp(_cursorColumn, 0, _lines[_cursorLine].Length);
        _anchorLine = Math.Clamp(_anchorLine, 0, _lines.Count - 1);
        _anchorColumn = Math.Clamp(_anchorColumn, 0, _lines[_anchorLine].Length);
    }

    private (int Line, int Column) SelectionStart()
    {
        return Compare(_anchorLine, _anchorColumn, _cursorLine, _cursorColumn) <= 0
            ? (_anchorLine, _anchorColumn)
            : (_cursorLine, _cursorColumn);
    }

    private (int Line, int Column) SelectionEnd()
    {
        return Compare(_anchorLine, _anchorColumn, _cursorLine, _cursorColumn) <= 0
            ? (_cursorLine, _cursorColumn)
            : (_anchorLine, _anchorColumn);
    }

    private (int Line, int Column, int EndLine, int EndColumn)? GetSelectionRange()
    {
        var start = SelectionStart();
        var end = SelectionEnd();
        if (start == end)
            return null;

        return (start.Line, start.Column, end.Line, end.Column);
    }

    private string GetSelectedText()
    {
        var start = GetIndex(SelectionStart());
        var end = GetIndex(SelectionEnd());
        return end <= start ? string.Empty : Text[start..end];
    }

    private int GetIndex((int Line, int Column) position)
    {
        var index = 0;
        for (var i = 0; i < position.Line; i++)
            index += _lines[i].Length + 1;

        return index + Math.Min(position.Column, _lines[position.Line].Length);
    }

    private (int Line, int Column) PositionFromIndex(int index)
    {
        var remaining = index;
        for (var line = 0; line < _lines.Count; line++)
        {
            if (remaining <= _lines[line].Length)
                return (line, remaining);

            remaining -= _lines[line].Length + 1;
        }

        return (_lines.Count - 1, _lines[^1].Length);
    }

    private string GetIndent(int line)
    {
        var text = _lines[line];
        var spaces = 0;
        while (spaces < text.Length && text[spaces] == ' ')
            spaces++;

        return new string(' ', spaces);
    }

    private void SetLinesFromText(string text)
    {
        _lines.Clear();
        _lines.AddRange(text.Split('\n'));
        if (_lines.Count == 0)
            _lines.Add(string.Empty);
    }

    private bool lineIndexVisible(int line, int visibleLines)
    {
        return line >= _scrollLine && line < _scrollLine + visibleLines;
    }

    private static int Compare(int lineA, int columnA, int lineB, int columnB)
    {
        var line = lineA.CompareTo(lineB);
        return line != 0 ? line : columnA.CompareTo(columnB);
    }

    private static IEnumerable<(string Text, Color Color)> Tokenize(string line)
    {
        var index = 0;
        while (index < line.Length)
        {
            var start = index;
            var character = line[index];

            if (character == '-' && index + 1 < line.Length && line[index + 1] == '-')
            {
                yield return (line[index..], CommentColor);
                yield break;
            }

            if (character is '"' or '\'')
            {
                index++;
                var escaped = false;
                while (index < line.Length)
                {
                    var current = line[index++];
                    if (escaped)
                    {
                        escaped = false;
                        continue;
                    }

                    if (current == '\\')
                    {
                        escaped = true;
                        continue;
                    }

                    if (current == character)
                        break;
                }

                yield return (line[start..index], StringColor);
                continue;
            }

            if (char.IsDigit(character) ||
                character == '.' && index + 1 < line.Length && char.IsDigit(line[index + 1]))
            {
                index++;
                while (index < line.Length &&
                       (char.IsLetterOrDigit(line[index]) || line[index] is '.' or '_'))
                {
                    index++;
                }

                yield return (line[start..index], NumberColor);
                continue;
            }

            if (char.IsLetter(character) || character == '_')
            {
                index++;
                while (index < line.Length &&
                       (char.IsLetterOrDigit(line[index]) || line[index] == '_'))
                {
                    index++;
                }

                var word = line[start..index];
                var color = Keywords.Contains(word)
                    ? KeywordColor
                    : Builtins.Contains(word)
                        ? BuiltinColor
                        : ApiFunctions.Contains(word) || NextNonSpace(line, index) == '('
                            ? FunctionColor
                            : DefaultColor;

                yield return (word, color);
                continue;
            }

            index++;
            yield return (line[start..index], DefaultColor);
        }
    }

    private static char NextNonSpace(string line, int index)
    {
        while (index < line.Length && char.IsWhiteSpace(line[index]))
            index++;

        return index < line.Length ? line[index] : '\0';
    }
}
