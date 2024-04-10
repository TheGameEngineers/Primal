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

namespace PrimalEditor.Content
{
    class TextureDimensionToBooleanConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
            => int.TryParse((string)parameter, out var index) && (int)(value as TextureDimension?) == index;

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
            => int.TryParse((string)parameter, out var index) ? (TextureDimension)index : TextureDimension.Texture2D;
    }

    /// <summary>
    /// Interaction logic for TextureImportSettingsView.xaml
    /// </summary>
    public partial class TextureImportSettingsView : UserControl
    {
        public TextureImportSettingsView()
        {
            InitializeComponent();
        }
    }
}
