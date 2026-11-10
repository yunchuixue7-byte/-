using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Shapes;
using Gomoku.Core.Domain.Models;

namespace Gomoku.App.Rendering;

/// <summary>棋盘绘制工具：网格、星位与立体棋子。对局界面与复盘界面共用同一套画法，保证观感一致。</summary>
public static class BoardRenderer
{
    /// <summary>棋盘画布边长（逻辑像素）。</summary>
    public const double CanvasSize = 570;

    /// <summary>相邻交叉点间距。</summary>
    public const double CellSize = CanvasSize / (Board.Size - 1);

    /// <summary>棋子半径。</summary>
    public const double StoneRadius = 14;

    /// <summary>球面径向渐变：光从左上照入，中心高光、边缘渐暗，形成立体棋子。</summary>
    public static Brush CreateStoneBrush(Stone stone)
    {
        var brush = new RadialGradientBrush
        {
            GradientOrigin = new Point(0.35, 0.3),
            Center = new Point(0.5, 0.5),
            RadiusX = 0.55,
            RadiusY = 0.55
        };

        if (stone == Stone.Black)
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x66, 0x66, 0x66), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x28, 0x28, 0x28), 0.45));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0x00, 0x00, 0x00), 1.0));
        }
        else
        {
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xFF, 0xFF, 0xFF), 0.0));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xE6, 0xE6, 0xE6), 0.55));
            brush.GradientStops.Add(new GradientStop(Color.FromRgb(0xB0, 0xB0, 0xB0), 1.0));
        }

        return brush;
    }

    /// <summary>网格线颜色：木质棋盘偏暖褐，石制棋盘偏冷灰。</summary>
    public static Brush CreateGridBrush(string boardStyle)
        => new SolidColorBrush(boardStyle == "stone"
            ? Color.FromRgb(0x4A, 0x4A, 0x4A)
            : Color.FromRgb(0x3B, 0x24, 0x10));

    /// <summary>清空画布并绘制网格线与九星位。</summary>
    public static void DrawGrid(Canvas canvas, Brush gridBrush)
    {
        canvas.Children.Clear();

        for (int i = 0; i < Board.Size; i++)
        {
            double offset = i * CellSize;
            canvas.Children.Add(new Line
            {
                X1 = 0, Y1 = offset, X2 = CanvasSize - 1, Y2 = offset,
                Stroke = gridBrush, StrokeThickness = 1.6
            });
            canvas.Children.Add(new Line
            {
                X1 = offset, Y1 = 0, X2 = offset, Y2 = CanvasSize - 1,
                Stroke = gridBrush, StrokeThickness = 1.6
            });
        }

        int[] stars = { 3, 9, 15 };
        foreach (int r in stars)
        {
            foreach (int c in stars)
            {
                var star = new Ellipse
                {
                    Width = 5,
                    Height = 5,
                    Fill = gridBrush
                };
                Canvas.SetLeft(star, c * CellSize - 2.5);
                Canvas.SetTop(star, r * CellSize - 2.5);
                canvas.Children.Add(star);
            }
        }
    }

    /// <summary>
    /// 绘制一颗立体棋子（球面渐变 + 内部高光 + 投影）。isWin 为 true 时叠加金色描边，
    /// highlight 为 true 时再加一圈淡色光晕，用于复盘时标示当前这一步。
    /// </summary>
    public static FrameworkElement DrawStone(Canvas canvas, int row, int col, Stone stone, bool isWin = false, bool highlight = false)
    {
        var cell = new Grid
        {
            Width = StoneRadius * 2,
            Height = StoneRadius * 2,
            RenderTransformOrigin = new Point(0.5, 0.5),
            RenderTransform = new ScaleTransform(1, 1)
        };

        cell.Children.Add(new Ellipse
        {
            Fill = new SolidColorBrush(Color.FromArgb(60, 0, 0, 0)),
            Margin = new Thickness(StoneRadius * 0.15, StoneRadius * 0.35, 0, 0),
            IsHitTestVisible = false
        });

        cell.Children.Add(new Ellipse { Fill = CreateStoneBrush(stone) });

        if (isWin)
        {
            cell.Children.Add(new Ellipse
            {
                Stroke = new SolidColorBrush(Color.FromRgb(0xFF, 0xC0, 0x2A)),
                StrokeThickness = 3,
                IsHitTestVisible = false
            });
        }

        if (highlight)
        {
            cell.Children.Add(new Ellipse
            {
                Stroke = new SolidColorBrush(Color.FromArgb(200, 0xE0, 0x4B, 0x3A)),
                StrokeThickness = 2,
                Margin = new Thickness(-3),
                IsHitTestVisible = false
            });
        }

        cell.Children.Add(new Ellipse
        {
            Width = StoneRadius * 0.85,
            Height = StoneRadius * 0.45,
            Fill = new SolidColorBrush(Color.FromArgb(70, 255, 255, 255)),
            IsHitTestVisible = false,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(StoneRadius * 0.25, StoneRadius * 0.2, 0, 0)
        });

        Canvas.SetLeft(cell, col * CellSize - StoneRadius);
        Canvas.SetTop(cell, row * CellSize - StoneRadius);
        canvas.Children.Add(cell);
        return cell;
    }
}
