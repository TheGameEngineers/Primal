// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using PrimalEditor.Editors;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;

namespace PrimalEditor.Content;

enum PrimitiveMeshType
{
    Plane,
    Cube,
    UvSphere,
    IcoSphere,
    Cylinder,
    Capsule
}

// NOTE: copy of element_type enum in ContentTools Geometry.h
enum ElementsType : UInt32
{
    PositionOnly = 0x00,
    StaticNormal = 0x01,
    StaticNormalTexture = 0x03,
    StaticColor = 0x04,
    Skeletal = 0x08,
    SkeletalColor = Skeletal | StaticColor,
    SkeletalNormal = Skeletal | StaticNormal,
    SkeletalNormalColor = SkeletalNormal | StaticColor,
    SkeletalNormalTexture = Skeletal | StaticNormalTexture,
    SkeletalNormalTextureColor = SkeletalNormalTexture | StaticColor,
}

enum PrimitiveTopology
{
    PointList = 1,
    LineList,
    LineStrip,
    TriangleList,
    TriangleStrip,
}

class MeshInfo : ViewModelBase
{
    public string Name { get; init; }

    public byte[] Icon
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Icon));
            }
        }
    }

    public int IndexCount { get; init; }
    public int VertexCount { get; init; }
    public int TriangleCount { get; init; }
}
class LodInfo
{
    public string Name { get; init; }
    public float Threshold { get; init; }
    public List<MeshInfo> Meshes { get; init; }
}

class GeometryMetadata : AssetMetadata
{
    public string Name { get; init; }
    public List<LodInfo> LODs { get; init; }
}

class Mesh : ViewModelBase
{
    public static int PositionSize => sizeof(float) * 3;

    public int ElementSize
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(ElementSize));
            }
        }
    }

    public int VertexCount
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(VertexCount));
            }
        }
    }

    public int IndexSize
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IndexSize));
            }
        }
    }

    public int IndexCount
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IndexCount));
            }
        }
    }

    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public ElementsType ElementsType { get; set; }

    public PrimitiveTopology PrimitiveTopology { get; set; }

    public byte[] Positions { get; set; }
    public byte[] Elements { get; set; }
    public byte[] Indices { get; set; }
}

class MeshLOD : ViewModelBase
{
    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public float LodThreshold
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(LodThreshold));
            }
        }
    }

    public List<Mesh> Meshes { get; } = [];
}

class LODGroup : ViewModelBase
{
    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public List<MeshLOD> LODs { get; } = [];
}

class GeometryImportSettings : ViewModelBase, IAssetImportSettings
{
    public bool CalculateNormals
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(CalculateNormals));
            }
        }
    }

    public bool CalculateTangents
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(CalculateTangents));
            }
        }
    }

    public float SmoothingAngle
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(SmoothingAngle));
            }
        }
    }

    public bool ReverseHandedness
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(ReverseHandedness));
            }
        }
    }

    public bool ImportEmbeddedTextures
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(ImportEmbeddedTextures));
            }
        }
    }

    public bool ImportAnimations
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(ImportAnimations));
            }
        }
    }

    public bool CoalesceMeshes
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(CoalesceMeshes));
            }
        }
    }

    public GeometryImportSettings()
    {
        CalculateNormals = false;
        CalculateTangents = true;
        SmoothingAngle = 178f;
        ReverseHandedness = false;
        ImportEmbeddedTextures = true;
        ImportAnimations = true;
        CoalesceMeshes = false;
    }

    public void ToBinary(BinaryWriter writer)
    {
        writer.Write(CalculateNormals);
        writer.Write(CalculateTangents);
        writer.Write(SmoothingAngle);
        writer.Write(ReverseHandedness);
        writer.Write(ImportEmbeddedTextures);
        writer.Write(ImportAnimations);
        writer.Write(CoalesceMeshes);
    }

    public void FromBinary(BinaryReader reader)
    {
        CalculateNormals = reader.ReadBoolean();
        CalculateTangents = reader.ReadBoolean();
        SmoothingAngle = reader.ReadSingle();
        ReverseHandedness = reader.ReadBoolean();
        ImportEmbeddedTextures = reader.ReadBoolean();
        ImportAnimations = reader.ReadBoolean();
        CoalesceMeshes = reader.ReadBoolean();
    }
}

