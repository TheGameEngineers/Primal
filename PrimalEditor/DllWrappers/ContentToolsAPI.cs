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
using System.Threading.Tasks;

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
        [Description("Number of images for cube-maps should be a multiple of 6, or the source images should be equirectangular images with the same size and format.")]
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
        public int CubeMapSize;
        public int MirrorCubeMap;
        public int PrefilterCubeMap;

        public void FromContentSettings(Content.TextureImportSettings settings)
        {
            Sources = string.Join(";", settings.Sources);
            SourceCount = settings.Sources.Count;
            Dimension = (int)settings.Dimension;
            MipLevels = settings.MipLevels;
            AlphaThreshold = settings.AlphaThreshold;
            PreferBC7 = settings.PreferBC7 ? 1 : 0;
            OutputFormat = (int)settings.OutputFormat;
            Compress = settings.Compress ? 1 : 0;
            CubeMapSize = settings.CubeMapSize;
            MirrorCubeMap = settings.MirrorCubeMap ? 1 : 0;
            PrefilterCubeMap = settings.PrefilterCubeMap ? 1 : 0;
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

        public TextureInfo Clone() => new()
        {
            Width = Width,
            Height = Height,
            ArraySize = ArraySize,
            MipLevels = MipLevels,
            Format = Format,
            ImportError = ImportError,
            Flags = Flags,
        };
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

        public static SliceArray3D SlicesFromBinary(byte[] data, int arraySize, int mipLevels, bool is3D)
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
            var slices = new SliceArray3D();
            for (var i = 0; i < arraySize; ++i)
            {
                var arraySlice = new List<List<Slice>>();
                for (var j = 0; j < mipLevels; ++j)
                {
                    var mipSlice = new List<Slice>();
                    for (var k = 0; k < depthPerMipLevel[j]; ++k)
                    {
                        var slice = new Slice()
                        {
                            Width = reader.ReadInt32(),
                            Height = reader.ReadInt32(),
                            RowPitch = reader.ReadInt32(),
                            SlicePitch = reader.ReadInt32()
                        };
                        slice.RawContent = reader.ReadBytes(slice.SlicePitch);

                        mipSlice.Add(slice);
                    }

                    arraySlice.Add(mipSlice);
                }

                slices.Add(arraySlice);
            }

            return slices;
        }

        public static byte[] SlicesToBinary(SliceArray3D slices)
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

        public SliceArray3D GetSlices()
        {
            Debug.Assert(Info.MipLevels > 0);
            Debug.Assert(SubresourceData != IntPtr.Zero && SubresourceSize > 0);

            var subresourceData = new byte[SubresourceSize];
            Marshal.Copy(SubresourceData, subresourceData, 0, SubresourceSize);

            return SlicesFromBinary(subresourceData, Info.ArraySize, Info.MipLevels,
                ((TextureFlags)Info.Flags).HasFlag(TextureFlags.IsVolumeMap));
        }

        public Slice GetIcon()
        {
            // Subresources are not compressed. Just use the first image for the icon.
            if (ImportSettings.Compress == 0) return null;

            Debug.Assert(Icon != IntPtr.Zero && IconSize > 0);

            var icon = new byte[IconSize];
            Marshal.Copy(Icon, icon, 0, IconSize);

            return SlicesFromBinary(icon, 1, 1, false).First()?.First()?.First();
        }

        public void SetSubresourceData(SliceArray3D slices)
        {
            var subresourceData = SlicesToBinary(slices);
            SubresourceData = Marshal.AllocCoTaskMem(subresourceData.Length);
            SubresourceSize = subresourceData.Length;
            Marshal.Copy(subresourceData, 0, SubresourceData, SubresourceSize);
        }

        public void GetTextureDataInfo(Texture texture)
        {
            Info.Width = texture.Width;
            Info.Height = texture.Height;
            Info.ArraySize = texture.ArraySize;
            Info.MipLevels = texture.MipLevels;
            Info.Format = (int)texture.Format;
            Info.Flags = (int)texture.Flags;
        }

        public void GetTextureInfo(Texture texture)
        {
            // NOTE: set the flags first, because some properties check flags when they're set.
            texture.Flags = (TextureFlags)Info.Flags;
            texture.Width = Info.Width;
            texture.Height = Info.Height;
            texture.ArraySize = Info.ArraySize;
            texture.MipLevels = Info.MipLevels;
            texture.Format = (DXGI_FORMAT)Info.Format;
        }

        public TextureData Clone(Content.TextureImportSettings settings)
        {
            TextureData data = new();
            if (SubresourceData != IntPtr.Zero && SubresourceSize > 0)
            {
                var bytes = new byte[SubresourceSize];
                data.SubresourceData = Marshal.AllocCoTaskMem(SubresourceSize);
                data.SubresourceSize = SubresourceSize;
                Marshal.Copy(SubresourceData, bytes, 0, SubresourceSize);
                Marshal.Copy(bytes, 0, data.SubresourceData, SubresourceSize);
            }

            if (Icon != IntPtr.Zero && IconSize > 0)
            {
                var bytes = new byte[IconSize];
                data.Icon = Marshal.AllocCoTaskMem(IconSize);
                data.IconSize = IconSize;
                Marshal.Copy(Icon, bytes, 0, IconSize);
                Marshal.Copy(bytes, 0, data.Icon, IconSize);
            }

            data.Info = Info.Clone();
            data.ImportSettings.FromContentSettings(settings);

            return data;
        }

        public void Dispose()
        {
            Marshal.FreeCoTaskMem(SubresourceData);
            Marshal.FreeCoTaskMem(Icon);
            GC.SuppressFinalize(this);
        }

        ~TextureData() { Dispose(); }
    }

    [StructLayout(LayoutKind.Sequential)]
    class GeometryImportSettings
    {
        // TODO: get from application settings
        public float SmoothingAngle = 178f;
        public byte CalculateNormals = 0;
        public byte CalculateTangents = 1;
        public byte ReverseHandedness = 0;
        public byte ImportEmbeddedTextures = 1;
        public byte ImportAnimations = 1;
        public byte CoalesceMeshes = 0;

        private byte ToByte(bool value) => value ? (byte)1 : (byte)0;

        public void FromContentSettings(Geometry geometry)
        {
            var settings = geometry.ImportSettings;

            SmoothingAngle = settings.SmoothingAngle;
            CalculateNormals = ToByte(settings.CalculateNormals);
            CalculateTangents = ToByte(settings.CalculateTangents);
            ReverseHandedness = ToByte(settings.ReverseHandedness);
            ImportEmbeddedTextures = ToByte(settings.ImportEmbeddedTextures);
            ImportAnimations = ToByte(settings.ImportAnimations);
            CoalesceMeshes = ToByte(settings.CoalesceMeshes);
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
        public PrimitiveMeshType Type;
        public int SegmentsX = 1;
        public int SegmentsY = 1;
        public int SegmentsZ = 1;
        public Vector3 Size = new(1f);
        public int LOD = 0;
    }
}

