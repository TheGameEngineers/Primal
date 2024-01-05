// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using PrimalEditor.ContentToolsAPIStructs;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Markup;

namespace PrimalEditor.ContentToolsAPIStructs
{
    enum TextureImportError : int
    {
        [Description("Import succeeded")]
        Succeeded = 0,
        [Description("Unknown error")]
        Unknown,
        [Description("Texture compression failed")]
        Compress,
        [Description("Texture decompression failed")]
        Decompress,
        [Description("Failed to load the texture into memory")]
        Load,
        [Description("Texture mipmap generation failed")]
        MipmapGeneration,
        [Description("Maximum subresource size of 4GB exceeded")]
        MaxSizeExceeded,
        [Description("Source images don't have the same dimensions")]
        SizeMismatch,
        [Description("Source images don't have the same format")]
        FormatMismatch,
        [Description("Source image file not found")]
        FileNotFound,
        [Description("Number of images for cube-maps should be a multiple of 6")]
        NeedSixImages,
    }

    [StructLayout(LayoutKind.Sequential)]
    class TextureImportSettings
    {
        public string Sources;
        public int SourceCount;
        public int Dimension;
        public int MipLevels;
        public float AlphaThreshold;
        public int PreferBC7;
        public int OutputFormat;
        public int Compress;

