// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using PrimalEditor.Content;
using System;
using System.Threading.Tasks;

namespace PrimalEditor.Editors
{
    enum AssetEditorState
    {
        Done=0,
        Importing,
        Processing,
        Loading,
        Saving,
    }

    interface IAssetEditor
    {
        AssetEditorState State { get; }
        Asset Asset { get; }
        bool CheckAssetGuid(Guid guid);
        Task SetAsset(AssetInfo info);
    }
}