class Geometry : Asset
{
    private readonly List<LODGroup> _lodGroups = [];
    private static readonly Lock _lock = new();

    public static AssetInfo Default => DefaultAssets.DefaultGeometry;

    public GeometryImportSettings ImportSettings { get; } = new();

    public LODGroup GetLODGroup(int lodGroup = 0)
    {
        Debug.Assert(lodGroup >= 0 && lodGroup < _lodGroups.Count);
        return (lodGroup < _lodGroups.Count) ? _lodGroups[lodGroup] : null;
    }

    public void FromRawData(byte[] data)
    {
        Debug.Assert(data?.Length > 0);
        _lodGroups.Clear();

        using var reader = new BinaryReader(new MemoryStream(data));
        // skip scene name string
        var s = reader.ReadInt32();
        reader.BaseStream.Position += s;
        // get number of LODs
        var numLODGroups = reader.ReadInt32();
        Debug.Assert(numLODGroups > 0);

        for (int i = 0; i < numLODGroups; ++i)
        {
            // get LOD group's name
            s = reader.ReadInt32();
            string lodGroupName;
            if (s > 0)
            {
                var nameBytes = reader.ReadBytes(s);
                lodGroupName = Encoding.UTF8.GetString(nameBytes);
            }
            else
            {
                lodGroupName = $"lod_{ContentHelper.GetRandomString()}";
            }

            // get number of meshes in this LOD group
            var numMeshes = reader.ReadInt32();
            Debug.Assert(numMeshes > 0);
            var lods = ReadMeshLODs(numMeshes, reader);

            var lodGroup = new LODGroup() { Name = lodGroupName };
            lods.ForEach(lodGroup.LODs.Add);

            _lodGroups.Add(lodGroup);
        }
    }

    private static List<MeshLOD> ReadMeshLODs(int numMeshes, BinaryReader reader)
    {
        var lodIds = new List<int>();
        var lodList = new List<MeshLOD>();
        for (int i = 0; i < numMeshes; ++i)
        {
            ReadMeshes(reader, lodIds, lodList);
        }

        return lodList;
    }

    private static void ReadMeshes(BinaryReader reader, List<int> lodIds, List<MeshLOD> lodList)
    {
        // get mesh's name
        var s = reader.ReadInt32();
        string meshName;
        if (s > 0)
        {
            var nameBytes = reader.ReadBytes(s);
            meshName = Encoding.UTF8.GetString(nameBytes);
        }
        else
        {
            meshName = $"mesh_{ContentHelper.GetRandomString()}";
        }

        var mesh = new Mesh() { Name = meshName };

        var lodId = reader.ReadInt32();
        mesh.ElementSize = reader.ReadInt32();
        mesh.ElementsType = (ElementsType)reader.ReadInt32();
        mesh.PrimitiveTopology = PrimitiveTopology.TriangleList; // ContentTools currently only support triangle list meshes.
        mesh.VertexCount = reader.ReadInt32();
        mesh.IndexSize = reader.ReadInt32();
        mesh.IndexCount = reader.ReadInt32();
        var lodThreshold = reader.ReadSingle();

        var elementsBufferSize = mesh.ElementSize * mesh.VertexCount;
        var indexBufferSize = mesh.IndexSize * mesh.IndexCount;

        mesh.Positions = reader.ReadBytes(Mesh.PositionSize * mesh.VertexCount);
        mesh.Elements = reader.ReadBytes(elementsBufferSize);
        mesh.Indices = reader.ReadBytes(indexBufferSize);

        MeshLOD lod;
        if (ID.IsValid(lodId) && lodIds.Contains(lodId))
        {
            lod = lodList[lodIds.IndexOf(lodId)];
            Debug.Assert(lod != null);
        }
        else
        {
            lodIds.Add(lodId);
            lod = new MeshLOD() { Name = meshName, LodThreshold = lodThreshold };
            lodList.Add(lod);
        }

        lod.Meshes.Add(mesh);
    }