        public void FromContentSettings(Texture texture)
        {
            var settings = texture.ImportSettings;

            Sources = string.Join(";", settings.Sources);
            SourceCount = settings.Sources.Count;
            Dimension = (int)settings.Dimension;
            MipLevels = settings.MipLevels;
            AlphaThreshold = settings.AlphaThreshold;
            PreferBC7 = settings.PreferBC7 ? 1 : 0;
            OutputFormat = (int)settings.OutputFormat;
            Compress = settings.Compress ? 1 : 0;
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    class TextureInfo
    {
        public int Width;
        public int Height;
        public int ArraySize;
        public int MipLevels;
        public int Format;
        public int ImportError;
        public int Flags;
    }

    [StructLayout(LayoutKind.Sequential)]
    class TextureData : IDisposable
    {
        public IntPtr SubresourceData;
        public int SubresourceSize;
        public IntPtr Icon;
        public int IconSize;
        public TextureInfo Info = new();
        public TextureImportSettings ImportSettings = new();

        public void Dispose()
        {
            Marshal.FreeCoTaskMem(SubresourceData);
            Marshal.FreeCoTaskMem(Icon);
            GC.SuppressFinalize(this);
        }

        ~TextureData()
        {
            Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    class GeometryImportSettings
    {
        public float SmoothingAngle = 178f;
        public byte CalculateNormals = 0;
        public byte CalculateTangents = 1;
        public byte ReverseHandedness = 0;
        public byte ImportEmbededTextures = 1;
        public byte ImportAnimations = 1;

        private byte ToByte(bool value) => value ? (byte)1 : (byte)0;

        public void FromContentSettings(Content.Geometry geometry)
        {
            var settings = geometry.ImportSettings;

            SmoothingAngle = settings.SmoothingAngle;
            CalculateNormals = ToByte(settings.CalculateNormals);
            CalculateTangents = ToByte(settings.CalculateTangents);
            ReverseHandedness = ToByte(settings.ReverseHandedness);
            ImportEmbededTextures = ToByte(settings.ImportEmbeddedTextures);
            ImportAnimations = ToByte(settings.ImportAnimations);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    class SceneData : IDisposable
    {
        public IntPtr Data;
        public int DataSize;
        public GeometryImportSettings ImportSettings = new();

        public void Dispose()
        {
            Marshal.FreeCoTaskMem(Data);
            GC.SuppressFinalize(this);
        }

        ~SceneData()
        {
            Dispose();
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    class PrimitiveInitInfo
    {
        public Content.PrimitiveMeshType Type;
        public int SegmentX = 1;
        public int SegmentY = 1;
        public int SegmentZ = 1;
        public Vector3 Size = new(1f);
        public int LOD = 0;
    }
}

namespace PrimalEditor.DllWrappers
{
    static class ContentToolsAPI
    {
        private const string _toolsDLL = "ContentTools.dll";

        [DllImport(_toolsDLL)]
        public static extern void ShutDownContentTools();

        #region Texture
        private static List<List<List<Slice>>> GetSlices(TextureData data)
        {
            Debug.Assert(data.Info.MipLevels > 0);
            Debug.Assert(data.SubresourceData != IntPtr.Zero && data.SubresourceSize > 0);

            var subresourceData = new byte[data.SubresourceSize];
            Marshal.Copy(data.SubresourceData, subresourceData, 0, data.SubresourceSize);

            return SlicesFromBinary(subresourceData, data.Info.ArraySize, data.Info.MipLevels,
                ((TextureFlags)data.Info.Flags).HasFlag(TextureFlags.IsVolumeMap));
        }

        private static Slice GetIcon(TextureData data)
        {
            // Subresources are not compressed. Just use the first image for the icon.
            if (data.ImportSettings.Compress == 0) return null;

            Debug.Assert(data.Icon != IntPtr.Zero && data.IconSize > 0);

            var icon = new byte[data.IconSize];
            Marshal.Copy(data.Icon, icon, 0, data.IconSize);

            return SlicesFromBinary(icon, 1, 1, false).First()?.First()?.First();
        }

        private static void SetSubresourceData(List<List<List<Slice>>> slices, TextureData data)
        {
            var subresourceData = SlicesToBinary(slices);
            data.SubresourceData = Marshal.AllocCoTaskMem(subresourceData.Length);
            data.SubresourceSize= subresourceData.Length;
            Marshal.Copy(subresourceData, 0, data.SubresourceData, data.SubresourceSize);
        }

        private static void GetTextureDataInfo(Texture texture, TextureData data)
        {
            var info = data.Info;

            info.Width = texture.Width;
            info.Height = texture.Height;
            info.ArraySize = texture.ArraySize;
            info.MipLevels = texture.MipLevels;
            info.Format = (int)texture.Format;
            info.Flags = (int)texture.Flags;
        }

        private static void GetTextureInfo(Texture texture, TextureData data)
        {
            var info = data.Info;

            texture.Width = info.Width;
            texture.Height = info.Height;
            texture.ArraySize = info.ArraySize;
            texture.MipLevels = info.MipLevels;
            texture.Format = (DXGI_FORMAT)info.Format;
            texture.Flags = (TextureFlags)info.Flags;
        }

        public static List<List<List<Slice>>> SlicesFromBinary(byte[] data, int arraySize, int mipLevels, bool is3D)
        {
            Debug.Assert(data?.Length > 0 && arraySize > 0);
            Debug.Assert(mipLevels > 0 && mipLevels < Texture.MaxMipLevels);

            var depthPerMipLevel = Enumerable.Repeat(1, mipLevels).ToList();

            if (is3D)
            {
                var depth = arraySize;
                arraySize = 1;
                for (var i = 0; i < mipLevels; ++i)
                {
                    depthPerMipLevel[i] = depth;
                    depth = Math.Max(depth >> 1, 1);
                }
            }

            using var reader = new BinaryReader(new MemoryStream(data));
            var slices = new List<List<List<Slice>>>();
            for (var i = 0; i < arraySize; ++i)
            {
                var arraySlice = new List<List<Slice>>();
                for (var j = 0; j < mipLevels; ++j)
                {
                    var mipSlice = new List<Slice>();
                    for (var k = 0; k < depthPerMipLevel[i]; ++k)
                    {
                        var slice = new Slice();
                        slice.Width = reader.ReadInt32();
                        slice.Height = reader.ReadInt32();
                        slice.RowPitch = reader.ReadInt32();
                        slice.SlicePitch = reader.ReadInt32();
                        slice.RawContent = reader.ReadBytes(slice.SlicePitch);

                        mipSlice.Add(slice);
                    }

                    arraySlice.Add(mipSlice);
                }

                slices.Add(arraySlice);
            }

            return slices;
        }

        public static byte[] SlicesToBinary(List<List<List<Slice>>> slices)
        {
            Debug.Assert(slices?.Any() == true && slices.First()?.Any() == true);
            using var writer = new BinaryWriter(new MemoryStream());
            foreach (var arraySlice in slices)
            {
                foreach (var mipLevel in arraySlice)
                {
                    foreach (var slice in mipLevel)
                    {
                        writer.Write(slice.Width);
                        writer.Write(slice.Height);
                        writer.Write(slice.RowPitch);
                        writer.Write(slice.SlicePitch);
                        writer.Write(slice.RawContent);
                    }
                }
            }

            writer.Flush();
            var data = (writer.BaseStream as MemoryStream)?.ToArray();
            Debug.Assert(data?.Length > 0);

            return data;
        }

        [DllImport(_toolsDLL)]
        private static extern void Decompress([In, Out] TextureData data);

        public static List<List<List<Slice>>> Decompress(Texture texture)
        {
            Debug.Assert(texture.ImportSettings.Compress);
            using var textureData = new TextureData();

            try
            {
                GetTextureDataInfo(texture, textureData);
                textureData.ImportSettings.FromContentSettings(texture);
                SetSubresourceData(texture.Slices, textureData);

                Decompress(textureData);

                if (textureData.Info.ImportError != 0)
                {
                    Logger.Log(MessageType.Error, $"Error: {EnumExtensions.GetDescription((TextureImportError)textureData.Info.ImportError)}");
                    throw new Exception($"Error while trying to decompress mipmaps. Error code {textureData.Info.ImportError}");
                }

                return GetSlices(textureData);
            }
            catch (Exception ex)
            {
                Logger.Log(MessageType.Error, $"Failed to decompress mipmaps from {texture.FileName}");
                Debug.WriteLine(ex.Message);
                return new();
            }
        }

        [DllImport(_toolsDLL)]
        private static extern void Import([In, Out] TextureData data);

        public static (List<List<List<Slice>>> slices, Slice icon) Import(Texture texture)
        {
            Debug.Assert(texture.ImportSettings.Sources.Any());
            using var textureData = new TextureData();

            try
            {
                textureData.ImportSettings.FromContentSettings(texture);
                Import(textureData);

                if (textureData.Info.ImportError != 0)
                {
                    Logger.Log(MessageType.Error, $"Texture import error: {EnumExtensions.GetDescription((TextureImportError)textureData.Info.ImportError)}");
                    throw new Exception($"Error while trying to import image. Error code {textureData.Info.ImportError}");
                }

                GetTextureInfo(texture, textureData);
                return (GetSlices(textureData), GetIcon(textureData));
            }
            catch (Exception ex)
            {
                Logger.Log(MessageType.Error, $"Failed to import from {texture.FileName}");
                Debug.WriteLine(ex.Message);
                return new();
            }
        }

        #endregion Texture
        #region Geometry
        private static void GeometryFromSceneData(Content.Geometry geometry, Action<SceneData> sceneDataGenerator, string failureMessage)
        {
            Debug.Assert(geometry != null);
            using var sceneData = new SceneData();
            try
            {
                sceneData.ImportSettings.FromContentSettings(geometry);
                sceneDataGenerator(sceneData);
                Debug.Assert(sceneData.Data != IntPtr.Zero && sceneData.DataSize > 0);
                var data = new byte[sceneData.DataSize];
                Marshal.Copy(sceneData.Data, data, 0, sceneData.DataSize);
                geometry.FromRawData(data);
            }
            catch (Exception ex)
            {
                Logger.Log(MessageType.Error, failureMessage);
                Debug.WriteLine(ex.Message);
            }
        }

        [DllImport(_toolsDLL)]
        private static extern void CreatePrimitiveMesh([In, Out] SceneData data, PrimitiveInitInfo info);
        public static void CreatePrimitveMesh(Content.Geometry geometry, PrimitiveInitInfo info)
        {
            GeometryFromSceneData(geometry, (sceneData) => CreatePrimitiveMesh(sceneData, info), $"Failed to create {info.Type} primitive mesh.");
        }

        [DllImport(_toolsDLL)]
        private static extern void ImportFbx(string file, [In, Out] SceneData data);

        public static void ImportFbx(string file, Content.Geometry geometry)
        {
            GeometryFromSceneData(geometry, (sceneData) => ImportFbx(file, sceneData), $"Failed to import from FBX file: {file}");
        }
        #endregion Geometry
    }
}
