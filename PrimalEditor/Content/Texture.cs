// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;

namespace PrimalEditor.Content
{
    enum DXGI_FORMAT : int
    {
        DXGI_FORMAT_UNKNOWN = 0,
        DXGI_FORMAT_R32G32B32A32_TYPELESS = 1,
        DXGI_FORMAT_R32G32B32A32_FLOAT = 2,
        DXGI_FORMAT_R32G32B32A32_UINT = 3,
        DXGI_FORMAT_R32G32B32A32_SINT = 4,
        DXGI_FORMAT_R32G32B32_TYPELESS = 5,
        DXGI_FORMAT_R32G32B32_FLOAT = 6,
        DXGI_FORMAT_R32G32B32_UINT = 7,
        DXGI_FORMAT_R32G32B32_SINT = 8,
        DXGI_FORMAT_R16G16B16A16_TYPELESS = 9,
        DXGI_FORMAT_R16G16B16A16_FLOAT = 10,
        DXGI_FORMAT_R16G16B16A16_UNORM = 11,
        DXGI_FORMAT_R16G16B16A16_UINT = 12,
        DXGI_FORMAT_R16G16B16A16_SNORM = 13,
        DXGI_FORMAT_R16G16B16A16_SINT = 14,
        DXGI_FORMAT_R32G32_TYPELESS = 15,
        DXGI_FORMAT_R32G32_FLOAT = 16,
        DXGI_FORMAT_R32G32_UINT = 17,
        DXGI_FORMAT_R32G32_SINT = 18,
        DXGI_FORMAT_R32G8X24_TYPELESS = 19,
        DXGI_FORMAT_D32_FLOAT_S8X24_UINT = 20,
        DXGI_FORMAT_R32_FLOAT_X8X24_TYPELESS = 21,
        DXGI_FORMAT_X32_TYPELESS_G8X24_UINT = 22,
        DXGI_FORMAT_R10G10B10A2_TYPELESS = 23,
        DXGI_FORMAT_R10G10B10A2_UNORM = 24,
        DXGI_FORMAT_R10G10B10A2_UINT = 25,
        DXGI_FORMAT_R11G11B10_FLOAT = 26,
        DXGI_FORMAT_R8G8B8A8_TYPELESS = 27,
        DXGI_FORMAT_R8G8B8A8_UNORM = 28,
        DXGI_FORMAT_R8G8B8A8_UNORM_SRGB = 29,
        DXGI_FORMAT_R8G8B8A8_UINT = 30,
        DXGI_FORMAT_R8G8B8A8_SNORM = 31,
        DXGI_FORMAT_R8G8B8A8_SINT = 32,
        DXGI_FORMAT_R16G16_TYPELESS = 33,
        DXGI_FORMAT_R16G16_FLOAT = 34,
        DXGI_FORMAT_R16G16_UNORM = 35,
        DXGI_FORMAT_R16G16_UINT = 36,
        DXGI_FORMAT_R16G16_SNORM = 37,
        DXGI_FORMAT_R16G16_SINT = 38,
        DXGI_FORMAT_R32_TYPELESS = 39,
        DXGI_FORMAT_D32_FLOAT = 40,
        DXGI_FORMAT_R32_FLOAT = 41,
        DXGI_FORMAT_R32_UINT = 42,
        DXGI_FORMAT_R32_SINT = 43,
        DXGI_FORMAT_R24G8_TYPELESS = 44,
        DXGI_FORMAT_D24_UNORM_S8_UINT = 45,
        DXGI_FORMAT_R24_UNORM_X8_TYPELESS = 46,
        DXGI_FORMAT_X24_TYPELESS_G8_UINT = 47,
        DXGI_FORMAT_R8G8_TYPELESS = 48,
        DXGI_FORMAT_R8G8_UNORM = 49,
        DXGI_FORMAT_R8G8_UINT = 50,
        DXGI_FORMAT_R8G8_SNORM = 51,
        DXGI_FORMAT_R8G8_SINT = 52,
        DXGI_FORMAT_R16_TYPELESS = 53,
        DXGI_FORMAT_R16_FLOAT = 54,
        DXGI_FORMAT_D16_UNORM = 55,
        DXGI_FORMAT_R16_UNORM = 56,
        DXGI_FORMAT_R16_UINT = 57,
        DXGI_FORMAT_R16_SNORM = 58,
        DXGI_FORMAT_R16_SINT = 59,
        DXGI_FORMAT_R8_TYPELESS = 60,
        DXGI_FORMAT_R8_UNORM = 61,
        DXGI_FORMAT_R8_UINT = 62,
        DXGI_FORMAT_R8_SNORM = 63,
        DXGI_FORMAT_R8_SINT = 64,
        DXGI_FORMAT_A8_UNORM = 65,
        DXGI_FORMAT_R1_UNORM = 66,
        DXGI_FORMAT_R9G9B9E5_SHAREDEXP = 67,
        DXGI_FORMAT_R8G8_B8G8_UNORM = 68,
        DXGI_FORMAT_G8R8_G8B8_UNORM = 69,
        DXGI_FORMAT_BC1_TYPELESS = 70,
        DXGI_FORMAT_BC1_UNORM = 71,
        [Description("BC1 (sRGBA) Low Quality Alpha")]
        DXGI_FORMAT_BC1_UNORM_SRGB = 72,
        DXGI_FORMAT_BC2_TYPELESS = 73,
        DXGI_FORMAT_BC2_UNORM = 74,
        DXGI_FORMAT_BC2_UNORM_SRGB = 75,
        DXGI_FORMAT_BC3_TYPELESS = 76,
        DXGI_FORMAT_BC3_UNORM = 77,
        [Description("BC3 (sRGBA) Medium Quality")]
        DXGI_FORMAT_BC3_UNORM_SRGB = 78,
        DXGI_FORMAT_BC4_TYPELESS = 79,
        DXGI_FORMAT_BC4_UNORM = 80,
        DXGI_FORMAT_BC4_SNORM = 81,
        DXGI_FORMAT_BC5_TYPELESS = 82,
        DXGI_FORMAT_BC5_UNORM = 83,
        DXGI_FORMAT_BC5_SNORM = 84,
        DXGI_FORMAT_B5G6R5_UNORM = 85,
        DXGI_FORMAT_B5G5R5A1_UNORM = 86,
        DXGI_FORMAT_B8G8R8A8_UNORM = 87,
        DXGI_FORMAT_B8G8R8X8_UNORM = 88,
        DXGI_FORMAT_R10G10B10_XR_BIAS_A2_UNORM = 89,
        DXGI_FORMAT_B8G8R8A8_TYPELESS = 90,
        DXGI_FORMAT_B8G8R8A8_UNORM_SRGB = 91,
        DXGI_FORMAT_B8G8R8X8_TYPELESS = 92,
        DXGI_FORMAT_B8G8R8X8_UNORM_SRGB = 93,
        DXGI_FORMAT_BC6H_TYPELESS = 94,
        DXGI_FORMAT_BC6H_UF16 = 95,
        DXGI_FORMAT_BC6H_SF16 = 96,
        DXGI_FORMAT_BC7_TYPELESS = 97,
        DXGI_FORMAT_BC7_UNORM = 98,
        [Description("BC7 (sRGBA) High Quality")]
        DXGI_FORMAT_BC7_UNORM_SRGB = 99,
        DXGI_FORMAT_AYUV = 100,
        DXGI_FORMAT_Y410 = 101,
        DXGI_FORMAT_Y416 = 102,
        DXGI_FORMAT_NV12 = 103,
        DXGI_FORMAT_P010 = 104,
        DXGI_FORMAT_P016 = 105,
        DXGI_FORMAT_420_OPAQUE = 106,
        DXGI_FORMAT_YUY2 = 107,
        DXGI_FORMAT_Y210 = 108,
        DXGI_FORMAT_Y216 = 109,
        DXGI_FORMAT_NV11 = 110,
        DXGI_FORMAT_AI44 = 111,
        DXGI_FORMAT_IA44 = 112,
        DXGI_FORMAT_P8 = 113,
        DXGI_FORMAT_A8P8 = 114,
        DXGI_FORMAT_B4G4R4A4_UNORM = 115,

