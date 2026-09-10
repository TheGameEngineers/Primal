// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
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

    public Point3DCollection Positions { get; } = [];
    public Vector3DCollection Normals { get; } = [];
    public PointCollection UVs { get; } = [];
    public Int32Collection Indices { get; } = [];
    public MeshInfo MeshInfo { get; init; }
}

// NOTE: the purpose of this class is to enable viewing 3D geometry in WPF while
//       we don't have a graphics renderer in the game engine. When we have a
//       renderer, this class and the WPF viewer will become obsolete.
class MeshRenderer : ViewModelBase
{
    public ObservableCollection<MeshRendererVertexData> Meshes { get; } = [];

    public Vector3D CameraDirection
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(CameraDirection));
            }
        }
    } = new(0, 0, -10);

    public Point3D CameraPosition
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                CameraDirection = new Vector3D(-value.X, -value.Y, -value.Z);
                OnPropertyChanged(nameof(OffsetCameraPosition));
                OnPropertyChanged(nameof(CameraPosition));
            }
        }
    } = new(0, 0, 10);

    public Point3D CameraTarget
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(OffsetCameraPosition));
                OnPropertyChanged(nameof(CameraTarget));
            }
        }
    } = new(0, 0, 0);

    public Point3D OffsetCameraPosition =>
        new(CameraPosition.X + CameraTarget.X, CameraPosition.Y + CameraTarget.Y, CameraPosition.Z + CameraTarget.Z);

    public Color KeyLight
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(KeyLight));
            }
        }
    } = (Color)ColorConverter.ConvertFromString("#ffaeaeae");

    public Color SkyLight
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(SkyLight));
            }
        }
    } = (Color)ColorConverter.ConvertFromString("#ff111b30");

    public Color GroundLight
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(GroundLight));
            }
        }
    } = (Color)ColorConverter.ConvertFromString("#ff3f2f1e");

    public Color AmbientLight
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(AmbientLight));
            }
        }
    } = (Color)ColorConverter.ConvertFromString("#ff3b3b3b");

    public int IndexCount
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(IndexCount));
            }
        }
    }

    public int VertexCount
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(VertexCount));
            }
        }
    }

    public MeshRenderer(MeshLOD lod, LodInfo lodInfo, MeshRenderer old)
    {
        Debug.Assert(lod?.Meshes.Count > 0);
        // In order to set up camera position and target properly, we need to figure out how big
        // this object is that we're rendering. Hence, we need to know its bounding box.
        double minX, minY, minZ; minX = minY = minZ = double.MaxValue;
        double maxX, maxY, maxZ; maxX = maxY = maxZ = double.MinValue;
        Vector3D avgNormal = new();
        // This is to unpack the packed normals:
        var intervals = 2.0f / ((1 << 16) - 1);

        lodInfo ??= new() { Meshes = [.. lod.Meshes.Select(m => new MeshInfo() { Name = m.Name })] };
        var lodData = lod.Meshes.Zip(lodInfo.Meshes, (Mesh, Info) => (Mesh, Info));
        IndexCount = lodData.Sum(item => item.Mesh.IndexCount);
        VertexCount = lodData.Sum(item => item.Mesh.VertexCount);

        foreach (var item in lodData)
        {
            var mesh = item.Mesh;
            var vertexData = new MeshRendererVertexData() { MeshInfo = item.Info };
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
            if (avgNormal.Length > 0.1)
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
    private readonly GameEntity _entity;
    public Components.AmbientLight AmbientLight { get; }

    public event EventHandler<IdType> GeometryChanged;

    public ulong LightSetKey { get; private set; }

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

    public GeometryMetadata GeometryMetadata
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(GeometryMetadata));
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
                MeshRenderer = new MeshRenderer(lods[value], GeometryMetadata.LODs[LODIndex], MeshRenderer);
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

    private void SetGeometryComponent(AssetInfo info)
    {
        GeometryChanged?.Invoke(this, ID.INVALID_ID);
        if (_entity.GetComponent<Components.Geometry>() is Components.Geometry geometry)
        {
            geometry.SetGeometry(info.Guid);
        }
        else
        {
            geometry = new Components.Geometry(_entity, info);
            _entity.AddComponent(geometry);
        }

        List<AppliedMaterial> materials = [];
        if (MSEntity.CurrentSelection?.SelectedEntities.FirstOrDefault() is GameEntity entity &&
            entity.GetComponent<Components.Geometry>() is Components.Geometry selectedGeometry &&
            selectedGeometry.GeometryGuid == info.Guid)
        {
            materials = [.. selectedGeometry.MaterialsList.Select(mtl => mtl.Clone())];
        }
        else if (Project.Current.ActiveScene.GameEntities
            .Select(e => e.GetComponent<Components.Geometry>())
            .FirstOrDefault(g => g?.GeometryGuid == info.Guid) is Components.Geometry someGeometry)
        {
            materials = [.. someGeometry.MaterialsList.Select(mtl => mtl.Clone())];
        }

        if (materials.Count > 0)
        {
            geometry.SetMaterials(materials);
        }

        var componentId = _entity.GetComponent<Components.Geometry>().GetComponentId();
        LightSetKey = LightSet.AddLightSet(info.Guid.ToString(), false);
        AmbientLight.LightSetKey = info.Guid.ToString();
        GeometryChanged?.Invoke(this, componentId);
    }

    public bool CheckAssetGuid(Guid guid) => _assetGuid == guid;

    public void SetAsset(Asset asset)
    {
        Debug.Assert(asset is Content.Geometry);
        if (asset is Content.Geometry geometry)
        {
            _assetGuid = asset.Guid;
            Geometry = geometry;
            GeometryMetadata = Project.Current.ActiveScene.GameEntities
                .Select(e => e.GetComponent<Components.Geometry>())
                .FirstOrDefault(g => g?.GeometryGuid == _assetGuid)?.Metadata ?? geometry.GetMetadata();
            var numLods = geometry.GetLODGroup().LODs.Count;
            if (LODIndex >= numLods)
            {
                LODIndex = numLods - 1;
            }
            else
            {
                MeshRenderer = new MeshRenderer(Geometry.GetLODGroup().LODs[LODIndex], GeometryMetadata.LODs[LODIndex], MeshRenderer);
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
            SetGeometryComponent(info);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Debug.WriteLine($"Failed to set geometry for use in geometry editor. File: {info.FullPath}");
        }
    }

    public void Unload()
    {
        GeometryChanged?.Invoke(this, ID.INVALID_ID);
        _entity.IsActive = false;
        AmbientLight.IsActive = false;
    }

    public GeometryEditor()
    {
        _entity = new(Project.Current.ActiveScene) { IsActive = true, IsEnabled = true };
        LightSet.AddLightSet(nameof(GeometryEditor), false);
        AmbientLight = new(Project.Current.ActiveScene, nameof(GeometryEditor)) { IsActive = true, IsEnabled = true };
    }
}
