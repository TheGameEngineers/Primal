// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;

namespace PrimalEditor.Utilities;

/// <summary>
/// Interaction logic for LoggerView.xaml
/// </summary>
public partial class LoggerView : UserControl
{
    public LoggerView()
    {
        InitializeComponent();
        ((INotifyCollectionChanged)Logger.Messages).CollectionChanged += (s, e) => scrollViewer.ScrollToEnd();
    }

    private void OnClear_Button_Click(object sender, RoutedEventArgs e) => Logger.Clear();

    private void OnMessageFilter_Button_Click(object sender, RoutedEventArgs e)
    {
        MessageType filter = 0x0;
        if (toggleInfo.IsChecked == true) filter |= MessageType.Info;
        if (toggleWarnings.IsChecked == true) filter |= MessageType.Warning;
        if (toggleErrors.IsChecked == true) filter |= MessageType.Error;
        Logger.SetMessageFilter(filter);
    }
}
