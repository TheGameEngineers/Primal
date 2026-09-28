// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Components;
using PrimalEditor.Content;
using PrimalEditor.GameDev;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using System.Xml.Linq;

namespace PrimalEditor.GameProject;

enum EntityType
{
    GameEntity,
    Light,
    Camera,
}

static class Chef
{
    private static class Indexer
    {
        private class MaterialInfo
        {
            public int MaterialGuidIndex { get; init; }
            public MaterialType MaterialType { get; init; }
            public int ShaderFlags { get; init; }
            public List<int> ShaderCountPerGroup { get; init; }
            public List<string> ShaderFiles { get; init; }
        }

        private static readonly List<Guid> _usedAssets = [];
        private static readonly Dictionary<Guid, int> _assetGuidToIndex = [];
        private static readonly Dictionary<Guid, MaterialInfo> _savedMaterials = [];

        public static List<Guid> GetUsedAssets() => [.. _usedAssets];

        public static int AddGuid(Guid guid)
        {
            if (!_assetGuidToIndex.TryGetValue(guid, out var assetIndex))
            {
                assetIndex = _usedAssets.Count;
                _usedAssets.Add(guid);
                _assetGuidToIndex[guid] = assetIndex;
            }

            Debug.Assert(_usedAssets.Contains(guid));
            return assetIndex;
        }

        public static int SaveAssetData(string contentPath, Guid guid, Func<AssetInfo, Asset> loadAsset)
        {
            if (_assetGuidToIndex.TryGetValue(guid, out var assetIndex))
            {
                Debug.Assert(_usedAssets.Contains(guid));
                return assetIndex;
            }

            var filePath = $"{contentPath}{guid}{Asset.AssetFileExtension}";

            if (AssetRegistry.GetAssetInfo(guid) is AssetInfo info)
            {
                if (ContentHelper.IsMissingOrOutdated(filePath, info.ImportDate))
                {
                    if (File.Exists(filePath))
                    {
                        Logger.Log(MessageType.Warning, $"Overwriting asset data: {filePath}");
                    }

                    var asset = loadAsset(info);
                    var data = asset.PackForEngine();
                    using var fs = File.Open(filePath, FileMode.Create, FileAccess.Write);
                    fs.Write(BitConverter.GetBytes((int)info.Type));
                    fs.Write(data, 0, data.Length);
                }

                return AddGuid(guid);
            }
            else
            {
                Logger.Log(MessageType.Error, $"Failed to save asset data. Asset file not found: {filePath}");
                throw new FileNotFoundException("Asset file not found.", filePath);
            }
        }

        public static (int DiffuseIndex, int SpecularIndex, int BrdfLutIndex) SaveIBLTextureData(string contentPath, Guid guid)
        {

            if (AssetRegistry.GetAssetInfo(guid) is not AssetInfo info || info.Type != AssetType.Texture)
            {
                var filePath = $"{contentPath}{guid}{Asset.AssetFileExtension}";
                Logger.Log(MessageType.Error, $"Failed to save IBL texture data. Asset file not found: {filePath}");
                throw new FileNotFoundException("Asset file not found.", filePath);
            }

            var texture = new Texture(info);

            if (!texture.IsPrefilteredIBL)
            {
                throw new InvalidDataException("Invalid texture type.");
            }

            var diffuseGuid = texture.IsDiffuseIBL ? texture.Guid : texture.IBLPair.Guid;
            var specularGuid = texture.IsDiffuseIBL ? texture.IBLPair.Guid : texture.Guid;
            var brdfLutGuid = DefaultAssets.BrdfIntegrationLut.Guid;

            var diffuseIndex = SaveAssetData(contentPath, diffuseGuid, (info) => new Texture(info));
            var specularIndex = SaveAssetData(contentPath, specularGuid, (info) => new Texture(info));
            var brdfLutIndex = SaveAssetData(contentPath, brdfLutGuid, (info) => new Texture(info));

            return (diffuseIndex, specularIndex, brdfLutIndex);
        }

