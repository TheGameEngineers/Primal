// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using PrimalEditor.DllWrappers;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;

namespace PrimalEditor.Components;

class MeshWithMaterial : ViewModelBase
{
    public MeshInfo MeshInfo { get; }

    private AppliedMaterial _material;
    public AppliedMaterial Material
    {
        get => _material;
        set
        {
            if (_material != value && value != null)
            {
                Debug.Assert(ID.IsValid(value.UploadedAsset?.ContentId ?? ID.INVALID_ID));
                _material?.UnloadFromEngine();
                _material = value;
                OnPropertyChanged(nameof(Material));
            }
        }
    }

    public MeshWithMaterial(MeshInfo mesh, AppliedMaterial material)
    {
        Debug.Assert(mesh != null && material != null);
        MeshInfo = mesh;
        Material = material;
    }
}

class LodWithMaterials(string name, float threshold, List<MeshWithMaterial> meshes)
{
    public string Name { get; } = name;
    public float Threshold { get; } = threshold;
    public List<MeshWithMaterial> Meshes { get; } = meshes;
}

class GeometryWithMaterials(string name, byte[] icon, List<LodWithMaterials> lods)
{
    public string Name { get; } = name;
    public byte[] Icon { get; } = icon;
    public List<LodWithMaterials> LODs { get; } = lods;
}

[DataContract]
class Geometry : Component
{
    private UploadedAsset _geometry;
    private IdType _componentId = ID.INVALID_ID;

    [DataMember(Name = "Geometry")]
    public Guid GeometryGuid { get; private set; }
    [DataMember(Name = "Materials")]
    private List<AppliedMaterial> _materials = [];

    private GeometryWithMaterials _geometryWithMaterials;
    public GeometryWithMaterials GeometryWithMaterials
    {
        get => _geometryWithMaterials;
        private set
        {
            if (_geometryWithMaterials != value)
            {
                _geometryWithMaterials = value;
                OnPropertyChanged(nameof(GeometryWithMaterials));
            }
        }
    }

    public List<AppliedMaterial> MaterialsList => GeometryWithMaterials?.LODs?.SelectMany(x => x.Meshes, (x, m) => m.Material)?.ToList() ?? [];

    public IdType ContentId => _geometry?.ContentId ?? ID.INVALID_ID;

    private static AppliedMaterial CreateAndUploadAppliedMaterial(AssetInfo material)
    {
        var appliedMtl = new AppliedMaterial(material);
        appliedMtl.UploadToEngine();
        Debug.Assert(appliedMtl.UploadedAsset != null);
        return appliedMtl;
    }

    private void Load(AssetInfo geometry)
    {
        Debug.Assert(_geometry == null && GeometryWithMaterials == null);

        _geometry = UploadedAsset.AddToScene(geometry);
        Debug.Assert(_geometry != null && ID.IsValid(_geometry.ContentId));

        if (_geometry?.Metadata is GeometryMetadata metadata && ID.IsValid(_geometry.ContentId))
        {
            var index = 0;
            GeometryWithMaterials = new(metadata.Name, _geometry.AssetInfo.Icon, [.. metadata.LODs
                .Select(lod => new LodWithMaterials(lod.Name, lod.Threshold, [.. lod.Meshes
                .Select(mesh => new MeshWithMaterial(mesh,
                index < _materials.Count ? _materials[index++] : CreateAndUploadAppliedMaterial(Material.Default)))]))]);
        }

        Debug.Assert(GeometryWithMaterials != null && GeometryWithMaterials.LODs.Count > 0);
    }

    public override void Load()
    {
        Debug.Assert(_geometry == null && GeometryWithMaterials == null);
        Debug.Assert(GeometryGuid != Guid.Empty);
        var assetInfo = AssetRegistry.GetAssetInfo(GeometryGuid) ?? DefaultAssets.DefaultGeometry; // TODO: warn the user that the geometry is missing.
        Debug.Assert(assetInfo?.Type == AssetType.Mesh);
        Debug.Assert(assetInfo?.Guid == GeometryGuid);

        _materials.ForEach(x => x.UploadToEngine());
        Debug.Assert(_materials.All(x => x.UploadedAsset != null && ID.IsValid(x.UploadedAsset.ContentId)));

        Load(assetInfo);
    }

