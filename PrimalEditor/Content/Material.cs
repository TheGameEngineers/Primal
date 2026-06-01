// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Threading;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrimalEditor.Content;

enum MaterialType : int
{
    Opaque = 0,
}

enum MaterialMode : int
{
    NoInput,
    Default,
    Node,
    Code,
}

class MaterialMetadata : AssetMetadata
{
    public byte[] PackedData { get; init; }
}

class MaterialInput : ViewModelBase
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

    public MaterialInput(string name)
    {
        Name = name;
    }
}

class AppliedMaterialInput : MaterialInput
{
    private UploadedAsset _uploadedAsset;

    private AssetInfo _asset = Texture.Default;
    public AssetInfo Asset
    {
        get => _asset;
        private set
        {
            if (_asset != value && _asset?.Guid != value.Guid)
            {
                _asset = value;
                OnPropertyChanged(nameof(Asset));
                OnPropertyChanged(nameof(Ignore));
            }
        }
    }

    public bool Ignore => Asset.Guid == Texture.Default.Guid;

    public void SetInputAsset(AssetInfo assetInfo)
    {
        Debug.Assert(assetInfo != null && assetInfo.Guid != Guid.Empty);
        if (assetInfo != null && assetInfo.Guid != Guid.Empty)
        {
            Unload();
            Asset = assetInfo;
            Load();
        }
    }

    public void Load()
    {
        if (_uploadedAsset == null)
        {
            _uploadedAsset = UploadedAsset.AddToScene(Asset);
            Debug.Assert(_uploadedAsset != null && ID.IsValid(_uploadedAsset.ContentId));
        }
    }

    public void Unload()
    {
        if (_uploadedAsset != null)
        {
            Debug.Assert(UploadedAsset.GetContentId(Asset.Guid) == _uploadedAsset.ContentId);
            UploadedAsset.RemoveFromScene(_uploadedAsset);
            _uploadedAsset = null;
        }
    }

    public AppliedMaterialInput(MaterialInput input, AssetInfo asset = null) : base(input.Name)
    {
        Debug.Assert(!(asset != null && asset.Guid == Guid.Empty));
        SetInputAsset(asset ?? _asset);
    }
}

[DataContract]
class MaterialSurface
{
    [DataMember]
    public Color BaseColor = Color.FromScRgb(1f, 0.7f, 0.7f, 0.7f);

    [DataMember]
    public Color EmissiveColor = Color.FromScRgb(1f, 0f, 0f, 0f);

    [DataMember]
    public float EmissiveIntensity = 1f;

    [DataMember]
    public float Metallic = 0f;

    [DataMember]
    public float Roughness = 0.9f;

    public byte InputMask = 0;

    public void FromBinary(BinaryReader reader)
    {
        BaseColor.ScR = reader.ReadSingle(); BaseColor.ScG = reader.ReadSingle(); BaseColor.ScB = reader.ReadSingle(); BaseColor.ScA = reader.ReadSingle();
        EmissiveColor.ScR = reader.ReadSingle(); EmissiveColor.ScG = reader.ReadSingle(); EmissiveColor.ScB = reader.ReadSingle();
        EmissiveIntensity = reader.ReadSingle();
        Metallic = reader.ReadSingle();
        Roughness = reader.ReadSingle();
    }

    public void ToBinary(BinaryWriter writer, bool asInteger)
    {
        if (asInteger)
        {
            // NOTE: this is the order of bytes in material_surface struct in the engine.
            writer.Write(BaseColor.R); writer.Write(BaseColor.G); writer.Write(BaseColor.B); writer.Write(BaseColor.A);
            writer.Write(EmissiveColor.R); writer.Write(EmissiveColor.G); writer.Write(EmissiveColor.B);
            writer.Write((byte)(Metallic * 255));
            writer.Write((byte)(Roughness * 255));
            writer.Write(InputMask);
            writer.Write((ushort)(EmissiveIntensity * (65535f / Material.MaxEmissiveIntensity)));
        }
        else
        {
            writer.Write(BaseColor.ScR); writer.Write(BaseColor.ScG); writer.Write(BaseColor.ScB); writer.Write(BaseColor.ScA);
            writer.Write(EmissiveColor.ScR); writer.Write(EmissiveColor.ScG); writer.Write(EmissiveColor.ScB);
            writer.Write(EmissiveIntensity);
            writer.Write(Metallic);
            writer.Write(Roughness);
        }
    }