        private static bool TryGetMaterialInfo(AssetInfo info, string contentPath, string mtlPath, out MaterialInfo mtlInfo)
        {
            ArgumentNullException.ThrowIfNull(info);

            if (_savedMaterials.TryGetValue(info.Guid, out mtlInfo)) return true;

            var shouldSave = ContentHelper.IsMissingOrOutdated(mtlPath, info.ImportDate);

            if (shouldSave) return false;

            // Material asset file exists and is up-to-date. Check if shader files are missing.
            using var reader = new BinaryReader(File.Open(mtlPath, FileMode.Open, FileAccess.Read));
            var assetType = (AssetType)reader.ReadInt32();
            var mtlType = (MaterialType)reader.ReadInt32();
            var shaderFlags = reader.ReadInt32();
            var shaderCount = reader.ReadInt32();

            var shaderFiles = new List<string>();
            var countsPerGroup = new List<int>();

            for (var i = 0; i < shaderCount; ++i)
            {
                countsPerGroup.Add(reader.ReadInt32());
            }

            for (var i = 0; i < shaderCount; ++i)
            {
                var nameLength = reader.ReadInt32();
                var nameBytes = reader.ReadBytes(nameLength);
                var shaderFile = Encoding.UTF8.GetString(nameBytes);
                shaderFiles.Add(shaderFile);

                if (ContentHelper.IsMissingOrOutdated($"{contentPath}{shaderFile}", info.ImportDate))
                {
                    Logger.Log(MessageType.Warning, $"Shader file is missing or outdated: {shaderFile}");
                    shouldSave = true;
                    break;
                }
            }

            // Everything is still there and up-to-date, but it wasn't in the dictionary. So, add a new entry and return it.
            if (!shouldSave)
            {
                _savedMaterials[info.Guid] = new()
                {
                    MaterialGuidIndex = AddGuid(info.Guid),
                    MaterialType = mtlType,
                    ShaderFlags = shaderFlags,
                    ShaderCountPerGroup = countsPerGroup,
                    ShaderFiles = shaderFiles
                };

                mtlInfo = _savedMaterials[info.Guid];
                return true;
            }

            return false;
        }

        // Output format:
        // struct{
        //   u32    material_type;
        //   u32    shader_flags;
        //   u32    shader_count;
        //   u32[]  counts_per_group;
        //   struct{
        //     u32  name_length;
        //     u8[] name_bytes;
        //   } shader_files[shader_count];
        // }
        public static int SaveMaterialData(AssetInfo info, string contentPath)
        {
            ArgumentNullException.ThrowIfNull(info);
            var mtlPath = $"{contentPath}{info.Guid}{Asset.AssetFileExtension}";

            if (TryGetMaterialInfo(info, contentPath, mtlPath, out var mtlInfo)) return mtlInfo.MaterialGuidIndex;

            if (File.Exists(mtlPath))
            {
                Logger.Log(MessageType.Warning, $"Overwriting material data: {mtlPath}");
            }

            // Either the material file is missing or outdated, or one of the shader files is missing or outdated. Re-save everything.
            var mtl = new Material(info);
            var shaderFiles = new List<string>();
            var countsPerGroup = new List<int>();
            var shaderFlags = 0;
            var shaderIndex = 0;

            // Save shader files first.
            foreach (var shader_type in Enum.GetValues<ShaderType>())
            {
                if (mtl.GetShaderGroup(shader_type) is not ShaderGroup shaderGroup)
                {
                    ++shaderIndex;
                    continue;
                }

                shaderFlags |= (1 << shaderIndex);
                ++shaderIndex;

                var shaderFileName = $"{mtl.Guid}.{shaderGroup.Type}.shader";
                var shaderPath = $"{contentPath}{shaderFileName}";
                shaderFiles.Add(shaderFileName);
                countsPerGroup.Add(shaderGroup.Count);

                var data = shaderGroup.PackForEngine();
                using var fs = File.Open(shaderPath, FileMode.Create, FileAccess.Write);
                fs.Write(BitConverter.GetBytes((int)shaderGroup.Type));
                fs.Write(data, 0, data.Length);
            }

            // Save material data
            using var writer = new BinaryWriter(File.Open(mtlPath, FileMode.Create, FileAccess.Write));
            writer.Write((int)AssetType.Material);
            writer.Write((int)mtl.MaterialType);
            writer.Write(shaderFlags);
            writer.Write(shaderFiles.Count);
            countsPerGroup.ForEach(writer.Write);
            shaderFiles.ForEach(s =>
            {
                var shaderFileBytes = Encoding.UTF8.GetBytes(s);
                writer.Write(shaderFileBytes.Length);
                writer.Write(shaderFileBytes);
            });

            _savedMaterials[info.Guid] = new()
            {
                MaterialGuidIndex = AddGuid(info.Guid),
                MaterialType = mtl.MaterialType,
                ShaderFlags = shaderFlags,
                ShaderCountPerGroup = countsPerGroup,
                ShaderFiles = shaderFiles
            };

            return _savedMaterials[info.Guid].MaterialGuidIndex;
        }

