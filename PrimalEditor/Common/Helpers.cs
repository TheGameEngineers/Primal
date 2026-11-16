// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrimalEditor;

static class EnumExtensions
{
    public static string GetDescription(this Enum value)
    {
        return (value.GetType().GetField(value.ToString())
            .GetCustomAttributes(typeof(DescriptionAttribute), false) as DescriptionAttribute[]).FirstOrDefault()?.Description ?? value.ToString();
    }
}

static class GuidExtensions
{
    public static void WriteToBinary(this Guid guid, BinaryWriter writer)
    {
        var bytes = guid.ToByteArray();
        writer.Write(bytes.Length);
        writer.Write(bytes);
    }

    public static Guid ReadFromBinary(BinaryReader reader)
    {
        var size = reader.ReadInt32();
        var bytes = reader.ReadBytes(size);
        return new(bytes);
    }
}

static partial class MouseHelper
{
    [StructLayout(LayoutKind.Sequential)]
    private struct POINT(int x, int y)
    {
        public int X = x;
        public int Y = y;

        public POINT(Point pt) : this((int)pt.X, (int)pt.Y) { }

        public static implicit operator Point(POINT p) => new(p.X, p.Y);

        public static implicit operator POINT(Point p) => new((int)p.X, (int)p.Y);
    }