    public void CopyTo(MaterialSurface dst)
    {
        dst.BaseColor = BaseColor;
        dst.EmissiveColor = EmissiveColor;
        dst.EmissiveIntensity = EmissiveIntensity;
        dst.Metallic = Metallic;
        dst.Roughness = Roughness;
    }
}

class DefaultMaterialInputs : ViewModelBase
{
    private readonly List<MaterialInput> _inputs;

    public List<MaterialInput> GetInputs() => _inputs;

    public void AddInput(MaterialInput input)
    {
        if (!_inputs.Any(x => x.Name == input.Name))
        {
            _inputs.Add(input);
        }
    }

    public void RemoveInput(string name)
    {
        _inputs.Remove(_inputs.Find(x => x.Name == name));
    }

    public void FromBinary(BinaryReader reader)
    {
        foreach (var input in _inputs)
        {
            input.Name = reader.ReadString();
        }
    }

    public void ToBinary(BinaryWriter writer)
    {
        foreach (var input in _inputs)
        {
            writer.Write(input.Name);
        }
    }

    public DefaultMaterialInputs()
    {
        _inputs = [new("Base Color"), new("Emissive Color"), new("Normal Map"), new("Metallic and Roughness"), new("Ambient Occlusion"),];
    }
}

class NodeMaterial : ViewModelBase
{
    private readonly List<MaterialInput> _inputs;

    public List<MaterialInput> GetInputs() => _inputs;
}

class CodeMaterial : ViewModelBase
{
    private readonly List<MaterialInput> _inputs;

    public List<MaterialInput> GetInputs() => _inputs;
}

[DataContract]
class AppliedMaterial : Asset
{
    private class RefCountedMaterial
    {
        public int ReferenceCount { get; set; }
        public Material Material { get; set; }
    }

    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, UploadedAsset> _packedMaterials = [];
    private static readonly Dictionary<IdType, string> _packedMaterialIds = [];
    private static readonly Dictionary<Guid, RefCountedMaterial> _loadedMaterials = [];

    [DataMember(Name = "Material")]
    private Guid _materialGuid;
    [DataMember(Name = "Inputs")]
    private readonly List<Guid> _inputGuids = [];
    [DataMember(Name = "InputNames")]
    private readonly List<string> _inputNames = [];

    private string _name = "Material";
    [DataMember]
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


    private Material _material;
    private ObservableCollection<AppliedMaterialInput> _inputs = [];
    public ReadOnlyObservableCollection<AppliedMaterialInput> Inputs { get; private set; }
    private List<IdType> _shaderIds = [];

    [DataMember]
    public MaterialSurface MaterialSurface { get; private set; } = new();
    public UploadedAsset UploadedAsset { get; private set; }

    private byte[] _packedData = [];
    private byte[] _previousPackedData = [];

    public override List<AssetInfo> GetReferencedAssets() => [.. Inputs.Where(x => x.Asset != null && x.Asset.Guid != Guid.Empty).Select(x => x.Asset)];

    private void UploadShaders()
    {
        _shaderIds.Clear();

        foreach (var shaderType in Enum.GetValues<ShaderType>())
        {
            var shaderGroup = _material.GetShaderGroup(shaderType);
            _shaderIds.Add(shaderGroup?.UploadToEngine() ?? ID.INVALID_ID);
        }

        Debug.Assert(_material != null);
        if (_loadedMaterials.TryGetValue(_material.Guid, out var loadedMaterial))
        {
            ++loadedMaterial.ReferenceCount;
        }
        else
        {
            _loadedMaterials.Add(_material.Guid, new() { ReferenceCount = 1, Material = _material });
        }
    }

    private void UnloadShaders()
    {
        foreach (var shaderType in Enum.GetValues<ShaderType>())
        {
            _material.GetShaderGroup(shaderType)?.UnloadFromEngine();
        }

        _shaderIds.Clear();

        Debug.Assert(_loadedMaterials.ContainsKey(_material.Guid));
        if (_loadedMaterials.TryGetValue(_material.Guid, out var loadedMaterial))
        {
            Debug.Assert(loadedMaterial.ReferenceCount > 0);
            --loadedMaterial.ReferenceCount;
            if (loadedMaterial.ReferenceCount == 0)
            {
                _loadedMaterials.Remove(_material.Guid);
            }
        }
    }

