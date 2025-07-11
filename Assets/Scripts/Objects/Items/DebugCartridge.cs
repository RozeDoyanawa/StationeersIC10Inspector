using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text;
using Assets.Scripts;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.UI;
using ridorana.IC10Inspector.Utilities;
using StationeersMods.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace ridorana.IC10Inspector.Objects.Items {
    
    public class DebugCartridge : Cartridge, IPatchable, ISetable {
 
        [SerializeField] private TextMeshProUGUI _displayTextMesh;


        private static readonly string NotApplicableString = "N/A";
        private string _selectedText = string.Empty;
        private string _outputText = string.Empty;
        private Device _scannedDevice;
        private Device _interpretDevice;
        private Device _lastScannedDevice;
        private Device _lastScannedDevice2;
        private bool _needTopScroll;

        private readonly Dictionary<short, double> ChangeList = new();


        public void PatchOnLoad() {
            var existingCartridge = StationeersModsUtility.FindPrefab("CartridgeConfiguration");
            
            this.Thumbnail = existingCartridge.Thumbnail;
            this.Blueprint = existingCartridge.Blueprint;

            var erenderer = existingCartridge.GetComponent<MeshRenderer>();
            var renderer = this.GetComponent<MeshRenderer>();
            renderer.materials = erenderer.materials;
            var emesh = existingCartridge.GetComponent<MeshFilter>();
            var mesh = this.GetComponent<MeshFilter>();
            mesh.mesh = emesh.mesh;
            
            //Component[] localComps = GetComponentsInChildren(typeof(Component));
            //Component[] factoryComps = existing.GetComponentsInChildren(typeof(Component));

            PrefabUtils.CopyParams<Text>(this, existingCartridge, "PanelNormal/Title", PrefabUtils.CopyTextFontData);
            PrefabUtils.CopyParams<TextMeshProUGUI>(this, existingCartridge, "PanelNormal/ScrollPanel/Viewport/Content/Text", (target, source) => { 
                PrefabUtils.CopyMeshTextFontData(target, source);
                target.fontSize = 9;
                target.font = InputSourceCode.Instance.LineOfCodePrefab.FormattedText.font;
            }); 
            PrefabUtils.CopyParams<TextMeshProUGUI>(this, existingCartridge, "PanelNormal/Selected", PrefabUtils.CopyMeshTextFontData);
            PrefabUtils.CopyParams<Text>(this, existingCartridge, "PanelNormal/ActualTitle", PrefabUtils.CopyTextFontData);
            PrefabUtils.CopyParams<Text>(this, existingCartridge, "PanelNormal/DevicesTitle", PrefabUtils.CopyTextFontData);

        }

        public override void BuildUpdate(RocketBinaryWriter writer, ushort networkUpdateType) {
            base.BuildUpdate(writer, networkUpdateType);
            if (Thing.IsNetworkUpdateRequired(NETWORK_UPDATE_ID, networkUpdateType)) {
                for (int i = 0; i < _registerBuffer.Length; i++) {
                    writer.WriteDouble(_registerBuffer[i]);
                }

                lock (ChangeList) {
                    writer.WriteInt16((short)ChangeList.Count);
                    foreach (short index in ChangeList.Keys) {
                        writer.WriteInt16(index);
                        writer.WriteDouble(ChangeList[index]);
                    }
                    ChangeList.Clear();
                }
            }
        }

        private const uint NETWORK_UPDATE_ID = 512;
        private const LogicType LOGIC_TYPE = (LogicType)500;

        public override void ProcessUpdate(RocketBinaryReader reader, ushort networkUpdateType) {
            base.ProcessUpdate(reader, networkUpdateType);
            if (Thing.IsNetworkUpdateRequired(NETWORK_UPDATE_ID /*0x20*/, networkUpdateType)){
                for (int i = 0; i < _registerBuffer.Length; i++) {
                    _registerBuffer[i] = reader.ReadDouble();
                }

                int readLength = reader.ReadInt16();
                for (int i = 0; i < readLength; i++) {
                    int offset = reader.ReadInt16();
                    double data = reader.ReadDouble();
                    _stackBuffer[offset] = data;
                }
            }
        }

        public override void OnInteractableUpdated(Interactable interactable) {
            base.OnInteractableUpdated(interactable);
        }
        
        
        

        public Device ScannedDevice {
            get { return !(bool)(Object)RootParent || !RootParent.HasAuthority || !(bool)(Object)CursorManager.CursorThing ? null : CursorManager.CursorThing as Device; }
        }

        public override void OnMainTick() {
            base.OnMainTick();
                if (GameManager.RunSimulation) {
                    if (ScannedDevice != null && _interpretDevice != ScannedDevice) {
                        _interpretDevice = ScannedDevice;
                    }

                    FetchCPUData();
                } else {
                    var scannedDevice = ScannedDevice;
                    if (scannedDevice != _lastScannedDevice2) {
                        double refIdToSend;
                        if (scannedDevice != null) {
                            refIdToSend = ScannedDevice.ReferenceId;
                        } else {
                            refIdToSend = 0;
                        }

                        new SetLogicValueMessage  {
                            DeviceReferenceId = ReferenceId, LogicType = LOGIC_TYPE, LogicValue = refIdToSend
                        }.SendToServer();
                    }

                    _lastScannedDevice2 = scannedDevice;
                }

                ReadProcessorState();
            
        }
        
        

        public override bool CanLogicWrite(LogicType logicType) {
            //ConsoleWindow.Print("CanLogicWrite( " + logicType + ")");
            //if (logicType == LOGIC_TYPE) {
            //    return true;
            //}
            return base.CanLogicWrite(logicType);
        }

        public override void SetLogicValue(LogicType logicType, double value) {
            //ConsoleWindow.Print("SetLogicType( " + logicType + ")="  + value);
            if (logicType == LOGIC_TYPE) {
                //ConsoleWindow.Print("Received device id "  + (long)value);
                if (value > 0) {
                    _interpretDevice = Find<CircuitHousing>((long)value);
                } else {
                    _interpretDevice = null;
                }
                //ConsoleWindow.Print(_interpretDevice != null?_interpretDevice.ToString():"NullDevice");
            }else{
                base.SetLogicValue(logicType, value);
            }
        }
 
        private readonly double[] _registerBuffer = new double[19];

        private readonly double[] _stackBuffer = new double[512];

        private readonly double[] _stackCheck = new double[512]; 

        private const int LineNumberOffset = 18;
        private const int StackPointerOffset = 16;
        private const int ReturnAddressOffset = 17;

        private void FetchCPUData() {
            if (_interpretDevice != null) {
                //ConsoleWindow.Print("CW.P: FetchCPUData()");
                //Debug.Log("D.L: FetchCPUData()");
                CircuitHousing housing = _interpretDevice as CircuitHousing;
                if (housing != null) {
                    Slot chipSlot = housing.Slots[0];
                    ProgrammableChip programmableChip = chipSlot.Get<ProgrammableChip>();
                    if (programmableChip) { 
                        double[] registers = CopyRegisters(programmableChip);
                        bool changed = false;
                        for (int i = 0; i < registers.Length; i++) {
                            double old = _registerBuffer[i];
                            _registerBuffer[i] = registers[i];
                            changed |= (!old.Equals(registers[i]));
                        }

                        _registerBuffer[LineNumberOffset] = programmableChip.LineNumber;
                        lock (ChangeList) {
                            double[] stack = CopyStack(programmableChip);
                            for (short i = 0; i < stack.Length; i++) {
                                double o = _stackCheck[i];
                                _stackBuffer[i] = _stackCheck[i] = stack[i];
                                if (o != _stackCheck[i]) {
                                    ChangeList.TryAdd(i, _stackCheck[i]);
                                }
                            }

                            changed |= ChangeList.Count > 0;
                        }

                        if (changed && NetworkManager.IsServer) {
                            NetworkUpdateFlags |= (ushort)NETWORK_UPDATE_ID;
                        }
                    }
                }
            }
        }


        private double[] CopyRegisters(ProgrammableChip chip) {
            FieldInfo f = chip.GetType().GetField("_Registers", BindingFlags.NonPublic | BindingFlags.Instance);
            var retVal = (double[])f.GetValue(chip);
            return retVal;
        }
        private double[] CopyStack(ProgrammableChip chip) {
            FieldInfo f = chip.GetType().GetField("_Stack", BindingFlags.NonPublic | BindingFlags.Instance);
            var retVal = (double[])f.GetValue(chip);
            return retVal;
        }

        private void ReadProcessorState() {
            _scannedDevice = ScannedDevice;
            lock (_outputText) {
                if (_scannedDevice != null) {
                    if (_lastScannedDevice != _scannedDevice) {
                        _needTopScroll = true;
                    }
                    _lastScannedDevice = _scannedDevice;
                    _selectedText = _scannedDevice.DisplayName.ToUpper();
                    CircuitHousing housing = _scannedDevice as CircuitHousing;
                    if (housing != null) {
                        Slot chipSlot = housing.Slots[0];
                        ProgrammableChip programmableChip = chipSlot.Get<ProgrammableChip>();
                        if (programmableChip) {
                            StringBuilder stringBuilder = new StringBuilder();
                            if (programmableChip.CompilationError) {
                                stringBuilder.Append("Compilation error");
                                stringBuilder.Append(": ");
                                stringBuilder.AppendFormat("<color=#20B2AA>{0}</color>", programmableChip.CompilationError.ToString());
                            } else {
                                stringBuilder.Append("Line number");
                                stringBuilder.Append(": ");
                                stringBuilder.AppendFormat("<color=#62B8E9>{0,3:D}</color>   ", (int)_registerBuffer[LineNumberOffset]);
                                stringBuilder.Append("Setting");
                                stringBuilder.Append(": ");
                                stringBuilder.AppendFormat("<color=#{1}>{0,19}</color>", $"{housing.Setting:0.#################}", housing.Setting == 0?"707070":"20B2AA");
                                stringBuilder.Append("\n");
                                stringBuilder.Append("Registers");
                                stringBuilder.Append(": \n");
                                for (int i = 0; i < 18; i++) {
                                    if (i > 0 && i % 2 == 0) {
                                        stringBuilder.Append("\n");
                                    }
                                    stringBuilder.AppendFormat("<color=#62B8E9>{0,3}</color>=<color=#{2}>{1,19}</color>  ", (i == StackPointerOffset?"sp":(i == ReturnAddressOffset?"ra":$"r{i:D}")), $"{_registerBuffer[i]:0.#################}", _registerBuffer[i] == 0?"707070":"20B2AA");

                                }
                                stringBuilder.AppendFormat("\n \n");
                                stringBuilder.Append("Stack");
                                stringBuilder.Append(": \n");
                                var sp = (int)_registerBuffer[StackPointerOffset];
                                int start = Math.Min(Math.Max(sp - 3, 0), 512);
                                int end = Math.Min(Math.Max(sp + 4, 0), 512);
                                for (int i = start; i < end; i++) {
                                    int rel = i - sp;
                                    string addr = $"sp{(rel > 0 ? "+" : "")}{rel:D}";
                                    stringBuilder.AppendFormat("<color=#62B8E9>{0,6}</color> (<color=#62B8E9>{3,3}</color>)=<color=#{2}>{1,19}</color>\n", addr, $"{_stackBuffer[i]:0.#################}", _stackBuffer[i] == 0?"707070":"20B2AA", $"{i:D}");
                                }
                                stringBuilder.Append("\n");
                                for (int i = 0; i < 512; i++) {
                                    if (i > 0 && i % 2 == 0) {
                                        stringBuilder.Append("\n");
                                    }
                                    stringBuilder.AppendFormat("<color=#62B8E9>{0,3}</color>=<color=#{2}>{1,19}</color>  ", $"{i:D}", $"{_stackBuffer[i]:0.#################}", _stackBuffer[i] == 0?"707070":"20B2AA");

                                }
                            }
                            stringBuilder.Append("\n");

                            _outputText = stringBuilder.ToString();
                            return;
                        }
                    }
                }
                _selectedText = NotApplicableString;
                _outputText = string.Empty;                        
            }
            
        }

        public override void OnScreenUpdate() {
            base.OnScreenUpdate();
            if (_needTopScroll) {
                _needTopScroll = false;
                _scrollPanel.SetScrollPosition(0.0f);
            }

            SelectedTitle.text = _selectedText;
            _displayTextMesh.text = _outputText;
            _scrollPanel.SetContentHeight(_displayTextMesh.preferredHeight);
        }

        public double Setting { get; set; }
    }
}