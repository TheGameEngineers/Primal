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
using System.Linq;
using System.Numerics;
using System.Runtime.Serialization;
using System.Windows.Media;

namespace PrimalEditor.Components;

enum LightType
{
    Directional,
    Point,
    Spot,
    Ambient,
}

static class LightSet
{
    private static readonly Dictionary<string, ulong> _lightSetKeyMap = [];
    private static readonly ObservableCollection<string> _lightSets = [];
    public static ReadOnlyObservableCollection<string> LightSets { get; } = new(_lightSets);
    public static string DefaultKey => "Default";
    public static ulong InvalidKey => 0;

    private static UploadedAsset _uploadedBrdfLut;
    public static IdType BrdfLutId => _uploadedBrdfLut?.ContentId ?? ID.INVALID_ID;

    public static ulong AddLightSet(string name, bool canBeSelected)
    {
        Debug.Assert(!string.IsNullOrWhiteSpace(name));
        if (string.IsNullOrWhiteSpace(name))
        {
            name = DefaultKey;
        }

        if (_lightSetKeyMap.TryGetValue(name, out var key))
        {
            return key;
        }

        _uploadedBrdfLut ??= UploadedAsset.AddToScene(DefaultAssets.BrdfIntegrationLut);

        Debug.Assert(!_lightSets.Contains(name));
        if (canBeSelected)
        {
            _lightSets.Add(name);
        }

        _lightSetKeyMap[name] = key = EngineAPI.CreateLightSet(name);
        return key;
    }

    public static ulong GetKey(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
        {
            name = DefaultKey;
        }

        if (_lightSetKeyMap.TryGetValue(name, out var key))
        {
            return key;
        }

        return InvalidKey;
    }

    public static void Reset()
    {
        if (_uploadedBrdfLut != null)
        {
            UploadedAsset.RemoveFromScene(_uploadedBrdfLut);
            _uploadedBrdfLut = null;
        }

        foreach (var key in _lightSetKeyMap.Values)
        {
            EngineAPI.RemoveLightSet(key);
        }
        _lightSetKeyMap.Clear();
        _lightSets.Clear();
    }
}

[DataContract]
abstract class Light(Scene scene, LightType type, string lightSetKey) : GameEntity(scene)
{
    [DataMember]
    public LightType Type { get; init; } = type;

    public IdType LightId { get; private set; } = ID.INVALID_ID;

    [DataMember]
    public string LightSetKey
    {
        get;
        set
        {
            if (field != value)
            {
                var oldKey = field;
                field = value;
                if (ID.IsValid(LightId))
                {
                    EngineAPI.RemoveLight(LightId, LightSet.GetKey(oldKey));
                    LightId = EngineAPI.CreateLight(this);
                    Debug.Assert(ID.IsValid(LightId));
                }
                OnPropertyChanged(nameof(LightSetKey));
            }
        }
    } = lightSetKey;

    public override bool IsActive
    {
        get => base.IsActive;
        set
        {
            if (base.IsActive != value)
            {
                if (value)
                {
                    base.IsActive = value;
                    Debug.Assert(!ID.IsValid(LightId) && ID.IsValid(EntityId));
                    LightId = EngineAPI.CreateLight(this);
                    Debug.Assert(ID.IsValid(LightId));
                }
                else if (ID.IsValid(LightId))
                {
                    EngineAPI.RemoveLight(LightId, LightSet.GetKey(LightSetKey));
                    LightId = ID.INVALID_ID;
                    base.IsActive = value;
                }
            }
        }
    }

    [DataMember]
    public float Intensity
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(Intensity));
            }
        }
    } = 1f;

    [DataMember]
    public Color Color
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Color));
            }
        }
    } = Colors.White;

    protected void Renew()
    {
        if (ID.IsValid(LightId))
        {
            EngineAPI.RemoveLight(LightId, LightSet.GetKey(LightSetKey));
            LightId = EngineAPI.CreateLight(this);
            Debug.Assert(ID.IsValid(LightId));
        }
    }

    [OnDeserializing]
    private void OnDeserializing(StreamingContext context)
    {
        LightId = ID.INVALID_ID;
    }
}

