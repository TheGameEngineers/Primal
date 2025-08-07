// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.DllWrappers;
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.Serialization;
using System.Threading;

namespace PrimalEditor.Content;

enum AssetType
{
    Unknown,
    Animation,
    Audio,
    Material,
    Mesh,
    Skeleton,
    Texture,
}

interface IAssetImportSettings
{
    void ToBinary(BinaryWriter writer);
    void FromBinary(BinaryReader reader);

    static void CopyImportSettings(IAssetImportSettings fromSettings, IAssetImportSettings toSettings)
    {
        if (fromSettings == null || toSettings == null)
        {
            throw new ArgumentNullException("Arguments should not be null.");
        }
        else if (fromSettings.GetType() != toSettings.GetType())
        {
            throw new ArgumentException("Arguments should be of the same type.");
        }

        using BinaryWriter writer = new(new MemoryStream());
        fromSettings.ToBinary(writer);
        writer.Flush();
        var bytes = (writer.BaseStream as MemoryStream).ToArray();

        using BinaryReader reader = new(new MemoryStream(bytes));
        toSettings.FromBinary(reader);
    }
}

sealed class AssetInfo
{
    public AssetType Type { get; set; }
    public byte[] Icon { get; set; }
    public string FullPath { get; set; }
    public string FileName => Path.GetFileNameWithoutExtension(FullPath);
    public DateTime RegisterTime { get; set; }
    public DateTime ImportDate { get; set; }
    public Guid Guid { get; set; }
    public byte[] Hash { get; set; }
}

abstract class AssetMetadata { }

[DataContract]
abstract class Asset : ViewModelBase
{
    public static string AssetFileExtension => ".asset";
    [DataMember]
    public AssetType Type { get; private set; }
    public byte[] Icon { get; protected set; }

    private string _fullPath;
    public string FullPath
    {
        get => _fullPath;
        set
        {
            if (_fullPath != value)
            {
                _fullPath = value;
                OnPropertyChanged(nameof(FullPath));
                OnPropertyChanged(nameof(FileName));
            }
        }
    }

    public string FileName => Path.GetFileNameWithoutExtension(FullPath);
    [DataMember]
    public Guid Guid { get; protected set; } = Guid.NewGuid();
    public DateTime ImportDate { get; protected set; }
    public byte[] Hash { get; protected set; }

    public abstract AssetMetadata GetMetadata();
    public abstract bool Import(string file);
    public abstract bool Load(string file);
    public abstract IEnumerable<string> Save(string file);
    public abstract byte[] PackForEngine();
    public virtual List<AssetInfo> GetReferencedAssets() => [];

    public AssetInfo GetAssetInfo()
        => new()
        {
            Type = Type,
            Icon = Icon,
            FullPath = FullPath,
            RegisterTime = AssetRegistry.GetAssetInfo(Guid)?.RegisterTime ?? default,
            ImportDate = ImportDate,
            Guid = Guid,
            Hash = Hash,
        };

    private static AssetInfo GetAssetInfo(BinaryReader reader)
    {
        reader.BaseStream.Position = 0;
        var info = new AssetInfo();

        info.Type = (AssetType)reader.ReadInt32();
        var idSize = reader.ReadInt32();
        info.Guid = new Guid(reader.ReadBytes(idSize));
        info.ImportDate = DateTime.FromBinary(reader.ReadInt64());
        var hashSize = reader.ReadInt32();
        if (hashSize > 0)
        {
            info.Hash = reader.ReadBytes(hashSize);
        }
        var iconSize = reader.ReadInt32();
        info.Icon = reader.ReadBytes(iconSize);

        return info;
    }

    public static AssetInfo TryGetAssetInfo(string file) => AssetRegistry.GetAssetInfo(file) ?? GetAssetInfo(file);

    public static AssetInfo GetAssetInfo(string file)
    {
        if (!File.Exists(file) || Path.GetExtension(file) != AssetFileExtension) return null;
        try
        {
            using var reader = new BinaryReader(File.Open(file, FileMode.Open, FileAccess.Read));
            var info = GetAssetInfo(reader);
            info.FullPath = file;
            return info;
        }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }

