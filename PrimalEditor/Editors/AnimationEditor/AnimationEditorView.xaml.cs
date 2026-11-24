// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System.Windows.Controls;

namespace PrimalEditor.Editors;

/// <summary>
/// Interaction logic for AnimationEditorView.xaml
/// </summary>
public partial class AnimationEditorView : UserControl
{
    public AnimationEditorView()
    {
        InitializeComponent();
        jointHierarchy.SelectedJointChanged += OnSelectedJointChanged;
    }

    private void OnChannels_ToggleButton_Click(object sender, System.Windows.RoutedEventArgs e)
    {
        RefreshSelectedJointChannels();
    }

    private void OnSelectedJointChanged(object sender, System.EventArgs e)
    {
        RefreshSelectedJointChannels();
    }

    private void RefreshSelectedJointChannels()
    {
        if (DataContext is not AnimationEditor editor || editor.Skeleton == null ||
            jointHierarchy.SelectedJoint is not { } selectedJoint)
            return;

        var jointIndex = -1;
        for (int i = 0; i < editor.Skeleton.JointNames.Count; ++i)
        {
            if (editor.Skeleton.JointNames[i] == selectedJoint.Name)
            {
                jointIndex = i;
                break;
            }
        }

        if (jointIndex < 0) return;

        var channels = (AnimationChannels)0;
        if (PosX.IsChecked == true) channels |= AnimationChannels.PositionX;
        if (PosY.IsChecked == true) channels |= AnimationChannels.PositionY;
        if (PosZ.IsChecked == true) channels |= AnimationChannels.PositionZ;
        if (RotX.IsChecked == true) channels |= AnimationChannels.RotationX;
        if (RotY.IsChecked == true) channels |= AnimationChannels.RotationY;
        if (RotZ.IsChecked == true) channels |= AnimationChannels.RotationZ;
        if (ScaleX.IsChecked == true) channels |= AnimationChannels.ScaleX;
        if (ScaleY.IsChecked == true) channels |= AnimationChannels.ScaleY;
        if (ScaleZ.IsChecked == true) channels |= AnimationChannels.ScaleZ;

        editor.ShowChannelsForJoint(jointIndex, channels);
    }
}