    [LibraryImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool SetCursorPos(int x, int y);

    [LibraryImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool GetCursorPos(out POINT lpPoint);

    [LibraryImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static partial bool ClientToScreen(IntPtr hWnd, ref POINT point);

    [LibraryImport("User32.dll")]
    public static partial IntPtr SetCapture(IntPtr hwnd);

    [LibraryImport("User32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReleaseCapture();

    public static void SetCursor(IntPtr hWnd, int x, int y)
    {
        var p = new POINT(x, y);
        ClientToScreen(hWnd, ref p);
        SetCursorPos(p.X, p.Y);
    }

    public static Point GetCursor()
    {
        GetCursorPos(out POINT p);
        return p;
    }
}

static partial class KeyboardHelper
{
    public enum VKey : int
    {
        MouseLeft = 0x01,
        MouseRight = 0x02,
        MouseMiddle = 0x04,
        Backspace = 0x08,
        Tab = 0x09,
        Enter = 0x0D,

        Shift = 0x10,
        Control = 0x11,
        Alt = 0x12,
        Pause = 0x13,
        Capslock = 0x14,
        Escape = 0x1B,

        Space = 0x20,
        PageUp = 0x21,
        PageDown = 0x22,
        End = 0x23,
        Home = 0x24,
        Left = 0x25,
        Up = 0x26,
        Right = 0x27,
        Down = 0x28,
        PrintScreen = 0x2C,
        Insert = 0x2D,
        Delete = 0x2E,

        D0 = 0x30,
        D1 = 0x31,
        D2 = 0x32,
        D3 = 0x33,
        D4 = 0x34,
        D5 = 0x35,
        D6 = 0x36,
        D7 = 0x37,
        D8 = 0x38,
        D9 = 0x39,

        A = 0x41,
        B = 0x42,
        C = 0x43,
        D = 0x44,
        E = 0x45,
        F = 0x46,
        G = 0x47,
        H = 0x48,
        I = 0x49,
        J = 0x4A,
        K = 0x4B,
        L = 0x4C,
        M = 0x4D,
        N = 0x4E,
        O = 0x4F,

        P = 0x50,
        Q = 0x51,
        R = 0x52,
        S = 0x53,
        T = 0x54,
        U = 0x55,
        V = 0x56,
        W = 0x57,
        X = 0x58,
        Y = 0x59,
        Z = 0x5A,

        Numpad0 = 0x60,
        Numpad1 = 0x61,
        Numpad2 = 0x62,
        Numpad3 = 0x63,
        Numpad4 = 0x64,
        Numpad5 = 0x65,
        Numpad6 = 0x66,
        Numpad7 = 0x67,
        Numpad8 = 0x68,
        Numpad9 = 0x69,
        Multiply = 0x6A,
        Add = 0x6B,

        Subtract = 0x6D,
        Decimal = 0x6E,
        Divide = 0x6F,

        F1 = 0x70,
        F2 = 0x71,
        F3 = 0x72,
        F4 = 0x73,
        F5 = 0x74,
        F6 = 0x75,
        F7 = 0x76,
        F8 = 0x77,
        F9 = 0x78,
        F10 = 0x79,
        F11 = 0x7A,
        F12 = 0x7B,

        Numlock = 0x90,
        Scrollock = 0x91,

        Colon = 0xBA,
        Plus = 0xBB,
        Comma = 0xBC,
        Minus = 0xBD,
        Period = 0xBE,
        Question = 0xBF,

        Tilde = 0xC0,

        BracketOpen = 0xDB,
        Pipe = 0xDC,
        BracketClose = 0xDD,
        Quote = 0xDE,
    }

    [LibraryImport("user32.dll")]
    public static partial short GetAsyncKeyState(VKey vKey);

    private static readonly Array _keys = Enum.GetValues<Key>();

    public static IEnumerable<Key> KeyDown()
    {
        foreach (Key key in _keys)
        {
            if (key != Key.None && Keyboard.IsKeyDown(key))
                yield return key;
        }
    }
}

static class VisualExtensions
{
    public static T FindVisualParent<T>(this DependencyObject depObject) where T : DependencyObject
    {
        if (depObject is not Visual) return null;

        var parent = VisualTreeHelper.GetParent(depObject);
        while (parent != null)
        {
            if (parent is T type)
            {
                return type;
            }
            parent = VisualTreeHelper.GetParent(parent);
        }
        return null;
    }

    public static T FindVisualChild<T>(this DependencyObject depObj) where T : DependencyObject
    {
        if (depObj is not Visual) return null;

        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(depObj); i++)
        {
            var child = VisualTreeHelper.GetChild(depObj, i);

            var result = (child as T) ?? FindVisualChild<T>(child);
            if (result != null) return result;
        }
        return null;
    }
}

static class ContentHelper
{
    public static string[] MeshFileExtensions { get; } = [".fbx"];
    public static string[] ImageFileExtensions { get; } = [".bmp", ".png", ".jpg", ".jpeg", ".tiff", ".tif", ".tga", ".dds", ".hdr"];
    public static string[] AudioFileExtensions { get; } = [".ogg", ".wav"];

    public static string GetRandomString(int length = 8)
    {
        if (length <= 0) length = 8;
        var n = length / 11;
        var sb = new StringBuilder();
        for (int i = 0; i <= n; ++i)
        {
            sb.Append(Path.GetRandomFileName().Replace(".", ""));
        }

        return sb.ToString(0, length);
    }

    public static bool IsDirectory(string path)
    {
        try
        {
            return File.GetAttributes(path).HasFlag(FileAttributes.Directory);
        }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }
        return false;
    }

    public static bool IsMissingOrOutdated(string filePath, DateTime importDate)
        => !File.Exists(filePath) || File.GetLastWriteTime(filePath).IsOlder(importDate);

    public static bool IsDirectory(this FileInfo info) => info.Attributes.HasFlag(FileAttributes.Directory);

    public static bool IsOlder(this DateTime date, DateTime other) => date < other;

    public static Uri GetPackUri(string relativePath, Type type)
    {
        var assemblyShortName = type.Assembly.ToString().Split(',')[0];
        var packUriString = $"pack://application:,,,/{assemblyShortName};component/{relativePath}";
        return new(packUriString);
    }

    public static string SanitizeFileName(string name)
    {
        Debug.Assert(!string.IsNullOrEmpty(name));
        var path = new StringBuilder(name[..(name.LastIndexOf(Path.DirectorySeparatorChar) + 1)]);
        var file = new StringBuilder(name[(name.LastIndexOf(Path.DirectorySeparatorChar) + 1)..]);
        foreach (var c in Path.GetInvalidPathChars())
        {
            path.Replace(c, '_');
        }
        foreach (var c in Path.GetInvalidFileNameChars())
        {
            file.Replace(c, '_');
        }
        return path.Append(file).ToString();
    }

    public static byte[] ComputeHash(byte[] data, int offset = 0, int count = 0)
        => data?.Length > 0 ? SHA256.HashData(data.AsSpan(offset, count > 0 ? count : data.Length)) : null;

    internal static IEnumerable<string> SaveAsset(this Asset asset)
    {
        try
        {
            ContentWatcher.EnableFileWatcher(false);
            Debug.Assert(!string.IsNullOrEmpty(asset.FullPath));
            return asset.Save(asset.FullPath);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to save asset {asset.FullPath}");
            Debug.WriteLine(ex.Message);
            return [];
        }
        finally
        {
            ContentWatcher.EnableFileWatcher(true);
        }
    }

    internal static async Task<List<Asset>> ImportFilesAsync(IEnumerable<AssetProxy> proxies)
    {
        List<Asset> assets = [];
        try
        {
            ImportingItemCollection.Init();
            ContentWatcher.EnableFileWatcher(false);
            var tasks = proxies.Select(proxy =>
                Task.Run(() => assets.Add(Import(proxy.FileInfo.FullName, proxy.ImportSettings, proxy.DestinationFolder))));

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"Failed to import files.");
            Debug.WriteLine(ex.Message);
        }
        finally
        {
            ContentWatcher.EnableFileWatcher(true);
        }

        return assets;
    }

    private static Asset Import(string file, IAssetImportSettings importSettings, string destination)
    {
        Debug.Assert(!string.IsNullOrEmpty(file));
        if (IsDirectory(file)) return null;
        var name = Path.GetFileNameWithoutExtension(file).ToLower();
        var ext = Path.GetExtension(file).ToLower();

        Asset asset = ext switch
        {
            { } when MeshFileExtensions.Contains(ext) => new Content.Geometry(importSettings),
            { } when ImageFileExtensions.Contains(ext) => new Texture(importSettings),
            { } when AudioFileExtensions.Contains(ext) => null,
            _ => null
        };

        if (asset != null)
        {
            Import(asset, name, file, destination);
        }

        return asset;
    }

    private static void Import(Asset asset, string name, string file, string destination)
    {
        destination = destination?.Trim();

        Debug.Assert(asset != null);
        Debug.Assert(!string.IsNullOrEmpty(destination) && Directory.Exists(destination));

        if (!destination.EndsWith(Path.DirectorySeparatorChar)) destination += Path.DirectorySeparatorChar;
        asset.FullPath = destination + name + Asset.AssetFileExtension;

        var importingItem = new ImportingItem(name, asset);
        ImportingItemCollection.Add(importingItem);
        bool importSucceeded = false;
        try
        {
            // NOTE: FullPath must be set before we call asset.Import().
            Debug.Assert(asset.FullPath?.Contains(destination) == true);
            importSucceeded = !string.IsNullOrEmpty(file) && asset.Import(file);

            if (importSucceeded)
            {
                asset.Save(asset.FullPath);
            }

            return;
        }
        finally
        {
            importingItem.Status = importSucceeded ? ImportStatus.Succeeded : ImportStatus.Failed;
        }
    }
}

static class CompressionHelper
{
    public static byte[] Compress(byte[] data)
    {
        Debug.Assert(data?.Length > 0);
        byte[] compressedData = null;
        using (var output = new MemoryStream())
        {
            using (var compressor = new DeflateStream(output, CompressionLevel.Optimal, true))
            {
                compressor.Write(data, 0, data.Length);
            }

            compressedData = output.ToArray();
        }

        return compressedData;
    }

    public static byte[] Decompress(byte[] data)
    {
        Debug.Assert(data?.Length > 0);
        byte[] decompressedData = null;
        using (var output = new MemoryStream())
        {
            using (var compressor = new DeflateStream(new MemoryStream(data), CompressionMode.Decompress))
            {
                compressor.CopyTo(output);
            }

            decompressedData = output.ToArray();
        }

        return decompressedData;
    }
}

static class BitmapHelper
{
    public static int BytesPerChannel(DXGI_FORMAT format)
    {
        switch (format)
        {
            case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_FLOAT:
            case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R32G32B32A32_SINT:
            case DXGI_FORMAT.DXGI_FORMAT_R32G32B32_FLOAT:
            case DXGI_FORMAT.DXGI_FORMAT_R32G32B32_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R32G32B32_SINT:
            case DXGI_FORMAT.DXGI_FORMAT_R32G32_FLOAT:
            case DXGI_FORMAT.DXGI_FORMAT_R32G32_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R32G32_SINT:
            case DXGI_FORMAT.DXGI_FORMAT_R32_FLOAT:
            case DXGI_FORMAT.DXGI_FORMAT_R32_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R32_SINT:

            case DXGI_FORMAT.DXGI_FORMAT_BC6H_SF16:
            case DXGI_FORMAT.DXGI_FORMAT_BC6H_UF16:

                return 4;
            case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_FLOAT:
            case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R16G16B16A16_SINT:
            case DXGI_FORMAT.DXGI_FORMAT_R16G16_FLOAT:
            case DXGI_FORMAT.DXGI_FORMAT_R16G16_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R16G16_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R16G16_SNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R16G16_SINT:
            case DXGI_FORMAT.DXGI_FORMAT_R16_FLOAT:
            case DXGI_FORMAT.DXGI_FORMAT_R16_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R16_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R16_SNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R16_SINT:
                return 2;
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UNORM_SRGB:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8B8A8_SINT:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8_SNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8G8_SINT:
            case DXGI_FORMAT.DXGI_FORMAT_R8_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8_UINT:
            case DXGI_FORMAT.DXGI_FORMAT_R8_SNORM:
            case DXGI_FORMAT.DXGI_FORMAT_R8_SINT:

            case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC1_UNORM_SRGB:
            case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC3_UNORM_SRGB:
            case DXGI_FORMAT.DXGI_FORMAT_BC4_SNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC4_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC5_SNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC5_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM:
            case DXGI_FORMAT.DXGI_FORMAT_BC7_UNORM_SRGB:
                return 1;
            default:
                break;
        }

        return -1;
    }

    public static byte[] CreateThumbnail(BitmapSource image, int maxWidth, int maxHeight)
    {
        var scaleX = maxWidth / (double)image.PixelWidth;
        var scaleY = maxHeight / (double)image.PixelHeight;
        var ratio = Math.Min(scaleX, scaleY);

        var thumbnail = new TransformedBitmap(image, new ScaleTransform(ratio, ratio, 0.5, 0.5));

        using var memStream = new MemoryStream();
        memStream.SetLength(0);

        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(thumbnail));
        encoder.Save(memStream);

        return memStream.ToArray();
    }

    public static BitmapSource ImageFromSlice(Slice slice, DXGI_FORMAT slice_format, bool isNormalMap = false)
    {
        var data = slice.RawContent;
        var bytesPerPixel = data.Length / (slice.Width * slice.Height);
        var bytesPerChannel = BytesPerChannel(slice_format);

        var stride = slice.Width * bytesPerPixel;
        var format = PixelFormats.Default;
        byte[] bgrData = null;

        if (bytesPerPixel == 16) format = PixelFormats.Rgba128Float;
        else if (bytesPerPixel == 4) format = PixelFormats.Bgra32;
        else if (bytesPerPixel == 2) format = PixelFormats.Bgr24;
        else if (bytesPerPixel == 1) format = PixelFormats.Gray8;

        if (bytesPerPixel == 16 || bytesPerPixel == 1)
        {
            bgrData = new byte[data.Length];
            Buffer.BlockCopy(data, 0, bgrData, 0, data.Length);
        }
        else if ((bytesPerPixel == 4) && bytesPerChannel == 1)
        {
            bgrData = new byte[data.Length];
            Buffer.BlockCopy(data, 0, bgrData, 0, data.Length);

            // swap R and B channels: RGB -> BGR
            for (int i = 0; i < bgrData.Length; i += bytesPerPixel)
            {
                (bgrData[i], bgrData[i + 2]) = (bgrData[i + 2], bgrData[i]);
            }
        }
        else if (bytesPerPixel == 4)
        {
            if (bytesPerChannel == 2)
            {
                int offset = 0;
                Half[] dataFloats =
                    [.. data.GroupBy(x => offset++ / bytesPerChannel).Select(x => BitConverter.ToHalf([.. x], 0))];
                using var writer = new BinaryWriter(new MemoryStream());
                for (int i = 0; i < dataFloats.Length; i += bytesPerChannel)
                {
                    writer.Write((float)dataFloats[i + 0]);
                    writer.Write((float)dataFloats[i + 1]);
                    writer.Write(0.0f);
                    writer.Write(1.0f);
                }
                writer.Flush();
                bgrData = (writer.BaseStream as MemoryStream).ToArray();
                format = PixelFormats.Rgba128Float;
                stride = slice.Width * 16;
            }
            else if (bytesPerChannel == 4)
            {
                int offset = 0;
                float[] dataFloats =
                    [.. data.GroupBy(x => offset++ / bytesPerChannel).Select(x => BitConverter.ToSingle([.. x.ToArray().Reverse()], 0))];
                using var writer = new BinaryWriter(new MemoryStream());
                foreach (var f in dataFloats)
                {
                    writer.Write(f);
                    writer.Write(0.0f);
                    writer.Write(0.0f);
                    writer.Write(1.0f);
                }
                writer.Flush();
                bgrData = (writer.BaseStream as MemoryStream).ToArray();
                format = PixelFormats.Rgba128Float;
                stride = slice.Width * 16;
            }
        }
        else if (bytesPerPixel == 2)
        {
            if (bytesPerChannel == 1)
            {
                bgrData = new byte[slice.Width * slice.Height * 3];
                stride = slice.Width * 3;
                int index = 0;
                for (int i = 0; i < data.Length; i += 2)
                {
                    bgrData[index + 2] = data[i + 0];
                    bgrData[index + 1] = data[i + 1];
                    bgrData[index + 0] = 0;
                    index += 3;
                }

                if (isNormalMap)
                {
                    var inv255 = 1.0 / 255.0;
                    index = 0;

                    for (int i = 0; i < data.Length; i += 2)
                    {
                        var r = data[i + 0] * inv255 * 2.0 - 1.0;
                        var g = data[i + 1] * inv255 * 2.0 - 1.0;
                        var b = (Math.Sqrt(Math.Clamp(1.0 - (r * r + g * g), 0.0, 1.0)) + 1.0) * 0.5 * 255.0;
                        bgrData[index + 0] = (byte)b;
                        index += 3;
                    }
                }
            }
            else if (bytesPerChannel == 2)
            {
                int offset = 0;
                Half[] dataFloats =
                    [.. data.GroupBy(x => offset++ / bytesPerChannel).Select(x => BitConverter.ToHalf([.. x], 0))];
                using var writer = new BinaryWriter(new MemoryStream());
                foreach (var f in dataFloats)
                {
                    writer.Write(f);
                    writer.Write(0.0f);
                    writer.Write(0.0f);
                    writer.Write(1.0f);
                }
                writer.Flush();
                bgrData = (writer.BaseStream as MemoryStream).ToArray();
                format = PixelFormats.Rgba128Float;
                stride = slice.Width * 16;
            }
        }

        BitmapSource image = null;
        if (bgrData != null)
        {
            image = BitmapSource.Create(slice.Width, slice.Height, 96.0, 96.0, format, null, bgrData, stride);
        }
        return image;
    }
}

static class ExpanderStateHelper
{
    private static readonly Dictionary<string, bool> _expandedStates = [];