    public override bool Import(string file)
    {
        Debug.Assert(File.Exists(file));
        Debug.Assert(!string.IsNullOrEmpty(FullPath));
        var ext = Path.GetExtension(file).ToLower();

        if (ext == ".fbx")
        {
            return ImportFbx(file);
        }

        return false;
    }

    private bool ImportFbx(string file)
    {
        Logger.Log(MessageType.Info, $"Importing FBX file {file}");
        var tempPath = Application.Current.Dispatcher.Invoke(() => Project.Current.TempFolder);
        if (string.IsNullOrEmpty(tempPath)) return false;

        lock (_lock)
        {
            if (!Directory.Exists(tempPath)) Directory.CreateDirectory(tempPath);
        }

        var tempFile = $"{tempPath}{ContentHelper.GetRandomString()}.fbx";
        File.Copy(file, tempFile, true);
        bool result = false;

        try
        {
            ContentToolsAPI.ImportFbx(tempFile, this);
            result = true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            var msg = $"Failed to read {file} for import";
            Debug.WriteLine(msg);
            Logger.Log(MessageType.Error, msg);
        }

        if (ImportSettings.ImportEmbeddedTextures)
        {
            var embeddedMediaDir = $@"{tempPath}{Path.GetFileNameWithoutExtension(tempFile)}.fbm{Path.DirectorySeparatorChar}";
            if (Directory.Exists(embeddedMediaDir))
            {
                Debug.Assert(!string.IsNullOrEmpty(FullPath));
                var files = Directory.GetFiles(embeddedMediaDir);
                new ConfigureImportSettings(files, Path.GetDirectoryName(FullPath)).Import();
            }
        }

        return result;
    }

    public override bool Load(string file)
    {
        Debug.Assert(File.Exists(file));
        Debug.Assert(Path.GetExtension(file).ToLower() == AssetFileExtension);

        try
        {
            byte[] data = null;
            using (var reader = new BinaryReader(File.Open(file, FileMode.Open, FileAccess.Read)))
            {
                ReadAssetFileHeader(reader);
                ImportSettings.FromBinary(reader);
                int dataLength = reader.ReadInt32();
                Debug.Assert(dataLength > 0);
                data = reader.ReadBytes(dataLength);
            }

            Debug.Assert(data?.Length > 0);

            using (var reader = new BinaryReader(new MemoryStream(data)))
            {
                LODGroup lodGroup = new() { Name = reader.ReadString() };
                var lodGroupCount = reader.ReadInt32();

                for (int i = 0; i < lodGroupCount; ++i)
                {
                    lodGroup.LODs.Add(BinaryToLOD(reader));
                }

                _lodGroups.Clear();
                _lodGroups.Add(lodGroup);
            }

            // For Testing. Remove later!
            //PackForEngine();
            // For Testing. Remove later!

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to load geometry asset from file: {file}");
        }

        return false;
    }

