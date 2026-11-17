using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Notch.Interop;

/// <summary>
/// The picture Explorer shows for a file: a thumbnail for photos, videos and documents
/// that have one, otherwise the file type's icon.
/// </summary>
static class ShellThumbnail
{
    [StructLayout(LayoutKind.Sequential)]
    struct SIZE
    {
        public int cx, cy;
    }

    [ComImport, Guid("bcc18b79-ba16-442f-80c4-8a59c30c463b"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    interface IShellItemImageFactory
    {
        [PreserveSig]
        int GetImage(SIZE size, int flags, out IntPtr bitmap);
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAP
    {
        public int bmType, bmWidth, bmHeight, bmWidthBytes;
        public ushort bmPlanes, bmBitsPixel;
        public IntPtr bmBits;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct BITMAPINFOHEADER
    {
        public uint biSize;
        public int biWidth, biHeight;
        public ushort biPlanes, biBitCount;
        public uint biCompression, biSizeImage;
        public int biXPelsPerMeter, biYPelsPerMeter;
        public uint biClrUsed, biClrImportant;
    }

    [StructLayout(LayoutKind.Sequential)]
    struct DIBSECTION
    {
        public BITMAP dsBm;
        public BITMAPINFOHEADER dsBmih;
        public uint dsBitfields0, dsBitfields1, dsBitfields2;
        public IntPtr dshSection;
        public uint dsOffset;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
    static extern void SHCreateItemFromParsingName(
        string path, IntPtr bindContext, [MarshalAs(UnmanagedType.LPStruct)] Guid riid,
        [MarshalAs(UnmanagedType.Interface)] out IShellItemImageFactory item);

    [DllImport("gdi32.dll", EntryPoint = "GetObjectW")]
    static extern int GetObject(IntPtr handle, int size, out DIBSECTION section);

    [DllImport("gdi32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    static extern bool DeleteObject(IntPtr handle);

    /// <summary>
    /// A frozen image about <paramref name="pixels"/> square, or null if Windows has none.
    /// Can be slow for big videos and documents, so call it off the UI thread.
    /// </summary>
    public static ImageSource? Get(string path, int pixels)
    {
        IntPtr bitmap = IntPtr.Zero;
        try
        {
            SHCreateItemFromParsingName(path, IntPtr.Zero, typeof(IShellItemImageFactory).GUID, out var factory);
            try
            {
                // Flags 0: a thumbnail if there is one, otherwise the icon, scaled to fit.
                if (factory.GetImage(new SIZE { cx = pixels, cy = pixels }, 0, out bitmap) != 0
                    || bitmap == IntPtr.Zero)
                    return null;
            }
            finally
            {
                Marshal.ReleaseComObject(factory);
            }

            return ToImage(bitmap);
        }
        catch
        {
            return null; // the file went away, or no shell extension could read it
        }
        finally
        {
            if (bitmap != IntPtr.Zero) DeleteObject(bitmap);
        }
    }

    /// <summary>
    /// Copies the bitmap's pixels out with their transparency. WPF's own converter drops
    /// the alpha channel, which leaves icons on a black square.
    /// </summary>
    static ImageSource? ToImage(IntPtr bitmap)
    {
        int written = GetObject(bitmap, Marshal.SizeOf<DIBSECTION>(), out var section);
        var bm = section.dsBm;

        bool readable = written == Marshal.SizeOf<DIBSECTION>()
                     && bm.bmBits != IntPtr.Zero && bm.bmBitsPixel == 32
                     && bm.bmWidth > 0 && bm.bmHeight != 0;
        if (!readable)
        {
            var fallback = Imaging.CreateBitmapSourceFromHBitmap(
                bitmap, IntPtr.Zero, Int32Rect.Empty, BitmapSizeOptions.FromEmptyOptions());
            fallback.Freeze();
            return fallback;
        }

        int width = bm.bmWidth;
        int height = Math.Abs(bm.bmHeight);
        int stride = bm.bmWidthBytes;
        bool bottomUp = section.dsBmih.biHeight > 0;

        var pixels = new byte[stride * height];
        for (int row = 0; row < height; row++)
        {
            int sourceRow = bottomUp ? height - 1 - row : row;
            Marshal.Copy(bm.bmBits + sourceRow * stride, pixels, row * stride, stride);
        }

        // Some thumbnails come without any alpha at all; treat those as opaque.
        bool hasAlpha = false;
        for (int i = 3; i < pixels.Length; i += 4)
        {
            if (pixels[i] != 0)
            {
                hasAlpha = true;
                break;
            }
        }

        var format = hasAlpha ? PixelFormats.Pbgra32 : PixelFormats.Bgr32;
        var image = BitmapSource.Create(width, height, 96, 96, format, null, pixels, stride);
        image.Freeze();
        return image;
    }
}
