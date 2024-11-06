// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Utilities;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;

namespace PrimalEditor.Content
{
    static class AssetRegistry
    {
        private static readonly Dictionary<string, AssetInfo> _assetsFileDictionary = new();
        private static readonly Dictionary<Guid, AssetInfo> _assetsGuidDictionary = new();
        private static readonly ObservableCollection<AssetInfo> _assets = new();

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

        private static void RegisterAsset(string file)
        {
            Debug.Assert(File.Exists(file));
            try
            {
                var fileInfo = new FileInfo(file);
                var isNew = !_assetsFileDictionary.ContainsKey(file);

                if (isNew || _assetsFileDictionary[file].RegisterTime.IsOlder(fileInfo.LastWriteTime))
                {
                    var info = Asset.GetAssetInfo(file);
                    Debug.Assert(info != null);
                    info.RegisterTime = DateTime.Now;

                    // Handle the case when the same asset file was imported using a different guid.
                    // NOTE: not sure if that is or should be possible.
                    if(!isNew && _assetsFileDictionary[file].Guid != info.Guid)
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
            if (_assetsFileDictionary.ContainsKey(file))
            {
                var info = _assetsFileDictionary[file];
                _assets.Remove(info);
                _assetsFileDictionary.Remove(file);
                // NOTE: when a file's renamed, the same GUID will be registered with the new name.
                //       We don't want to remove the entry in that case.
                if (_assetsGuidDictionary.ContainsKey(info.Guid) && !File.Exists(_assetsGuidDictionary[info.Guid].FullPath))
                {
                    _assetsGuidDictionary.Remove(info.Guid);
                }
            }
        }

        private static void OnContentModified(object sender, ContentModifiedEventArgs e)
        {
            if(ContentHelper.IsDirectory(e.FullPath))
            {
                RegisterAllAssets(e.FullPath);
            }
            else if(File.Exists(e.FullPath))
            {
                RegisterAsset(e.FullPath);
            }

            _assets.Where(x => !File.Exists(x.FullPath)).ToList().ForEach(x => UnregisterAsset(x.FullPath));
        }

        public static void Reset(string contentFolder)
        {
            ContentWatcher.ContentModified -= OnContentModified;

            _assetsFileDictionary.Clear();
            _assetsGuidDictionary.Clear();
            _assets.Clear();

            Debug.Assert(Directory.Exists(contentFolder));
            RegisterAllAssets(contentFolder);

            ContentWatcher.ContentModified += OnContentModified;
        }

        public static AssetInfo GetAssetInfo(string file) => _assetsFileDictionary.ContainsKey(file) ? _assetsFileDictionary[file] : null;

        public static AssetInfo GetAssetInfo(Guid guid) => _assetsGuidDictionary.ContainsKey(guid) ? _assetsGuidDictionary[guid] : null;
    }
}