        public static void Reset()
        {
            _usedAssets.Clear();
            _assetGuidToIndex.Clear();
            _savedMaterials.Clear();
        }

        public static void WriteGuidsArray(BinaryWriter bw)
        {
            var count = _usedAssets.Count;
            Debug.Assert(count == _assetGuidToIndex.Count);
            bw.Write(count);
            _usedAssets.ForEach(guid =>
            {
                var guidStr = guid.ToString("D");
                var guidBytes = Encoding.UTF8.GetBytes(guidStr);
                Debug.Assert(guidBytes.Length == 36);
                bw.Write(guidBytes);

            });
        }

        public static void DeleteUnused(string contentPath)
        {
            var files = Directory.GetFiles(contentPath);
            var existingAssets = new List<Guid>();
            var existingShaders = new List<string>();

            foreach (var file in files)
            {
                var extension = Path.GetExtension(file);
                var guidString = Path.GetFileNameWithoutExtension(file);

                if (extension.Equals(Asset.AssetFileExtension, StringComparison.CurrentCultureIgnoreCase) &&
                    Guid.TryParse(guidString, out var guid))
                {
                    existingAssets.Add(guid);
                }
                else if (extension.Equals(".shader", StringComparison.CurrentCultureIgnoreCase))
                {
                    existingShaders.Add(Path.GetFileName(file));
                }
            }

            // Remove unused assets
            foreach (var guid in existingAssets.Except(_usedAssets))
            {
                var file = $"{contentPath}{guid}{Asset.AssetFileExtension}";
                Debug.Assert(File.Exists(file) && !_usedAssets.Contains(guid));
                File.Delete(file);
            }

            var usedShaders = _savedMaterials.Values.SelectMany(x => x.ShaderFiles);

            // Remove unused shaders
            foreach (var fileName in existingShaders.Except(usedShaders))
            {
                var file = $"{contentPath}{fileName}";
                Debug.Assert(File.Exists(file) && !usedShaders.Contains(fileName));
                File.Delete(file);
            }
        }
    } // class Indexer

    private static readonly ImmutableDictionary<string, EntityType> _entityTypeMapping = ImmutableDictionary.CreateRange<string, EntityType>(
        [
            new (nameof(GameEntity), EntityType.GameEntity),

            new (nameof(DirectionalLight), EntityType.Light),
            new (nameof(PointLight), EntityType.Light),
            new (nameof(Spotlight), EntityType.Light),
            new (nameof(AmbientLight), EntityType.Light),

            new (nameof(PerspectiveCamera), EntityType.Camera),
            new (nameof(OrthographicCamera), EntityType.Camera),
        ]);

    private static int ParseInt(XElement i)
        => i != null ? int.Parse(i.Value, CultureInfo.InvariantCulture) : 0;

    private static float ParseFloat(XElement f)
        => f != null ? float.Parse(f.Value, CultureInfo.InvariantCulture) : 0f;

    private static (float x, float y, float z) ParseVector3(XElement v)
        => v != null ? (ParseFloat(v.Element(nameof(Vector3.X))), ParseFloat(v.Element(nameof(Vector3.Y))), ParseFloat(v.Element(nameof(Vector3.Z)))) : (0f, 0f, 0f);

    private static (byte r, byte g, byte b, byte a) ParseColor(XElement c)
        => c != null ? (
        (byte)ParseInt(c.Element(nameof(System.Windows.Media.Color.R))),
        (byte)ParseInt(c.Element(nameof(System.Windows.Media.Color.G))),
        (byte)ParseInt(c.Element(nameof(System.Windows.Media.Color.B))),
        (byte)ParseInt(c.Element(nameof(System.Windows.Media.Color.A)))) : ((byte)0, (byte)0, (byte)0, (byte)0);

