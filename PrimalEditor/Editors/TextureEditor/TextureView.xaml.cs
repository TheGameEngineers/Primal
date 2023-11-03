// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Utilities;
using System;
using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace PrimalEditor.Editors
{
    /// <summary>
    /// Interaction logic for TextureView.xaml
    /// </summary>
    public partial class TextureView : UserControl
    {
        Point _gridClickPosition = new(0, 0);
        bool _capturedRight;
        private Point _oldPanOffset = new(0, 0);

        private void OnGrid_Mouse_RBD(object sender, MouseButtonEventArgs e)
        {
            _gridClickPosition = e.GetPosition(this);
            _capturedRight = Mouse.Capture(sender as IInputElement);
            Debug.Assert(_capturedRight);
        }

        private void OnGrid_Mouse_RBU(object sender, MouseButtonEventArgs e)
        {
            _capturedRight = false;
            Mouse.Capture(null);
        }

        private void OnGrid_Mouse_Move(object sender, MouseEventArgs e)
        {
            var vm = DataContext as TextureEditor;
            if (_capturedRight && sender is Grid)
            {
                var mousePos = e.GetPosition(this);
                var offset = mousePos - _gridClickPosition;
                offset /= vm.ScaleFactor;

                vm.PanOffset = new(vm.PanOffset.X + offset.X, vm.PanOffset.Y + offset.Y);
                _gridClickPosition = mousePos;
            }
        }

        private void OnGrid_Mouse_Wheel(object sender, MouseWheelEventArgs e)
        {
            var vm = DataContext as TextureEditor;
            var newScaleFactor = vm.ScaleFactor * (1 + Math.Sign(e.Delta) * 0.1);
            Zoom(newScaleFactor, e.GetPosition(this));
        }

        private void Zoom(double scale, Point center)
        {
            if (scale < 0.1) scale = 0.1;

            var vm = DataContext as TextureEditor;
            if(MathUtil.IsTheSameAs(scale, vm.ScaleFactor))
            {
                return;
            }

            var oldScaleFactor = vm.ScaleFactor;
            vm.ScaleFactor = scale;

            var newPos = new Point(center.X * scale / oldScaleFactor, center.Y * scale / oldScaleFactor);
            var offset = (center - newPos) / scale;

            var vp = textureBackground.Viewport;
            var rect = new Rect(vp.X, vp.Y, vp.Width * oldScaleFactor/scale, vp.Height * oldScaleFactor/scale);
            textureBackground.Viewport = rect;

            vm.PanOffset = new(vm.PanOffset.X + offset.X, vm.PanOffset.Y + offset.Y);
        }

        private void OnPanOffsetPropertyChanged(TextureEditor editor)
        {
            if(backgroundGrid.Background is TileBrush brush)
            {
                var offset = editor.PanOffset - _oldPanOffset;
                var viewport = brush.Viewport;
                viewport.X += offset.X;
                viewport.Y += offset.Y;
                brush.Viewport = viewport;
            }

            _oldPanOffset = editor.PanOffset;
        }

        private void OnViewModelPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if(e.PropertyName == nameof(TextureEditor.PanOffset))
            {
                OnPanOffsetPropertyChanged(sender as TextureEditor);
            }
        }

        private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
        {
            if(e.OldValue is TextureEditor oldVm)
            {
                oldVm.PropertyChanged -= OnViewModelPropertyChanged;
            }

            if (e.NewValue is TextureEditor newVm)
            {
                _oldPanOffset = newVm.PanOffset;
                newVm.PropertyChanged += OnViewModelPropertyChanged;
            }
        }

        public TextureView()
        {
            InitializeComponent();
            PreviewMouseDown += (_, _) => Focus();
            Loaded += (_, _) => Focus();

            DataContextChanged += OnDataContextChanged;
        }
    }
}
