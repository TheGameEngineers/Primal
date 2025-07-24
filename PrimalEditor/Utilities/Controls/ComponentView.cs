// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System.Windows;
using System.Windows.Controls;

namespace PrimalEditor.Utilities.Controls;

class ComponentView : ContentControl
{
    public string Header
    {
        get { return (string)GetValue(HeaderProperty); }
        set { SetValue(HeaderProperty, value); }
    }

    public static readonly DependencyProperty HeaderProperty =
        DependencyProperty.Register(nameof(Header), typeof(string), typeof(ComponentView));    

    static ComponentView()
    {
        DefaultStyleKeyProperty.OverrideMetadata(typeof(ComponentView),
                new FrameworkPropertyMetadata(typeof(ComponentView)));
    }
}
