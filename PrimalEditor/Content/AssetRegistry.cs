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
        private static readonly DelayEventTimer _refreshTimer = new DelayEventTimer(TimeSpan.FromMilliseconds(250));
        private static readonly Dictionary<string, AssetInfo> _assetDictionary = new Dictionary<string, AssetInfo>();
        private static readonly ObservableCollection<AssetInfo> _assets = new ObservableCollection<AssetInfo>();
        private static readonly FileSystemWatcher _contentWatcher = new FileSystemWatcher()
        {
            IncludeSubdirectories = true,
            Filter = "",
            NotifyFilter = NotifyFilters.CreationTime |
                           NotifyFilters.DirectoryName |
                           NotifyFilters.FileName |
                           NotifyFilters.LastWrite
        };


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

                if (!_assetDictionary.ContainsKey(file) ||
                    _assetDictionary[file].RegisterTime.IsOlder(fileInfo.LastWriteTime))
                {
                    var info = Asset.GetAssetInfo(file);
                    Debug.Assert(info != null);
                    info.RegisterTime = DateTime.Now;
                    _assetDictionary[file] = info;

                    Debug.Assert(_assetDictionary.ContainsKey(file));
                    _assets.Add(_assetDictionary[file]);
                }
            }
            catch (Exception ex) { Debug.WriteLine(ex.Message); }
        }

        private static void UnregisterAsset(string file)
        {
            if (_assetDictionary.ContainsKey(file))
            {
                _assets.Remove(_assetDictionary[file]);
                _assetDictionary.Remove(file);
            }
        }


        private static async void OnContentModified(object sender, FileSystemEventArgs e)
        {
            if (Path.GetExtension(e.FullPath) != Asset.AssetFileExtension) return;

            await Application.Current.Dispatcher.BeginInvoke(new Action(() =>
            {
                _refreshTimer.Trigger(e);
            }));
        }

        private static void Refresh(object sender, DelayEventTimerArgs e)
        {
            foreach (var item in e.Data)
            {
                if (!(item is FileSystemEventArgs eventArgs)) continue;

                if (eventArgs.ChangeType == WatcherChangeTypes.Deleted)
                {
                    UnregisterAsset(eventArgs.FullPath);
                }
                else
                {
                    RegisterAsset(eventArgs.FullPath);
                    if (eventArgs.ChangeType == WatcherChangeTypes.Renamed)
                    {
                        _assetDictionary.Keys.Where(key => !File.Exists(key)).ToList().ForEach(file => UnregisterAsset(file));
                    }
                }
            }
        }

        public static void Clear()
        {
            _contentWatcher.EnableRaisingEvents = false;
            _assetDictionary.Clear();
            _assets.Clear();
        }

        public static void Reset(string contentFolder)
        {
            Clear();
            Debug.Assert(Directory.Exists(contentFolder));
            RegisterAllAssets(contentFolder);
            _contentWatcher.Path = contentFolder;
            _contentWatcher.EnableRaisingEvents = true;
        }

        public static AssetInfo GetAssetInfo(string file) => _assetDictionary.ContainsKey(file) ? _assetDictionary[file] : null;

        public static AssetInfo GetAssetInfo(Guid guid) => _assets.FirstOrDefault(x => x.Guid == guid);

        static AssetRegistry()
        {
            _contentWatcher.Changed += OnContentModified;
            _contentWatcher.Created += OnContentModified;
            _contentWatcher.Deleted += OnContentModified;
            _contentWatcher.Renamed += OnContentModified;

            _refreshTimer.Triggered += Refresh;
        }
    }
}
