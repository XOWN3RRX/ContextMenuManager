using System.Runtime.InteropServices;

namespace ContextMenuManager.Interop;

internal static unsafe partial class NativeMethods
{
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
    public const int ASFW_ANY = -1;

    // Stock icons (SHSTOCKICONID)
    public const int SIID_DOCNOASSOC = 0;
    public const int SIID_DOCASSOC = 1;
    public const int SIID_FOLDER = 3;
    public const int SIID_DRIVEFIXED = 8;

    private const uint SHGSI_ICONLOCATION = 0x0;

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct SHSTOCKICONINFO
    {
        public uint cbSize;
        public nint hIcon;
        public int iSysImageIndex;
        public int iIcon;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)]
        public string szPath;
    }

    [DllImport("shell32.dll", CharSet = CharSet.Unicode)]
    private static extern int SHGetStockIconInfo(int siid, uint uFlags, ref SHSTOCKICONINFO psii);

    [LibraryImport("shell32.dll", EntryPoint = "SHDefExtractIconW", StringMarshalling = StringMarshalling.Utf16)]
    private static partial int SHDefExtractIcon(string pszIconFile, int iIndex, uint uFlags, nint* phiconLarge, nint* phiconSmall, uint nIconSize);

    /// <summary>
    /// Loads an icon at an exact pixel size. <paramref name="index"/> is an icon index, or a
    /// negative resource id as used in "file.dll,-123".
    /// </summary>
    public static Bitmap? ExtractIcon(string file, int index, int size)
    {
        nint large = 0;
        if (SHDefExtractIcon(file, index, 0, &large, null, (uint)size) != 0 || large == 0)
        {
            return null;
        }

        try
        {
            using var icon = Icon.FromHandle(large);
            return icon.ToBitmap();
        }
        finally
        {
            DestroyIcon(large);
        }
    }

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool DestroyIcon(nint hIcon);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool AllowSetForegroundWindow(int dwProcessId);

    [LibraryImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetForegroundWindow(nint hWnd);

    [LibraryImport("dwmapi.dll")]
    public static partial int DwmSetWindowAttribute(nint hwnd, int dwAttribute, void* pvAttribute, int cbAttribute);

    public static void SetDarkTitleBar(nint hWnd, bool dark)
    {
        int value = dark ? 1 : 0;
        _ = DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, &value, sizeof(int));
    }

    /// <summary>A Windows stock icon (folder, drive, document) at an exact pixel size, or null.</summary>
    public static Bitmap? GetStockIcon(int id, int size)
    {
        var info = new SHSTOCKICONINFO { cbSize = (uint)Marshal.SizeOf<SHSTOCKICONINFO>() };
        if (SHGetStockIconInfo(id, SHGSI_ICONLOCATION, ref info) != 0 || string.IsNullOrEmpty(info.szPath))
        {
            return null;
        }

        return ExtractIcon(info.szPath, info.iIcon, size);
    }
}