    // Output format:
    // struct{
    //   u32        component_type;
    //   math::v3   position;
    //   math::v3   rotation;
    //   math::v3   scale;
    // }
    private static void TransformToBinary(XElement node, BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(node);

        writer.Write((int)ComponentType.Transform);
        var (x, y, z) = ParseVector3(node.Element(nameof(Transform.Position)));
        writer.Write(x); writer.Write(y); writer.Write(z);
        (x, y, z) = ParseVector3(node.Element(nameof(Transform.Rotation)));
        writer.Write(x); writer.Write(y); writer.Write(z);
        (x, y, z) = ParseVector3(node.Element(nameof(Transform.Scale)));
        writer.Write(x); writer.Write(y); writer.Write(z);
    }

    // Output format:
    // struct{
    //   u32    component_type;
    //   u32    script_name_length;
    //   u8[]   script name;
    // }
    private static void ScriptToBinary(XElement node, BinaryWriter writer)
    {
        ArgumentNullException.ThrowIfNull(node);

        writer.Write((int)ComponentType.Script);
        var name = (node.Element(nameof(Script.Name))?.Value) ?? throw new InvalidDataException("Invalid script name");
        var nameBytes = Encoding.UTF8.GetBytes(name);
        writer.Write(nameBytes.Length);
        writer.Write(nameBytes);
    }

    // Output format:
    // struct{
    //   u32    component_type;
    //   u32    geometry_asset_guid_index
    //   u32    material_count;
    //   struct{
    //     u32      material_guid_index
    //     u32      input_count
    //     u32[]    input_guid_indices
    //     u8[4]    base_color
    //     u8[3]    emissive_color
    //     u8       metallic
    //     u8       roughness
    //     u8       input_mask
    //     u16      emissive_intensity
    //   } materials[geometry_submesh_count]
    // }
    private static void GeometryToBinary(XElement node, BinaryWriter writer, string contentPath)
    {
        ArgumentNullException.ThrowIfNull(node);

        writer.Write((int)ComponentType.Geometry);

        var geometryGuid = node.Element(nameof(Content.Geometry))?.Value ?? string.Empty;
        if (!Guid.TryParse(geometryGuid, out var guid) || guid == Guid.Empty) throw new InvalidDataException("Invalid geometry asset GUID");

        writer.Write(Indexer.SaveAssetData(contentPath, guid, (info) => new Content.Geometry(info)));

        var materials = node.Element("Materials")?.Elements(nameof(AppliedMaterial)) ?? [];
        writer.Write(materials.Count());

        foreach (var material in materials)
        {
            var materialGuid = material.Element("Material")?.Value ?? string.Empty;
            if (!Guid.TryParse(materialGuid, out var mtlGuid) || mtlGuid == Guid.Empty) throw new InvalidDataException("Invalid material asset GUID");

            var mtlIndex = Indexer.SaveMaterialData(AssetRegistry.GetAssetInfo(mtlGuid), contentPath);
            writer.Write(mtlIndex);

            var inputs = material.Element(nameof(AppliedMaterial.Inputs))?.Elements("guid") ?? [];
            writer.Write(inputs.Count());
            var inputIndex = 0;
            var inputMask = 0;

            foreach (var input in inputs)
            {
                var inputGuid = input.Value ?? string.Empty;
                if (!Guid.TryParse(inputGuid, out var inputAssetGuid) || inputAssetGuid == Guid.Empty) throw new InvalidDataException("Invalid texture asset GUID");
                writer.Write(Indexer.SaveAssetData(contentPath, inputAssetGuid, (info) => new Texture(info)));

                if (inputAssetGuid != Texture.Default.Guid && inputIndex < 8)
                {
                    inputMask |= (1 << inputIndex);
                }
                ++inputIndex;
            }

            var mtlSurface = material.Element(nameof(MaterialSurface));

            var (r, g, b, a) = ParseColor(mtlSurface?.Element(nameof(MaterialSurface.BaseColor)));
            writer.Write(r); writer.Write(g); writer.Write(b); writer.Write(a);
            (r, g, b, _) = ParseColor(mtlSurface?.Element(nameof(MaterialSurface.EmissiveColor)));
            writer.Write(r); writer.Write(g); writer.Write(b);
            writer.Write((byte)(255 * ParseFloat(mtlSurface?.Element(nameof(MaterialSurface.Metallic)))));
            writer.Write((byte)(255 * ParseFloat(mtlSurface?.Element(nameof(MaterialSurface.Roughness)))));
            writer.Write((byte)(inputMask & 0xff));
            writer.Write((ushort)(ParseFloat(mtlSurface?.Element(nameof(MaterialSurface.EmissiveIntensity))) * (65535f / Material.MaxEmissiveIntensity)));
        }
    }