namespace PrimalEditor.DllWrappers
{
    static partial class ContentToolsAPI
    {
        private const string _toolsDLL = "ContentTools.dll";
        private delegate void ProgressCallback(int value, int maxValue);

        [LibraryImport(_toolsDLL)]
        public static partial void ShutDownContentTools();

        #region Texture       
        [DllImport(_toolsDLL)]
        private static extern void PrefilterDiffuseIBL([In, Out] TextureData data);

        [DllImport(_toolsDLL)]
        private static extern void PrefilterSpecularIBL([In, Out] TextureData data);

        [DllImport(_toolsDLL)]
        private static extern void ComputeBrdfIntegrationLut([In, Out] TextureData data);

        public static void ComputeBrdfIntegrationLut(Texture texture)
        {
            using var textureData = new TextureData();

            try
            {
                texture.ImportSettings.Compress = false;
                texture.ImportSettings.MipLevels = 1;
                textureData.ImportSettings.FromContentSettings(texture.ImportSettings);
                ComputeBrdfIntegrationLut(textureData);

                if (textureData.Info.ImportError != 0)
                {
                    Logger.Log(MessageType.Error, $"Error: {EnumExtensions.GetDescription((TextureImportError)textureData.Info.ImportError)}");
                    throw new Exception($"Error while trying to compute BRDF integration LUT. Error code {textureData.Info.ImportError}");
                }

                textureData.GetTextureInfo(texture);
                texture.SetData(textureData.GetSlices(), null, null);
            }
            catch (Exception ex)
            {
                Logger.Log(MessageType.Error, $"Failed to compute BRDF integration LUT {texture.FileName}");
                Debug.WriteLine(ex.Message);
            }
        }

        [DllImport(_toolsDLL)]
        private static extern void Decompress([In, Out] TextureData data);