    public override IEnumerable<string> Save(string file)
    {
        Debug.Assert(_lodGroups.Count > 0);
        var savedFiles = new List<string>();
        if (_lodGroups.Count == 0) return savedFiles;

        var path = Path.GetDirectoryName(file) + Path.DirectorySeparatorChar;
        var fileName = Path.GetFileNameWithoutExtension(file);

        try
        {
            foreach (var lodGroup in _lodGroups)
            {
                Debug.Assert(lodGroup.LODs.Count > 0);
                // Use the name of most detailed LOD for file name
                var meshFileName = ContentHelper.SanitizeFileName(
                    path + fileName + ((_lodGroups.Count > 1) ? "_" + ((lodGroup.LODs.Count > 1) ? lodGroup.Name : lodGroup.LODs[0].Name) : string.Empty)) + AssetFileExtension;
                // NOTE: we have to make a different id for each new asset file, but if a geometry asset file
                //       with the same name already exists then we use its guid instead.
                Guid = TryGetAssetInfo(meshFileName) is AssetInfo info && info.Type == Type ? info.Guid : Guid.NewGuid();
                byte[] data = null;
                using (var writer = new BinaryWriter(new MemoryStream()))
                {
                    writer.Write(lodGroup.Name);
                    writer.Write(lodGroup.LODs.Count);
                    var hashes = new List<byte>();
                    foreach (var lod in lodGroup.LODs)
                    {
                        LODToBinary(lod, writer, out var hash);
                        hashes.AddRange(hash);
                    }

                    Hash = ContentHelper.ComputeHash([.. hashes]);
                    data = (writer.BaseStream as MemoryStream).ToArray();
                    Icon = GenerateIcons(lodGroup.LODs[0])[0];
                }

                Debug.Assert(data?.Length > 0);

                using (var writer = new BinaryWriter(File.Open(meshFileName, FileMode.Create, FileAccess.Write)))
                {
                    WriteAssetFileHeader(writer);
                    ImportSettings.ToBinary(writer);
                    writer.Write(data.Length);
                    writer.Write(data);
                }

                Logger.Log(MessageType.Info, $"Saved geometry to {meshFileName}");
                savedFiles.Add(meshFileName);
            }

            FullPath = file;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to save geometry to {file}");
        }

        return savedFiles;
    }

    /// <summary>
    /// Packs the geometry into a byte array which can be used by the engine.
    /// </summary>
    /// <returns>
    /// Returns a byte array that contains
    /// struct{
    ///     u32 lod_count,
    ///     f32 lod_thresholds[lod_count],
    ///     u32 submesh_counts[lod_count],
    ///     u32 size_of_mesh_data,
    ///     struct{
    ///         u8 positions[sizeof(f32) * 3 * vertex_count],     // sizeof(positions) must be a multiple of 4 bytes. Pad if needed.
    ///         u8 elements[elements_size * vertex_count],        // sizeof(elements) must be a multiple of 4 bytes. Pad if needed.
    ///         u8 indices[index_size * index_count],             // sizeof(indices) must be a multiple of 4 bytes. Pad if needed.
    ///     } submesh_data[total_submesh_count]
    /// 
    ///     struct{
    ///         u32 elements_size, u32 vertex_count,
    ///         u32 index_count, u32 elements_type, u32 primitive_topology
    ///      } submesh_info[total_submesh_count]
    /// } geometry;
    /// </returns>
    public override byte[] PackForEngine()
    {
        using var writer = new BinaryWriter(new MemoryStream());

        var lods = GetLODGroup().LODs;

        writer.Write(lods.Count);
        lods.ForEach(lod => writer.Write(lod.LodThreshold));
        lods.ForEach(lod => writer.Write(lod.Meshes.Count));

        var sizeOfSubmeshesPosition = writer.BaseStream.Position;
        writer.Write(0);

        lods.ForEach(lod => lod.Meshes.ForEach(mesh =>
        {
            var alignedPositionBuffer = new byte[MathUtil.AlignSizeUp(mesh.Positions.Length, 4)];
            Array.Copy(mesh.Positions, alignedPositionBuffer, mesh.Positions.Length);
            var alignedElementBuffer = new byte[MathUtil.AlignSizeUp(mesh.Elements.Length, 4)];
            Array.Copy(mesh.Elements, alignedElementBuffer, mesh.Elements.Length);
            var alignedIndexBuffer = new byte[MathUtil.AlignSizeUp(mesh.Indices.Length, 4)];
            Array.Copy(mesh.Indices, alignedIndexBuffer, mesh.Indices.Length);

            writer.Write(alignedPositionBuffer);
            writer.Write(alignedElementBuffer);
            writer.Write(alignedIndexBuffer);
        }));

        var endOfSubmeshes = writer.BaseStream.Position;
        var sizeOfSubmeshes = (int)(endOfSubmeshes - sizeOfSubmeshesPosition - sizeof(int));

        writer.BaseStream.Position = sizeOfSubmeshesPosition;
        writer.Write(sizeOfSubmeshes);
        writer.BaseStream.Position = endOfSubmeshes;

        lods.ForEach(lod => lod.Meshes.ForEach(mesh =>
        {
            writer.Write(mesh.ElementSize);
            writer.Write(mesh.VertexCount);
            writer.Write(mesh.IndexCount);
            writer.Write((int)mesh.ElementsType);
            writer.Write((int)mesh.PrimitiveTopology);
        }));

        writer.Flush();
        var data = (writer.BaseStream as MemoryStream)?.ToArray();
        Debug.Assert(data?.Length > 0);

        // For Testing. Remove later!
        //using (var fs = new FileStream(@"..\..\x64\model.model", FileMode.Create))
        //{
        //    fs.Write(data, 0, data.Length);
        //}
        // For Testing. Remove later!

        return data;
    }

