using System.Text;
using System.Globalization;

namespace Yacplayer.Terminal;

public readonly record struct CellColor(int AnsiCode, byte R, byte G, byte B, bool IsRgb)
{
    private CellColor(int ansiCode) : this(ansiCode, 0, 0, 0, false) { }
    public static CellColor Black { get; } = new(30);
    public static CellColor Red { get; } = new(31);
    public static CellColor Green { get; } = new(32);
    public static CellColor Yellow { get; } = new(33);
    public static CellColor Blue { get; } = new(34);
    public static CellColor Cyan { get; } = new(36);
    public static CellColor White { get; } = new(37);
    public static CellColor Default { get; } = new(39);
    public static CellColor BrightGreen { get; } = new(92);
    public static CellColor BrightWhite { get; } = new(97);
    public static CellColor Rgb(int red, int green, int blue) => new(0, (byte)Math.Clamp(red, 0, 255), (byte)Math.Clamp(green, 0, 255), (byte)Math.Clamp(blue, 0, 255), true);
    internal void AppendEscape(StringBuilder output) => output.Append(IsRgb ? $"\u001b[38;2;{R};{G};{B}m" : $"\u001b[{AnsiCode}m");
}
public enum CellBackground { Default = 49, Black = 40, Green = 42, BrightGreen = 102 }
public readonly record struct Cell(char Character, CellColor Color, CellBackground Background = CellBackground.Default, bool Bold = false);

public sealed class ScreenBuffer
{
    private readonly Cell[] _cells;
    private readonly bool[] _variableWidthRows;
    public ScreenBuffer(int width, int height)
    {
        Width = width; Height = height;
        _cells = Enumerable.Repeat(new Cell(' ', CellColor.Default), Math.Max(1, width * height)).ToArray();
        _variableWidthRows = new bool[Math.Max(1, height)];
    }
    public int Width { get; }
    public int Height { get; }
    public void Clear() => Array.Fill(_cells, new Cell(' ', CellColor.Default));
    public void Write(int x, int y, string? text) => Write(x, y, text, CellColor.Default);
    public void Write(int x, int y, string? text, CellColor color, CellBackground background = CellBackground.Default, bool bold = false)
    {
        if (text is null || y < 0 || y >= Height) return;
        foreach (var raw in text)
        {
            if (MayUseVariableDisplayWidth(raw)) _variableWidthRows[y] = true;
            var ch = raw is '\u001b' or '\r' or '\n' or '\t' || char.IsControl(raw) ? ' ' : raw;
            if (x >= Width) break;
            if (x >= 0) _cells[y * Width + x] = new Cell(ch, color, background, bold);
            x++;
        }
    }
    public char this[int x, int y] => GetCell(x, y).Character;
    public Cell GetCell(int x, int y) => _cells[y * Width + x];
    public string Diff(ScreenBuffer? previous)
    {
        var output = new StringBuilder();
        var activeColor = CellColor.Default; var activeBackground = CellBackground.Default; var activeBold = false;
        var sameSize = previous is not null && previous.Width == Width && previous.Height == Height;
        for (var y = 0; y < Height; y++)
        {
            var repaintWholeRow = sameSize && (_variableWidthRows[y] || previous!._variableWidthRows[y]);
            if (repaintWholeRow)
            {
                output.Append("\u001b[0m");
                output.Append($"\u001b[{y + 1};1H\u001b[2K");
                activeColor = CellColor.Default; activeBackground = CellBackground.Default; activeBold = false;
            }
            var x = 0;
            while (x < Width)
            {
                var cell = GetCell(x, y);
                if (sameSize && !repaintWholeRow && previous!.GetCell(x, y) == cell) { x++; continue; }

                output.Append($"\u001b[{y + 1};{x + 1}H");
                if (cell.Color != activeColor) { cell.Color.AppendEscape(output); activeColor = cell.Color; }
                if (cell.Background != activeBackground) { output.Append($"\u001b[{(int)cell.Background}m"); activeBackground = cell.Background; }
                if (cell.Bold != activeBold) { output.Append(cell.Bold ? "\u001b[1m" : "\u001b[22m"); activeBold = cell.Bold; }

                var runStyle = cell;
                do
                {
                    output.Append(cell.Character);
                    x++;
                    if (x >= Width) break;
                    cell = GetCell(x, y);
                }
                while (SameStyle(cell, runStyle) && (!sameSize || repaintWholeRow || previous!.GetCell(x, y) != cell));
            }
        }
        if (activeColor != CellColor.Default || activeBackground != CellBackground.Default || activeBold) output.Append("\u001b[0m");
        return output.ToString();
    }

    private static bool SameStyle(Cell left, Cell right) => left.Color == right.Color && left.Background == right.Background && left.Bold == right.Bold;

    private static bool MayUseVariableDisplayWidth(char value)
    {
        if (char.IsSurrogate(value)) return true;
        var category = char.GetUnicodeCategory(value);
        if (category is UnicodeCategory.NonSpacingMark or UnicodeCategory.SpacingCombiningMark or UnicodeCategory.EnclosingMark or UnicodeCategory.Format) return true;
        return value is >= '\u1100' and <= '\u115f'
            or >= '\u2e80' and <= '\ua4cf'
            or >= '\uac00' and <= '\ud7a3'
            or >= '\uf900' and <= '\ufaff'
            or >= '\ufe10' and <= '\ufe6f'
            or >= '\uff00' and <= '\uff60'
            or >= '\uffe0' and <= '\uffe6';
    }
}
