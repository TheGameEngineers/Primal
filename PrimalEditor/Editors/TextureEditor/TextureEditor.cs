// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using PrimalEditor.DllWrappers;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media.Imaging;

namespace PrimalEditor.Editors
{
    class TextureEditor : ViewModelBase, IAssetEditor
    {
        private readonly List<List<List<BitmapSource>>> _sliceBitmaps = new();
        private List<List<List<Slice>>> _slices;

        private AssetEditorState _state;
        public AssetEditorState State
        {
            get => _state;
            set
            {
                if (_state != value)
                {
                    _state = value;
                    OnPropertyChanged(nameof(State));
                }
            }
        }

        public Guid AssetGuid { get; private set; }

        private Point _panOffset;
        public Point PanOffset
        {
            get => _panOffset;
            set
            {
                if (_panOffset!= value)
                {
                    _panOffset= value;
                    OnPropertyChanged(nameof(PanOffset));
                }
            }
        }

        private double _scaleFactor = 1.0;
        public double ScaleFactor
        {
            get => _scaleFactor;
            set
            {
                if (_scaleFactor != value)
                {
                    _scaleFactor = value;
                    OnPropertyChanged(nameof(ScaleFactor));
                }
            }
        }

        Asset IAssetEditor.Asset => Texture;

        private Texture _texture;
        public Texture Texture
        {
            get => _texture;
            private set
            {
                if (_texture != value)
                {
                    _texture = value;
                    OnPropertyChanged(nameof(Texture));
                    SetSelectedBitmap();
                }
            }
        }

        public int MaxMipIndex => _sliceBitmaps.Any() && _sliceBitmaps.First().Any() ? _sliceBitmaps.First().Count -1 : 0;
        public int MaxArrayIndex => _sliceBitmaps.Any() ? _sliceBitmaps.Count - 1 : 0;
        public int MaxDepthIndex => _sliceBitmaps.Any() && _sliceBitmaps.First().Any() && _sliceBitmaps.First().First().Any() ?
            _sliceBitmaps.ElementAtOrDefault(ArrayIndex).ElementAtOrDefault(MipIndex).Count - 1  : 0;

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
                    OnPropertyChanged(nameof(MipIndex));
                    OnPropertyChanged(nameof(MaxDepthIndex));
                    SetSelectedBitmap();
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
                }
            }
        }


        public BitmapSource SelectedSliceBitmap => _sliceBitmaps.ElementAtOrDefault(ArrayIndex)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex);
        public Slice SelectedSlice => Texture?.Slices?.ElementAtOrDefault(ArrayIndex)?.ElementAtOrDefault(MipIndex)?.ElementAtOrDefault(DepthIndex);

        private void SetSelectedBitmap()
        {
            OnPropertyChanged(nameof(SelectedSliceBitmap));
            OnPropertyChanged(nameof(SelectedSlice));
        }

        public async void SetAsset(AssetInfo info)
        {
            try
            {
                AssetGuid = info.Guid;
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
                Debug.Assert(_slices?.Any() == true && _slices.First()?.Any() == true);
                GenerateSliceBitMaps(texture.IsNormalMap);
                OnPropertyChanged(nameof(Texture));
            }
            catch (Exception ex)
            {
                Debug.WriteLine(ex.Message);
                Debug.WriteLine($"Failed to load mipmaps from {texture.FileName}");
            }
        }

        private void GenerateSliceBitMaps(bool isNormalMap)
        {
            _sliceBitmaps.Clear();
            foreach (var arraySlice in _slices)
            {
                List<List<BitmapSource>> mipmapsBitmaps = new();
                foreach (var mipLevel in arraySlice)
                {
                    List<BitmapSource> sliceBitmap = new();
                    foreach (var slice in mipLevel)
                    {
                        var image = BitmapHelper.ImageFromSlice(slice, isNormalMap);
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
    }
}
