// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Media3D;

namespace PrimalEditor.Editors;

// NOTE: the purpose of this class is to enable viewing 3D geometry in WPF while
//       we don't have a graphics renderer in the game engine. When we have a
//       renderer, this class and the WPF viewer will become obsolete.
class MeshRendererVertexData : ViewModelBase
{
    public bool IsHighlighted
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IsHighlighted));
                OnPropertyChanged(nameof(Diffuse));
            }
        }
    }

    public bool IsIsolated
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IsIsolated));
            }
        }
    }

    public Brush Specular
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Specular));
            }
        }
    } = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#ff111111"));

    public Brush Diffuse
    {
        get => IsHighlighted ? Brushes.Orange : field;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Diffuse));
            }
        }
    } = Brushes.White;

    public string Name { get; set; }
    public Point3DCollection Positions { get; } = [];
    public Vector3DCollection Normals { get; } = [];
    public PointCollection UVs { get; } = [];
    public Int32Collection Indices { get; } = [];
}

// NOTE: the purpose of this class is to enable viewing 3D geometry in WPF while
//       we don't have a graphics renderer in the game engine. When we have a
//       renderer, this class and the WPF viewer will become obsolete.
class MeshRenderer : ViewModelBase
{
    public ObservableCollection<MeshRendererVertexData> Meshes { get; } = [];

    private Vector3D _cameraDirection = new(0, 0, -10);
    public Vector3D CameraDirection
    {
        get => _cameraDirection;
        set
        {
            if (_cameraDirection != value)
            {
                _cameraDirection = value;
                OnPropertyChanged(nameof(CameraDirection));
            }
        }
    }

    private Point3D _cameraPosition = new(0, 0, 10);
    public Point3D CameraPosition
    {
        get => _cameraPosition;
        set
        {
            if (_cameraPosition != value)
            {
                _cameraPosition = value;
                CameraDirection = new Vector3D(-value.X, -value.Y, -value.Z);
                OnPropertyChanged(nameof(OffsetCameraPosition));
                OnPropertyChanged(nameof(CameraPosition));
            }
        }
    }

    private Point3D _cameraTarget = new(0, 0, 0);
    public Point3D CameraTarget
    {
        get => _cameraTarget;
        set
        {
            if (_cameraTarget != value)
            {
                _cameraTarget = value;
                OnPropertyChanged(nameof(OffsetCameraPosition));
                OnPropertyChanged(nameof(CameraTarget));
            }
        }
    }

    public Point3D OffsetCameraPosition =>
        new(CameraPosition.X + CameraTarget.X, CameraPosition.Y + CameraTarget.Y, CameraPosition.Z + CameraTarget.Z);

    private Color _keyLight = (Color)ColorConverter.ConvertFromString("#ffaeaeae");
    public Color KeyLight
    {
        get => _keyLight;
        set
        {
            if (_keyLight != value)
            {
                _keyLight = value;
                OnPropertyChanged(nameof(KeyLight));
            }
        }
    }

    private Color _skyLight = (Color)ColorConverter.ConvertFromString("#ff111b30");
    public Color SkyLight
    {
        get => _skyLight;
        set
        {
            if (_skyLight != value)
            {
                _skyLight = value;
                OnPropertyChanged(nameof(SkyLight));
            }
        }
    }

    private Color _groundLight = (Color)ColorConverter.ConvertFromString("#ff3f2f1e");
    public Color GroundLight
    {
        get => _groundLight;
        set
        {
            if (_groundLight != value)
            {
                _groundLight = value;
                OnPropertyChanged(nameof(GroundLight));
            }
        }
    }

    private Color _ambientLight = (Color)ColorConverter.ConvertFromString("#ff3b3b3b");
    public Color AmbientLight
    {
        get => _ambientLight;
        set
        {
            if (_ambientLight != value)
            {
                _ambientLight = value;
                OnPropertyChanged(nameof(AmbientLight));
            }
        }
    }

