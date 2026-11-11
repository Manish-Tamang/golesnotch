using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Notch.Services;

/// <summary>Picks the colour that best represents an album cover.</summary>
static class AlbumColor
{
    const int HueBins = 12;       // 30 degrees each
    const double Brightness = 215; // the result's strongest channel is scaled to this

    /// <summary>
    /// Returns the cover's dominant vivid colour, or null when the cover is missing or
    /// has no real colour (black and white art).
    /// </summary>
    /// <remarks>
    /// Pixels are grouped by hue; the group with the most colourful, bright pixels wins
    /// and its average is the answer. A plain average of the whole cover would come out
    /// as a muddy brown for most artwork.
    /// </remarks>
    public static Color? From(BitmapSource? cover)
    {
        if (cover == null) return null;

        try
        {
            var bgra = new FormatConvertedBitmap(cover, PixelFormats.Bgra32, null, 0);
            int width = bgra.PixelWidth, height = bgra.PixelHeight;
            int stride = width * 4;
            var pixels = new byte[stride * height];
            bgra.CopyPixels(pixels, stride, 0);

            var weight = new double[HueBins];
            var red = new double[HueBins];
            var green = new double[HueBins];
            var blue = new double[HueBins];

            // About 48 x 48 samples is plenty.
            int step = Math.Max(1, Math.Min(width, height) / 48);

            for (int y = 0; y < height; y += step)
            {
                for (int x = 0; x < width; x += step)
                {
                    int i = y * stride + x * 4;
                    int b = pixels[i], g = pixels[i + 1], r = pixels[i + 2];

                    int max = Math.Max(r, Math.Max(g, b));
                    int min = Math.Min(r, Math.Min(g, b));
                    double value = max / 255.0;
                    double saturation = max == 0 ? 0 : (max - min) / (double)max;

                    // Greys and near-blacks say nothing about the cover's colour.
                    if (saturation < 0.2 || value < 0.2) continue;

                    int bin = Math.Min(HueBins - 1, (int)(Hue(r, g, b, max, min) / (360.0 / HueBins)));
                    double w = saturation * value;
                    weight[bin] += w;
                    red[bin] += r * w;
                    green[bin] += g * w;
                    blue[bin] += b * w;
                }
            }

            int best = 0;
            for (int i = 1; i < HueBins; i++)
                if (weight[i] > weight[best]) best = i;
            if (weight[best] <= 0) return null;

            return Tune(red[best] / weight[best], green[best] / weight[best], blue[best] / weight[best]);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>Hue in degrees, 0 to 360.</summary>
    static double Hue(int r, int g, int b, int max, int min)
    {
        double delta = max - min;
        if (delta == 0) return 0;

        double hue = max == r ? (g - b) / delta % 6
                   : max == g ? (b - r) / delta + 2
                   : (r - g) / delta + 4;
        hue *= 60;
        return hue < 0 ? hue + 360 : hue;
    }

    /// <summary>Makes the colour a little more vivid and evenly bright, so it glows on black.</summary>
    static Color? Tune(double r, double g, double b)
    {
        double mean = (r + g + b) / 3;
        r = mean + (r - mean) * 1.25;
        g = mean + (g - mean) * 1.25;
        b = mean + (b - mean) * 1.25;

        double max = Math.Max(r, Math.Max(g, b));
        if (max <= 0) return null;
        double scale = Brightness / max;

        return Color.FromRgb(Channel(r * scale), Channel(g * scale), Channel(b * scale));
    }

    static byte Channel(double value) => (byte)Math.Clamp(value, 0, 255);
}
