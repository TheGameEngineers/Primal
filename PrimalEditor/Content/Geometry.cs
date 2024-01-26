// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace PrimalEditor.Content
{
    enum PrimitiveMeshType
    {
        Plane,
        Cube,
        UvSphere,
        IcoSphere,
        Cylinder,
        Capsule
    }

    enum ElementsType
    {
        Position = 0x00,
        Normals = 0x01,
        TSpace = 0x03,
        Joints = 0x04,
        Colors = 0x08
    }

    enum PrimitiveTopology
    {
        PointList = 1,
        LineList,
        LineStrip,
        TriangleList,
        TriangleStrip,
    }

    class Mesh : ViewModelBase
    {
        public static int PositionSize => sizeof(float) * 3;

        private int _elementSize;
        public int ElementSize
        {
            get => _elementSize;
            set
            {
                if (_elementSize != value)
                {
                    _elementSize = value;
                    OnPropertyChanged(nameof(ElementSize));
                }
            }
        }

        private int _vertexCount;
        public int VertexCount
        {
            get => _vertexCount;
            set
            {
                if (_vertexCount != value)
                {
                    _vertexCount = value;
                    OnPropertyChanged(nameof(VertexCount));
                }
            }
        }

        private int _indexSize;
        public int IndexSize
        {
            get => _indexSize;
            set
            {
                if (_indexSize != value)
                {
                    _indexSize = value;
                    OnPropertyChanged(nameof(IndexSize));
                }
            }
        }

        private int _indexCount;
        public int IndexCount
        {
            get => _indexCount;
            set
            {
                if (_indexCount != value)
                {
                    _indexCount = value;
                    OnPropertyChanged(nameof(IndexCount));
                }
            }
        }

        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
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
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        private float _lodThreshold;
        public float LodThreshold
        {
            get => _lodThreshold;
            set
            {
                if (_lodThreshold != value)
                {
                    _lodThreshold = value;
                    OnPropertyChanged(nameof(LodThreshold));
                }
            }
        }

        public ObservableCollection<Mesh> Meshes { get; } = new ObservableCollection<Mesh>();
    }

    class LODGroup : ViewModelBase
    {
        private string _name;
        public string Name
        {
            get => _name;
            set
            {
                if (_name != value)
                {
                    _name = value;
                    OnPropertyChanged(nameof(Name));
                }
            }
        }

        public ObservableCollection<MeshLOD> LODs { get; } = new ObservableCollection<MeshLOD>();
    }

    class GeometryImportSettings : ViewModelBase, IAssetImportSettings
    {
        private bool _calculateNormals;
        public bool CalculateNormals
        {
            get => _calculateNormals;
            set
            {
                if (_calculateNormals != value)
                {
                    _calculateNormals = value;
                    OnPropertyChanged(nameof(CalculateNormals));
                }
            }
        }

        private bool _calculateTangents;
        public bool CalculateTangents
        {
            get => _calculateTangents;
            set
            {
                if (_calculateTangents != value)
                {
                    _calculateTangents = value;
                    OnPropertyChanged(nameof(CalculateTangents));
                }
            }
        }

        private float _smoothingAngle;
        public float SmoothingAngle
        {
            get => _smoothingAngle;
            set
            {
                if (!_smoothingAngle.IsTheSameAs(value))
                {
                    _smoothingAngle = value;
                    OnPropertyChanged(nameof(SmoothingAngle));
                }
            }
        }

        private bool _reverseHandedness;
        public bool ReverseHandedness
        {
            get => _reverseHandedness;
            set
            {
                if (_reverseHandedness != value)
                {
                    _reverseHandedness = value;
                    OnPropertyChanged(nameof(ReverseHandedness));
                }
            }
        }

        private bool _importEmbeddedTextures;
        public bool ImportEmbeddedTextures
        {
            get => _importEmbeddedTextures;
            set
            {
                if (_importEmbeddedTextures != value)
                {
                    _importEmbeddedTextures = value;
                    OnPropertyChanged(nameof(ImportEmbeddedTextures));
                }
            }
        }

        private bool _importAnimations;
        public bool ImportAnimations
        {
            get => _importAnimations;
            set
            {
                if (_importAnimations != value)
                {
                    _importAnimations = value;
                    OnPropertyChanged(nameof(ImportAnimations));
                }
            }
        }

        public GeometryImportSettings()
        {
            CalculateNormals = false;
            CalculateTangents = false;
            SmoothingAngle = 178f;
            ReverseHandedness = false;
            ImportEmbeddedTextures = true;
            ImportAnimations = true;
        }

        public void ToBinary(BinaryWriter writer)
        {
            writer.Write(CalculateNormals);
            writer.Write(CalculateTangents);
            writer.Write(SmoothingAngle);
            writer.Write(ReverseHandedness);
            writer.Write(ImportEmbeddedTextures);
            writer.Write(ImportAnimations);
        }

        public void FromBinary(BinaryReader reader)
        {
            CalculateNormals = reader.ReadBoolean();
            CalculateTangents = reader.ReadBoolean();
            SmoothingAngle = reader.ReadSingle();
            ReverseHandedness = reader.ReadBoolean();
            ImportEmbeddedTextures = reader.ReadBoolean();
            ImportAnimations = reader.ReadBoolean();
        }
    }

    class Geometry : Asset
    {
        private readonly List<LODGroup> _lodGroups = new();
        private readonly object _lock = new();

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
                lods.ForEach(l => lodGroup.LODs.Add(l));

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

                Debug.Assert(data.Length > 0);

                using (var reader = new BinaryReader(new MemoryStream(data)))
                {
                    LODGroup lodGroup = new();
                    lodGroup.Name = reader.ReadString();
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
            Debug.Assert(_lodGroups.Any());
            var savedFiles = new List<string>();
            if (!_lodGroups.Any()) return savedFiles;

            var path = Path.GetDirectoryName(file) + Path.DirectorySeparatorChar;
            var fileName = Path.GetFileNameWithoutExtension(file);

            try
            {
                foreach (var lodGroup in _lodGroups)
                {
                    Debug.Assert(lodGroup.LODs.Any());
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

                        Hash = ContentHelper.ComputeHash(hashes.ToArray());
                        data = (writer.BaseStream as MemoryStream).ToArray();
                        Icon = GenerateIcon(lodGroup.LODs[0]);
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
        /// A byte array that contains
        /// struct{
        ///     u32 lod_count,
        ///     struct {
        ///         f32 lod_threshold,
        ///         u32 submesh_count,
        ///         u32 size_of_submeshes,
        ///         struct {
        ///             u32 element_size, u32 vertex_count,
        ///             u32 index_count, u32 elements_type, u32 primitive_topology
        ///             u8 positions[sizeof(f32) * 3 * vertex_count],     // sizeof(positions) must be a multiple of 4 bytes. Pad if needed.
        ///             u8 elements[sizeof(element_size) * vertex_count], // sizeof(elements) must be a multiple of 4 bytes. Pad if needed.
        ///             u8 indices[index_size * index_count]
        ///         } submeshes[submesh_count]
        ///     } mesh_lods[lod_count]
        /// } geometry;
        /// </returns>
        public override byte[] PackForEngine()
        {
            using var writer = new BinaryWriter(new MemoryStream());

            writer.Write(GetLODGroup().LODs.Count);
            foreach (var lod in GetLODGroup().LODs)
            {
                writer.Write(lod.LodThreshold);
                writer.Write(lod.Meshes.Count);
                var sizeOfSubmeshesPosition = writer.BaseStream.Position;
                writer.Write(0);
                foreach (var mesh in lod.Meshes)
                {
                    writer.Write(mesh.ElementSize);
                    writer.Write(mesh.VertexCount);
                    writer.Write(mesh.IndexCount);
                    writer.Write((int)mesh.ElementsType);
                    writer.Write((int)mesh.PrimitiveTopology);

                    var alignedPositionBuffer = new byte[MathUtil.AlignSizeUp(mesh.Positions.Length, 4)];
                    Array.Copy(mesh.Positions, alignedPositionBuffer, mesh.Positions.Length);
                    var alignedElementBuffer = new byte[MathUtil.AlignSizeUp(mesh.Elements.Length, 4)];
                    Array.Copy(mesh.Elements, alignedElementBuffer, mesh.Elements.Length);

                    writer.Write(alignedPositionBuffer);
                    writer.Write(alignedElementBuffer);
                    writer.Write(mesh.Indices);
                }

                var endOfSubmeshes = writer.BaseStream.Position;
                var sizeOfSubmeshes = (int)(endOfSubmeshes - sizeOfSubmeshesPosition - sizeof(int));

                writer.BaseStream.Position = sizeOfSubmeshesPosition;
                writer.Write(sizeOfSubmeshes);
                writer.BaseStream.Position = endOfSubmeshes;
            }

            writer.Flush();
            var data = (writer.BaseStream as MemoryStream)?.ToArray();
            Debug.Assert(data?.Length > 0);

            // For Testing. Remove later!
            using (var fs = new FileStream(@"..\..\x64\model.model", FileMode.Create))
            {
                fs.Write(data, 0, data.Length);
            }
            // For Testing. Remove later!

            return data;
        }

        private void LODToBinary(MeshLOD lod, BinaryWriter writer, out byte[] hash)
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

        private MeshLOD BinaryToLOD(BinaryReader reader)
        {
            var lod = new MeshLOD();
            lod.Name = reader.ReadString();
            lod.LodThreshold = reader.ReadSingle();
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

        private static byte[] GenerateIcon(MeshLOD lod)
        {
            var width = ContentInfo.IconWidth * 4;
            byte[] icon = null;
            // NOTE: it's not good practice to use a WPF control (view) in the ViewModel.
            //       But we need to make an exception for this case, for as long as we don't
            //       have a graphics renderer that we can use for screenshots.
            Application.Current.Dispatcher.Invoke(() =>
            {
                // Create an image that's 4x larger, so it's softened when it's scaled down.
                var bmp = Editors.GeometryView.RenderToBitmap(new Editors.MeshRenderer(lod, null), width, width);
                icon = BitmapHelper.CreateThumbnail(bmp, ContentInfo.IconWidth, ContentInfo.IconWidth);
            });

            return icon;
        }

        public Geometry() : base(AssetType.Mesh) { }

        public Geometry(IAssetImportSettings importSettings) : this()
        {
            Debug.Assert(importSettings is GeometryImportSettings);
            ImportSettings = (GeometryImportSettings)importSettings;
        }
    }
}
