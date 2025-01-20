// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using PrimalEditor.DllWrappers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace PrimalEditor.Editors
{
    class CubeMap
    {
        public int ArrayIndex { get; set; }
        public int MipIndex { get; set; }
        public BitmapSource PositiveX { get; set; }
        public BitmapSource NegativeX { get; set; }
        public BitmapSource PositiveY { get; set; }
        public BitmapSource NegativeY { get; set; }
        public BitmapSource PositiveZ { get; set; }
        public BitmapSource NegativeZ { get; set; }
    }

    class TextureEditor : ViewModelBase, IAssetEditor
    {
        private readonly List<List<List<BitmapSource>>> _sliceBitmaps = new();
        private SliceArray3D _slices;

        public ICommand SetAllChannelsCommand { get; init; }
        public ICommand SetChannelCommand { get; init; }
        public ICommand RegenerateBitmapsCommand { get; init; }
        public ICommand ReimportCommand { get; init; }
        public ICommand SaveCommand { get; init; }

        private AssetEditorState _state;
        public AssetEditorState State
        {
            get => _state;
            private set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged(nameof(State));
                }
            }
        }

        private Guid _assetGuid;

        private bool _canSaveChanges;
        public bool CanSaveChanges
        {
            get => _canSaveChanges;
            set
            {
                if (_canSaveChanges != value)
                {
                    _canSaveChanges = value;
                    OnPropertyChanged(nameof(CanSaveChanges));
                }
            }
        }

        private bool _isRedChannelSelected = true;
        public bool IsRedChannelSelected
        {
            get => _isRedChannelSelected;
            set
            {
                if (_isRedChannelSelected != value)
                {
                    _isRedChannelSelected = value;
                    OnPropertyChanged(nameof(IsRedChannelSelected));
                    SetImageChannels();
                }
            }
        }

        private bool _isGreenChannelSelected = true;
        public bool IsGreenChannelSelected
        {
            get => _isGreenChannelSelected;
            set
            {
                if (_isGreenChannelSelected != value)
                {
                    _isGreenChannelSelected = value;
                    OnPropertyChanged(nameof(IsGreenChannelSelected));
                    SetImageChannels();
                }
            }
        }

        private bool _isBlueChannelSelected = true;
        public bool IsBlueChannelSelected
        {
            get => _isBlueChannelSelected;
            set
            {
                if (_isBlueChannelSelected != value)
                {
                    _isBlueChannelSelected = value;
                    OnPropertyChanged(nameof(IsBlueChannelSelected));
                    SetImageChannels();
                }
            }
        }

        private bool _isAlphaChannelSelected = true;
        public bool IsAlphaChannelSelected
        {
            get => _isAlphaChannelSelected;
            set
            {
                if (_isAlphaChannelSelected != value)
                {
                    _isAlphaChannelSelected = value;
                    OnPropertyChanged(nameof(IsAlphaChannelSelected));
                    SetImageChannels();
                }
            }
        }

        public Color Channels => new()
        {
            ScR = IsRedChannelSelected ? 1.0f : 0.0f,
            ScG = IsGreenChannelSelected ? 1.0f : 0.0f,
            ScB = IsBlueChannelSelected ? 1.0f : 0.0f,
            ScA = IsAlphaChannelSelected ? 1.0f : 0.0f
        };

        public float Stride => (float?)SelectedSliceBitmap?.Format.BitsPerPixel / 8 ?? 1.0f;

        Asset IAssetEditor.Asset => Texture;

        private Texture _texture = new();
        public Texture Texture
        {
            get => _texture;
            private set
            {
                if (_texture != value)
                {
                    _texture = value;
                    if(_texture != null)
                    {
                        IAssetImportSettings.CopyImportSettings(_texture.ImportSettings, ImportSettings);
                    }
                    OnPropertyChanged(nameof(Texture));
                    SetSelectedBitmap();
                    SetImageChannels();
                }
            }
        }

        public TextureImportSettings ImportSettings { get; } = new();

        public int MaxMipIndex => _sliceBitmaps.Any() && _sliceBitmaps.First().Any() ? _sliceBitmaps.First().Count - 1 : 0;
        public int MaxArrayIndex => _sliceBitmaps.Any() ? _sliceBitmaps.Count - 1 : 0;
        public int MaxDepthIndex => _sliceBitmaps.Any() && _sliceBitmaps.First().Any() && _sliceBitmaps.First().First().Any() ?
            _sliceBitmaps.ElementAtOrDefault(ArrayIndex).ElementAtOrDefault(MipIndex).Count - 1 : 0;

        private int _arrayIndex;
        public int ArrayIndex
        {
            get => Math.Min(MaxArrayIndex, _arrayIndex);
            set
            {
                value = Math.Min(value, MaxArrayIndex);
                if (_arrayIndex != value)
                {
                    _arrayIndex = value;
                    OnPropertyChanged(nameof(ArrayIndex));
                    SetSelectedBitmap();
                    SetImageChannels();
                }
            }
        }

        private int _mipIndex;
        public int MipIndex
        {
            get => Math.Min(MaxMipIndex, _mipIndex);
            set
            {
                value = Math.Min(value, MaxMipIndex);
                if (_mipIndex != value)
                {
                    _mipIndex = value;
                    DepthIndex = _depthIndex;
                    OnPropertyChanged(nameof(MipIndex));
                    OnPropertyChanged(nameof(MaxDepthIndex));
                    SetSelectedBitmap();
                    SetImageChannels();
                }
            }
        }

        private int _depthIndex;
        public int DepthIndex
        {
            get => Math.Min(MaxDepthIndex, _depthIndex);
            set
            {
                value = Math.Min(value, MaxDepthIndex);
                if (_depthIndex != value)
                {
                    _depthIndex = value;
                    OnPropertyChanged(nameof(DepthIndex));
                    SetSelectedBitmap();
                    SetImageChannels();
                }
            }
        }

        private CubeMap _cubeMap;
        public CubeMap CubeMap
        {
            get => _cubeMap;
            private set
            {
                if (_cubeMap != value)
                {
                    _cubeMap = value;
                    OnPropertyChanged(nameof(CubeMap));
                }
            }
        }

        private bool _viewAsCubeMap = true;
        public bool ViewAsCubeMap
        {
            get => _viewAsCubeMap;
            set
            {
                if (_viewAsCubeMap != value)
                {
                    _viewAsCubeMap = value;
                    OnPropertyChanged(nameof(ViewAsCubeMap));
                }
            }
        }

        public BitmapSource SelectedSliceBitmap => _sliceBitmaps.ElementAtOrDefault(ArrayIndex)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex);
        public Slice SelectedSlice => Texture?.Slices?.ElementAtOrDefault(ArrayIndex)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex);
        public long DataSize=>Texture?.Slices?.Sum(x=>x.Sum(y=>y.Sum(z=>z.RawContent.LongLength))) ?? 0;

        private void SetCubeMap()
        {
            if (Texture?.IsCubeMap != true) return;

            var index = (ArrayIndex / 6) * 6;
            if (CubeMap == null || index != CubeMap.ArrayIndex || MipIndex != CubeMap.MipIndex)
            {
                Debug.Assert(index + 5 <= MaxArrayIndex);

                CubeMap = new CubeMap()
                {
                    ArrayIndex = index,
                    MipIndex = MipIndex,
                    PositiveX = _sliceBitmaps.ElementAtOrDefault(index + 0)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex),
                    NegativeX = _sliceBitmaps.ElementAtOrDefault(index + 1)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex),
                    PositiveY = _sliceBitmaps.ElementAtOrDefault(index + 2)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex),
                    NegativeY = _sliceBitmaps.ElementAtOrDefault(index + 3)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex),
                    PositiveZ = _sliceBitmaps.ElementAtOrDefault(index + 4)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex),
                    NegativeZ = _sliceBitmaps.ElementAtOrDefault(index + 5)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex),
                };
            }
        }

        private void SetSelectedBitmap()
        {
            SetCubeMap();
            OnPropertyChanged(nameof(SelectedSliceBitmap));
            OnPropertyChanged(nameof(SelectedSlice));
            OnPropertyChanged(nameof(DataSize));
        }

        private void SetImageChannels()
        {
            OnPropertyChanged(nameof(Channels));
            OnPropertyChanged(nameof(Stride));
        }

        private void OnSetAllChannelsCommand(object parameter)
        {
            _isRedChannelSelected = true;
            _isGreenChannelSelected = true;
            _isBlueChannelSelected = true;
            _isAlphaChannelSelected = true;
            OnPropertyChanged(nameof(IsRedChannelSelected));
            OnPropertyChanged(nameof(IsGreenChannelSelected));
            OnPropertyChanged(nameof(IsBlueChannelSelected));
            OnPropertyChanged(nameof(IsAlphaChannelSelected));
            SetImageChannels();
        }

        private void OnSetChannelCommand(string parameter)
        {
            if (!Keyboard.Modifiers.HasFlag(ModifierKeys.Shift))
            {
                _isRedChannelSelected = false;
                _isGreenChannelSelected = false;
                _isBlueChannelSelected = false;
                _isAlphaChannelSelected = false;
                OnPropertyChanged(nameof(IsRedChannelSelected));
                OnPropertyChanged(nameof(IsGreenChannelSelected));
                OnPropertyChanged(nameof(IsBlueChannelSelected));
                OnPropertyChanged(nameof(IsAlphaChannelSelected));
            }

            switch (parameter)
            {
                case "0": IsRedChannelSelected = !IsRedChannelSelected; break;
                case "1": IsGreenChannelSelected = !IsGreenChannelSelected; break;
                case "2": IsBlueChannelSelected = !IsBlueChannelSelected; break;
                case "3": IsAlphaChannelSelected = !IsAlphaChannelSelected; break;
            }
        }

        private void OnRegenerateBitmapsCommand(bool isNormalMap)
        {
            GenerateSliceBitmaps(isNormalMap, Texture?.Format ?? DXGI_FORMAT.DXGI_FORMAT_UNKNOWN);
            OnPropertyChanged(nameof(SelectedSliceBitmap));
            SetImageChannels();
        }

        public bool CheckAssetGuid(Guid guid) => _assetGuid == guid || Texture?.Guid == guid || Texture?.IBLPair?.Guid == guid;

        public async Task SetAsset(Asset asset)
        {
            Debug.Assert(asset is Texture);
            if (asset is Texture texture)
            {
                _assetGuid = texture.Guid;
                await SetMipmaps(texture);
                Texture = texture;
            }
        }

        public async Task SetAsset(AssetInfo info)
        {
            try
            {
                _assetGuid = info.Guid;
                Texture = null;
                Debug.Assert(info != null && File.Exists(info.FullPath));
                var texture = new Texture();
                State = AssetEditorState.Loading;

                await Task.Run(() =>
                {
                    texture.Load(info.FullPath);
                });

                await SetMipmaps(texture);
                Texture = texture;
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Debug.WriteLine($"Failed to set texture for use in texture editor. File: {info.FullPath}");
                Texture = new();
            }
            finally { State = AssetEditorState.Done; }
        }

        private async Task SetMipmaps(Texture texture)
        {
            try
            {
                await Task.Run(() => _slices = texture.ImportSettings.Compress ? ContentToolsAPI.Decompress(texture) : texture.Slices);
                Debug.Assert(_slices?.Any() == true && _slices.First().Any());
                GenerateSliceBitmaps(texture.IsNormalMap, texture.Format);
                OnPropertyChanged(nameof(Texture));
                OnPropertyChanged(nameof(DataSize));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Debug.WriteLine($"Failed to load mipmaps from {texture.FileName}");
            }
        }

        private void GenerateSliceBitmaps(bool isNormalMap, DXGI_FORMAT format)
        {
            _sliceBitmaps.Clear();
            _cubeMap = null;
            foreach (var arraySlice in _slices)
            {
                List<List<BitmapSource>> mipmapsBitmaps = new();
                foreach (var mipLevel in arraySlice)
                {
                    List<BitmapSource> sliceBitmap = new();
                    foreach (var slice in mipLevel)
                    {
                        var image = BitmapHelper.ImageFromSlice(slice, format, isNormalMap);
                        Debug.Assert(image != null);
                        sliceBitmap.Add(image);
                    }
                    mipmapsBitmaps.Add(sliceBitmap);
                }
                _sliceBitmaps.Add(mipmapsBitmaps);
            }

            OnPropertyChanged(nameof(MaxMipIndex));
            OnPropertyChanged(nameof(MaxArrayIndex));
            OnPropertyChanged(nameof(MaxDepthIndex));
        }

        private async Task OnReimportCommand(object obj)
        {
            if (Texture == null) return;

            TextureImportSettings settingsBackup = new();
            IAssetImportSettings.CopyImportSettings(Texture.ImportSettings, settingsBackup);
            IAssetImportSettings.CopyImportSettings(ImportSettings, Texture.ImportSettings);

            State = AssetEditorState.Importing;

            bool result = false;
            await Task.Run(() => result = Texture.Import(Texture.FullPath));

            if(result)
            {
                State = AssetEditorState.Loading;
                await SetMipmaps(Texture);
                SetSelectedBitmap();
                SetImageChannels();
                CanSaveChanges = true;
            }
            else
            {
                IAssetImportSettings.CopyImportSettings(settingsBackup, Texture.ImportSettings);
            }

            State = AssetEditorState.Done;
        }

        private async Task OnSaveCommand(object obj)
        {
            if(!CanSaveChanges || Texture == null) return;

            State = AssetEditorState.Saving;
            CanSaveChanges = false;
            await Task.Run(Texture.SaveAsset);
            State = AssetEditorState.Done;
        }

        public TextureEditor()
        {
            SetAllChannelsCommand = new RelayCommand<string>(OnSetAllChannelsCommand);
            SetChannelCommand = new RelayCommand<string>(OnSetChannelCommand);
            RegenerateBitmapsCommand = new RelayCommand<bool>(OnRegenerateBitmapsCommand);
            ReimportCommand = new RelayCommand<object>(async x => await OnReimportCommand(x));
            SaveCommand = new RelayCommand<object>(async x => await OnSaveCommand(x));
        }        
    }
}
