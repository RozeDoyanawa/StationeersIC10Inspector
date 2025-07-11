using System.Collections.Generic;
using HarmonyLib;
using JetBrains.Annotations;

namespace ridorana.IC10Inspector {
    
    public class TickManager {
        
        public interface ITickable {
            void OnTickableTick();
        }

        [HarmonyPatch(typeof(CartridgeManager), nameof(CartridgeManager.CartridgeTick))] // if possible use nameof() here
        public static class CartridgeManagerPatch {
            
            [UsedImplicitly]
            static bool Prefix() {
                foreach (var tickable in AllTickables) {
                    tickable.OnTickableTick();
                }
                return true;
            }
            
        }
        
        public static List<ITickable> AllTickables = new List<ITickable>();

        public static void RegisterTickable(ITickable tickable) {
            AllTickables.Add(tickable);
        }

        public static void UnregisterTickable(ITickable tickable) {
            AllTickables.Remove(tickable);
        }
    }
}