[DataContract]
class DirectionalLight(Scene scene, string lightSetKey) : Light(scene, LightType.Directional, lightSetKey) { }

[DataContract]
class PointLight : Light
{
    [DataMember]
    public Vector3 Attenuation
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Attenuation));
            }
        }
    } = new(1f, 0f, 0f);

    [DataMember]
    public float Range
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value) && value > 0f)
            {
                field = value;
                OnPropertyChanged(nameof(Range));
            }
        }
    } = 1f;

    public PointLight(Scene scene, string lightSetKey) : base(scene, LightType.Point, lightSetKey) { }

    // Just so that Spotlight can inherit from this class (see below).
    protected PointLight(Scene scene, LightType type, string lightSetKey) : base(scene, type, lightSetKey) { }
}

[DataContract]
class Spotlight(Scene scene, string lightSetKey) : PointLight(scene, LightType.Spot, lightSetKey)
{
    [DataMember]
    public float Umbra
    {
        get;
        set
        {
            value = float.Clamp(value, 0f, Penumbra);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(Umbra));
            }
        }
    } = 30f;

    [DataMember]
    public float Penumbra
    {
        get;
        set
        {
            value = float.Clamp(value, Umbra, 180f);
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(Penumbra));
            }
        }
    } = 60f;
}

[DataContract]
class AmbientLight(Scene scene, string lightSetKey) : Light(scene, LightType.Ambient, lightSetKey)
{
    [DataMember(Name = "EnvMap")]
    private Guid _envMapGuid;

    private UploadedAsset _uploadedSpecularIBL;
    private UploadedAsset _uploadedDiffuseIBL;

    public AssetInfo EnvMap
    {
        get;
        private set
        {
            if (value != null && field?.Guid != value.Guid)
            {
                field = value;
                OnPropertyChanged(nameof(EnvMap));
            }
        }
    } = Texture.DefaultSpecularIBL;

    public IdType SpecularContentId => _uploadedSpecularIBL?.ContentId ?? ID.INVALID_ID;
    public IdType DiffuseContentId => _uploadedDiffuseIBL?.ContentId ?? ID.INVALID_ID;
    public IdType BrdfLutContentId => LightSet.BrdfLutId;

    public override bool IsActive
    {
        get => base.IsActive;
        set
        {
            if (base.IsActive == value)
            {
                return;
            }

            if (value && !ID.IsValid(_uploadedSpecularIBL?.ContentId ?? ID.INVALID_ID))
            {
                UploadEnvMap();
            }
            else if (!value)
            {
                UnloadEnvMap();
            }

            base.IsActive = value;
        }
    }

    private bool UploadEnvMap()
    {
        Debug.Assert(EnvMap != null && EnvMap.Guid != Guid.Empty && EnvMap.Type == AssetType.Texture);
        // TODO: implement asset flags for AssetInfo so we can determine if a texture is a cube map without having to read it.
        var texture = new Texture(EnvMap);
        if (texture.IsPrefilteredIBL)
        {
            Debug.Assert(texture.IBLPair != null);
            var ibl1Info = EnvMap;
            var ibl2Info = texture.IBLPair.GetAssetInfo();
            var ibl1 = UploadedAsset.AddToScene(ibl1Info, texture);
            var ibl2 = UploadedAsset.AddToScene(ibl2Info, texture.IBLPair);
            _uploadedSpecularIBL = texture.IsSpecularIBL ? ibl1 : ibl2;
            _uploadedDiffuseIBL = texture.IsDiffuseIBL ? ibl1 : ibl2;
            //Use the asset info of specular component, because it has better icon.
            if (texture.IsDiffuseIBL) EnvMap = ibl2Info;
            Debug.Assert(_uploadedDiffuseIBL != _uploadedSpecularIBL);

            return true;
        }

        Logger.Log(MessageType.Warning, $"Texture {EnvMap.FileName} is not an environment map and cannot be used for the ambient light.");
        return false;
    }