    private static void SaveEntitySpecificData(string contentPath, EntityType entityType, XElement entity, BinaryWriter writer)
    {

        switch (entityType)
        {
            case EntityType.GameEntity: return;
            case EntityType.Light:
                {
                    // Type
                    var typeName = entity.Element(nameof(Light.Type))?.Value;
                    if (!Enum.TryParse(typeof(LightType), typeName, true, out var lightType)) throw new InvalidDataException("Invalid light type");
                    writer.Write((int)lightType);

                    // Light-set key
                    var lightSetKey = entity.Element(nameof(Light.LightSetKey))?.Value ?? LightSet.DefaultKey;
                    var keyBytes = Encoding.UTF8.GetBytes(lightSetKey);
                    writer.Write(keyBytes.Length);
                    writer.Write(keyBytes);

                    // Intensity
                    writer.Write(ParseFloat(entity.Element(nameof(Light.Intensity))));

                    // Color
                    var (r, g, b, _) = ParseColor(entity.Element(nameof(Light.Color)));
                    writer.Write((int)r); writer.Write((int)g); writer.Write((int)b);

                    // IsEnabled
                    writer.Write(bool.Parse(entity.Element(nameof(Light.IsEnabled))?.Value ?? bool.TrueString) ? 1 : 0);

                    // Diffuse, Specular, BRDF Lut asset indices
                    var envMapGuid = entity.Element(nameof(AmbientLight.EnvMap))?.Value ?? string.Empty;
                    if ((!Guid.TryParse(envMapGuid, out var guid) || guid == Guid.Empty) && (LightType)lightType == LightType.Ambient) throw new InvalidDataException("Invalid EnvMap GUID");
                    var (diffuseIndex, specularIndex, brdfLutIndex) = guid != Guid.Empty ? Indexer.SaveIBLTextureData(contentPath, guid) : (ID.INVALID_ID, ID.INVALID_ID, ID.INVALID_ID);
                    writer.Write(diffuseIndex); writer.Write(specularIndex); writer.Write(brdfLutIndex);

                    // Range
                    writer.Write(ParseFloat(entity.Element(nameof(PointLight.Range))));

                    // Attenuation
                    var (x, y, z) = ParseVector3(entity.Element(nameof(PointLight.Attenuation)));
                    writer.Write(x); writer.Write(y); writer.Write(z);

                    // Umbra
                    writer.Write(ParseFloat(entity.Element(nameof(Spotlight.Umbra))));

                    // Penumbra
                    writer.Write(ParseFloat(entity.Element(nameof(Spotlight.Penumbra))));
                }
                break;
            case EntityType.Camera:
                {
                    // Type
                    var typeName = entity.Element(nameof(Camera.Type))?.Value;
                    if (!Enum.TryParse(typeof(CameraType), typeName, true, out var cameraType)) throw new InvalidDataException("Invalid camera type");
                    writer.Write((int)cameraType);

                    // Field of View, or Orthographic size
                    if ((CameraType)cameraType == CameraType.Perspective)
                    {
                        writer.Write(ParseFloat(entity.Element(nameof(PerspectiveCamera.FieldOfView))));
                    }
                    else if ((CameraType)cameraType == CameraType.Orthographic)
                    {
                        writer.Write(ParseFloat(entity.Element(nameof(OrthographicCamera.OrthographicSize))));
                    }
                    else
                    {
                        throw new InvalidDataException($"Invalid camera type: {cameraType}");
                    }

                    // Near Z
                    writer.Write(ParseFloat(entity.Element(nameof(Camera.NearZ))));

                    // Far Z
                    writer.Write(ParseFloat(entity.Element(nameof(Camera.FarZ))));

                }
                break;
            default:
                break;
        }
    }