    public bool UploadToEngine()
    {
        lock (_lock)
        {
            // NOTE: this only uploads inputs if they haven't been uploaded before.
            _inputs.ToList().ForEach(x => x.Load());

            UploadShaders();
            _packedData = PackForEngine();

            if (_packedData == null)
            {
                UnloadShaders();
                return false;
            }

            // If the material hasn't been modified since the last upload, then there's no need to re-upload.
            // Just return the scene asset.
            // NOTE: SequenceEqual() is slow, but our data array is small in general and we don't do this very often.
            if (_packedData.SequenceEqual(_previousPackedData))
            {
                Debug.Assert(UploadedAsset != null && UploadedAsset.GetContentId(Guid) == UploadedAsset.ContentId);
                UnloadShaders();
                return true;
            }

            // Material was modified or hasn't been uploaded yet.
            // Check if there was any other material uploaded with the same data.
            var dataString = Convert.ToBase64String(_packedData);

            // An identical material was uploaded
            if (_packedMaterials.TryGetValue(dataString, out var uploadedAsset))
            {
                Debug.Assert(_packedMaterialIds.ContainsKey(uploadedAsset.ContentId));
                Debug.Assert((uploadedAsset.Metadata as MaterialMetadata).PackedData.SequenceEqual(_packedData));

                Guid = uploadedAsset.AssetInfo.Guid;
                // This will increment the ref-count
                var result = UploadedAsset.AddToScene(GetAssetInfo(), this);
                Debug.Assert(result == uploadedAsset);
            }
            // Material was modified and is unique or material is new and unique
            else
            {
                // material is not new, but is unique and need a new guid.
                if (ID.IsValid(UploadedAsset.GetContentId(Guid)))
                {
                    Debug.Assert(_previousPackedData.Length > 0 && UploadedAsset != null);
                    Guid = Guid.NewGuid();
                }

                uploadedAsset = UploadedAsset.AddToScene(GetAssetInfo(), this);

                Debug.Assert(!_packedMaterialIds.ContainsKey(uploadedAsset.ContentId));
                _packedMaterials.Add(dataString, uploadedAsset);
                _packedMaterialIds.Add(uploadedAsset.ContentId, dataString);
            }

            // Unload the old variant if any
            if (_previousPackedData.Length > 0)
            {
                Debug.Assert(UploadedAsset != null && UploadedAsset.ContentId != uploadedAsset.ContentId);
                UnloadFromEngine();
            }

            _previousPackedData = _packedData;
            UploadedAsset = uploadedAsset;

            return true;
        }
    }

    public void UnloadFromEngine()
    {
        lock (_lock)
        {
            Debug.Assert(UploadedAsset != null && _packedMaterialIds.ContainsKey(UploadedAsset.ContentId));
            Debug.Assert(UploadedAsset.GetContentId(UploadedAsset.AssetInfo.Guid) == UploadedAsset.ContentId);

            if (_packedMaterialIds.TryGetValue(UploadedAsset.ContentId, out var dataString) &&
                _packedMaterials.TryGetValue(dataString, out var uploadedAsset))
            {
                // We need contentId since UploadedAsset.RemoveFromScene() will set UploadedAsset.ContentId to ID.Invalid_ID
                // if the asset is removed from the scene.
                var contentId = uploadedAsset.ContentId;
                Debug.Assert(UploadedAsset == uploadedAsset);
                UploadedAsset.RemoveFromScene(uploadedAsset);
                UnloadShaders();

                if (UploadedAsset.ReferenceCount == 0)
                {
                    _packedMaterialIds.Remove(contentId);
                    _packedMaterials.Remove(dataString);
                }

                _inputs.ToList().ForEach(x => x.Unload());
                _previousPackedData = [];
                UploadedAsset = null;
            }
        }
    }

    public override MaterialMetadata GetMetadata() => new() { PackedData = _packedData };

    public override bool Import(string file) => throw new NotImplementedException();

    public override bool Load(string file) => throw new NotImplementedException();

    public override IEnumerable<string> Save(string file) => throw new NotImplementedException();

