// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Numerics;
using System.Threading.Tasks;

namespace PrimalEditor.Editors;

class SkeletonJoint
{
    public SkeletonJoint Parent { get; private set; }

    private readonly List<SkeletonJoint> _children = [];
    public IReadOnlyList<SkeletonJoint> Children => _children.AsReadOnly();
    public string Name { get; }
    public Matrix4x4 InverseBindPose { get; }
    private List<float> _matrixElements = [];
    public IReadOnlyList<float> MatrixElements => _matrixElements.AsReadOnly();

    public void AddChild(SkeletonJoint child)
    {
        if (!_children.Contains(child))
        {
            // NOTE: the joint hierarchy in the editor can't be modified.
            //       Therefore, it's not necessary to do formal parent-child
            //       management (e.g. remove child from parent's children etc.)
            child.Parent = this;
            _children.Add(child);
        }
    }

    public SkeletonJoint(string name, Matrix4x4 invBindPose)
    {
        Name = name;
        InverseBindPose = invBindPose;
        for (int row = 0; row < 4; ++row)
            for (int col = 0; col < 4; ++col)
                _matrixElements.Add(invBindPose[row, col]);
    }
}

class SkeletonData
{
    private readonly Skeleton _skeleton;

    private readonly List<SkeletonJoint> _flatJointList = [];
    private SkeletonJoint _rootJoint;
    public SkeletonJoint[] RootJoint => [_rootJoint];
    public int JointCount => _flatJointList.Count;

    private void MakeJointTree()
    {
        var numJoints = _skeleton.JointHierarchy.Count;
        Debug.Assert(numJoints > 0 &&
            numJoints == _skeleton.JointNames.Count &&
            numJoints == _skeleton.InverseBindPoses.Count);

        _flatJointList.Clear();
        
        for (int i = 0; i < numJoints; ++i)
        {
            _flatJointList.Add(new(_skeleton.JointNames[i], _skeleton.InverseBindPoses[i]));
        }

        _rootJoint = _flatJointList[0];
        // NOTE: the first entry is skipped because it's the root with parent index 0xffff.
        for (int i = 1; i < numJoints; ++i)
        {
            var jointIndex = _skeleton.JointHierarchy[i];
            _flatJointList[jointIndex].AddChild(_flatJointList[i]);
        }
    }

    public SkeletonData(Skeleton skeleton)
    {
        Debug.Assert(skeleton != null);
        _skeleton = skeleton;
        MakeJointTree();
    }
}

class SkeletonEditor : ViewModelBase, IAssetEditor
{
    public AssetEditorState State => AssetEditorState.Done;

    private Guid _assetGuid;
    public Asset Asset => Skeleton;

    public Skeleton Skeleton
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Skeleton));
            }
        }
    }

    public SkeletonData SkeletonData
    {
        get;
        private set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(SkeletonData));
            }
        }
    }

    public bool CheckAssetGuid(Guid guid) => _assetGuid == guid;

    public async Task SetAsset(AssetInfo info)
    {
        try
        {
            _assetGuid = info.Guid;
            Debug.Assert(info != null && File.Exists(info.FullPath));
            Debug.Assert(info.Type == AssetType.Skeleton);
            Skeleton skeleton = null;
            await Task.Run(() => skeleton = new(info));
            Skeleton = skeleton;
            SkeletonData = new(Skeleton);
        }
        catch (Exception ex)
        {
            Debug.WriteLine(ex.Message);
            Debug.WriteLine($"Failed to set skeleton for skeleton editor. File: {info.FullPath}\n{ex.Message}");
        }
    }
}
