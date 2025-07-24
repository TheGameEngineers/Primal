// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;

namespace PrimalEditor.Content;

static class AssetRegistry
{
    private static readonly Lock _lock = new();
    private static readonly Dictionary<string, AssetInfo> _assetsFileDictionary = [];
    private static readonly Dictionary<Guid, AssetInfo> _assetsGuidDictionary = [];
    private static readonly ObservableCollection<AssetInfo> _assets = [];
    private static string _cachePath = string.Empty;

    public static ReadOnlyObservableCollection<AssetInfo> Assets { get; } = new ReadOnlyObservableCollection<AssetInfo>(_assets);

    private static void RegisterAllAssets(string path)
    {
        Debug.Assert(Directory.Exists(path));
        foreach (var entry in Directory.GetFileSystemEntries(path))
        {
            if (ContentHelper.IsDirectory(entry))
            {
                RegisterAllAssets(entry);
            }
            else
            {
                RegisterAsset(entry);
            }
        }
    }

    private static void RegisterAsset(string file, AssetInfo info = null)
    {
        Debug.Assert(File.Exists(file));
        try
        {
            var fileInfo = new FileInfo(file);
            var isNew = !_assetsFileDictionary.ContainsKey(file);

            if (isNew || _assetsFileDictionary[file].RegisterTime.IsOlder(fileInfo.LastWriteTime))
            {
                info ??= Asset.GetAssetInfo(file);
                Debug.Assert(info != null);
                info.RegisterTime = DateTime.Now;

                // Handle the case when the same asset file was imported using a different guid.
                // NOTE: not sure if that is or should be possible.
                if (!isNew && _assetsFileDictionary[file].Guid != info.Guid)
                {
                    _assetsGuidDictionary.Remove(_assetsFileDictionary[file].Guid);
                }

                _assetsFileDictionary[file] = info;
                _assetsGuidDictionary[info.Guid] = info;

                if (isNew)
                {
                    Debug.Assert(!_assets.Contains(info));
                    _assets.Add(info);
                }
                else
                {
                    var oldInfo = _assets.FirstOrDefault(x => x.FullPath == info.FullPath);
                    Debug.Assert(oldInfo != null);
                    _assets[_assets.IndexOf(oldInfo)] = info;
                }
            }
        }
        catch (Exception ex) { Debug.WriteLine(ex.Message); }
    }

    private static void UnregisterAsset(string file)
    {
        if (_assetsFileDictionary.TryGetValue(file, out var info))
        {
            _assets.Remove(info);
            _assetsFileDictionary.Remove(file);
            // NOTE: when a file's renamed, the same GUID will be registered with the new name.
            //       We don't want to remove the entry in that case.
            if (_assetsGuidDictionary.TryGetValue(info.Guid, out var value) && !File.Exists(value.FullPath))
            {
                _assetsGuidDictionary.Remove(info.Guid);
            }
        }
    }

    private static void LoadCacheFile()
    {
        if (!File.Exists(_cachePath)) return;

        try
        {
            lock (_lock)
            {
                _assetsFileDictionary.Clear();
                _assetsGuidDictionary.Clear();
                _assets.Clear();

                using var reader = new BinaryReader(File.Open(_cachePath, FileMode.Open, FileAccess.Read));
                var numEntries = reader.ReadInt32();
                for (int i = 0; i < numEntries; ++i)
                {
                    var info = new AssetInfo();

                    info.Type = (AssetType)reader.ReadInt32();
                    var iconSize = reader.ReadInt32();
                    info.Icon = reader.ReadBytes(iconSize);
                    info.FullPath = reader.ReadString();
                    info.RegisterTime = DateTime.FromBinary(reader.ReadInt64());
                    info.ImportDate = DateTime.FromBinary(reader.ReadInt64());
                    info.Guid = new(reader.ReadString());
                    var hashSize = reader.ReadInt32();
                    info.Hash = (hashSize > 0) ? reader.ReadBytes(hashSize) : null;

                    if (File.Exists(info.FullPath))
                    {
                        _assetsFileDictionary[info.FullPath] = info;
                        _assetsGuidDictionary[info.Guid] = info;
                        _assets.Add(info);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Warning, "Failed to read Asset Registry cache file.");
        }
    }

    private static void SaveCacheFile()
    {
        try
        {
            List<AssetInfo> assets = [];
            lock (_lock)
            {
                assets = [.. _assets];
            }

            using var writer = new BinaryWriter(File.Open(_cachePath, FileMode.Create, FileAccess.Write));
            writer.Write(assets.Count);
            foreach (var info in assets)
            {
                writer.Write((int)info.Type);
                writer.Write(info.Icon.Length);
                writer.Write(info.Icon);
                writer.Write(info.FullPath);
                writer.Write(info.RegisterTime.ToBinary());
                writer.Write(info.ImportDate.ToBinary());
                writer.Write(info.Guid.ToString());
                var hashSize = info.Hash?.Length ?? 0;
                writer.Write(hashSize);
                if (hashSize > 0)
                {
                    writer.Write(info.Hash);
                }
            }
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Logger.Log(MessageType.Warning, "Failed to write Asset Registry cache file.");
            File.Delete(_cachePath); // If saving fails, delete the cache file to avoid using curropted version.
        }
    }

    private static void OnContentModified(object sender, ContentModifiedEventArgs e)
    {
        lock (_lock)
        {
            if (ContentHelper.IsDirectory(e.FullPath))
            {
                RegisterAllAssets(e.FullPath);
            }
            else if (File.Exists(e.FullPath))
            {
                RegisterAsset(e.FullPath);
            }

            _assets.Where(x => !File.Exists(x.FullPath)).ToList().ForEach(x => UnregisterAsset(x.FullPath));
        }
    }

    public static void Reset(string contentFolder, string projectPath)
    {
        ContentWatcher.ContentModified -= OnContentModified;

        Debug.Assert(!string.IsNullOrEmpty(projectPath) && Directory.Exists(projectPath));
        _cachePath = $@"{projectPath}.Primal\AssetInfoCache.bin";
        LoadCacheFile();

        Debug.Assert(!string.IsNullOrEmpty(contentFolder) && Directory.Exists(contentFolder));

        lock (_lock)
        {
            RegisterAllAssets(contentFolder);
            DefaultAssets.DefaultAssetsList.ForEach(x => RegisterAsset(x.FullPath, x));
        }


        ContentWatcher.ContentModified += OnContentModified;
    }

    public static void Save() => SaveCacheFile();

    public static AssetInfo GetAssetInfo(string file)
    {
        lock (_lock) { return _assetsFileDictionary.TryGetValue(file, out var value) ? value : null; }
    }

    public static AssetInfo GetAssetInfo(Guid guid)
    {
        lock (_lock) { return _assetsGuidDictionary.TryGetValue(guid, out var value) ? value : null; }
    }
}