    // Allow any value (including bindings/template bindings) to be set without throwing
    public static readonly DependencyProperty StateKeyProperty =
        DependencyProperty.RegisterAttached("StateKey", typeof(object), typeof(ExpanderStateHelper),
            new PropertyMetadata(null, OnStateKeyChanged));

    public static string GetStateKey(DependencyObject obj) => obj.GetValue(StateKeyProperty)?.ToString();
    public static void SetStateKey(DependencyObject obj, object value) => obj.SetValue(StateKeyProperty, value);

    private static void OnStateKeyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is Expander expander && e.NewValue is string newKey && !string.IsNullOrEmpty(newKey))
        {
            if (_expandedStates.TryGetValue(newKey, out var isExpanded))
            {
                expander.IsExpanded = isExpanded;
            }

            SubscribeToExpanderEvents(expander);
        }
    }

    private static void SubscribeToExpanderEvents(Expander expander)
    {
        WeakEventManager<Expander, RoutedEventArgs>.RemoveHandler(expander, nameof(Expander.Expanded), OnExpanded);
        WeakEventManager<Expander, RoutedEventArgs>.RemoveHandler(expander, nameof(Expander.Collapsed), OnCollapsed);

        WeakEventManager<Expander, RoutedEventArgs>.AddHandler(expander, nameof(Expander.Expanded), OnExpanded);
        WeakEventManager<Expander, RoutedEventArgs>.AddHandler(expander, nameof(Expander.Collapsed), OnCollapsed);
    }

    private static void OnExpanded(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && GetStateKey(expander) is string key)
        {
            _expandedStates[key] = true;
            e.Handled = true;
        }
    }

    private static void OnCollapsed(object sender, RoutedEventArgs e)
    {
        if (sender is Expander expander && GetStateKey(expander) is string key)
        {
            _expandedStates[key] = false;
            e.Handled = true;
        }
    }
}