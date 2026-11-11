using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Notch.Core;

namespace Notch.Controls;

/// <summary>The row of dots under the open notch. The current page's dot is a short bar.</summary>
public sealed class PageDots : StackPanel
{
    const double DotSize = 4;
    const double ActiveWidth = 14;
    const double DimOpacity = 0.35;

    readonly List<Border> _dots = new();

    public PageDots()
    {
        Orientation = Orientation.Horizontal;
    }

    /// <summary>A dot was clicked. The argument is the page index.</summary>
    public event Action<int>? Selected;

    public void SetCount(int count)
    {
        Children.Clear();
        _dots.Clear();

        for (int i = 0; i < count; i++)
        {
            var dot = new Border
            {
                Width = DotSize,
                Height = DotSize,
                CornerRadius = new CornerRadius(DotSize / 2),
                Background = Brushes.White,
                Opacity = DimOpacity,
            };

            // A larger invisible area around each dot, so it's easy to click.
            var target = new Border
            {
                Background = Brushes.Transparent,
                Padding = new Thickness(3, 6, 3, 6),
                Cursor = Cursors.Hand,
                Child = dot,
            };

            int index = i;
            target.MouseLeftButtonUp += (_, e) =>
            {
                e.Handled = true;
                Selected?.Invoke(index);
            };

            _dots.Add(dot);
            Children.Add(target);
        }

        Visibility = count > 1 ? Visibility.Visible : Visibility.Collapsed;
    }

    public void SetActive(int index, bool animate)
    {
        for (int i = 0; i < _dots.Count; i++)
        {
            double width = i == index ? ActiveWidth : DotSize;
            double opacity = i == index ? 1 : DimOpacity;

            if (animate)
            {
                Motion.To(_dots[i], WidthProperty, width, 420, Motion.Spring());
                Motion.To(_dots[i], OpacityProperty, opacity, 200);
            }
            else
            {
                Motion.Set(_dots[i], WidthProperty, width);
                Motion.Set(_dots[i], OpacityProperty, opacity);
            }
        }
    }
}