    public override void Unload()
    {
        Debug.Assert(_geometry != null && ID.IsValid(_geometry.ContentId));
        if (_geometry == null || !ID.IsValid(_geometry.ContentId))
        {
            return;
        }

        _materials = MaterialsList;
        _materials.ForEach(x => x.UnloadFromEngine());
        GeometryWithMaterials = null;
        GeometryGuid = _geometry.AssetInfo.Guid;
        UploadedAsset.RemoveFromScene(_geometry);
        _geometry = null;
        _componentId = ID.INVALID_ID;
    }

    private bool UpdateComponent(List<AppliedMaterial> materials, Guid guid)
    {
        var oldGeometryWithMaterials = GeometryWithMaterials;
        var oldMaterials = MaterialsList;
        var oldGeometry = _geometry;
        var oldGeometryId = _geometry?.ContentId ?? ID.INVALID_ID;
        var oldComponentId = _componentId;

        GeometryWithMaterials = null;
        GeometryGuid = guid;
        _materials = materials ?? [];
        _geometry = null;
        _componentId = ID.INVALID_ID;
        Load();

        if (ID.IsValid(_geometry?.ContentId ?? ID.INVALID_ID))
        {
            EngineAPI.EntityAPI.UpdateComponent(Owner, ComponentType.Geometry);

            oldMaterials.ForEach(x => x.UnloadFromEngine());
            UploadedAsset.RemoveFromScene(oldGeometry);
            return true;
        }

        GeometryWithMaterials = oldGeometryWithMaterials;
        GeometryGuid = oldGeometry?.AssetInfo.Guid ?? Guid.Empty;
        _geometry = oldGeometry;
        _materials = oldMaterials;
        _componentId = oldComponentId;

        return false;
    }

    public void SetGeometry(Guid guid)
    {
        if (_geometry?.AssetInfo.Guid != guid)
        {
            UpdateComponent(null, guid);
        }
    }

    public IdType GetComponentId()
    {
        if (!ID.IsValid(_componentId) && ID.IsValid(Owner.EntityId))
        {
            _componentId = EngineAPI.EntityAPI.GetComponentId(Owner.EntityId, ComponentType.Geometry);
            Debug.Assert(ID.IsValid(_componentId));
        }

        return _componentId;
    }

    public override IMSComponent GetMultiselectionComponent(MSEntity msEntity) => new MSGeometry(msEntity);

    public override void WriteToBinary(BinaryWriter bw) => throw new NotImplementedException();

    [OnSerializing]
    private void OnSerializing(StreamingContext context)
    {
        Debug.Assert(_geometry != null && _geometry.AssetInfo.Guid != Guid.Empty);
        GeometryGuid = _geometry.AssetInfo.Guid;
        _materials = MaterialsList;
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        _componentId = ID.INVALID_ID;
    }

    public Geometry(GameEntity owner, AssetInfo geometry) : base(owner)
    {
        Debug.Assert(geometry?.Type == AssetType.Mesh);
        GeometryGuid = geometry.Guid;
    }
}

sealed class MSGeometry : MSComponent<Geometry>
{
    private GeometryWithMaterials _geometryWithMaterials;
    public GeometryWithMaterials GeometryWithMaterials
    {
        get => _geometryWithMaterials;
        private set
        {
            if (_geometryWithMaterials != value)
            {
                _geometryWithMaterials = value;
                OnPropertyChanged(nameof(GeometryWithMaterials));
            }
        }
    }

    public Guid GeometryGuid => _geometryWithMaterials != null ? SelectedComponents.First().GeometryGuid : Guid.Empty;

    public void SetGeometry(Guid guid)
    {
        var scene = SelectedComponents[0].Owner.ParentScene;
        var enableList = scene.DisableAndUpdate(MSEntity.CurrentSelection.SelectedEntities);
        SelectedComponents.ForEach(x => x.SetGeometry(guid));
        scene.EnableAndUpdate(enableList);
        Refresh();
    }

    protected override bool UpdateComponents(string propertyName) => false;

    protected override bool UpdateMSComponent()
    {
        var contentId = MSEntity.GetMixedValue(SelectedComponents, new Func<Geometry, IdType>(x => x.ContentId));
        GeometryWithMaterials = contentId.HasValue ? SelectedComponents.First().GeometryWithMaterials : null;

        return true;
    }

    public MSGeometry(MSEntity msEntity) : base(msEntity)
    {
        Refresh();
    }
}