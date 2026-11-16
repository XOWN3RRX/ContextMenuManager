using System.Globalization;
using System.Runtime.InteropServices;
using ContextMenuManager.Interop;

namespace ContextMenuManager.UI;

/// <summary>Loads and caches icons given as "file" or "file,index" (the format the registry uses).</summary>
internal static class IconLoader
{
    private static readonly Dictionary<(string, int), Bitmap?> s_cache = [];

    /// <summary>The icon at <paramref name="size"/> pixels, or null if the file has no icon.</summary>
    public static Bitmap? Load(string? location, int size)
    {
        if (string.IsNullOrWhiteSpace(location))
        {
            return null;
        }

        var key = (location.Trim(), size);
        if (s_cache.TryGetValue(key, out Bitmap? cached))
        {
            return cached;
        }

        Bitmap? bitmap = null;
        try
        {
            (string file, int index) = Parse(location);
            if (file.Length > 0)
            {
                bitmap = NativeMethods.ExtractIcon(file, index, size);
            }
        }
        catch (Exception ex) when (ex is ArgumentException or ExternalException or IOException)
        {
            bitmap = null;
        }

        s_cache[key] = bitmap;
        return bitmap;
    }

    public static Bitmap? Stock(int id, int size)
    {
        var key = ("stock:" + id.ToString(CultureInfo.InvariantCulture), size);
        if (!s_cache.TryGetValue(key, out Bitmap? bitmap))
        {
            bitmap = NativeMethods.GetStockIcon(id, size);
            s_cache[key] = bitmap;
        }

        return bitmap;
    }

    /// <summary>Forgets cached icons (after the user picked a new icon file, for example).</summary>
    public static void Clear()
    {
        foreach (Bitmap? bitmap in s_cache.Values)
        {
            bitmap?.Dispose();
        }

        s_cache.Clear();
    }

    private static (string File, int Index) Parse(string location)
    {
        string value = Environment.ExpandEnvironmentVariables(location.Trim());
        int index = 0;

        int comma = value.LastIndexOf(',');
        if (comma > 0 && int.TryParse(value[(comma + 1)..].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int parsed))
        {
            index = parsed;
            value = value[..comma];
        }

        return (value.Trim().Trim('"'), index);
    }
}
