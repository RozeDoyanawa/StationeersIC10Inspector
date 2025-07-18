using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Assets.Scripts.Objects.Electrical;
using BepInEx;
using HarmonyLib;
using IC10_Extender;
using LaunchPadBooster;
using ridorana.IC10Inspector.Objects.Items;
using ridorana.IC10Inspector.patches;
using StationeersMods.Interface;
using UnityEngine;

namespace ridorana.IC10Inspector {
    
    [StationeersMod("IC10Inspector", "IC10Inspector [StationeersMods]", "0.2.4657.21547.1")]
    [BepInDependency("net.lawofsynergy.stationeers.ic10e", BepInDependency.DependencyFlags.SoftDependency)]
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

        private void Awake() {
            if (IC10E_Compat.Enabled)
            {
                IC10E_Compat.Init();
            }
        }
    }

    public static class IC10E_Compat {

        private static bool? _enabled;

        public static bool Enabled {
            [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
            get {
                if (_enabled == null) {
                    Assembly[] assemblies = System.AppDomain.CurrentDomain.GetAssemblies();
                    bool found = false;
                    foreach (var assembly in assemblies) {
                        string message = assembly.GetName().Name;
                        if (message.Equals("IC10Extender")) {
                            found = true;
                            break;
                        }
                    }

                    _enabled = found;
                }

                return (bool)_enabled;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining | MethodImplOptions.NoOptimization)]
        public static void Init() {
            IC10Extender.Register(new Break());
        }

        private class Break : ExtendedOpCode {
            //implementation that extends my abstract class
            private class Instance : Operation {
                //private ChipWrapper wrapper;
                public Instance(ChipWrapper chip, int lineNumber) : base(chip, lineNumber) {

                }

                public override int Execute(int index) {
                    DebugMotherboard.ChipDebugManager.SetPaused(Chip.chip, true);
                    return index + 1;
                }
            }

            public Break() : base("break") {

            }

            public override void Accept(int lineNumber, string[] source) {
                if (source.Length != 1) throw new ProgrammableChipException(ProgrammableChipException.ICExceptionType.IncorrectArgumentCount, lineNumber);
            }

            public override Operation Create(ChipWrapper chip, int lineNumber, string[] source) {
                return new Instance(chip, lineNumber);
            }

            public override HelpString[] Params(int currentArgCount) {
                return Array.Empty<HelpString>();
            }
        }

    }

}