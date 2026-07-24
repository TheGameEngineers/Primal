// Copyright (c) Arash Khatami
// Distributed under the MIT license. See the LICENSE file in the project root for more information.
using System;
using System.IO;
using System.Runtime.Serialization;
using System.Text;

namespace PrimalEditor.Components;

[DataContract]
class Script(GameEntity owner) : Component(owner)
{
    [DataMember]
    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    public override IMSComponent GetMultiselectionComponent(MSEntity msEntity) => new MSScript(msEntity);

    public override void WriteToBinary(BinaryWriter bw)
    {
        var nameBytes = Encoding.UTF8.GetBytes(Name);
        bw.Write(nameBytes.Length);
        bw.Write(nameBytes);
    }
}

sealed class MSScript : MSComponent<Script>
{
    public string Name
    {
        get;
        set
        {
            if (field != value)
            {
                field = value;
                OnPropertyChanged(nameof(Name));
            }
        }
    }

    protected override bool UpdateComponents(string propertyName)
    {
        if (propertyName == nameof(Name))
        {
            SelectedComponents.ForEach(c => c.Name = Name);
            return true;
        }

        return false;
    }

    protected override bool UpdateMSComponent()
    {
        Name = MSEntity.GetMixedValue(SelectedComponents, new Func<Script, string>(x => x.Name));
        return true;
    }

    public MSScript(MSEntity msEntity) : base(msEntity)
    {
        Refresh();
    }
}
