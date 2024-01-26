// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.GameProject;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;

namespace PrimalEditor.Content
{
    abstract class AssetProxy : ViewModelBase
    {
        public FileInfo FileInfo { get; }

        private string _destinationFolder;
        public string DestinationFolder
        {
            get => _destinationFolder;
            set
            {
                if (!Path.EndsInDirectorySeparator(value))
                    value += Path.DirectorySeparatorChar;

                if (_destinationFolder != value)
                {
                    _destinationFolder = value;
                    OnPropertyChanged(nameof(DestinationFolder));
                }
            }
        }

        public abstract IAssetImportSettings ImportSettings { get; }

        public abstract void CopySettings(IAssetImportSettings settings);

        public AssetProxy(string fileName, string destinationFolder)
        {
            Debug.Assert(File.Exists(fileName));
            FileInfo = new FileInfo(fileName);
            DestinationFolder = destinationFolder;
        }
    }

    class GeometryProxy : AssetProxy
    {
        public override GeometryImportSettings ImportSettings { get; } = new();

        public override void CopySettings(IAssetImportSettings settings)
        {
            Debug.Assert(settings is GeometryImportSettings);
            if (settings is GeometryImportSettings geometryImportSettings)
            {
                IAssetImportSettings.CopyImportSettings(geometryImportSettings, ImportSettings);
            }
        }

        public GeometryProxy(string fileName, string destinationFolder)
            : base(fileName, destinationFolder)
        {
        }

    }

    class TextureProxy : AssetProxy
    {
        public override TextureImportSettings ImportSettings { get; } = new();

        public override void CopySettings(IAssetImportSettings settings)
        {
            Debug.Assert(settings is TextureImportSettings);
            if (settings is TextureImportSettings textureImportSettings)
            {
                IAssetImportSettings.CopyImportSettings(textureImportSettings, ImportSettings);
            }
        }

        public TextureProxy(string fileName, string destinationFolder)
            : base(fileName, destinationFolder)
        {
        }

    }

    class AudioProxy : AssetProxy
    {
        public override IAssetImportSettings ImportSettings => throw new NotImplementedException();

        public override void CopySettings(IAssetImportSettings settings)
        {
            throw new NotImplementedException();
        }

        public AudioProxy(string fileName, string destinationFolder)
            : base(fileName, destinationFolder)
        {
        }
    }

    interface IImportSettingsConfigurator<T> where T : AssetProxy
    {
        void AddFiles(IEnumerable<string> files, string destinationFolder);
        void RemoveFile(T proxy);
        void Import();
    }

    class GeometryImportSettingsConfigurator : ViewModelBase, IImportSettingsConfigurator<GeometryProxy>
    {
        private readonly ObservableCollection<GeometryProxy> _geometryProxies = new();
        public ReadOnlyObservableCollection<GeometryProxy> GeometryProxies { get; }

        public void AddFiles(IEnumerable<string> files, string destinationFolder)
        {
            files.Except(_geometryProxies.Select(proxy => proxy.FileInfo.FullName))
                .ToList().ForEach(file => _geometryProxies.Add(new(file, destinationFolder)));
        }
        public void RemoveFile(GeometryProxy proxy) => _geometryProxies.Remove(proxy);

        public void Import()
        {
            if (!_geometryProxies.Any()) return;

            _ = ContentHelper.ImportFilesAsync(_geometryProxies);
            _geometryProxies.Clear();
        }

        public GeometryImportSettingsConfigurator()
        {
            GeometryProxies = new(_geometryProxies);
        }
    }

    class TextureImportSettingsConfigurator : ViewModelBase, IImportSettingsConfigurator<TextureProxy>
    {
        private readonly ObservableCollection<TextureProxy> _textureProxies = new();
        public ReadOnlyObservableCollection<TextureProxy> TextureProxies { get; }

        public void AddFiles(IEnumerable<string> files, string destinationFolder)
        {
            files.Except(_textureProxies.Select(proxy => proxy.FileInfo.FullName))
                .ToList().ForEach(file => _textureProxies.Add(new(file, destinationFolder)));
        }
        public void RemoveFile(TextureProxy proxy) => _textureProxies.Remove(proxy);