    /// <summary>
    /// Removes XML namespaces from the provided <see cref="XElement"/> and its descendants.
    /// Returns a new <see cref="XElement"/> that uses local element and attribute names
    /// (namespace declarations are dropped) and preserves element values and non-namespace attributes.
    /// </summary>
    private static XElement RemoveAllNamespaces(XElement element)
    => new(element.Name.LocalName, element.HasElements ? element.Elements().Select(RemoveAllNamespaces) : element.Value,
        element.Attributes().Where(a => !a.IsNamespaceDeclaration).Select(a => new XAttribute(a.Name.LocalName, a.Value)));

    private static EntityType GetEntityType(XElement entity)
    {
        var typeName = entity.Attribute("type")?.Value?.Split(':').Last();
        if (string.IsNullOrEmpty(typeName))
        {
            return _entityTypeMapping[nameof(GameEntity)];
        }
        else if (_entityTypeMapping.TryGetValue(typeName, out var type))
        {
            return type;
        }

        throw new ArgumentException("Invalid entity type");
    }

    // NOTE: we want to serialize data from all scenes, not only the current active scene.
    //       Therefore, we need to parse the XML content of the game project file.
    private static void ProjectToBinary(Project project, string outputPath, string contentPath)
    {
        var doc = XDocument.Load($"{project.Path}{project.Name}{Project.Extension}");
        var game = RemoveAllNamespaces(doc.Root);

        var scenes = game.Element(nameof(Project.Scenes))?.Elements(nameof(Scene)) ?? [];
        var sceneData = new List<(int IsActive, List<int> AssetIndices, byte[] Data)>();

        foreach (var scene in scenes)
        {
            var oldAssetList = Indexer.GetUsedAssets();
            using var writer = new BinaryWriter(new MemoryStream());
            var isActive = bool.Parse(scene.Element(nameof(Scene.IsActive))?.Value ?? bool.FalseString) ? 1 : 0;

            var entities = scene.Element(nameof(Scene.GameEntities))?.Elements(nameof(GameEntity)) ?? [];
            writer.Write(entities.Count());

            foreach (var entity in entities)
            {
                var entityType = GetEntityType(entity);
                writer.Write((int)entityType);

                var components = entity.Element(nameof(GameEntity.Components))?.Elements(nameof(Component)) ?? [];
                Debug.Assert(components.Any());
                writer.Write(components.Count());

                foreach (var component in components)
                {
                    var componentType = component.Attribute("type")?.Value.Split(':').Last();

                    switch (componentType)
                    {
                        case nameof(Transform): TransformToBinary(component, writer); break;
                        case nameof(Script): ScriptToBinary(component, writer); break;
                        case nameof(Components.Geometry): GeometryToBinary(component, writer, contentPath); break;
                        default:
                            throw new InvalidDataException("Invalid component type.");
                    }
                }

                SaveEntitySpecificData(contentPath, entityType, entity, writer);
            }

            var newAssetList = Indexer.GetUsedAssets();
            var indices = newAssetList.Except(oldAssetList).Select(x => newAssetList.IndexOf(x)).ToList();

            writer.Flush();
            sceneData.Add((isActive, indices, (writer.BaseStream as MemoryStream).ToArray()));
        }

        var bin = $"{outputPath}game.bin";
        using var bw = new BinaryWriter(File.Open(bin, FileMode.Create, FileAccess.Write));

        Indexer.WriteGuidsArray(bw);

        bw.Write(sceneData.Count);

        foreach (var (isActive, assetIndices, data) in sceneData)
        {
            bw.Write(data.Length + assetIndices.Count * sizeof(int));
            bw.Write(isActive);
            bw.Write(assetIndices.Count);
            assetIndices.ForEach(bw.Write);
            bw.Write(data);
        }
    }

    private static void SaveProjectData()
    {
        var project = Project.Current;
        var configName = VisualStudio.GetConfigurationName(project.StandAloneBuildConfig);
        var outputPath = Path.Combine(project.Path, "x64", $@"{configName}\");
        var contentPath = Path.Combine(outputPath, @"Content\");
        Debug.Assert(Directory.Exists(outputPath));
        Directory.CreateDirectory(contentPath);
        ProjectToBinary(project, outputPath, contentPath);
        Indexer.DeleteUnused(contentPath);
    }

    public static void SaveForDryRun()
    {
        Logger.Log(MessageType.Info, "Preparing the project for launch...");
        Indexer.Reset();
        SaveProjectData();
    }
}
