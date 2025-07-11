using System.Collections.Generic;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Serialization;
using HarmonyLib;
using ridorana.IC10Inspector.Objects.Items;

namespace ridorana.IC10Inspector.patches {

    [HarmonyPatch]
    public class SaveDataPatch
    {
        [HarmonyPatch]
        public static class Patch_XmlSaveLoad {
            [HarmonyPatch(typeof(XmlSaveLoad), nameof(XmlSaveLoad.AddExtraTypes))]
            public static void Prefix(ref List<System.Type> extraTypes) {
                extraTypes.Add(typeof(DebugMotherboardSaveData));
            }
        }

        //[HarmonyPatch]
        //public static class Patch_ProgrammableChip_Serialize {
        //    
        //    [HarmonyPatch(typeof(ProgrammableChip), nameof(ProgrammableChip.SerializeSave))]
        //    public static void Prefix(ref List<System.Type> extraTypes) {
        //        extraTypes.Add(typeof(DebugMotherboardSaveData));
        //    }
        //}
    }
}