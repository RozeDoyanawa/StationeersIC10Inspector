using System.Collections.ObjectModel;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.UI;
using HarmonyLib;
using JetBrains.Annotations;
using LaunchPadBooster;
using ridorana.IC10Inspector.Objects.Items;
using ridorana.IC10Inspector.patches;
using StationeersMods.Interface;
using UnityEngine;

namespace ridorana.IC10Inspector {
    
    [StationeersMod("IC10Inspector", "IC10Inspector [StationeersMods]", "0.2.4657.21547.1")]
    public class IC10Inspector : ModBehaviour {
        
        public static readonly Mod MOD = new("IC10Inspector", "0.1");
        
        public override void OnLoaded(ContentHandler contentHandler) {
            Debug.Log("IC10Inspector says: Hello World!");
            MOD.SetMultiplayerRequired();

            Harmony harmony = new Harmony("IC10Inspector");
            PrefabPatch.prefabs = contentHandler.prefabs;
            harmony.PatchAll();
            
            
            //NetworkPatches.PushMessage(typeof(DebugMotherboard.SetIC10ValueMessage));
            MOD.RegisterNetworkMessage<DebugMotherboard.SetIC10ValueMessage>();

            Debug.Log("IC10Inspector Loaded");
        }
        
    }
}