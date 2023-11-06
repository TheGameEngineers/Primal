// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Navigation;
using System.Windows.Shapes;

namespace PrimalEditor.Editors
{
    class TextureSizeToStringConverter : IMultiValueConverter
    {
        public object Convert(object[] values, Type targetType, object parameter, CultureInfo culture)
        {
            if (values?.Length>0 && values[0] is TextureEditor editor && editor.Texture != null && editor.SelectedSlice != null)
            {
                var texture = editor.Texture;
                var size = $"{texture.Width} x {texture.Height}";
                var mipSize = $" ({editor.SelectedSlice.Width} x {editor.SelectedSlice.Height}";
                if (texture.IsVolumeMap)
                {
                    size += $" x {texture.Slices[0][0].Count}";
                    mipSize += $" x {texture.Slices[0][editor.MipIndex].Count}";
                }

                return $"{size}{mipSize})";
            }

            return string.Empty;
        }

        public object[] ConvertBack(object value, Type[] targetTypes, object parameter, CultureInfo culture)=> throw new NotImplementedException();
    }
    /// <summary>
    /// Interaction logic for TextureDetailsView.xaml
    /// </summary>
    public partial class TextureDetailsView : UserControl
    {
        public TextureDetailsView()
        {
            InitializeComponent();
        }
    }
}