    public MeshRenderer(MeshLOD lod, MeshRenderer old)
    {
        Debug.Assert(lod?.Meshes.Any() == true);
        // In order to set up camera position and target properly, we need to figure out how big
        // this object is that we're rendering. Hence, we need to know its bounding box.
        double minX, minY, minZ; minX = minY = minZ = double.MaxValue;
        double maxX, maxY, maxZ; maxX = maxY = maxZ = double.MinValue;
        Vector3D avgNormal = new();
        // This is to unpack the packed normals:
        var intervals = 2.0f / ((1 << 16) - 1);

        foreach (var mesh in lod.Meshes)
        {
            var vertexData = new MeshRendererVertexData() { Name = mesh.Name };
            // Unpack all vertices
            using (var reader = new BinaryReader(new MemoryStream(mesh.Positions)))
                for (int i = 0; i < mesh.VertexCount; ++i)
                {
                    // Read positions
                    var posX = reader.ReadSingle();
                    var posY = reader.ReadSingle();
                    var posZ = reader.ReadSingle();
                    vertexData.Positions.Add(new Point3D(posX, posY, posZ));

                    // Adjust the bounding box:
                    minX = Math.Min(minX, posX); maxX = Math.Max(maxX, posX);
                    minY = Math.Min(minY, posY); maxY = Math.Max(maxY, posY);
                    minZ = Math.Min(minZ, posZ); maxZ = Math.Max(maxZ, posZ);
                }

            if (mesh.ElementsType.HasFlag(ElementsType.StaticNormal))
            {
                var tSpaceOffset = 0;
                if (mesh.ElementsType.HasFlag(ElementsType.Skeletal)) tSpaceOffset = sizeof(short) * 4; // skip joint indices.
                // Read tangent space
                using (var reader = new BinaryReader(new MemoryStream(mesh.Elements)))
                    for (int i = 0; i < mesh.VertexCount; ++i)
                    {
                        var signs = (reader.ReadUInt32() >> 24) & 0x000000ff;
                        reader.BaseStream.Position += tSpaceOffset;
                        // Read normals
                        var nrmX = reader.ReadUInt16() * intervals - 1.0f;
                        var nrmY = reader.ReadUInt16() * intervals - 1.0f;
                        var nrmZ = Math.Sqrt(Math.Clamp(1f - (nrmX * nrmX + nrmY * nrmY), 0f, 1f)) * (((signs & 0x4) >> 1) - 1f);
                        var normal = new Vector3D(nrmX, nrmY, nrmZ);
                        normal.Normalize();
                        vertexData.Normals.Add(normal);
                        avgNormal += normal;

                        // Read UVs
                        if (mesh.ElementsType.HasFlag(ElementsType.StaticNormalTexture))
                        {
                            reader.BaseStream.Position += sizeof(short) * 2; // skip tangents.
                            var u = reader.ReadSingle();
                            var v = reader.ReadSingle();
                            vertexData.UVs.Add(new Point(u, v));
                        }

                        if (mesh.ElementsType.HasFlag(ElementsType.SkeletalColor))
                        {
                            reader.BaseStream.Position += 4; // skip colors.
                        }
                    }
            }

            using (var reader = new BinaryReader(new MemoryStream(mesh.Indices)))
                if (mesh.IndexSize == sizeof(short))
                    for (int i = 0; i < mesh.IndexCount; ++i) vertexData.Indices.Add(reader.ReadUInt16());
                else
                    for (int i = 0; i < mesh.IndexCount; ++i) vertexData.Indices.Add(reader.ReadInt32());

            vertexData.Positions.Freeze();
            vertexData.Normals.Freeze();
            vertexData.UVs.Freeze();
            vertexData.Indices.Freeze();
            Meshes.Add(vertexData);
        }

        // set camera target and position
        if (old != null)
        {
            CameraTarget = old.CameraTarget;
            CameraPosition = old.CameraPosition;

            // NOTE: this is only for primitive meshes with multiple LODs,
            //       because they're displayed with textures:
            foreach (var mesh in old.Meshes)
            {
                mesh.IsHighlighted = false;
            }

            foreach (var mesh in Meshes)
            {
                mesh.Diffuse = old.Meshes.First().Diffuse;
            }
        }
        else
        {
            // compute bounding box dimensions
            var width = maxX - minX;
            var height = maxY - minY;
            var depth = maxZ - minZ;
            var radius = new Vector3D(height, width, depth).Length * 1.2;
            if (avgNormal.Length > 0.8)
            {
                avgNormal.Normalize();
                avgNormal *= radius;
                CameraPosition = new Point3D(avgNormal.X, avgNormal.Y, avgNormal.Z);
            }
            else
            {
                CameraPosition = new Point3D(width, height * 0.5, radius);
            }

            CameraTarget = new Point3D(minX + width * 0.5, minY + height * 0.5, minZ + depth * 0.5);
        }
    }
}

