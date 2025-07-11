using System.Xml.Serialization;
using Assets.Scripts.Objects.Items;

namespace ridorana.IC10Inspector.Objects.Items {
    [XmlInclude(typeof(DebugMotherboardSaveData))]
    public class DebugMotherboardSaveData : MotherboardSaveData
    {
        [XmlElement] public int SelectedHolderIndex;
            
        [XmlElement] public bool SteppingEnabled;

        [XmlElement] public bool DebugModeEnabled;
    }
}