    // NOTE: expects data to contain
    // struct {
    //  u32                 texture_count,
    //  id::id_type         texture_ids[texture_count];
    //  material_surface    surface;
    //  material_type::type type,
    //  u32                 texture_count,
    //  id::id_type         shader_ids[shader_type::count],
    // } material_init_info
    public override byte[] PackForEngine()
    {
        using var writer = new BinaryWriter(new MemoryStream());
        var referencedAssets = GetReferencedAssets();

        writer.Write(referencedAssets.Count);

        if (referencedAssets.Count > 0)
        {
            foreach (var input in referencedAssets)
            {
                var contentId = UploadedAsset.GetContentId(input.Guid);
                Debug.Assert(ID.IsValid(contentId));

                if (!ID.IsValid(contentId)) return null;

                writer.Write(contentId);
            }
        }

        // Leave room for a pointer to texture ids. It will remain null if no textures are used.
        writer.Write(IntPtr.Zero);
        MaterialSurface.InputMask = 0;
        for (int i = 0; i < Math.Min(_inputs.Count, 8); ++i)
        {
            if (!_inputs[i].Ignore)
            {
                MaterialSurface.InputMask |= (byte)(1 << i);
            }
        }
        MaterialSurface.ToBinary(writer, asInteger: true);
        writer.Write((int)_material.MaterialType);
        writer.Write(referencedAssets.Count);
        _shaderIds.ForEach(writer.Write);

        writer.Flush();
        var data = (writer.BaseStream as MemoryStream)?.ToArray();
        Debug.Assert(data?.Length > 0);
        return data;
    }

    private void LoadMaterial(AssetInfo materialAssetInfo)
    {
        Debug.Assert(_material == null);
        Debug.Assert(materialAssetInfo != null && materialAssetInfo.Guid != Guid.Empty);

        // Note: we increment the reference count when the applied material is uploaded.
        if (_loadedMaterials.TryGetValue(materialAssetInfo.Guid, out var loadedMaterial))
        {
            _material = loadedMaterial.Material;
        }
        else
        {
            _material = new(materialAssetInfo);
            _loadedMaterials.Add(_material.Guid, new() { ReferenceCount = 0, Material = _material });
        }
    }

    [OnSerializing]
    private void OnSerializing(StreamingContext context)
    {
        Debug.Assert(_material != null && _material.Guid != Guid.Empty);
        _materialGuid = _material.Guid;

        _inputGuids.Clear();
        _inputNames.Clear();
        foreach (var input in _inputs)
        {
            Debug.Assert(input.Asset != null && input.Asset.Guid != Guid.Empty);
            _inputGuids.Add(input.Asset.Guid);
            _inputNames.Add(input.Name);
        }
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        Debug.Assert(Type == AssetType.Material);
        Debug.Assert(_materialGuid != Guid.Empty);
        var assetInfo = AssetRegistry.GetAssetInfo(_materialGuid) ?? Material.Default; // TODO: warn if material not found
        Debug.Assert(assetInfo != null && assetInfo.Type == AssetType.Material);
        LoadMaterial(assetInfo);

        _inputs = [];
        Inputs = new(_inputs);

        for (int i = 0; i < _inputGuids.Count; ++i)
        {
            var inputAssetInfo = AssetRegistry.GetAssetInfo(_inputGuids[i]) ?? Texture.Default; // TODO: warn if texture not found
            Debug.Assert(inputAssetInfo != null && inputAssetInfo.Guid == _inputGuids[i]);
            _inputs.Add(new(new(_inputNames[i]), inputAssetInfo));
        }

        Icon = _material.Icon;
        _shaderIds = [];
        _packedData = [];
        _previousPackedData = [];

        _materialGuid = Guid.Empty;
        _inputGuids.Clear();
        _inputNames.Clear();
    }

    public AppliedMaterial(AssetInfo materialAssetInfo) : base(AssetType.Material)
    {
        LoadMaterial(materialAssetInfo);

        Debug.Assert(_material != null);
        _material.MaterialSurface.CopyTo(MaterialSurface);
        _material.GetInputs().ForEach(x => _inputs.Add(new(x)));
        Icon = _material.Icon;
        Inputs = new(_inputs);
    }
}

class Material : Asset
{
    public static AssetInfo Default => DefaultAssets.DefaultMaterial;
    public static float MaxEmissiveIntensity => 10000f; // 10,000 cd/m^2
    private readonly Dictionary<ShaderType, ShaderGroup> _shaders = [];

    private MaterialType _materialType;
    public MaterialType MaterialType
    {
        get => _materialType;
        set
        {
            if (_materialType != value)
            {
                _materialType = value;
                OnPropertyChanged(nameof(MaterialType));
            }
        }
    }

