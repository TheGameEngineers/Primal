// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrimalEditor.Content
{
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

    class MaterialInputAsset : MaterialInput
    {
        private AssetInfo _asset;
        public AssetInfo Asset
        {
            get => _asset;
            set
            {
                if (_asset != value)
                {
                    _asset = value;
                    OnPropertyChanged(nameof(Asset));
                }
            }
        }

        public MaterialInputAsset(MaterialInput input) : base(input.Name) { }
    }

    class MaterialSurface : ViewModelBase
    {
        private Color _baseColor = Color.FromScRgb(1f, 0.7f, 0.7f, 0.7f);
        public Color BaseColor
        {
            get => _baseColor;
            set
            {
                if (_baseColor != value)
                {
                    _baseColor = value;
                    OnPropertyChanged(nameof(BaseColor));
                }
            }
        }

        private Color _emissiveColor = Color.FromScRgb(1f, 0f, 0f, 0f);
        public Color EmissiveColor
        {
            get => _emissiveColor;
            set
            {
                if (_emissiveColor != value)
                {
                    _emissiveColor = value;
                    OnPropertyChanged(nameof(EmissiveColor));
                }
            }
        }

        private float _emissiveIntensity = 1f;
        public float EmissiveIntensity
        {
            get => _emissiveIntensity;
            set
            {
                if (!_emissiveIntensity.IsTheSameAs(value))
                {
                    _emissiveIntensity = value;
                    OnPropertyChanged(nameof(EmissiveIntensity));
                }
            }
        }

        private float _metallic = 0f;
        public float Metallic
        {
            get => _metallic;
            set
            {
                if (!_metallic.IsTheSameAs(value))
                {
                    _metallic = value;
                    OnPropertyChanged(nameof(Metallic));
                }
            }
        }

        private float _roughness = 0.9f;
        public float Roughness
        {
            get => _roughness;
            set
            {
                if (!_roughness.IsTheSameAs(value))
                {
                    _roughness = value;
                    OnPropertyChanged(nameof(Roughness));
                }
            }
        }

        public void FromBinary(BinaryReader reader)
        {
            _baseColor.ScR = reader.ReadSingle(); _baseColor.ScG = reader.ReadSingle(); _baseColor.ScB = reader.ReadSingle(); _baseColor.ScA = reader.ReadSingle();
            _emissiveColor.ScR = reader.ReadSingle(); _emissiveColor.ScG = reader.ReadSingle(); _emissiveColor.ScB = reader.ReadSingle();
            _emissiveIntensity = reader.ReadSingle();
            _metallic = reader.ReadSingle();
            _roughness = reader.ReadSingle();
        }

        public void ToBinary(BinaryWriter writer)
        {
            writer.Write(_baseColor.ScR); writer.Write(_baseColor.ScG); writer.Write(_baseColor.ScB); writer.Write(_baseColor.ScA);
            writer.Write(_emissiveColor.ScR); writer.Write(_emissiveColor.ScG); writer.Write(_emissiveColor.ScB);
            writer.Write(_emissiveIntensity);
            writer.Write(_metallic);
            writer.Write(_roughness);
        }

        public MaterialSurface Clone()
            => new()
            {
                BaseColor = BaseColor,
                EmissiveColor = EmissiveColor,
                EmissiveIntensity = EmissiveIntensity,
                Metallic = Metallic,
                Roughness = Roughness,
            };
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

    class AppliedMaterial : Asset
    {
        private class RefCountedMaterial
        {
            public int ReferenceCount { get; set; }
            public Material Material { get; set; }
        }

        private static readonly Dictionary<string, UploadedAsset> _packedMaterials = [];
        private static readonly Dictionary<IdType, string> _packedMaterialIds = [];
        private static readonly Dictionary<Guid, RefCountedMaterial> _loadedMaterials = [];

        private readonly Material _material;
        private readonly ObservableCollection<MaterialInputAsset> _inputs = [];
        public ReadOnlyObservableCollection<MaterialInputAsset> Inputs;
        private readonly List<IdType> _shaderIds = [];
        public MaterialSurface MaterialSurface { get; init; }
        public UploadedAsset UploadedAsset { get; private set; }

        private byte[] _packedData = [];
        private byte[] _previousPackedData = [];

        public override List<AssetInfo> GetReferencedAssets() => Inputs.Where(x => x.Asset != null && x.Asset.Guid != Guid.Empty).Select(x => x.Asset).ToList();

        public AppliedMaterial Clone()
        {
            var clone = new AppliedMaterial(_material.GetAssetInfo()) { MaterialSurface = MaterialSurface.Clone(), Icon = Icon };
            return clone;
        }

        private void UploadShaders()
        {
            _shaderIds.Clear();

            foreach (var shaderType in Enum.GetValues<ShaderType>())
            {
                var shaderGroup = _material.GetShaderGroup(shaderType);
                _shaderIds.Add(shaderGroup != null ? shaderGroup.UploadToEngine() : ID.INVALID_ID);
            }

            Debug.Assert(_loadedMaterials.ContainsKey(_material.Guid));
            if (_loadedMaterials.TryGetValue(_material.Guid, out var loadedMaterial))
            {
                ++loadedMaterial.ReferenceCount;
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

        public void UnloadFromEngine()
        {
            Debug.Assert(UploadedAsset != null && _packedMaterialIds.ContainsKey(UploadedAsset.ContentId));
            Debug.Assert(UploadedAsset.GetContentId(UploadedAsset.AssetInfo.Guid) == UploadedAsset.ContentId);

            if (_packedMaterialIds.TryGetValue(UploadedAsset.ContentId, out var dataString) &&
                _packedMaterials.TryGetValue(dataString, out var uploadedAsset))
            {
                Debug.Assert(UploadedAsset == uploadedAsset);
                UploadedAsset.RemoveFromScene(uploadedAsset);
                UnloadShaders();

                if (UploadedAsset.ReferenceCount == 0)
                {
                    _packedMaterialIds.Remove(UploadedAsset.ContentId);
                    _packedMaterials.Remove(dataString);
                    _previousPackedData = [];
                    UploadedAsset = null;
                }
            }
        }

        public override AssetMetadata GetMetadata() => new MaterialMetadata() { PackedData = _packedData };

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

            if(referencedAssets.Count > 0)
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
            MaterialSurface.ToBinary(writer);
            writer.Write((int)_material.MaterialType);
            writer.Write(referencedAssets.Count);
            _shaderIds.ForEach(writer.Write);

            writer.Flush();
            var data = (writer.BaseStream as MemoryStream)?.ToArray();
            Debug.Assert(data?.Length>0);
            return data;
        }

        public AppliedMaterial(AssetInfo materialAssetInfo) : base(AssetType.Material)
        {
            Debug.Assert(materialAssetInfo != null && materialAssetInfo.Guid != Guid.Empty);
            if (_loadedMaterials.TryGetValue(materialAssetInfo.Guid, out var loadedMaterial))
            {
                // Note: we increment the reference count when the applied material is uploaded.
                _material = loadedMaterial.Material;
            }
            else
            {
                _material = new(materialAssetInfo);
                _loadedMaterials.Add(_material.Guid, new() { Material = _material });
            }

            Debug.Assert(_material != null);
            MaterialSurface = _material.MaterialSurface.Clone();
            _material.GetInputs().ForEach(x => _inputs.Add(new(x)));
            Icon = _material.Icon;
            Inputs = new(_inputs);
        }
    }

    class Material : Asset
    {
        public static AssetInfo Default => DefaultAssets.DefaultMaterial;

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

                MaterialSurface.ToBinary(writer);
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
}