        public static SliceArray3D Decompress(Texture texture)
        {
            Debug.Assert(texture.ImportSettings.Compress);
            using var textureData = new TextureData();

            try
            {
                textureData.GetTextureDataInfo(texture);
                textureData.ImportSettings.FromContentSettings(texture.ImportSettings);
                textureData.SetSubresourceData(texture.Slices);

                Decompress(textureData);

                if (textureData.Info.ImportError != 0)
                {
                    Logger.Log(MessageType.Error, $"Error: {EnumExtensions.GetDescription((TextureImportError)textureData.Info.ImportError)}");
                    throw new Exception($"Error while trying to decompress mipmaps. Error code {textureData.Info.ImportError}");
                }

                return textureData.GetSlices();
            }
            catch (Exception ex)
            {
                Logger.Log(MessageType.Error, $"Failed to decompress mipmaps from {texture.FileName}");
                Debug.WriteLine(ex.Message);
                return [];
            }
        }

        [DllImport(_toolsDLL)]
        private static extern void Import([In, Out] TextureData data);

        public static void Import(Texture texture)
        {
            Debug.Assert(texture.ImportSettings.Sources.Any());
            using var textureData = new TextureData();

            try
            {
                textureData.ImportSettings.FromContentSettings(texture.ImportSettings);
                Import(textureData);

                if (textureData.Info.ImportError != 0)
                {
                    Logger.Log(MessageType.Error, $"Texture import error: {EnumExtensions.GetDescription((TextureImportError)textureData.Info.ImportError)}");
                    throw new Exception($"Error while trying to import image. Error code {textureData.Info.ImportError}");
                }

                Texture diffuseIBLCubemap = null;

                if (texture.ImportSettings.PrefilterCubeMap &&
                    ((TextureFlags)textureData.Info.Flags).HasFlag(TextureFlags.IsCubeMap))
                {
                    using var diffuseData = textureData.Clone(texture.ImportSettings);
                    var diffuseResult = Task.Run(() => PrefilterDiffuseIBL(diffuseData));
                    var specularResult = Task.Run(() => PrefilterSpecularIBL(textureData));

                    diffuseIBLCubemap = texture.IBLPair ?? new();

                    diffuseResult.Wait();

                    IAssetImportSettings.CopyImportSettings(texture.ImportSettings, diffuseIBLCubemap.ImportSettings);
                    diffuseIBLCubemap.ImportSettings.Sources.Clear();
                    diffuseData.GetTextureInfo(diffuseIBLCubemap);
                    if (!diffuseIBLCubemap.SetData(diffuseData.GetSlices(), diffuseData.GetIcon(), texture)) throw new InvalidDataException();

                    specularResult.Wait();
                }

                textureData.GetTextureInfo(texture);
                if (!texture.SetData(textureData.GetSlices(), textureData.GetIcon(), diffuseIBLCubemap)) throw new InvalidDataException();
            }
            catch (Exception ex)
            {
                Logger.Log(MessageType.Error, $"Failed to import from {texture.FileName}");
                Debug.WriteLine(ex.Message);
            }
        }

        #endregion Texture
        #region Geometry
        private static void GeometryFromSceneData(Geometry geometry, Action<SceneData> sceneDataGenerator, string failureMessage)
        {
            Debug.Assert(geometry != null);
            using var sceneData = new SceneData();
            try
            {
                sceneData.ImportSettings.FromContentSettings(geometry);
                sceneDataGenerator(sceneData);
                if (sceneData.Data == IntPtr.Zero || sceneData.DataSize == 0)
                {
                    throw new Exception(failureMessage);
                }

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
        public static void CreatePrimitiveMesh(Geometry geometry, PrimitiveInitInfo info)
        {
            GeometryFromSceneData(geometry, (sceneData) => CreatePrimitiveMesh(sceneData, info), $"Failed to create {info.Type} primitive mesh.");
        }

        [DllImport(_toolsDLL)]
        private static extern void ImportFbx(string file, [In, Out] SceneData data, ProgressCallback callback);

        public static void ImportFbx(string file, Geometry geometry)
        {
            var item = ImportingItemCollection.GetItem(geometry);
            ProgressCallback callback = item != null ? item.SetProgress : null;
            GeometryFromSceneData(geometry, (sceneData) => ImportFbx(file, sceneData, callback), $"Failed to import from FBX file: {file}");
        }
        #endregion Geometry
    }
}