        DXGI_FORMAT_P208 = 130,
        DXGI_FORMAT_V208 = 131,
        DXGI_FORMAT_V408 = 132,


        DXGI_FORMAT_SAMPLER_FEEDBACK_MIN_MIP_OPAQUE = 189,
        DXGI_FORMAT_SAMPLER_FEEDBACK_MIP_REGION_USED_OPAQUE = 190,


        DXGI_FORMAT_FORCE_UINT = -1
    }

    enum BC_FORMAT : int
    {
        [Description("Pick Best Fit")]
        DXGI_FORMAT_UNKNOWN = 0,
        [Description("BC1 (RGBA) Low Quality Alpha")]
        DXGI_FORMAT_BC1_UNORM = 71,
        [Description("BC3 (RGBA) Medium Quality")]
        DXGI_FORMAT_BC3_UNORM = 77,
        [Description("BC4 (R8) Single-Channel Gray")]
        DXGI_FORMAT_BC4_UNORM = 80,
        [Description("BC5 (R8G8) Dual-Channel Gray")]
        DXGI_FORMAT_BC5_UNORM = 83,
        [Description("BC6 (UF16) HDR")]
        DXGI_FORMAT_BC6H_UF16 = 95,
        [Description("BC7 (RGBA) High Quality")]
        DXGI_FORMAT_BC7_UNORM = 98,
    }

