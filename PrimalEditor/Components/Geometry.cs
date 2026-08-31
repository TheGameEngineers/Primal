// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using PrimalEditor.DllWrappers;
using PrimalEditor.GameProject;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Windows.Input;

namespace PrimalEditor.Components;

class MeshWithMaterial : ViewModelBase
{
    public MeshInfo MeshInfo { get; }

    public AppliedMaterial Material
    {
        get;
        set
        {
            if (field != value && value != null)
            {
                Debug.Assert(ID.IsValid(value.ContentId));
                field?.UnloadFromEngine();
                field = value;
                OnPropertyChanged(nameof(Material));
            }
        }
    }

    public AppliedMaterialProxy MaterialProxy
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(MaterialProxy));
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

    public GeometryWithMaterials GeometryWithMaterials
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(GeometryWithMaterials));
            }
        }
    }

    public List<AppliedMaterial> MaterialsList => GeometryWithMaterials?.LODs?.SelectMany(x => x.Meshes, (x, m) => m.Material)?.ToList() ?? [];

    public IdType ContentId => _geometry?.ContentId ?? ID.INVALID_ID;
    public GeometryMetadata Metadata => _geometry?.Metadata as GeometryMetadata;

    private static AppliedMaterial CreateAndUploadAppliedMaterial(AssetInfo material)
    {
        var appliedMtl = new AppliedMaterial(material);
        appliedMtl.UploadToEngine();
        Debug.Assert(ID.IsValid(appliedMtl.ContentId));
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
        var assetInfo = AssetRegistry.GetAssetInfo(GeometryGuid);
        if (assetInfo == null)
        {
            assetInfo = DefaultAssets.DefaultGeometry;
            Logger.Log(MessageType.Warning, $"Geometry asset with GUID {GeometryGuid} not found for entity {Owner.Name}. Using default geometry.");
        }
        Debug.Assert(assetInfo?.Type == AssetType.Mesh);
        Debug.Assert(assetInfo?.Guid == GeometryGuid);

        _materials.ForEach(x => x.UploadToEngine());
        Debug.Assert(_materials.All(x => ID.IsValid(x.ContentId)));

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

    public void SetMaterials(List<AppliedMaterial> materials)
    {
        Debug.Assert(materials?.Count > 0 && ID.IsValid(ContentId));

        // Increment the ref count of the geometry asset to prevent it from being unloaded.
        var geometry = UploadedAsset.AddToScene(_geometry.AssetInfo);
        UpdateComponent(materials, _geometry.AssetInfo.Guid);
        UploadedAsset.RemoveFromScene(geometry);
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

class InputProxy : ViewModelBase
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

    public AssetInfo Asset
    {
        get;
        set
        {
            if (value != null && field?.Guid != value.Guid)
            {
                field = value;
                OnPropertyChanged(nameof(Asset));
                OnPropertyChanged(nameof(Ignore));
            }
        }
    }

    public bool Ignore => Asset.Guid == Texture.Default.Guid;

    public AppliedMaterialProxy MaterialProxy { get; }

    public InputProxy(string name, AssetInfo asset, AppliedMaterialProxy materialProxy)
    {
        Debug.Assert(name != null && asset != null && materialProxy != null);
        Name = name;
        Asset = asset;
        MaterialProxy = materialProxy;
    }
}

class AppliedMaterialProxy : ViewModelBase
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

    public bool IsMultipleMaterials { get; private set; }

    public static bool IsDirty
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                CommandManager.InvalidateRequerySuggested();
            }
        }
    }

    public static bool IsNameDirty { get; set; }

    private AssetInfo _materialInfo;
    public AssetInfo MaterialInfo
    {
        get => _materialInfo;
        set
        {
            if (value != null && _materialInfo?.Guid != value.Guid && value.Guid != Guid.Empty)
            {
                _materialInfo = value;
                OnPropertyChanged(nameof(MaterialInfo));
                IsMultipleMaterials = false;
                ResetInputs();
            }
        }
    }

    public ObservableCollection<InputProxy> Inputs { get; } = [];

    public List<float?> M { get; } = [0, 0, 0, 0,   // base color
                                      0, 0, 0,      // emissive color
                                      0,            // emissive intensity
                                      0,            // metallic
                                      0];           // roughness

    private void ResetInputs()
    {
        // TODO: assigning a new material loads it once here and once in the applied material. Can we do better?
        var mtlAsset = new Material(_materialInfo);
        Inputs.Clear();
        mtlAsset.GetInputs().ForEach(input => Inputs.Add(new(input.Name, Texture.Default, this)));
    }

    private static float? GetMixedValue(float? dst, float src, bool slap) => (slap && dst.HasValue && !src.IsTheSameAs(dst.Value)) ? null : src;

    private void MaterialSurfaceToArray(MaterialSurface s, bool slap)
    {
        M[0] = GetMixedValue(M[0], s.BaseColor.ScR, slap);
        M[1] = GetMixedValue(M[1], s.BaseColor.ScG, slap);
        M[2] = GetMixedValue(M[2], s.BaseColor.ScB, slap);
        M[3] = GetMixedValue(M[3], s.BaseColor.ScA, slap);

        M[4] = GetMixedValue(M[4], s.EmissiveColor.ScR, slap);
        M[5] = GetMixedValue(M[5], s.EmissiveColor.ScG, slap);
        M[6] = GetMixedValue(M[6], s.EmissiveColor.ScB, slap);

        M[7] = GetMixedValue(M[7], s.EmissiveIntensity, slap);
        M[8] = GetMixedValue(M[8], s.Metallic, slap);
        M[9] = GetMixedValue(M[9], s.Roughness, slap);
    }

    public void SetMaterialSurfaceArray(List<float?> m)
    {
        Debug.Assert(m?.Count == M.Count);
        M.Clear();
        M.AddRange(m);
        OnPropertyChanged(nameof(M));
    }

    public void SetMaterialSurface(MaterialSurface s)
    {
        if (M[0].HasValue) s.BaseColor.ScR = M[0].Value;
        if (M[1].HasValue) s.BaseColor.ScG = M[1].Value;
        if (M[2].HasValue) s.BaseColor.ScB = M[2].Value;
        if (M[3].HasValue) s.BaseColor.ScA = M[3].Value;

        if (M[4].HasValue) s.EmissiveColor.ScR = M[4].Value;
        if (M[5].HasValue) s.EmissiveColor.ScG = M[5].Value;
        if (M[6].HasValue) s.EmissiveColor.ScB = M[6].Value;

        if (M[7].HasValue) s.EmissiveIntensity = M[7].Value;
        if (M[8].HasValue) s.Metallic = M[8].Value;
        if (M[9].HasValue) s.Roughness = M[9].Value;
    }

    public void Slap(AppliedMaterial mtl)
    {
        Debug.Assert(mtl != null);

        if (mtl.MaterialInfo.Guid != MaterialInfo.Guid)
        {
            IsMultipleMaterials = true;
            return;
        }

        if (Name != mtl.Name)
        {
            Name = null;
        }

        Debug.Assert(Inputs.Count == mtl.Inputs.Count);

        for (int i = 0; i < Inputs.Count; ++i)
        {
            var input = mtl.Inputs[i];
            if (Inputs[i].Asset.Guid != input.Asset.Guid)
            {
                Debug.Assert(Inputs[i].Name == input.Name);
                Inputs[i].Asset = null;
            }
        }

        MaterialSurfaceToArray(mtl.MaterialSurface, true);
    }

    public AppliedMaterialProxy(AppliedMaterial mtl)
    {
        Debug.Assert(mtl?.MaterialInfo != null && mtl.MaterialInfo.Guid != Guid.Empty);
        // NOTE: use the backing field instead of MaterialInfo property here since it will reset inputs.
        _materialInfo = mtl.MaterialInfo;
        Name = mtl.Name;
        mtl.Inputs.ToList().ForEach(input => Inputs.Add(new(input.Name, input.Asset, this)));
        MaterialSurfaceToArray(mtl.MaterialSurface, false);
    }
}