    private void UnloadEnvMap()
    {
        if (_uploadedSpecularIBL != null && _uploadedDiffuseIBL != null)
        {
            UploadedAsset.RemoveFromScene(_uploadedSpecularIBL);
            UploadedAsset.RemoveFromScene(_uploadedDiffuseIBL);
            _uploadedSpecularIBL = null;
            _uploadedDiffuseIBL = null;
        }
    }

    public bool SetEnvMap(AssetInfo asset)
    {
        Debug.Assert(asset != null);
        if (asset.Guid != EnvMap.Guid && asset?.Type == AssetType.Texture)
        {
            var oldEnvMap = EnvMap;
            var oldUploadedSpecularIBL = _uploadedSpecularIBL;
            var oldUploadedDiffuseIBL = _uploadedDiffuseIBL;
            EnvMap = asset;

            if (!UploadEnvMap())
            {
                EnvMap = oldEnvMap;
                _uploadedSpecularIBL = oldUploadedSpecularIBL;
                _uploadedDiffuseIBL = oldUploadedDiffuseIBL;
                return false;
            }

            Renew();

            if (oldUploadedSpecularIBL != null && oldUploadedDiffuseIBL != null)
            {
                UploadedAsset.RemoveFromScene(oldUploadedSpecularIBL);
                UploadedAsset.RemoveFromScene(oldUploadedDiffuseIBL);
            }

            return true;
        }

        return false;
    }

    [OnSerializing]
    private void OnSerializing(StreamingContext context)
    {
        _envMapGuid = EnvMap.Guid;
    }

    [OnDeserialized]
    private void OnDeserialized(StreamingContext context)
    {
        Debug.Assert(_envMapGuid != Guid.Empty);
        var assetInfo = AssetRegistry.GetAssetInfo(_envMapGuid);
        if (assetInfo != null)
        {
            assetInfo = Texture.DefaultSpecularIBL;
            Logger.Log(MessageType.Warning, $"Environment map texture asset with GUID {_envMapGuid} not found in the asset registry. Using default environment map.");
        }
        Debug.Assert(assetInfo != null && assetInfo.Type == AssetType.Texture);
        EnvMap = assetInfo;
    }
}

abstract class MSLight<T> : MSEntity where T : Light
{
    public List<T> SelectedLights { get; }

