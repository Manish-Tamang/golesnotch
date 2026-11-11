using System.IO;
using System.Windows.Media.Imaging;
using Windows.Storage.Streams;

namespace Notch.Interop;

/// <summary>Turns WinRT image streams (album art, app logos) into WPF bitmaps.</summary>
static class WinRtImage
{
    public static async Task<BitmapImage?> LoadAsync(IRandomAccessStreamReference? reference, int decodeWidth)
    {
        if (reference == null) return null;

        try
        {
            using var winStream = await reference.OpenReadAsync();
            using var stream = winStream.AsStreamForRead();
            var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            buffer.Position = 0;

            var bitmap = new BitmapImage();
            bitmap.BeginInit();
            bitmap.CacheOption = BitmapCacheOption.OnLoad;
            bitmap.DecodePixelWidth = decodeWidth;
            bitmap.StreamSource = buffer;
            bitmap.EndInit();
            bitmap.Freeze(); // safe to hand to the UI thread
            return bitmap;
        }
        catch
        {
            return null;
        }
    }
}
