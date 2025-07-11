using System;
using System.Collections.Generic;
using System.Reflection;
using HarmonyLib;
using JetBrains.Annotations;
using UnityEngine.Networking;

namespace ridorana.IC10Inspector.patches {
    
    //[HarmonyPatch]
    public class NetworkPatches {
        private static readonly Dictionary<Type, byte> MessageTypeToIndex = new();
        private static readonly Dictionary<byte, Type> MessageIndexToType = new();
        private static int NextIndex;

        static NetworkPatches() {
            lock (MessageTypeToIndex) {
                FieldInfo field = typeof(MessageFactory).GetField("MessageTypeToIndex", BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
                Dictionary<Type, byte> msgs = (Dictionary<Type, byte>)field.GetValue(null);
                int highest = 0;
                foreach (Type type in msgs.Keys) {
                    int index = msgs[type];
                    MessageTypeToIndex.Add(type, (byte)index);
                    MessageIndexToType.Add((byte)index, type);
                    if (index > highest) {
                        highest = index;
                    }
                }

                NextIndex = highest + 1;
            }
        }


        public static void PushMessage(Type msgType) {
            lock (MessageTypeToIndex) {
                if (!MessageTypeToIndex.ContainsKey(msgType) && NextIndex < 256) {
                    int index = NextIndex++;
                    MessageTypeToIndex.Add(msgType, (byte)index);
                    MessageIndexToType.Add((byte)index, msgType);
                } else if(NextIndex > 255) {
                    throw new Exception("No more free indexes");
                }
            }
        }

        [HarmonyPatch(typeof(MessageFactory), nameof(MessageFactory.GetIndexFromType), typeof(Type))]
        public static class Patch_MessageFactory_GetIndexFromType {

            [UsedImplicitly]
            public static bool Prefix(out byte __result, Type type) {
                __result = MessageTypeToIndex[type];
                return false;
            }
            
        }
        
        [HarmonyPatch(typeof(MessageFactory), nameof(MessageFactory.GetTypeFromIndex), typeof(byte))]
        public static class Patch_MessageFactory_GetTypeFromIndex {

            [UsedImplicitly]
            public static bool Prefix(out Type __result, byte index) {
                __result = MessageIndexToType[index];
                return false;
            }
            
        }
    }
}