    public string LightSetKey
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(LightSetKey));
            }
        }
    }

    public float? Intensity
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(Intensity));
            }
        }
    }

    public float? ColorR
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(ColorR));
            }
        }
    }

    public float? ColorG
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(ColorG));
            }
        }
    }

    public float? ColorB
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(ColorB));
            }
        }
    }

    protected override bool UpdateGameEntities(string propertyName)
    {
        var count = SelectedLights.Count;
        var ids = SelectedLights.Select(x => x.LightId).ToArray();
        var lightSetKeys = SelectedLights.Select(x => LightSet.GetKey(x.LightSetKey)).ToArray();
        var index = 0;

        //NOTE: IsEnabled is handled in GameEntityView because of heterogenous selections.
        //      See note in GameEntityView.SetIsEnabled()
        switch (propertyName)
        {
            case nameof(LightSetKey):
                {
                    SelectedLights.ForEach(x => x.LightSetKey = LightSetKey);
                    return true;
                }
            case nameof(Intensity):
                {
                    var intensity = new float[count];
                    SelectedLights.ForEach(x =>
                    {
                        x.Intensity = Intensity.Value;
                        intensity[index] = x.Intensity; ++index;
                    });
                    EngineAPI.SetLightIntensity(ids, lightSetKeys, intensity, count);
                    return true;
                }
            case nameof(ColorR):
            case nameof(ColorG):
            case nameof(ColorB):
                {
                    var r = new float[count]; var g = new float[count]; var b = new float[count];
                    SelectedLights.ForEach(x =>
                    {
                        var color = x.Color;
                        color.ScR = ColorR ?? color.ScR;
                        color.ScG = ColorG ?? color.ScG;
                        color.ScB = ColorB ?? color.ScB;
                        x.Color = color;

                        r[index] = color.ScR; g[index] = color.ScG; b[index] = color.ScB; ++index;
                    });
                    EngineAPI.SetLightColor(ids, lightSetKeys, r, g, b, count);
                    return true;
                }
        }

        return base.UpdateGameEntities(propertyName);
    }

    protected override bool UpdateMSGameEntity()
    {
        if (SelectedLights.Count == 1)
        {
            var light = SelectedLights[0];
            LightSetKey = light.LightSetKey;
            Intensity = light.Intensity;
            ColorR = light.Color.ScR;
            ColorG = light.Color.ScG;
            ColorB = light.Color.ScB;
        }
        else
        {
            LightSetKey = GetMixedValue(SelectedLights, new Func<Light, string>(x => x.LightSetKey));
            Intensity = GetMixedValue(SelectedLights, new Func<Light, float>(x => x.Intensity));
            ColorR = GetMixedValue(SelectedLights, new Func<Light, float>(x => x.Color.ScR));
            ColorG = GetMixedValue(SelectedLights, new Func<Light, float>(x => x.Color.ScG));
            ColorB = GetMixedValue(SelectedLights, new Func<Light, float>(x => x.Color.ScB));
        }

        return base.UpdateMSGameEntity();
    }

    protected MSLight(List<GameEntity> entities) : base(entities)
    {
        SelectedLights = [.. entities.OfType<T>()];
        Refresh();
    }
}

class MSLight(List<GameEntity> entities) : MSLight<Light>(entities) { }

class MSDirectionalLight(List<GameEntity> entities) : MSLight(entities) { }

abstract class MSPointLight<T>(List<GameEntity> entities) : MSLight<T>(entities) where T : PointLight
{
    public float? Range
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(Range));
            }
        }
    }

    public float? AttenuationA
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(AttenuationA));
            }
        }
    }

    public float? AttenuationB
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(AttenuationB));
            }
        }
    }

    public float? AttenuationC
    {
        get;
        set
        {
            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(AttenuationC));
            }
        }
    }

    protected override bool UpdateGameEntities(string propertyName)
    {
        var count = SelectedLights.Count;
        var ids = SelectedLights.Select(x => x.LightId).ToArray();
        var lightSetKeys = SelectedLights.Select(x => LightSet.GetKey(x.LightSetKey)).ToArray();
        var index = 0;

        switch (propertyName)
        {
            case nameof(Range):
                {
                    var range = new float[count];
                    SelectedLights.ForEach(x =>
                    {
                        x.Range = Range.Value;
                        range[index] = x.Range; ++index;
                    });
                    EngineAPI.SetLightRange(ids, lightSetKeys, range, count);
                    return true;
                }
            case nameof(AttenuationA):
            case nameof(AttenuationB):
            case nameof(AttenuationC):
                {
                    var a = new float[count]; var b = new float[count]; var c = new float[count];
                    SelectedLights.ForEach(x =>
                    {
                        var attenuation = x.Attenuation;
                        attenuation.X = AttenuationA ?? attenuation.X;
                        attenuation.Y = AttenuationB ?? attenuation.Y;
                        attenuation.Z = AttenuationC ?? attenuation.Z;
                        x.Attenuation = attenuation;

                        a[index] = attenuation.X; b[index] = attenuation.Y; c[index] = attenuation.Z; ++index;
                    });
                    EngineAPI.SetLightAttenuation(ids, lightSetKeys, a, b, c, count);
                    return true;
                }
        }

        return base.UpdateGameEntities(propertyName);
    }

    protected override bool UpdateMSGameEntity()
    {
        if (SelectedLights.Count == 1)
        {
            var light = SelectedLights[0];
            Range = light.Range;
            AttenuationA = light.Attenuation.X;
            AttenuationB = light.Attenuation.Y;
            AttenuationC = light.Attenuation.Z;
        }
        else
        {
            Range = GetMixedValue(SelectedLights, new Func<PointLight, float>(x => x.Range));
            AttenuationA = GetMixedValue(SelectedLights, new Func<PointLight, float>(x => x.Attenuation.X));
            AttenuationB = GetMixedValue(SelectedLights, new Func<PointLight, float>(x => x.Attenuation.Y));
            AttenuationC = GetMixedValue(SelectedLights, new Func<PointLight, float>(x => x.Attenuation.Z));
        }

        return base.UpdateMSGameEntity();
    }
}