class GeometryEditor : ViewModelBase, IAssetEditor
{
    public AssetEditorState State
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(State));
            }
        }
    }

    private Guid _assetGuid;

    Asset IAssetEditor.Asset => Geometry;

    public Content.Geometry Geometry
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Geometry));
            }
        }
    }

    public MeshRenderer MeshRenderer
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(MeshRenderer));
                var lods = Geometry.GetLODGroup().LODs;
                MaxLODIndex = (lods.Count > 0) ? lods.Count - 1 : 0;
                OnPropertyChanged(nameof(MaxLODIndex));
                if (lods.Count > 1)
                {
                    MeshRenderer.PropertyChanged += (s, e) =>
                    {
                        if (e.PropertyName == nameof(MeshRenderer.OffsetCameraPosition) && AutoLOD) ComputeLOD(lods);
                    };

                    ComputeLOD(lods);
                }
            }
        }
    }

    public bool AutoLOD
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(AutoLOD));
            }
        }
    } = true;

    public int MaxLODIndex { get; private set; }

    public int LODIndex
    {
        get;
        set
        {
            var lods = Geometry.GetLODGroup().LODs;
            value = Math.Clamp(value, 0, lods.Count);
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(LODIndex));
                MeshRenderer = new MeshRenderer(lods[value], MeshRenderer);
            }
        }
    }

    private void ComputeLOD(IList<MeshLOD> lods)
    {
        if (!AutoLOD) return;

        var p = MeshRenderer.OffsetCameraPosition;
        var distance = new Vector3D(p.X, p.Y, p.Z).Length;
        for (int i = MaxLODIndex; i >= 0; --i)
        {
            if (lods[i].LodThreshold < distance)
            {
                LODIndex = i;
                break;
            }
        }
    }

    public bool CheckAssetGuid(Guid guid) => _assetGuid == guid;

    public void SetAsset(Asset asset)
    {
        Debug.Assert(asset is Content.Geometry);
        if (asset is Content.Geometry geometry)
        {
            _assetGuid = asset.Guid;
            Geometry = geometry;
            var numLods = geometry.GetLODGroup().LODs.Count;
            if (LODIndex >= numLods)
            {
                LODIndex = numLods - 1;
            }
            else
            {
                MeshRenderer = new MeshRenderer(Geometry.GetLODGroup().LODs[LODIndex], MeshRenderer);
            }
        }
    }

    public async Task SetAsset(AssetInfo info)
    {
        try
        {
            _assetGuid = info.Guid;
            Debug.Assert(info != null && File.Exists(info.FullPath));
            var geometry = new Content.Geometry();
            await Task.Run(() =>
            {
                geometry.Load(info.FullPath);
            });

            SetAsset(geometry);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Debug.WriteLine($"Failed to set geometry for use in geometry editor. File: {info.FullPath}");
        }
    }
}