    enum TextureDimension : int
    {
        [Description("1D Texture")]
        Texture1D,
        [Description("2D Texture")]
        Texture2D,
        [Description("3D Texture")]
        Texture3D,
        [Description("Texture Cube")]
        TextureCube,
    }

    // NOTE: should be the same as primal::content::texture_flags::flags enumeration in ContentToEngine.h
    enum TextureFlags : int
    {
        IsHdr = 0x01,
        HasAlpha = 0x02,
        IsPremultipliedAlpha = 0x04,
        IsImportedAsNormalMap = 0x08,
        IsCubeMap = 0x10,
        IsVolumeMap = 0x20,
        IsSRGB = 0x40,
    }

    class TextureImportSettings : ViewModelBase, IAssetImportSettings
    {
        public ObservableCollection<string> Sources { get; } = new();

        private TextureDimension _dimension = TextureDimension.Texture2D;
        public TextureDimension Dimension
        {
            get => _dimension;
            set
            {
                if (_dimension != value)
                {
                    _dimension = value;
                    OnPropertyChanged(nameof(Dimension));
                }
            }
        }

        private int _mipLevels;
        public int MipLevels
        {
            get => _mipLevels;
            set
            {
                value = Math.Clamp(value, 0, Texture.MaxMipLevels);
                if (_mipLevels != value)
                {
                    _mipLevels = value;
                    OnPropertyChanged(nameof(MipLevels));
                }
            }
        }

        private float _alphaThreshold;
        public float AlphaThreshold
        {
            get => _alphaThreshold;
            set
            {
                value = Math.Clamp(value, 0.0f, 1.0f);
                if (_alphaThreshold != value)
                {
                    _alphaThreshold = value;
                    OnPropertyChanged(nameof(AlphaThreshold));
                }
            }
        }

        private bool _preferBC7;
        public bool PreferBC7
        {
            get => _preferBC7;
            set
            {
                if (_preferBC7 != value)
                {
                    _preferBC7 = value;
                    OnPropertyChanged(nameof(PreferBC7));
                }
            }
        }

        private int _formatIndex;
        public int FormatIndex
        {
            get => _formatIndex;
            set
            {
                value = Math.Clamp(value, 0, Enum.GetValues<BC_FORMAT>().Length);
                if (_formatIndex != value)
                {
                    _formatIndex = value;
                    OnPropertyChanged(nameof(FormatIndex));
                    OnPropertyChanged(nameof(OutputFormat));
                }
            }
        }

        public DXGI_FORMAT OutputFormat => Compress ? (DXGI_FORMAT)Enum.GetValues<BC_FORMAT>()[FormatIndex] : DXGI_FORMAT.DXGI_FORMAT_UNKNOWN;

        private bool _compress;
        public bool Compress
        {
            get => _compress;
            set
            {
                if (_compress != value)
                {
                    _compress = value;
                    OnPropertyChanged(nameof(Compress));
                }
            }
        }

        public void ToBinary(BinaryWriter writer)
        {
            writer.Write(string.Join(";", Sources.ToArray()));
            writer.Write((int)Dimension);
            writer.Write(MipLevels);
            writer.Write(AlphaThreshold);
            writer.Write(PreferBC7);
            writer.Write(FormatIndex);
            writer.Write(Compress);
        }

        public void FromBinary(BinaryReader reader)
        {
            Sources.Clear();
            reader.ReadString().Split(";").ToList().ForEach(x => Sources.Add(x));
            Dimension = (TextureDimension)reader.ReadInt32();
            MipLevels = reader.ReadInt32();
            AlphaThreshold = reader.ReadSingle();
            PreferBC7 = reader.ReadBoolean();
            FormatIndex = reader.ReadInt32();
            Compress = reader.ReadBoolean();
        }

        public TextureImportSettings()
        {
            MipLevels = 0;
            AlphaThreshold = 0.5f;
            PreferBC7 = true;
            FormatIndex = 0;
            Compress = true;
        }
    }

    class Slice
    {
        public int Width { get; set; }
        public int Height { get; set; }
        public int RowPitch { get; set; }
        public int SlicePitch { get; set; }
        public byte[] RawContent { get; set; }
    }