sealed class MSGeometry : MSComponent<Geometry>
{
    public ICommand ResetCommand =>
        new RelayCommand<object>(x => UpdateMSComponent(), x => AppliedMaterialProxy.IsDirty || AppliedMaterialProxy.IsNameDirty);

    public ICommand ApplyChangesCommand =>
        new RelayCommand<object>(x => OnApplyChangesCommand(), x => AppliedMaterialProxy.IsDirty || AppliedMaterialProxy.IsNameDirty);

    public GeometryWithMaterials GeometryWithMaterials
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(GeometryWithMaterials));
            }
        }
    }

    public Guid GeometryGuid => GeometryWithMaterials != null ? SelectedComponents.First().GeometryGuid : Guid.Empty;

    public List<AppliedMaterialProxy> MaterialProxies { get; private set; } = [];

    private static void RenameMaterials(List<Geometry> geometries, List<AppliedMaterialProxy> materialProxies)
    {
        Debug.Assert(geometries?.Count > 0 && materialProxies?.Count > 0);

        foreach (var geometry in geometries)
        {
            var materials = geometry.MaterialsList;

            for (int i = 0; i < materials.Count; ++i)
            {
                var proxy = materialProxies[i];
                if (proxy.Name != null)
                {
                    materials[i].Name = proxy.Name;
                }
            }
        }
    }

    private static void ApplyMaterialsToComponents(List<Geometry> geometries, List<AppliedMaterialProxy> materialProxies)
    {
        Debug.Assert(geometries?.Count > 0 && materialProxies?.Count > 0);

        // We don't need to re-upload materials if only names are changed
        if (!AppliedMaterialProxy.IsDirty)
        {
            RenameMaterials(geometries, materialProxies);
            return;
        }

        List<(Geometry Geometry, List<AppliedMaterial> Materials)> allMaterials = [];

        foreach (var geometry in geometries)
        {
            var oldMaterials = geometry.MaterialsList;
            Debug.Assert(oldMaterials.Count == materialProxies.Count);
            var newMaterials = oldMaterials.ToList();

            for (int i = 0; i < oldMaterials.Count; ++i)
            {
                var proxy = materialProxies[i];
                var oldMtl = oldMaterials[i];
                // current submesh has the same material asset in all selected components
                if (oldMtl.MaterialInfo.Guid == proxy.MaterialInfo.Guid)
                {
                    newMaterials[i] = oldMtl.Clone();
                }
                else
                {
                    newMaterials[i] = new(proxy.MaterialInfo);
                }

                var newMtl = newMaterials[i];
                if (proxy.Name != null && AppliedMaterialProxy.IsNameDirty)
                {
                    newMtl.Name = proxy.Name;
                }

                Debug.Assert(newMtl.Inputs.Count == proxy.Inputs.Count);
                for (int j = 0; j < newMtl.Inputs.Count; ++j)
                {
                    if (proxy.Inputs[j].Asset != null)
                    {
                        newMtl.Inputs[j].SetInputAsset(proxy.Inputs[j].Asset);
                    }
                }

                proxy.SetMaterialSurface(newMtl.MaterialSurface);
            }

            allMaterials.Add((geometry, newMaterials));
        }

        var entities = geometries.Select(g => g.Owner).ToList();
        var scene = geometries[0].Owner.ParentScene;
        var enableList = scene.DisableAndUpdate(entities);

        allMaterials.ForEach(x => x.Geometry.SetMaterials(x.Materials));

        scene.EnableAndUpdate(enableList);
    }

    private void OnApplyChangesCommand()
    {
        var undoSelection = SelectedComponents
            .Select(g => (Geometry: g, Materials: g.MaterialsList.Select(m => new AppliedMaterialProxy(m)).ToList()))
            .ToList();

        var materialProxies = GeometryWithMaterials?.LODs?.SelectMany(x => x.Meshes, (x, m) => m.MaterialProxy)?.ToList();
        var isDirty = AppliedMaterialProxy.IsDirty;
        var isNameDirty = AppliedMaterialProxy.IsNameDirty;

        Project.UndoRedo.Add(new UndoRedoAction(
            () =>
            {
                AppliedMaterialProxy.IsDirty = isDirty;
                AppliedMaterialProxy.IsNameDirty = isNameDirty;
                undoSelection.ForEach(x => ApplyMaterialsToComponents([x.Geometry], x.Materials));
                MSEntity.CurrentSelection?.GetMSComponent<MSGeometry>().Refresh();
            },
            () =>
            {
                AppliedMaterialProxy.IsDirty = isDirty;
                AppliedMaterialProxy.IsNameDirty = isNameDirty;
                ApplyMaterialsToComponents(SelectedComponents, materialProxies);
                MSEntity.CurrentSelection?.GetMSComponent<MSGeometry>().Refresh();
            },
            $"Apply materials to geometry components"));

        ApplyMaterialsToComponents(SelectedComponents, materialProxies);
        Refresh();
    }

    private List<AppliedMaterialProxy> ConstructMaterialProxies()
    {
        var geometry = SelectedComponents.First();
        var geometryWithMaterials = geometry.GeometryWithMaterials;
        var materials = geometry.MaterialsList;
        var proxies = materials.Select(m => new AppliedMaterialProxy(m)).ToList();
        Debug.Assert(proxies.Count == materials.Count && proxies.Count > 0);

        foreach (var c in SelectedComponents.Skip(1))
        {
            var mtls = c.MaterialsList;
            Debug.Assert(mtls.Count == materials.Count);

            for (int i = 0; i < proxies.Count; ++i)
            {
                proxies[i].Slap(mtls[i]);
            }
        }

        var index = 0;

        geometryWithMaterials.LODs
            .SelectMany(lod => lod.Meshes)
            .ToList().ForEach(mesh => mesh.MaterialProxy = proxies[index++]);

        return proxies;
    }

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
        AppliedMaterialProxy.IsDirty = false;
        AppliedMaterialProxy.IsNameDirty = false;
        var contentId = MSEntity.GetMixedValue(SelectedComponents, new Func<Geometry, IdType>(x => x.ContentId));
        if (contentId.HasValue)
        {
            if (ID.IsValid(contentId.Value))
            {
                Debug.Assert(ID.IsValid(contentId.Value));
                MaterialProxies = ConstructMaterialProxies();
                GeometryWithMaterials = SelectedComponents.First().GeometryWithMaterials;
            }
            else if (AssetRegistry.GetAssetInfo(SelectedComponents.First().GeometryGuid) is AssetInfo info)
            {
                GeometryWithMaterials = new(info.FileName, info.Icon, null);
            }
        }
        else
        {
            GeometryWithMaterials = null;
            MaterialProxies = [];
        }

        return true;
    }

    public MSGeometry(MSEntity msEntity) : base(msEntity)
    {
        Refresh();
    }
}