    private static void LODToBinary(MeshLOD lod, BinaryWriter writer, out byte[] hash)
    {
        writer.Write(lod.Name);
        writer.Write(lod.LodThreshold);
        writer.Write(lod.Meshes.Count);

        var meshDataBegin = writer.BaseStream.Position;

        foreach (var mesh in lod.Meshes)
        {
            writer.Write(mesh.Name);
            writer.Write(mesh.ElementSize);
            writer.Write((int)mesh.ElementsType);
            writer.Write((int)mesh.PrimitiveTopology);
            writer.Write(mesh.VertexCount);
            writer.Write(mesh.IndexSize);
            writer.Write(mesh.IndexCount);
            writer.Write(mesh.Positions);
            writer.Write(mesh.Elements);
            writer.Write(mesh.Indices);
        }

        var meshDataSize = writer.BaseStream.Position - meshDataBegin;
        Debug.Assert(meshDataSize > 0);
        var buffer = (writer.BaseStream as MemoryStream).ToArray();
        hash = ContentHelper.ComputeHash(buffer, (int)meshDataBegin, (int)meshDataSize);
    }

    private static MeshLOD BinaryToLOD(BinaryReader reader)
    {
        var lod = new MeshLOD
        {
            Name = reader.ReadString(),
            LodThreshold = reader.ReadSingle()
        };
        var meshCount = reader.ReadInt32();

        for (int i = 0; i < meshCount; ++i)
        {
            var mesh = new Mesh()
            {
                Name = reader.ReadString(),
                ElementSize = reader.ReadInt32(),
                ElementsType = (ElementsType)reader.ReadInt32(),
                PrimitiveTopology = (PrimitiveTopology)reader.ReadInt32(),
                VertexCount = reader.ReadInt32(),
                IndexSize = reader.ReadInt32(),
                IndexCount = reader.ReadInt32()
            };

            mesh.Positions = reader.ReadBytes(Mesh.PositionSize * mesh.VertexCount);
            mesh.Elements = reader.ReadBytes(mesh.ElementSize * mesh.VertexCount);
            mesh.Indices = reader.ReadBytes(mesh.IndexSize * mesh.IndexCount);

            lod.Meshes.Add(mesh);
        }

        return lod;
    }