    class Texture : Asset
    {
        public static int MaxMipLevels => 14;
        public static int MaxArraySize => 2048;
        public static int Max3DSize => 2048;

        public TextureImportSettings ImportSettings { get; } = new();

        // array ( mip ( subresource (slices) ) )
        // see https://learn.microsoft.com/en-us/windows/win32/direct3d12/subresources
        // for the order of slices within the array
        private List<List<List<Slice>>> _slices;
        public List<List<List<Slice>>> Slices
        {
            get => _slices;
            private set
            {
                if (_slices != value)
                {
                    _slices = value;
                    OnPropertyChanged(nameof(Slices));
                }
            }
        }

        private int _width;
        public int Width
        {
            get => _width;
            set
            {
                if (_width != value)
                {
                    _width = value;
                    OnPropertyChanged(nameof(Width));
                }
            }
        }

        private int _height;
        public int Height
        {
            get => _height;
            set
            {
                if (_height != value)
                {
                    _height = value;
                    OnPropertyChanged(nameof(Height));
                }
            }
        }

        private int _arraySize;
        public int ArraySize
        {
            get => _arraySize;
            set
            {
                if (_arraySize != value)
                {
                    // If this is a cube map then array size should be a multiple of 6
                    Debug.Assert(!(IsCubeMap && (value % 6) != 0));
                    _arraySize = value;
                    OnPropertyChanged(nameof(ArraySize));
                }
            }
        }

        private TextureFlags _flags;
        public TextureFlags Flags
        {
            get => _flags;
            set
            {
                if (_flags != value)
                {
                    _flags = value;
                    OnPropertyChanged(nameof(IsHDR));
                    OnPropertyChanged(nameof(HasAlpha));
                    OnPropertyChanged(nameof(IsPremultipliedAlpha));
                    OnPropertyChanged(nameof(IsNormalMap));
                    OnPropertyChanged(nameof(IsCubeMap));
                    OnPropertyChanged(nameof(IsVolumeMap));
                    OnPropertyChanged(nameof(IsSRGB));
                }
            }
        }

        public bool IsHDR => Flags.HasFlag(TextureFlags.IsHdr);
        public bool HasAlpha => Flags.HasFlag(TextureFlags.HasAlpha);
        public bool IsPremultipliedAlpha => Flags.HasFlag(TextureFlags.IsPremultipliedAlpha);
        public bool IsNormalMap => Flags.HasFlag(TextureFlags.IsImportedAsNormalMap);
        public bool IsCubeMap => Flags.HasFlag(TextureFlags.IsCubeMap);
        public bool IsVolumeMap => Flags.HasFlag(TextureFlags.IsVolumeMap);
        public bool IsSRGB => Flags.HasFlag(TextureFlags.IsSRGB);

        private int _mipLevels;
        public int MipLevels
        {
            get => _mipLevels;
            set
            {
                // NOTE: value is not clamped here, because we want to show the actual mip count of the loaded texture.
                if (_mipLevels != value)
                {
                    _mipLevels = value;
                    OnPropertyChanged(nameof(MipLevels));
                }
            }
        }

        private DXGI_FORMAT _format;
        public DXGI_FORMAT Format
        {
            get => _format;
            set
            {
                if (_format != value)
                {
                    _format = value;
                    OnPropertyChanged(nameof(Format));
                    OnPropertyChanged(nameof(FormatName));
                }
            }
        }

        public string FormatName => ImportSettings.Compress && !IsSRGB ? ((BC_FORMAT)Format).GetDescription() : Format.GetDescription();

        private static bool HasValidDimensions(int width, int height, int arrayOrDepth, bool is3D, string file)
        {
            bool result = true;

            if (width > (1 << MaxMipLevels) || height > (1 << MaxMipLevels))
            {
                Logger.Log(MessageType.Error, $"Image dimensions greater than {1 << MaxMipLevels}! (file: {file})");
                result = false;
            }

            if (width % 4 != 0 || height % 4 != 0)
            {
                Logger.Log(MessageType.Error, $"Image dimensions not a multiple of 4! (file: {file})");
                result = false;
            }

            if (is3D && (width > Max3DSize || height > Max3DSize || arrayOrDepth > Max3DSize))
            {
                Logger.Log(MessageType.Error, $"3D texture dimensions greater than {Max3DSize}! (file: {file})");
                result = false;
            }
            else if (arrayOrDepth > MaxArraySize)
            {
                Logger.Log(MessageType.Error, $"2D texture dimensions greater than {MaxArraySize}! (file: {file})");
                result = false;
            }

            if (width != height)
            {
                Logger.Log(MessageType.Warning, $"Non-square image (width and height not equal)! (file: {file})");
            }

            if (!MathUtil.IsPow2(width) || !MathUtil.IsPow2(height))
            {
                Logger.Log(MessageType.Warning, $"Image dimensions not power of 2! (file: {file})");
            }

            return result;
        }

