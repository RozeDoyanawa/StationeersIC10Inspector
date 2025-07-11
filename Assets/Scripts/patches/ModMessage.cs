using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using Assets.Scripts.Networking;
using ridorana.IC10Inspector.Objects.Items;
using UnityEditor;
using UnityEngine;
using UnityEngine.Networking;
using Object = UnityEngine.Object;

namespace ridorana.IC10Inspector.patches {

    public class ModMessage<T> : ProcessedMessage<DebugMotherboard.SetIC10ValueMessage> where T : ProcessedMessage<T>, new() {
        private static readonly Dictionary<Type, int> MessageTypeToIndex = new();
        private static readonly Dictionary<int, Type> MessageIndexToType = new();
        private static int NextIndex;

        private static ProcessedMessage<T> _message;

        static ModMessage() {
            //NetworkPatches.PushMessage(typeof(ModMessage<>));
        }

        public static void PushMessageType(Type msgType) {
            lock (MessageTypeToIndex) {
                if (!MessageTypeToIndex.ContainsKey(msgType) && NextIndex < 65000) {
                    int index = NextIndex++;
                    MessageTypeToIndex.Add(msgType, (byte)index);
                    MessageIndexToType.Add((byte)index, msgType);
                } else if (NextIndex > 65000) {
                    throw new Exception("No more free indexes");
                } else {
                    throw new Exception("Message type " + msgType.Name + " not found");
                }
            }
        }

        public override void Deserialize(RocketBinaryReader reader) {
            int index = reader.ReadInt32();
            Type type = MessageIndexToType[index];

            PropertyInfo property = type.GetProperty("Singleton", BindingFlags.Static | BindingFlags.Public | BindingFlags.FlattenHierarchy);
            if (property == (PropertyInfo)null) {
                throw new NullReferenceException($"Failed find {"Singleton"} on type {type}");
            }

            object obj = property.GetGetMethod()?.Invoke((object)null, (object[])null);
            if (!(obj is IMessageSerialisable messageSerialisable)) {
                throw new InvalidCastException($"Type {obj} could not be casted to {"IMessageSerialisable"}");
            }

            try {
                messageSerialisable.Deserialize(reader);
            } catch (EndOfStreamException ex) {
                Debug.LogException((Exception)ex);
                Debug.LogError((object)$"Message: ({obj.GetType()}) {obj}");
            }
        }

        public override void Serialize(RocketBinaryWriter writer) {
            writer.WriteInt32(MessageTypeToIndex[typeof(T)]);
            _message.Serialize(writer);
        }
    }
}