        return null;
    }

    protected void WriteAssetFileHeader(BinaryWriter writer)
    {
        var id = Guid.ToByteArray();
        var importDate = DateTime.Now.ToBinary();

        writer.BaseStream.Position = 0;

        writer.Write((int)Type);
        writer.Write(id.Length);
        writer.Write(id);
        writer.Write(importDate);
        // asset hash is optional
        if (Hash?.Length > 0)
        {
            writer.Write(Hash.Length);
            writer.Write(Hash);
        }
        else
        {
            writer.Write(0);
        }

        writer.Write(Icon.Length);
        writer.Write(Icon);
    }

    protected void ReadAssetFileHeader(BinaryReader reader)
    {
        var info = GetAssetInfo(reader);

        Debug.Assert(Type == info.Type);
        Guid = info.Guid;
        ImportDate = info.ImportDate;
        Hash = info.Hash;
        Icon = info.Icon;
    }

    protected Asset(AssetType type)
    {
        Debug.Assert(type != AssetType.Unknown);
        Type = type;
    }
}

class UploadedAsset
{
    public IdType ContentId { get; private set; } = ID.INVALID_ID;
    public int ReferenceCount { get; private set; }
    public AssetInfo AssetInfo { get; private set; }
    public AssetMetadata Metadata { get; private set; }
    private List<UploadedAsset> _referencedAssets = [];

    private static readonly Lock _lock = new();
    // NOTE: use a lock to synchronize access to _uploadedAssets across multiple threads.
    private static readonly Dictionary<Guid, UploadedAsset> _uploadedAssets = [];

    private static UploadedAsset UploadAssetToEngine(AssetInfo assetInfo, Asset asset = null)
    {
        Debug.Assert(assetInfo != null);

        asset ??= assetInfo.Type switch
        {
            AssetType.Animation => null,
            AssetType.Audio => null,
            AssetType.Material => null,
            AssetType.Mesh => new Geometry(assetInfo),
            AssetType.Skeleton => null,
            AssetType.Texture => new Texture(assetInfo),
            _ => null
        };

        Debug.Assert(asset != null);

        if (asset != null)
        {
            Debug.Assert(asset.Guid == assetInfo.Guid);
            var referencedAssets = new List<UploadedAsset>();
            asset.GetReferencedAssets().ForEach(x => referencedAssets.Add(AddToScene(x)));
            var data = asset.PackForEngine();

            if (data?.Length > 0)
            {
                return new()
                {
                    AssetInfo = assetInfo,
                    Metadata = asset.GetMetadata(),
                    ContentId = EngineAPI.CreateResource(data, assetInfo.Type),
                    ReferenceCount = 1,
                    _referencedAssets = referencedAssets
                };
            }
        }

        return null;
    }

    private static void UnloadAssetFromEngine(UploadedAsset uploadedAsset)
    {
        Debug.Assert(uploadedAsset?.AssetInfo != null && ID.IsValid(uploadedAsset.ContentId));
        EngineAPI.DestroyResource(uploadedAsset.ContentId, (int)uploadedAsset.AssetInfo.Type);
    }

    public static UploadedAsset AddToScene(AssetInfo assetInfo, Asset asset = null)
    {
        Debug.Assert(assetInfo != null && assetInfo.Guid != Guid.Empty);
        var key = assetInfo.Guid;

        lock (_lock)
        {
            if (_uploadedAssets.TryGetValue(key, out var value))
            {
                ++value.ReferenceCount;
                value._referencedAssets.ForEach(x => ++x.ReferenceCount);
            }
            else
            {
                var uploadedAsset = UploadAssetToEngine(assetInfo, asset);
                Debug.Assert(ID.IsValid(uploadedAsset.ContentId));

                if (ID.IsValid(uploadedAsset.ContentId))
                {
                    _uploadedAssets[key] = uploadedAsset;
                }
                else
                {
                    Logger.Log(MessageType.Error, $"Failed to upload asset {assetInfo.FileName} to engine.");
                    return null;
                }
            }

            Debug.Assert(_uploadedAssets.ContainsKey(key));
            return _uploadedAssets[key];
        }
    }

    public static void RemoveFromScene(UploadedAsset uploadedAsset)
    {
        lock (_lock)
        {
            Debug.Assert(uploadedAsset != null && _uploadedAssets.ContainsKey(uploadedAsset.AssetInfo.Guid));

            uploadedAsset._referencedAssets.ForEach(RemoveFromScene);
            --uploadedAsset.ReferenceCount;
            if (uploadedAsset.ReferenceCount == 0)
            {
                UnloadAssetFromEngine(uploadedAsset);
                _uploadedAssets.Remove(uploadedAsset.AssetInfo.Guid);
                uploadedAsset.ContentId = ID.INVALID_ID;
            }
        }
    }

    public static IdType GetContentId(Guid id)
    {
        Debug.Assert(id != Guid.Empty);
        lock (_lock) { return _uploadedAssets.TryGetValue(id, out var uploadedAsset) ? uploadedAsset.ContentId : ID.INVALID_ID; }
    }

    // Note: can only be created by UploadedAsset.AddToScene()
    private UploadedAsset() { }
}