        public void Import()
        {
            if (!_textureProxies.Any()) return;

            _ = ContentHelper.ImportFilesAsync(_textureProxies);
            _textureProxies.Clear();
        }

        public TextureImportSettingsConfigurator()
        {
            TextureProxies = new(_textureProxies);
        }
    }

    class AudioImportSettingsConfigurator : ViewModelBase, IImportSettingsConfigurator<AudioProxy>
    {
        private readonly ObservableCollection<AudioProxy> _audioProxies = new();
        public ReadOnlyObservableCollection<AudioProxy> AudioProxies { get; }

        public void AddFiles(IEnumerable<string> files, string destinationFolder)
        {
            files.Except(_audioProxies.Select(proxy => proxy.FileInfo.FullName))
                .ToList().ForEach(file => _audioProxies.Add(new(file, destinationFolder)));
        }
        public void RemoveFile(AudioProxy proxy) => _audioProxies.Remove(proxy);

        public void Import()
        {
            if (!_audioProxies.Any()) return;

            _ = ContentHelper.ImportFilesAsync(_audioProxies);
            _audioProxies.Clear();
        }

        public AudioImportSettingsConfigurator()
        {
            AudioProxies = new(_audioProxies);
        }
    }

    class ConfigureImportSettings : ViewModelBase
    {
        public string LastDestinationFolder { get; private set; }

        public GeometryImportSettingsConfigurator GeometryImportSettingsConfigurator { get; } = new();
        public TextureImportSettingsConfigurator TextureImportSettingsConfigurator { get; } = new();
        public AudioImportSettingsConfigurator AudioImportSettingsConfigurator { get; } = new();

        public int FileCount =>
            GeometryImportSettingsConfigurator.GeometryProxies.Count +
            TextureImportSettingsConfigurator.TextureProxies.Count +
            AudioImportSettingsConfigurator.AudioProxies.Count;

        public void Import()
        {
            GeometryImportSettingsConfigurator.Import();
            TextureImportSettingsConfigurator.Import();
            AudioImportSettingsConfigurator.Import();
        }

        public void AddFiles(string[] files, string destinationFolder)
        {
            Debug.Assert(files != null);
            Debug.Assert(!string.IsNullOrEmpty(destinationFolder) && Directory.Exists(destinationFolder));
            if (!destinationFolder.EndsWith(Path.DirectorySeparatorChar)) destinationFolder += Path.DirectorySeparatorChar;
            Debug.Assert(Application.Current.Dispatcher.Invoke(() => destinationFolder.Contains(Project.Current.ContentPath)));
            LastDestinationFolder = destinationFolder;

            var meshFiles = files.Where(file => ContentHelper.MeshFileExtensions.Contains(Path.GetExtension(file).ToLower()));
            var imageFiles = files.Where(file => ContentHelper.ImageFileExtensions.Contains(Path.GetExtension(file).ToLower()));
            var audioFiles = files.Where(file => ContentHelper.AudioFileExtensions.Contains(Path.GetExtension(file).ToLower()));

            GeometryImportSettingsConfigurator.AddFiles(meshFiles, destinationFolder);
            TextureImportSettingsConfigurator.AddFiles(imageFiles, destinationFolder);
            AudioImportSettingsConfigurator.AddFiles(audioFiles, destinationFolder);
        }

        public ConfigureImportSettings(string[] files, string destinationFolder)
        {
            AddFiles(files, destinationFolder);
        }

        public ConfigureImportSettings(string destinationFolder)
        {
            Debug.Assert(!string.IsNullOrEmpty(destinationFolder) && Directory.Exists(destinationFolder));
            if (!destinationFolder.EndsWith(Path.DirectorySeparatorChar)) destinationFolder += Path.DirectorySeparatorChar;
            Debug.Assert(Application.Current.Dispatcher.Invoke(() => destinationFolder.Contains(Project.Current.ContentPath)));
            LastDestinationFolder = destinationFolder;
        }
    }
}