        public override bool Import(string file)
        {
            Debug.Assert(File.Exists(file));

            try
            {
                Logger.Log(MessageType.Info, $"Importing image file {file}");
                ImportSettings.Sources.Add(file);
                (var slices, var icon) = ContentToolsAPI.Import(this);
                Debug.Assert(slices.Any() && slices.First().Any() && slices.First().First().Any());

                if (slices.Any() && slices.First().Any() && slices.First().First().Any())
                {
                    Slices = slices;
                }
                else
                {
                    return false;
                }

                var firstMip = Slices[0][0][0];
                if (!HasValidDimensions(firstMip.Width, firstMip.Height, ArraySize, IsVolumeMap, file))
                {
                    return false;
                }

                if (icon == null)
                {
                    Debug.Assert(!ImportSettings.Compress);
                    icon = firstMip;
                }

                Icon = BitmapHelper.CreateThumbnail(BitmapHelper.ImageFromSlice(icon, IsNormalMap), ContentInfo.IconWidth, ContentInfo.IconWidth);

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                var msg = $"Failed to read {file} for import";
                Debug.WriteLine(msg);
                Logger.Log(MessageType.Error, msg);
            }

            return false;
        }

        public override bool Load(string file)
        {
            Debug.Assert(File.Exists(file));
            Debug.Assert(Path.GetExtension(file).ToLower() == AssetFileExtension);

            try
            {
                using var reader = new BinaryReader(File.Open(file, FileMode.Open, FileAccess.Read));

                ReadAssetFileHeader(reader);
                ImportSettings.FromBinary(reader);

                Width = reader.ReadInt32();
                Height = reader.ReadInt32();
                ArraySize = reader.ReadInt32();
                Flags = (TextureFlags)reader.ReadInt32();
                MipLevels = reader.ReadInt32();
                Format = (DXGI_FORMAT)reader.ReadInt32();
                var compressedLength = reader.ReadInt32();
                Debug.Assert(compressedLength > 0);
                var compressed = reader.ReadBytes(compressedLength);
                DecompressContent(compressed);

                HasValidDimensions(Width, Height, ArraySize, IsVolumeMap, file);
                FullPath = file;

                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Logger.Log(MessageType.Error, $"Failed to load texture asset from file {file}");
            }

            return false;
        }

        public override byte[] PackForEngine()
        {
            throw new NotImplementedException();
        }

        public override IEnumerable<string> Save(string file)
        {
            try
            {
                if (TryGetAssetInfo(file) is AssetInfo info && info.Type == Type)
                {
                    Guid = info.Guid;
                }

                var compressed = CompressContent();
                Debug.Assert(compressed?.Length > 0);
                Hash = ContentHelper.ComputeHash(compressed);

                using var writer = new BinaryWriter(File.Open(file, FileMode.Create, FileAccess.Write));

                WriteAssetFileHeader(writer);
                ImportSettings.ToBinary(writer);

                writer.Write(Width);
                writer.Write(Height);
                writer.Write(ArraySize);
                writer.Write((int)Flags);
                writer.Write(MipLevels);
                writer.Write((int)Format);
                writer.Write(compressed.Length);
                writer.Write(compressed);

                FullPath = file;
                Logger.Log(MessageType.Info, $"Saved texture to {file}");

                var savedFiles = new List<string>() { file };
                return savedFiles;

            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Logger.Log(MessageType.Error, $"Failed to save texture to {file}");
            }

            return new List<string>();
        }

        private byte[] CompressContent()
        {
            Debug.Assert(Slices.First().Any() && Slices.First().Count == MipLevels);
            var data = ContentToolsAPI.SlicesToBinary(Slices);

            return CompressionHelper.Compress(data);
        }

        private void DecompressContent(byte[] compressed)
        {
            var decompressed = CompressionHelper.Decompress(compressed);
            Slices = ContentToolsAPI.SlicesFromBinary(decompressed, ArraySize, MipLevels, IsVolumeMap);
        }

        public Texture() : base(AssetType.Texture) { }

        public Texture(IAssetImportSettings importSettings) : this()
        {
            Debug.Assert(importSettings is TextureImportSettings);
            ImportSettings = (TextureImportSettings)importSettings;
        }
    }
}
