using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Notch.Core;

namespace Notch.Controls;

/// <summary>
/// One line of text that changes with a small roll: the old line slides up and fades
/// while the new one rises into place. Used for the lyric line in the closed notch.
/// </summary>
public sealed class LyricTicker : Grid
{
    const double Travel = 9;

    // Two text blocks take turns being the visible one.
    readonly TextBlock[] _lines = new TextBlock[2];
    readonly TranslateTransform[] _shifts = new TranslateTransform[2];
    int _front;
    string _text = "";

    public LyricTicker()
    {
        ClipToBounds = true;

        for (int i = 0; i < 2; i++)
        {
            _shifts[i] = new TranslateTransform();
            _lines[i] = new TextBlock
            {
                FontSize = 13,
                FontWeight = FontWeights.SemiBold,
                Foreground = Brushes.White,
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment = VerticalAlignment.Center,
                TextTrimming = TextTrimming.CharacterEllipsis,
                Opacity = i == 0 ? 1 : 0,
                RenderTransform = _shifts[i],
            };
            Children.Add(_lines[i]);
        }
    }

    public void SetText(string text, bool animate)
    {
        if (text == _text) return;
        _text = text;

        var oldLine = _lines[_front];
        var oldShift = _shifts[_front];
        _front = 1 - _front;
        var newLine = _lines[_front];
        var newShift = _shifts[_front];

        newLine.Text = text;

        if (!animate)
        {
            Motion.Set(oldLine, OpacityProperty, 0);
            Motion.Set(newLine, OpacityProperty, 1);
            Motion.Set(newShift, TranslateTransform.YProperty, 0);
            return;
        }

        Motion.To(oldLine, OpacityProperty, 0, 180);
        Motion.To(oldShift, TranslateTransform.YProperty, -Travel, 260);

        Motion.Set(newShift, TranslateTransform.YProperty, Travel);
        Motion.To(newLine, OpacityProperty, 1, 260, delayMs: 60);
        Motion.To(newShift, TranslateTransform.YProperty, 0, 460, Motion.Settle());
    }
}