class MSPointLight(List<GameEntity> entities) : MSPointLight<PointLight>(entities) { }

class MSSpotlight(List<GameEntity> entities) : MSPointLight<Spotlight>(entities)
{
    public float? Umbra
    {
        get;
        set
        {
            if (value.HasValue)
            {
                value = float.Clamp(value.Value, 0f, Penumbra ?? 180f);
            }

            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(Umbra));
            }
        }
    }

    public float? Penumbra
    {
        get;
        set
        {
            if (value.HasValue)
            {
                value = float.Clamp(value.Value, Umbra ?? 0f, 180f);
            }

            if (!field.IsTheSameAs(value))
            {
                field = value;
                OnPropertyChanged(nameof(Penumbra));
            }
        }
    }

    protected override bool UpdateGameEntities(string propertyName)
    {
        var count = SelectedLights.Count;
        var ids = SelectedLights.Select(x => x.LightId).ToArray();
        var lightSetKeys = SelectedLights.Select(x => LightSet.GetKey(x.LightSetKey)).ToArray();
        var index = 0;

        switch (propertyName)
        {
            case nameof(Umbra):
                {
                    var umbra = new float[count]; var penumbra = new float[count];
                    SelectedLights.ForEach(x =>
                    {
                        x.Umbra = Umbra.Value;
                        umbra[index] = x.Umbra; penumbra[index] = x.Penumbra; ++index;
                    });
                    EngineAPI.SetLightConeAngles(ids, lightSetKeys, umbra, penumbra, count);
                    return true;
                }
            case nameof(Penumbra):
                {
                    var umbra = new float[count]; var penumbra = new float[count];
                    SelectedLights.ForEach(x =>
                    {
                        x.Penumbra = Penumbra.Value;
                        umbra[index] = x.Umbra; penumbra[index] = x.Penumbra; ++index;
                    });
                    EngineAPI.SetLightConeAngles(ids, lightSetKeys, umbra, penumbra, count);
                    return true;
                }
        }

        return base.UpdateGameEntities(propertyName);
    }

    protected override bool UpdateMSGameEntity()
    {
        if (SelectedLights.Count == 1)
        {
            var light = SelectedLights[0];
            Umbra = light.Umbra;
            Penumbra = light.Penumbra;
        }
        else
        {
            Umbra = GetMixedValue(SelectedLights, new Func<Spotlight, float>(x => x.Umbra));
            Penumbra = GetMixedValue(SelectedLights, new Func<Spotlight, float>(x => x.Penumbra));
        }

        return base.UpdateMSGameEntity();
    }
}

class MSAmbientLight(List<GameEntity> entities) : MSLight<AmbientLight>(entities)
{
    // NOTE: there can only be one ambient light in a scene.
    public AssetInfo EnvMap
    {
        get => SelectedLights[0].EnvMap;
        set
        {
            SelectedLights[0].SetEnvMap(value);
            OnPropertyChanged(nameof(EnvMap));
        }
    }

    protected override bool UpdateMSGameEntity()
    {
        OnPropertyChanged(nameof(EnvMap));
        return base.UpdateMSGameEntity();
    }
}