    private MaterialMode _materialMode;
    public MaterialMode MaterialMode
    {
        get => _materialMode;
        set
        {
            if (_materialMode != value)
            {
                _materialMode = value;
                OnPropertyChanged(nameof(MaterialMode));
            }
        }
    }

    public MaterialSurface MaterialSurface { get; } = new();

    public DefaultMaterialInputs DefaultMaterialInputs { get; } = new();
    public NodeMaterial NodeMaterial { get; } = new();
    public CodeMaterial CodeMaterial { get; } = new();

    public List<MaterialInput> GetInputs() =>
        _materialMode switch
        {
            MaterialMode.NoInput => [],
            MaterialMode.Default => DefaultMaterialInputs.GetInputs(),
            MaterialMode.Node => NodeMaterial.GetInputs(),
            MaterialMode.Code => CodeMaterial.GetInputs(),
            _ => throw new NotImplementedException()
        };

    public override bool Import(string file) => throw new NotImplementedException();

    public override bool Load(string file)
    {
        Debug.Assert(File.Exists(file));
        Debug.Assert(Path.GetExtension(file).ToLower() == AssetFileExtension);

        if (!File.Exists(file)) return false;

        try
        {
            using var reader = new BinaryReader(File.Open(file, FileMode.Open, FileAccess.Read));

            ReadAssetFileHeader(reader);

            var shaderGroupCount = reader.ReadInt32();

            _shaders.Clear();

            for (int i = 0; i < shaderGroupCount; ++i)
            {
                var shaderGroup = new ShaderGroup();
                shaderGroup.FromBinary(reader);
                Debug.Assert(!_shaders.ContainsKey(shaderGroup.Type));
                _shaders.Add(shaderGroup.Type, shaderGroup);
            }

            MaterialType = (MaterialType)reader.ReadInt32();
            MaterialMode = (MaterialMode)reader.ReadInt32();

            MaterialSurface.FromBinary(reader);
            DefaultMaterialInputs.FromBinary(reader);
            //NodeMaterial.FromBinary(reader);
            //CodeMaterial.FromBinary(reader);

            FullPath = file;

            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to load material asset from file: {file}");
        }

        return false;
    }

    public override byte[] PackForEngine() => throw new NotImplementedException();

    public override IEnumerable<string> Save(string file)
    {
        try
        {
            if (TryGetAssetInfo(file) is AssetInfo info && info.Type == Type)
            {
                Guid = info.Guid;
            }

            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri("pack://application:,,,/Resources/TextureEditor/Checker64.png");
            bmp.DecodePixelWidth = ContentInfo.IconWidth;
            bmp.EndInit();
            Icon = BitmapHelper.CreateThumbnail(bmp, ContentInfo.IconWidth, ContentInfo.IconWidth);

            using var writer = new BinaryWriter(File.Open(file, FileMode.Create, FileAccess.Write));

            WriteAssetFileHeader(writer);

            writer.Write(_shaders.Count);

            foreach (var (_, shaderGroup) in _shaders)
            {
                shaderGroup.ToBinary(writer);
            }

            writer.Write((int)MaterialType);
            writer.Write((int)MaterialMode);

            MaterialSurface.ToBinary(writer, asInteger: false);
            DefaultMaterialInputs.ToBinary(writer);
            //NodeMaterial.ToBinary(writer);
            //CodeMaterial.ToBinary(writer);

            FullPath = file;
            Logger.Log(MessageType.Info, $"Saved material to {file}");

            var savedFiles = new List<string>() { file };
            return savedFiles;
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Error, $"Failed to save material to: {file}");
            return [];
        }
    }

    public bool AddShaderGroup(ShaderGroup shaderGroup)
    {
        Debug.Assert(shaderGroup != null && !_shaders.ContainsKey(shaderGroup.Type));
        return _shaders.TryAdd(shaderGroup.Type, shaderGroup);
    }

    public ShaderGroup GetShaderGroup(ShaderType shaderType)
    {
        _shaders.TryGetValue(shaderType, out var shaderGroup);
        return shaderGroup;
    }

    public override AssetMetadata GetMetadata() => throw new NotImplementedException();

    public Material() : base(AssetType.Material) { }

    public Material(AssetInfo assetInfo) : this()
    {
        Debug.Assert(assetInfo != null && assetInfo.Guid != Guid.Empty);
        Debug.Assert(File.Exists(assetInfo.FullPath) && assetInfo.Type == Type);
        Load(assetInfo.FullPath);
    }
}
