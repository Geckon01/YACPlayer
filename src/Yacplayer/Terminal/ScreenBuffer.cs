using System.Text;

namespace Yacplayer.Terminal;

public enum CellColor { Default = 39, Red = 31, Yellow = 33, Green = 32, Blue = 34, Cyan = 36, White = 37 }
public readonly record struct Cell(char Character, CellColor Color);

public sealed class ScreenBuffer
{
    private readonly Cell[] _cells;
    public ScreenBuffer(int width, int height)
    {
        Width = width; Height = height;
        _cells = Enumerable.Repeat(new Cell(' ', CellColor.Default), Math.Max(1, width * height)).ToArray();
    }
    public int Width { get; }
    public int Height { get; }
    public void Clear() => Array.Fill(_cells, new Cell(' ', CellColor.Default));
    public void Write(int x, int y, string? text, CellColor color = CellColor.Default)
    {
        if (text is null || y < 0 || y >= Height) return;
        foreach (var raw in text)
        {
            var ch = raw is '\u001b' or '\r' or '\n' or '\t' || char.IsControl(raw) ? ' ' : raw;
            if (x >= Width) break;
            if (x >= 0) _cells[y * Width + x] = new Cell(ch, color);
            x++;
        }
    }
    public char this[int x, int y] => GetCell(x, y).Character;
    public Cell GetCell(int x, int y) => _cells[y * Width + x];
    public string Diff(ScreenBuffer? previous)
    {
        var output = new StringBuilder();
        var activeColor = CellColor.Default;
        var sameSize = previous is not null && previous.Width == Width && previous.Height == Height;
        for (var y = 0; y < Height; y++)
        {
            var x = 0;
            while (x < Width)
            {
                var cell = GetCell(x, y);
                if (sameSize && previous!.GetCell(x, y) == cell) { x++; continue; }

                output.Append($"\u001b[{y + 1};{x + 1}H");
                if (cell.Color != activeColor) { output.Append($"\u001b[{(int)cell.Color}m"); activeColor = cell.Color; }

                var runColor = cell.Color;
                do
                {
                    output.Append(cell.Character);
                    x++;
                    if (x >= Width) break;
                    cell = GetCell(x, y);
                }
                while (cell.Color == runColor && (!sameSize || previous!.GetCell(x, y) != cell));
            }
        }
        if (activeColor != CellColor.Default) output.Append("\u001b[39m");
        return output.ToString();
    }
}