    internal static List<byte[]> GenerateIcons(MeshLOD lod, bool createIconPerSubmesh = false)
    {
        var ready = new AutoResetEvent(false);
        var width = ContentInfo.IconWidth * 4;
        var height = width;
        var iconList = new List<byte[]>();
        var color = (Color)Application.Current.FindResource("Editor.Window.GrayColor1");

        Thread thread = new(() =>
        {
            // Set up Dispatcher exception handling
            Dispatcher.CurrentDispatcher.UnhandledException += (s, e) =>
                Debug.WriteLine($"Dispatcher Exception: {e.Exception.Message}");

            // Create our context, and install it:
            SynchronizationContext.SetSynchronizationContext(
                new DispatcherSynchronizationContext(
                    Dispatcher.CurrentDispatcher));

            // Perform all UI operations on the Dispatcher
            Dispatcher.CurrentDispatcher.Invoke(() =>
            {
                try
                {
                    // NOTE: it's not good practice to use a WPF control (view) in the ViewModel.
                    //       But we need to make an exception for this case, for as long as we don't
                    //       have a graphics renderer that we can use for screenshots.
                    GeometryView view = new()
                    {
                        Background = new SolidColorBrush(color),
                        DataContext = new MeshRenderer(lod, null),
                        Width = width,
                        Height = height,
                    };

                    for (int i = createIconPerSubmesh ? 0 : -1; i < lod.Meshes.Count; ++i)
                    {
                        view.SetGeometry(i);
                        view.Measure(new Size(width, height));
                        view.Arrange(new Rect(0, 0, width, height));
                        view.UpdateLayout();
                        // Create an image that's 4x larger, so it's softened when it's scaled down.
                        var bmp = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Default);
                        bmp.Render(view);
                        iconList.Add(BitmapHelper.CreateThumbnail(bmp, ContentInfo.IconWidth, ContentInfo.IconWidth));
                        if (i == -1) break;
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Render Exception: {ex.Message}\nStackTrace: {ex.StackTrace}");
                    throw;
                }
                finally
                {
                    ready.Set();
                    Dispatcher.CurrentDispatcher.BeginInvokeShutdown(DispatcherPriority.Background);
                }
            });

            Dispatcher.Run();
        });

        thread.SetApartmentState(ApartmentState.STA);
        thread.IsBackground = true;
        thread.Start();
        ready.WaitOne();
        thread.Join();

        return iconList;
    }

    private static void GenerateAndSetIcons(List<MeshInfo> meshes, MeshLOD lod)
    {
        var iconList = GenerateIcons(lod, true);
        var index = 0;
        meshes.ForEach(mesh => mesh.Icon = iconList[index++]);
    }

    public override GeometryMetadata GetMetadata()
    {
        var lodGroup = GetLODGroup();
        if (lodGroup == null) return new() { LODs = [] };
        var lods = new List<LodInfo>();

        foreach (var lod in lodGroup.LODs)
        {
            LodInfo lodInfo = new() { Name = lod.Name, Threshold = lod.LodThreshold, Meshes = [] };
            lods.Add(lodInfo);

            foreach (var mesh in lod.Meshes)
            {
                MeshInfo meshInfo = new()
                {
                    Name = mesh.Name,
                    Icon = Icon,
                    IndexCount = mesh.IndexCount,
                    TriangleCount = mesh.IndexCount / 3,
                    VertexCount = mesh.VertexCount
                };

                lodInfo.Meshes.Add(meshInfo);
            }
            _ = Task.Run(() => GenerateAndSetIcons(lodInfo.Meshes, lod));
        }

        return new() { Name = lodGroup.Name, LODs = lods };
    }

    public Geometry() : base(AssetType.Mesh) { }

    public Geometry(IAssetImportSettings importSettings) : this()
    {
        Debug.Assert(importSettings is GeometryImportSettings);
        ImportSettings = (GeometryImportSettings)importSettings;
    }

    public Geometry(AssetInfo assetInfo) : this()
    {
        Debug.Assert(assetInfo != null && assetInfo.Guid != Guid.Empty);
        Debug.Assert(File.Exists(assetInfo.FullPath) && assetInfo.Type == Type);
        Load(assetInfo.FullPath);
    }
}
