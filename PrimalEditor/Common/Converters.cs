// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Data;

namespace PrimalEditor
{
    class BooleanToYesNoConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture) => (value is bool b && b) ? "Yes" : "No";

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => value is string s && s.ToLower() == "yes";
    }

    class EnumDescriptionConverter : IValueConverter
    {
        public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
        {
            if (value is Array array)
            {
                var list = new List<string>();
                foreach (var item in array)
                {
                    if (item is Enum e)
                    {
                        list.Add(e.GetDescription());
                    }
                }

                return list;
            }
            else if (value is Enum e)
            {
                return e.GetDescription();
            }

            return value;
        }

        public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotImplementedException();
    }
}
