using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Xml.Serialization;
using Assets.Scripts;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Motherboards;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.UI;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using HarmonyLib;
using JetBrains.Annotations;
using LaunchPadBooster.Networking;
using ridorana.IC10Inspector.Utilities;
using StationeersMods.Interface;
using TMPro;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UI;
using Console = System.Console;
using Object = System.Object;

namespace ridorana.IC10Inspector.Objects.Items {
    public class DebugMotherboard : Motherboard, IPatchable, ISetable, TickManager.ITickable {

        
        [XmlInclude(typeof(ChipDebugData))]
        public class ChipDebugData {
            [XmlElement] 
            public long deviceID;
            [XmlElement] 
            public bool slowMode;
            [XmlElement] 
            public bool step;
            [XmlElement] 
            public bool pause;

            public bool IsDefault() {
                return !step && !pause && !slowMode;
            }
        }

        [HarmonyPatch(typeof(ProgrammableChip), "Execute", typeof(int))]
        public static class PatchProgrammableChip {
            
            [UsedImplicitly]
            static bool Prefix(ProgrammableChip __instance, ref int runCount) {
                if (ChipDebugManager._debugDatas.TryGetValue(__instance.ReferenceId, out var data)) {
                    if (data.step) {
                        data.step = false;
                        runCount = 1;
                    }else if (data.pause) {
                        runCount = 0;
                    }else if (data.slowMode) {
                        runCount = 1;
                    }
                }
                return true;
            }
        }
        
        
        public static class ChipDebugManager {
            public static Dictionary<long, ChipDebugData> _debugDatas = new();

            public static ChipDebugData PushMonitoredChip(ProgrammableChip chip) {
                if (_debugDatas.TryGetValue(chip.ReferenceId, out var monitoredChip)) {
                    return monitoredChip;
                }
                var chipDebugData = new ChipDebugData {deviceID = chip.ReferenceId, slowMode = false, pause = false, step = false};
                _debugDatas.Add(chip.ReferenceId, chipDebugData);
                return chipDebugData;
            }

            public static void SetPaused(ProgrammableChip chip, bool state) {
                var info = PushMonitoredChip(chip);
                info.pause = state;
                if (info.IsDefault()) {
                    UnregisterChip(chip);
                }
            }
            public static void SetSlow(ProgrammableChip chip, bool state) {
                var info = PushMonitoredChip(chip);
                info.slowMode = state;
                if (info.IsDefault()) {
                    UnregisterChip(chip);
                }
            }

            public static void SetStep(ProgrammableChip chip, bool state) {
                var info = PushMonitoredChip(chip);
                info.step = state;
                if (info.IsDefault()) {
                    UnregisterChip(chip);
                }
            }

            public static void FlagStep(ProgrammableChip chip) {
                var info = PushMonitoredChip(chip);
                info.pause = true;
                info.step = true;
                if (info.IsDefault()) {
                    UnregisterChip(chip);
                }
            }

            public static void TogglePause(ProgrammableChip chip) {
                var info = PushMonitoredChip(chip);
                if (info.pause && info.step) {
                    info.step = false;
                }
                info.pause = !info.pause;
                if (info.IsDefault()) {
                    UnregisterChip(chip);
                }
            }

            public static void ToggleSlowmode(ProgrammableChip chip) {
                var info = PushMonitoredChip(chip);
                info.slowMode = !info.slowMode;
                if (info.IsDefault()) {
                    UnregisterChip(chip);
                }
            }

            public static void UnregisterChip(ProgrammableChip chip) {
                _debugDatas.Remove(chip.ReferenceId);
            }

            public static bool IsPaused(ProgrammableChip chip) {
                if (!_debugDatas.ContainsKey(chip.ReferenceId)) {
                    return false;
                }
                return _debugDatas[chip.ReferenceId].pause;
            }
            
            public static bool IsWaitingStep(ProgrammableChip chip) {
                if (!_debugDatas.ContainsKey(chip.ReferenceId)) {
                    return false;
                }
                return _debugDatas[chip.ReferenceId].step;
            }
            
            public static bool IsRunningOnReducedSpeed(ProgrammableChip chip) {
                if (!_debugDatas.ContainsKey(chip.ReferenceId)) {
                    return false;
                }
                return _debugDatas[chip.ReferenceId].slowMode;
            }
        }
        

        private class MarkerData {
            public int row;
            public int column;

            public MarkerData(int row, int column) {
                this.row = row;
                this.column = column;
            }

            public override string ToString() {
                return $"{nameof(row)}: {row}, {nameof(column)}: {column}";
            }
        }

        private class RegMarker : MarkerData {
            public int Index;
            public double Value;
            
            public RegMarker(int row, int column, int index, double value) : base(row, column) {
                Index = index;
                Value = value;
            }

            public override string ToString() {
                return $"{base.ToString()}, {nameof(Index)}: {Index}, {nameof(Value)}: {Value}";
            }
        }

        private class StackMarker : MarkerData {

            public int Address;
            public double Value;
            
            public StackMarker(int row, int column, int address, double value) : base(row, column) {
                Address = address;
                Value = value;
            }
            
            public override string ToString() {
                return $"{base.ToString()}, {nameof(Address)}: {Address}, {nameof(Value)}: {Value}";
            }
        }

        private List<MarkerData> _markers = new();
        private int _rowCount;
        
        private const uint NETWORK_UPDATE_ID = 512;
        private const LogicType SETDEVICE_LOGIC_TYPE = (LogicType)500;
        private const LogicType SETSELDEVICE_LOGIC_TYPE = (LogicType)501;
        private const LogicType COMMAND_LOGIC_TYPE = (LogicType)502;

        private enum LogicCommands {
            NoCommand = 0,
            TogglePause = 1,
            ToggleSlow = 2,
            Step = 3,
            SlowOn = 4,
            SlowOff = 5,
            PauseOn = 6,
            PauseOff = 7,
        }

        private const int LineNumberOffset = 18;
        private const int CPUStatusOffset = 19;
        private const int StackPointerOffset = 16;
        private const int ReturnAddressOffset = 17;
        private const int ColumnCount = 5;
        
        private const string DropdownLabelPath = "PanelNormal/AccessibleProgammableChipList/Label";
        private const string DropdownTemplateLabelPath = "PanelNormal/AccessibleProgammableChipList/Template/DropdownViewport/DropdownContent/Item/Item Label";
        private const string DropdownPath = "PanelNormal/AccessibleProgammableChipList";
        private const string DropdownTemplateItemPath = "PanelNormal/AccessibleProgammableChipList/Template/DropdownViewport/DropdownContent/Item";
        private const string DropdownTemplateScrolbarPath = "PanelNormal/AccessibleProgammableChipList/Template/DropdownScrollbar";
        private const string PauseButtonPath = "PanelNormal/PauseButton";
        private const string SlowmodeButtonPath = "PanelNormal/SlowModeButton";
        private const string StepButtonPath = "PanelNormal/StepButton";
        private const string TitlePath = "PanelNormal/ScreenTitle";
        private const string DropdownArrowPath = "PanelNormal/AccessibleProgammableChipList/Arrow";
        private const string DropdownTemplateItemBackgroundPath = "PanelNormal/AccessibleProgammableChipList/Template/DropdownViewport/DropdownContent/Item/Item Background";
        private const string DropdownTemplateItemCheckmarkPath = "PanelNormal/AccessibleProgammableChipList/Template/DropdownViewport/DropdownContent/Item/Item Checkmark";
        private const string DropdownTemplateScrollbarHandlePath = "PanelNormal/AccessibleProgammableChipList/Template/DropdownScrollbar/Sliding Area/DropdownScrollHandle";
        private const string MainTextFieldPath = "PanelNormal/ScrollPanel/Viewport/Content/ContentText";
        private const string StatusTextPath = "PanelNormal/StatusText";

        private static readonly string NotApplicableString = "N/A";
        [FormerlySerializedAs("_displayTextMesh")] [SerializeField] private TextMeshProUGUI txtDataText;
        
        private Canvas _templateCanvas;
        
        private readonly List<ICircuitHolder> _circuitHolders = new List<ICircuitHolder>();
        //public Text SelectedTitle;

        private const int RegisterBufferCount = 20;
        private const int StackBufferCount = 512;

        private readonly double[] _registerBuffer = new double[RegisterBufferCount];

        private readonly double[] _stackBuffer = new double[StackBufferCount];

        private readonly double[] _stackCheck = new double[StackBufferCount];

        private readonly Dictionary<Int16, double> ChangeList = new();
        private Device _interpretDevice;
        private Device _lastScannedDevice;
        private Device _lastScannedDevice2;
        private bool _needTopScroll;
        private string _outputText = string.Empty;
        private string _statusText = string.Empty;
        private Device _scannedDevice;
        private bool _DevicesChanged;

        [FormerlySerializedAs("_scrollPanel")] [SerializeField]
        protected ScrollPanel scpDataScrollpanel;
        
        [FormerlySerializedAs("AccessibleProgammableChipList")] [SerializeField]
        private Dropdown AccessibleProgrammableChipList;

        [FormerlySerializedAs("DropDownTemplate")] [SerializeField]
        private GameObject cboChipsItemTemplate;
        
        [FormerlySerializedAs("ScreenTitle")] [SerializeField]
        private Text txtScreenTitle;

        [SerializeField]
        private TextMeshProUGUI txtStatusField;

        [FormerlySerializedAs("ChipList")] [SerializeField]
        private GameObject lstChips;

        [FormerlySerializedAs("StepButton")] [SerializeField]
        private GameObject btnStep;
        
        [FormerlySerializedAs("StopButton")] [SerializeField]
        private GameObject btnStop;

        [FormerlySerializedAs("SlowDownButton")] [SerializeField]
        private GameObject btnSlowDown;
        
        private string _selectedText = string.Empty;

        public override void OnAssignedReference() {
            base.OnAssignedReference();
            TickManager.RegisterTickable(this);
        }

        public override void OnDestroy() {
            TickManager.UnregisterTickable(this);
            Device selectedDevice = GetSelectedDevice(SelectedDeviceIndex);
            if (selectedDevice is ICircuitHolder holder) {
                ProgrammableChip chip = PrefabUtils.GetChipFromHousing(holder);
                if (chip != null) {
                    ChipDebugManager.UnregisterChip(chip);
                }
            }

            base.OnDestroy();
        }
        
        public void OnStepForward() {
            if(GameManager.RunSimulation){
                Device selectedDevice = GetSelectedDevice(SelectedDeviceIndex);
                if (selectedDevice is ICircuitHolder holder) {
                    ProgrammableChip chip = PrefabUtils.GetChipFromHousing(holder);
                    if (chip != null) {
                        ChipDebugManager.FlagStep(chip);
                    }
                }
            } else {
                new SetLogicValueMessage {
                    DeviceReferenceId = ReferenceId, LogicType = COMMAND_LOGIC_TYPE, LogicValue = (double)LogicCommands.Step
                }.SendToServer();
            }
        }

        public void OnSlowModeEnable() {
            if(GameManager.RunSimulation){
                Device selectedDevice = GetSelectedDevice(SelectedDeviceIndex);
                if (selectedDevice is ICircuitHolder holder) {
                    ProgrammableChip chip = PrefabUtils.GetChipFromHousing(holder);
                    if (chip != null) {
                        ChipDebugManager.ToggleSlowmode(chip);
                    }
                }
            } else {
                new SetLogicValueMessage {
                    DeviceReferenceId = ReferenceId, LogicType = COMMAND_LOGIC_TYPE, LogicValue = (double)LogicCommands.ToggleSlow
                }.SendToServer();
            }
        }

        public void OnTogglePause() {
            if (GameManager.RunSimulation) {
                Device selectedDevice = GetSelectedDevice(SelectedDeviceIndex);
                if (selectedDevice is ICircuitHolder holder) {
                    ProgrammableChip chip = PrefabUtils.GetChipFromHousing(holder);
                    if (chip != null) {
                        ChipDebugManager.TogglePause(chip);
                    }
                }
            } else {
                new SetLogicValueMessage {
                    DeviceReferenceId = ReferenceId, LogicType = COMMAND_LOGIC_TYPE, LogicValue = (double)LogicCommands.TogglePause
                }.SendToServer();
            }
        }
        

        public void PatchOnLoad() {
            var existingMotherboard = StationeersModsUtility.FindPrefab("MotherboardProgrammableChip");
            var existingCartridge = StationeersModsUtility.FindPrefab("CartridgeConfiguration");

            Thumbnail = existingMotherboard.Thumbnail;
            Blueprint = existingMotherboard.Blueprint;

            var erenderer = existingMotherboard.GetComponent<MeshRenderer>();
            var renderer = GetComponent<MeshRenderer>();
            renderer.materials = erenderer.materials;
            var emesh = existingMotherboard.GetComponent<MeshFilter>();
            var mesh = GetComponent<MeshFilter>();
            mesh.mesh = emesh.mesh;

            //Component[] localComps = GetComponentsInChildren(typeof(Component));
            //Component[] factoryComps = existing.GetComponentsInChildren(typeof(Component));

            //PrefabUtils.CopyParams(this, existingCartridge, "PanelNormal/Title", CopyTextFontData);
            //PrefabUtils.CopyParams<TextMeshProUGUI>(this, existingCartridge, "PanelNormal/ScrollPanel/Viewport/Content/Text", (target, source) => {
            //    CopyMeshTextFontData(target, source);
            //    target.fontSize = 9;
            //    target.font = InputSourceCode.Instance.LineOfCodePrefab.FormattedText.font;
            //});
            //PrefabUtils.CopyParams(this, existingCartridge, "PanelNormal/Selected", CopyMeshTextFontData);
            //PrefabUtils.CopyParams(this, existingCartridge, "PanelNormal/ActualTitle", CopyTextFontData);
            //PrefabUtils.CopyParams(this, existingCartridge, "PanelNormal/DevicesTitle", CopyTextFontData);
            PrefabUtils.CopyParams<Text>(this, existingMotherboard, TitlePath, "ProgrammingWindow/ScreenTitle", PrefabUtils.CopyTextFontData);
            PrefabUtils.CopyParams<Text>(this, existingMotherboard, DropdownLabelPath, "ProgrammingWindow/AccessibleProgammableChipList/Label", PrefabUtils.CopyTextFontData);
            PrefabUtils.CopyParams<Image>(this, existingMotherboard, DropdownArrowPath, "ProgrammingWindow/AccessibleProgammableChipList/Arrow", PrefabUtils.CopyImageData);
            PrefabUtils.CopyParams<Image>(this, existingMotherboard, DropdownTemplateItemBackgroundPath, "ProgrammingWindow/AccessibleProgammableChipList/Template/Viewport/Content/Item/Item Background", PrefabUtils.CopyImageData);
            PrefabUtils.CopyParams<Image>(this, existingMotherboard, DropdownTemplateItemCheckmarkPath, "ProgrammingWindow/AccessibleProgammableChipList/Template/Viewport/Content/Item/Item Checkmark", PrefabUtils.CopyImageData);
            PrefabUtils.CopyParams<Text>(this, existingMotherboard, DropdownTemplateLabelPath, "ProgrammingWindow/AccessibleProgammableChipList/Template/Viewport/Content/Item/Item Label", PrefabUtils.CopyTextFontData);
            PrefabUtils.CopyParams<Image>(this, existingMotherboard, DropdownTemplateScrolbarPath, "ProgrammingWindow/AccessibleProgammableChipList/Template/Scrollbar", PrefabUtils.CopyImageData);
            PrefabUtils.CopyParams<Image>(this, existingMotherboard, DropdownTemplateScrollbarHandlePath, "ProgrammingWindow/AccessibleProgammableChipList/Template/Scrollbar/Sliding Area/Handle", PrefabUtils.CopyImageData);
            PrefabUtils.CopyParams<Image>(this, existingMotherboard, StepButtonPath, "ProgrammingWindow/ExportButton", PrefabUtils.CopyImageData);
            PrefabUtils.CopyParams<Image>(this, existingMotherboard, SlowmodeButtonPath, "ProgrammingWindow/ExportButton", PrefabUtils.CopyImageData);
            PrefabUtils.CopyParams<Image>(this, existingMotherboard, PauseButtonPath, "ProgrammingWindow/ExportButton", PrefabUtils.CopyImageData);
            PrefabUtils.CopyParams<Button>(this, existingMotherboard, StepButtonPath, "ProgrammingWindow/ExportButton", PrefabUtils.CopyButtonStyleRoze);
            PrefabUtils.CopyParams<Button>(this, existingMotherboard, SlowmodeButtonPath, "ProgrammingWindow/ExportButton", PrefabUtils.CopyButtonStyleRoze);
            PrefabUtils.CopyParams<Button>(this, existingMotherboard, PauseButtonPath, "ProgrammingWindow/ExportButton", PrefabUtils.CopyButtonStyleRoze);
            PrefabUtils.CopyParams<Text>(this, existingMotherboard, StepButtonPath + "/Text", "ProgrammingWindow/ExportButton/Text", PrefabUtils.CopyTextFontData);
            PrefabUtils.CopyParams<Text>(this, existingMotherboard, SlowmodeButtonPath + "/Text", "ProgrammingWindow/ExportButton/Text", PrefabUtils.CopyTextFontData);
            PrefabUtils.CopyParams<Text>(this, existingMotherboard, PauseButtonPath + "/Text", "ProgrammingWindow/ExportButton/Text", PrefabUtils.CopyTextFontData);
            PrefabUtils.CopyParams<Image>(this, existingMotherboard, DropdownArrowPath, "ProgrammingWindow/AccessibleProgammableChipList/Arrow", PrefabUtils.CopyImageData);
            PrefabUtils.CopyParams<TextMeshProUGUI>(this, existingCartridge, MainTextFieldPath, "PanelNormal/ScrollPanel/Viewport/Content/Text", (target, source) => { 
                PrefabUtils.CopyMeshTextFontData(target, source);
                target.fontSize = 9;
                target.font = InputSourceCode.Instance.LineOfCodePrefab.FormattedText.font;
            });             
            PrefabUtils.CopyParams<TextMeshProUGUI>(this, existingCartridge, StatusTextPath, "PanelNormal/ScrollPanel/Viewport/Content/Text", (target, source) => { 
                PrefabUtils.CopyMeshTextFontData(target, source);
                target.fontSize = 14;
                target.verticalAlignment = VerticalAlignmentOptions.Middle;
                target.font = InputSourceCode.Instance.LineOfCodePrefab.FormattedText.font;
            }); 
            
            PrefabUtils.RozeColorize<Button>(this, StepButtonPath);
            PrefabUtils.ColorizeCallback<Text> buttonText = target => {
                PrefabUtils.ColorizeUIButtonText(target);
                target.fontSize-= 4;
            };
            PrefabUtils.RozeColorize(this, StepButtonPath + "/Text", buttonText);
            PrefabUtils.RozeColorize<Button>(this, SlowmodeButtonPath);
            PrefabUtils.RozeColorize(this, SlowmodeButtonPath + "/Text", buttonText);
            PrefabUtils.RozeColorize<Button>(this, PauseButtonPath);
            PrefabUtils.RozeColorize(this, PauseButtonPath + "/Text", buttonText);
            PrefabUtils.RozeColorize<Scrollbar>(this, DropdownTemplateScrolbarPath);
            PrefabUtils.RozeColorize<Toggle>(this, DropdownTemplateItemPath);
            PrefabUtils.RozeColorize<Dropdown>(this, DropdownPath);
            PrefabUtils.RozeColorize<Text>(this, DropdownLabelPath, PrefabUtils.ColorizeUIButtonText);
            PrefabUtils.RozeColorize<Text>(this, DropdownTemplateLabelPath, PrefabUtils.ColorizeUIButtonText);

        }
        
        public enum IC10ValueType {
            None,
            Register,
            Stack
        }

        public static void SetValueOnChip(Thing thing, IC10ValueType type, int index, double value) {
            ProgrammableChip chip;
            if (thing is ICircuitHolder housing) {
                chip = PrefabUtils.GetChipFromHousing(housing);
            }else if (thing is ProgrammableChip tchip) {
                chip = tchip;
            } else {
                Console.Write("Thing " + (thing!=null?thing:"Null") + " is not a chip!" );
                throw new Exception("Thing is invalid");
            }
            if (chip) {
                switch (type) {
                    case IC10ValueType.Register: {
                        FieldInfo f = chip.GetType().GetField("_Registers", BindingFlags.NonPublic | BindingFlags.Instance);
                        double[] retVal = (double[])f.GetValue(chip);
                        retVal[index] = value;
                        break;
                    }
                    case IC10ValueType.Stack: {
                        FieldInfo f = chip.GetType().GetField("_Stack", BindingFlags.NonPublic | BindingFlags.Instance);
                        double[] retVal = (double[])f.GetValue(chip);
                        retVal[index] = value;
                        break;
                    }
                }
            } else {
                Console.Write("Chip is null");
            }
        }

        public class SetSelectedIC10Device : ProcessedMessage<SetSelectedIC10Device> {
            
            public override void Process(long hostId) {
                base.Process(hostId);
            }

            public override void Deserialize(RocketBinaryReader reader) {
                
            }

            public override void Serialize(RocketBinaryWriter writer) {
                
            }
        }
        
        public class SetIC10ValueMessage : ModNetworkMessage<SetIC10ValueMessage>
        {
            public long ThingId;
            public IC10ValueType Type;
            public int Index;
            public double Value;

            public override void Process(long hostId) {
                Console.Write("Process SetIC10ValueMessage\n");
                Thing thing = Find<Thing>(ThingId);
                SetValueOnChip(thing, Type, Index, Value);
            }

            public override void Deserialize(RocketBinaryReader reader)
            {
                Console.Write("SetIC10ValueMessage received!\n");
                ThingId = reader.ReadInt64();
                Type = (IC10ValueType)reader.ReadInt32();
                Index = reader.ReadInt32();
                Value = reader.ReadDouble();
                Console.Write($" -- ThingId={ThingId}, Type={Type}, Index={Index}, Value={Value}\n");
            }

            public override void Serialize(RocketBinaryWriter writer)
            {
                writer.WriteInt64(ThingId);
                writer.WriteInt32((int)Type);
                writer.WriteInt32(Index);
                writer.WriteDouble(Value);
            }
        }
        
        private delegate void ChipCommand(ProgrammableChip chip);

        public override void SetLogicValue(LogicType logicType, double value) {
            //ConsoleWindow.Print("SetLogicType( " + logicType + ")="  + value);
            switch (logicType) {
                case SETDEVICE_LOGIC_TYPE: {
                    if (value > 0) {
                        _interpretDevice = Find<CircuitHousing>((long)value);
                    } else {
                        _interpretDevice = null;
                    }

                    break;
                }
                case COMMAND_LOGIC_TYPE: {
                    LogicCommands commands = (LogicCommands)value;
                    var selectedDevice = GetSelectedDevice(SelectedDeviceIndex);
                    Console.Write("Logic Command " + commands + "\n");
                    ChipCommand command = null;
                    switch (commands) {
                        case LogicCommands.Step: {
                            command = chip => ChipDebugManager.FlagStep(chip);
                            break;
                        }
                        case LogicCommands.TogglePause: {
                            command = chip => ChipDebugManager.TogglePause(chip);
                            break;
                        }
                        case LogicCommands.ToggleSlow: {
                            command = chip => ChipDebugManager.ToggleSlowmode(chip);
                            break;
                        }
                        case LogicCommands.PauseOn: {
                            command = chip => ChipDebugManager.SetPaused(chip, true);
                            break;
                        }
                        case LogicCommands.PauseOff: {
                            command = chip => ChipDebugManager.SetPaused(chip, false);
                            break;
                        }
                        case LogicCommands.SlowOn: {
                            command = chip => ChipDebugManager.SetSlow(chip, true);
                            break;
                        }
                        case LogicCommands.SlowOff: {
                            command = chip => ChipDebugManager.SetSlow(chip, false);
                            break;
                        }
                    }

                    if (command != null) {
                        if (selectedDevice is ICircuitHolder holder) {
                            ProgrammableChip chip = PrefabUtils.GetChipFromHousing(holder);
                            if (chip != null) {
                                command(chip);
                            } else {
                                Console.Write("No chip!\n");
                            }
                        } else {
                            Console.Write("No device!\n");
                        }
                    } else {
                        throw new Exception("Command not implemented");
                    }
                    break;
                }
                case SETSELDEVICE_LOGIC_TYPE: {
                    SelectedDeviceIndex = (int)value;
                    break;
                }
                default: {
                    base.SetLogicValue(logicType, value);
                    break;
                }
            }
        }

        public override bool IsOperable => _circuitHolders.Count > 0;

        public void OnValueChanged(Int32 value) {
            if (value >= _circuitHolders.Count) {
                return;
            }

            SelectedDeviceIndex = AccessibleProgrammableChipList.value;
        }
        
        public double Setting { get; set; }

        private Device GetSelectedDevice(int value) {
            if (value >= _circuitHolders.Count) {
                return null;
            }

            return _circuitHolders[value] as Device;
        }

        protected int _hoverRow = -1;
        protected int _hoverCol = -1;

        public void OnDataAreaExit() {
            ResetCursors();
        }

        public void ResetCursors() {
            _hoverCol = -1;
            _hoverRow = -1;
        }

        public void OnDataAreaMove(Vector2 position) {
            //Debug.Log("Clicked: " + position);
            Vector2 localPosition = new ();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(txtDataText.rectTransform, position, Camera.current, out localPosition);
            //Debug.Log(localPosition);
            _hoverRow = (int)((txtDataText.renderedHeight - localPosition.y) / txtDataText.renderedHeight * _rowCount);
            _hoverCol = (int)(localPosition.x / txtDataText.renderedWidth * _dataAreaColumnCount);
        }

        public void OnDataAreaClicked(Vector2 position) {
            //Debug.Log("Clicked: " + position);
            Vector2 localPosition = new ();
            RectTransformUtility.ScreenPointToLocalPointInRectangle(txtDataText.rectTransform, position, Camera.current, out localPosition);
            //Debug.Log(localPosition);
            float y = (txtDataText.renderedHeight - localPosition.y) / txtDataText.renderedHeight * _rowCount;
            float x = (localPosition.x / txtDataText.renderedWidth) * _dataAreaColumnCount;
            int row = (int)y;
            int column = (int)x;
            //Debug.Log(x + ", " + y + ", " + column + ", " + row);
            MarkerData match = null;
            lock (_markers) {
                foreach (MarkerData marker in _markers) {
                    if (marker.column == column && marker.row == row) {
                        match = marker;
                        break;
                    }
                }
            }

            if (match != null) {
                Debug.Log("Marker Match " + match.GetType().Name + "   " + match);
                Device d = GetSelectedDevice(SelectedDeviceIndex);
                if (d is ICircuitHolder holder) {
                    ProgrammableChip chip = PrefabUtils.GetChipFromHousing(holder);
                    if (match is RegMarker regMarker) {
                        SetRegister(chip, regMarker.Index, regMarker.Value);
                    } else if (match is StackMarker stackMarker) {
                        SetStack(chip, stackMarker.Address, stackMarker.Value);
                    }
                }
                
            }
        }

        public void OnTickableTick() {
            Device scannedDevice = GetSelectedDevice(SelectedDeviceIndex);
            if (GameManager.RunSimulation) {
                if (scannedDevice != null && _interpretDevice != scannedDevice) {
                    _interpretDevice = scannedDevice;
                }
 
                FetchCPUData();
            } else {
                if (scannedDevice != _lastScannedDevice2) {
                    double refIdToSend;
                    if (scannedDevice != null) {
                        refIdToSend = scannedDevice.ReferenceId;
                    } else {
                        refIdToSend = 0;
                    }
                    new SetLogicValueMessage {
                        DeviceReferenceId = ReferenceId, LogicType = SETSELDEVICE_LOGIC_TYPE, LogicValue = SelectedDeviceIndex
                    }.SendToServer();

                    //new SetLogicValueMessage {
                    //    DeviceReferenceId = ReferenceId, LogicType = SETDEVICE_LOGIC_TYPE, LogicValue = refIdToSend
                    //}.SendToServer();
                }

                _lastScannedDevice2 = scannedDevice;
            }

            ReadProcessorState();
        }

        public int SelectedDeviceIndex { get; set; }

        public override void BuildUpdate(RocketBinaryWriter writer, ushort networkUpdateType) {
            base.BuildUpdate(writer, networkUpdateType);
            if (IsNetworkUpdateRequired(NETWORK_UPDATE_ID, networkUpdateType)) {
                for (var i = 0; i < _registerBuffer.Length; i++) writer.WriteDouble(_registerBuffer[i]);

                lock (ChangeList) {
                    //if (ChangeList.Count > 0) {
                    //    Console.Write("Writing " + ChangeList.Count + " changes to clients\n");
                    //    Console.Write(ChangeList.ToString());
                    //    Console.Write("\n");
                    //}
                    writer.WriteInt16((short)ChangeList.Count);
                    foreach (var index in ChangeList.Keys) {
                        writer.WriteInt16(index);
                        writer.WriteDouble(ChangeList[index]);
                    }
                    
                    ChangeList.Clear();
                }
    
                //writer.WriteBoolean(_codeChanged);
                if (_codeChanged) {
                    lock (_codeLines) {
                        writer.WriteInt16((short)_codeLines.Length);
                        foreach (string line in _codeLines) {
                            writer.WriteAscii(AsciiString.Parse(line));
                        }
                        _codeChanged = false;
                    }
                } else {
                    writer.WriteInt16(0);
                }
            }
        }

        public override void ProcessUpdate(RocketBinaryReader reader, ushort networkUpdateType) {
            base.ProcessUpdate(reader, networkUpdateType);
            if (IsNetworkUpdateRequired(NETWORK_UPDATE_ID /*0x20*/, networkUpdateType)) {
                for (var i = 0; i < _registerBuffer.Length; i++) _registerBuffer[i] = reader.ReadDouble();

                int readLength = reader.ReadInt16();
                if (readLength > 0) {
                    Console.Write("Received " + readLength + " changes\n");
                }
                for (var i = 0; i < readLength; i++) {
                    int offset = reader.ReadInt16();
                    var data = reader.ReadDouble();
                    _stackBuffer[offset] = data;
                }

                int codeCount = reader.ReadInt16();
                if (codeCount > 0) {
                    for (int i = 0; i < codeCount; i++) {
                        AsciiString str = reader.ReadAscii();
                        _codeLines[i] = str.ToString();
                    }
                }
            }
        }


        public void OnEdit() {
            //ImGuiFPGAEditor.ShowEditor(this);
        }

        public override void OnInsertedToComputer(IComputer computer) {
            base.OnInsertedToComputer(computer);
            if (!_DevicesChanged)
            {
                _DevicesChanged = true;
                HandleDeviceListChange().Forget();
                StartCoroutine(SetDropdownTemplateLayer());
            }
            LoadConnected();
        }

        public override void OnDeviceListChanged() {
            base.OnDeviceListChanged();
            if (!_DevicesChanged)
            {
                _DevicesChanged = true;
                HandleDeviceListChange().Forget();
            }

            ResetCursors();
            LoadConnected();
        }

        public override void OnRemovedFromComputer(IComputer computer) {
            base.OnRemovedFromComputer(computer);
            AccessibleProgrammableChipList.options.Clear();
            LoadConnected();
        }

        private void LoadConnected() {
        }
        
        protected async UniTaskVoid HandleDeviceListChange()
        {
            if (!GameManager.IsMainThread)
            {
                await UniTask.SwitchToMainThread();
            }
            CancellationToken cancelToken = this.GetCancellationTokenOnDestroy();
            while (GameManager.GameState != GameState.Running)
            {
                await UniTask.NextFrame(cancelToken);
            }
            await UniTask.NextFrame(cancelToken);
            if (cancelToken.IsCancellationRequested || ParentComputer == null || !ParentComputer.AsThing().isActiveAndEnabled)
            {
                return;
            }
            List<ILogicable> list = ParentComputer.DeviceList();
            list.Sort((a, b) => {
                if (a == null && b == null) {
                    return 0;
                }
                if(a?.DisplayName == null) {
                    return 1;
                }
                if (b?.DisplayName == null) {
                    return -1;
                }
                return a.DisplayName.CompareTo(b.DisplayName);
            });
            AccessibleProgrammableChipList.options.Clear();
            _circuitHolders.Clear();
            foreach (ILogicable item2 in list)
            {
                if (item2 is ICircuitHolder item)
                {
                    AccessibleProgrammableChipList.options.Add(new Dropdown.OptionData(item2.DisplayName));
                    _circuitHolders.Add(item);
                }
            }
            AccessibleProgrammableChipList.RefreshShownValue();
            _DevicesChanged = false;
        }

        public override void OnInteractableUpdated(Interactable interactable) {
            base.OnInteractableUpdated(interactable);
        }


        public override ThingSaveData SerializeSave() {
            var saveData = new DebugMotherboardSaveData();
            var baseData = saveData as ThingSaveData;
            InitialiseSaveData(ref baseData);
            return saveData;
        }


        public override void DeserializeSave(ThingSaveData baseData) {
            base.DeserializeSave(baseData);
            if (baseData is not DebugMotherboardSaveData saveData) return;
            
            SelectedDeviceIndex = AccessibleProgrammableChipList.value = saveData.SelectedHolderIndex;
            
        }

        protected override void InitialiseSaveData(ref ThingSaveData baseData) {
            base.InitialiseSaveData(ref baseData);
            if (baseData is not DebugMotherboardSaveData saveData) return;
            
            saveData.SelectedHolderIndex = AccessibleProgrammableChipList.value;
        }

        [Flags]
        private enum CPUStatus {
            None = 0,
            CompileError = 1,
            Paused = 2,
            Slow = 8,
            Stepping = 4
        }
        
        private readonly string[] _codeLines = Enumerable.Repeat(String.Empty, CodeLinesTotal).ToArray(); //new string[CodeLinesBefore + CodeLinesAhead];

        private void FetchCPUData() {
            if (_interpretDevice != null) {
                //ConsoleWindow.Print("CW.P: FetchCPUData()");
                //Debug.Log("D.L: FetchCPUData()");
                if (_interpretDevice is ICircuitHolder housing) {
                    ProgrammableChip chip = PrefabUtils.GetChipFromHousing(housing);
                    if (chip) {
                        var registers = CopyRegisters(chip);
                        var changed = false;
                        for (var i = 0; i < registers.Length; i++) {
                            var old = _registerBuffer[i];
                            _registerBuffer[i] = registers[i];
                            changed |= !old.Equals(registers[i]);
                        }

                        _registerBuffer[LineNumberOffset] = chip.LineNumber;
                        

                        CPUStatus status = 
                            (chip.CompilationError?CPUStatus.CompileError:0) | 
                            (ChipDebugManager.IsPaused(chip)?CPUStatus.Paused:0) | 
                            (ChipDebugManager.IsRunningOnReducedSpeed(chip)?CPUStatus.Slow:0) | 
                            (ChipDebugManager.IsWaitingStep(chip)?CPUStatus.Stepping:0);
                        _registerBuffer[CPUStatusOffset] = (int)status;

                        CopyCodeLines(_codeLines, chip);
                        
                        lock (ChangeList) {
                            var stack = CopyStack(chip);
                            for (short i = 0; i < stack.Length; i++) {
                                var o = _stackCheck[i];
                                _stackBuffer[i] = _stackCheck[i] = stack[i];
                                if (o != _stackCheck[i]) {
                                    ChangeList.TryAdd(i, _stackCheck[i]);
                                }
                            }

                            changed |= ChangeList.Count > 0;
                            
                            //if (ChangeList.Count > 0) {
                            //    Console.Write("Scheduled " + ChangeList.Count + " changes to send to clients");
                            //}
                        }

                        if (changed && NetworkManager.IsServer) NetworkUpdateFlags |= (ushort)NETWORK_UPDATE_ID;
                    }
                }
            }
        }

        private bool _codeChanged = false;

        private const int CodeLinesBefore = 2;
        private const int CodeLinesTotal = 6;
        private const int StackValuesBefore = 3;
        private const int StackValuesAhead = 4;
        
        [MethodImpl(MethodImplOptions.NoOptimization)]
        private void CopyCodeLines(string[] codeLines, ProgrammableChip chip) {
            int lineNumber = (int)chip.LineNumber;
            FieldInfo field = chip.GetType().GetField("_LinesOfCode", BindingFlags.Instance | BindingFlags.NonPublic);
            //Debug.Log(field);
            var o = field.GetValue(chip);
            IList list = (IList)o;
            IEnumerable enumerable = (IEnumerable)o;
            //MethodInfo item = o.GetType().GetMethod("Item");
            //MethodInfo count = o.GetType().GetMethod("Count");
            //Debug.Log(o);
            int length = list.Count; //(int)count.Invoke(o);
            int start = Math.Min(Math.Max(lineNumber - CodeLinesBefore, 0), length);
            int end = Math.Min(start + CodeLinesTotal, length);
            int j = 0;
            bool codeChanged = false;
            FieldInfo LineOfCode = null;
            for (int i = start; i < end; i++) {
                string oldLine = codeLines[j];
                object v = list[i];
                if (LineOfCode == null && v != null) {
                    LineOfCode = v.GetType().GetField("LineOfCode", BindingFlags.Public | BindingFlags.Instance);
                }

                if (LineOfCode != null) {
                    int number = i - lineNumber;
                    var s = string.Format("{0}{1}", number >= 0?"+":"", number);
                    codeLines[j] = String.Format("pc{0,-2}: {1}", s, (string)LineOfCode.GetValue(v));
                    codeChanged |= String.Equals(oldLine, codeLines[j]);
                    j++;
                }
            }

            for (int i = j; i < (CodeLinesTotal); i++) {
                string oldLine = codeLines[i];
                codeLines[i] = string.Empty;
                codeChanged |= String.Equals(oldLine, codeLines[j]);
            }

            _codeChanged = codeChanged;

        }


        private double[] CopyRegisters(ProgrammableChip chip) {
            var f = chip.GetType().GetField("_Registers", BindingFlags.NonPublic | BindingFlags.Instance);
            var retVal = (double[])f.GetValue(chip);
            return retVal;
        }

        private double[] CopyStack(ProgrammableChip chip) {
            var f = chip.GetType().GetField("_Stack", BindingFlags.NonPublic | BindingFlags.Instance);
            var retVal = (double[])f.GetValue(chip);
            return retVal;
        }

        public override void FlashCircuit()
        {
            base.FlashCircuit();
            AccessibleProgrammableChipList.options.Clear();
            AccessibleProgrammableChipList.RefreshShownValue();
            _circuitHolders.Clear();
            _DevicesChanged = false;
        }
        
        private void ReadProcessorState() {
            _scannedDevice = GetSelectedDevice(SelectedDeviceIndex);
            lock (_outputText) lock(_statusText) lock(_markers) {
                if (_scannedDevice != null) {
                    if (_lastScannedDevice != _scannedDevice) _needTopScroll = true;
                    _markers.Clear();

                    _lastScannedDevice = _scannedDevice;
                    _selectedText = _scannedDevice.DisplayName.ToUpper();
                    if (_scannedDevice is ICircuitHolder housing) {
                        ProgrammableChip programmableChip = PrefabUtils.GetChipFromHousing(housing);
                        if (programmableChip) {
                            var stringBuilder = new StringBuilder();
                            int row = 0;
                            stringBuilder.Append("Line number");
                            stringBuilder.Append(": ");
                            stringBuilder.AppendFormat("<color=#62B8E9>{0,3:D}</color>   ", (int)_registerBuffer[LineNumberOffset]);
                            if (housing is ISetable thing) {
                                stringBuilder.Append("Setting");
                                stringBuilder.Append(": ");
                                stringBuilder.AppendFormat("<color=#{1}>{0,19}</color>", $"{thing.Setting:0.#################}", thing.Setting == 0 ? "707070" : "20B2AA");
                                _markers.Add(new MarkerData(row, 0));
                            }
                            stringBuilder.Append("\n");
                            row++;

                            stringBuilder.Append("Registers");
                            stringBuilder.Append(": \n");
                            row++;
                            for (var i = 0; i < 18; i++) {
                                if (i > 0 && i % ColumnCount == 0) {
                                    stringBuilder.Append("\n");
                                    row++;
                                }else if (i > 0) {
                                    stringBuilder.Append("  ");
                                }

                                string numericColor = _registerBuffer[i] == 0 ? "707070" : "20B2AA";
                                string registerColor = "62B8E9";
                                if (i % ColumnCount == _hoverCol && row == _hoverRow) {
                                    numericColor = "ff66cc";
                                    registerColor = "ff66cc";
                                }
                                
                                stringBuilder.AppendFormat("<color=#{3}>{0,3}</color>=<color=#{2}>{1,19}</color>", i == StackPointerOffset ? "sp" : i == ReturnAddressOffset ? "ra" : $"r{i:D}", $"{_registerBuffer[i]:0.#################}", numericColor, registerColor);
                                _markers.Add(new RegMarker(row, i % ColumnCount, i, _registerBuffer[i]));
                            }

                            stringBuilder.AppendFormat("\n \n");
                            row += 2;
                            stringBuilder.Append("Stack");
                            stringBuilder.Append(": \n");
                            row++;
                            var sp = (int)_registerBuffer[StackPointerOffset];
                            var start = Math.Min(Math.Max(sp - StackValuesBefore, 0), StackBufferCount);
                            var end = Math.Min(Math.Max(sp + StackValuesAhead, 0), StackBufferCount);
                            int codeIndex = 0;
                            for (var i = 0; i < 8; i++) {
                                var _sp = i + start;
                                var rel = _sp - sp;
                                string stackString;
                                if (_sp <= end) {
                                    var addr = $"sp{(rel >= 0 ? "+" : "")}{rel:D}";
                                    double value = _stackBuffer[_sp];
                                    string numericColor = value == 0 ? "707070" : "20B2AA";
                                    string registerColor = "62B8E9";

                                    if ((_hoverCol == 0 || _hoverCol == 1) && row == _hoverRow) {
                                        numericColor = "ff66cc";
                                        registerColor = "ff66cc";
                                    }

                                    stackString = String.Format("<color=#{4}>{0,6}</color> (<color=#62B8E9>{3,3}</color>)=<color=#{2}>{1,19}</color>", addr, $"{value:0.#################}", numericColor, $"{_sp:D}", registerColor);
                                    _markers.Add(new StackMarker(row, 0, _sp, value));
                                    _markers.Add(new StackMarker(row, 1, _sp, value));
                                } else {
                                    stackString = $"{String.Empty,32}";
                                }

                                string codeLine;
                                string pp = "";
                                if (_codeLines.Length > 0 && codeIndex < _codeLines.Length) {
                                    codeLine = _codeLines[codeIndex++].Trim();
                                    int p = codeLine.IndexOf(':');
                                    if (codeLine.Length > 0 && p > 0) {
                                        pp = codeLine.Substring(0, p);
                                        codeLine = codeLine.Substring(Math.Min(p + 2, codeLine.Length));
                                    }
                                    var acceptedStrings = new List<string>();
                                    var acceptedJumps = new List<string>();
                                    codeLine = codeLine.Substring(0, Math.Min(96, codeLine.Length));
                                    codeLine = String.Format("<color=#62B8E9>{1}</color>{2}<color=#ff9933>{0}</color>", Localization.ParseScript(Regex.Replace(codeLine, "([<>])", "<noparse>$1</noparse>"), ref acceptedStrings, ref acceptedJumps), pp, codeLine.Length == 0 ? "" : ": ");
                                } else {
                                    codeLine = "";
                                }
                                
                                stringBuilder.AppendFormat("{0}      {1}\n", stackString, codeLine);
                                row++;
                            }

                            stringBuilder.Append("\n");
                            row++;
                            for (var i = 0; i < StackBufferCount; i++) {
                                if (i > 0 && i % ColumnCount == 0) {
                                    stringBuilder.Append("\n");
                                    row++;
                                }
                                string numericColor = _stackBuffer[i] == 0 ? "707070" : "20B2AA";
                                string registerColor = "62B8E9";
                                if (i % ColumnCount == _hoverCol && row == _hoverRow) {
                                    numericColor = "ff66cc";
                                    registerColor = "ff66cc";
                                }
                                
                                stringBuilder.AppendFormat(String.Format("<color=#{3}>{0,3}</color>=<color=#{2}>{1,19}</color>  ", $"{i:D}", $"{_stackBuffer[i]:0.#################}", numericColor, registerColor));
                                _markers.Add(new StackMarker(row, i % ColumnCount, i, _stackBuffer[i]));
                            }

                            stringBuilder.Append("\n");
                            row++;
                            _rowCount = row;
                            _outputText = stringBuilder.ToString();

                            stringBuilder = new StringBuilder();
                            CPUStatus status = (CPUStatus)_registerBuffer[CPUStatusOffset];
                            if ((status & CPUStatus.CompileError) != 0) {
                                stringBuilder.Append("Compilation error");
                                stringBuilder.Append(": ");
                                stringBuilder.AppendFormat("<color=#ff6666>{0}</color>", programmableChip.CompilationError.ToString());
                            }else{
                                var errorCode = programmableChip.GetErrorCode();
                                if (!String.IsNullOrEmpty(errorCode)) {
                                    stringBuilder.Append($"RuntimeError: <color=#ff6666>{errorCode}</color> ");
                                } else {
                                    bool paused = (status & CPUStatus.Paused) != 0;
                                    bool slow = (status & CPUStatus.Slow) != 0;
                                    bool step = (status & CPUStatus.Stepping) != 0;
                                    if (slow) {
                                        stringBuilder.Append("<color=#a9a33f>Slow Rate</color> ");
                                    }

                                    if (paused) {
                                        stringBuilder.Append("<color=#b4820b>Paused</color> ");
                                    }

                                    if (step) {
                                        stringBuilder.Append("<color=#63b138>Stepping...</color> ");
                                    }

                                    if (!paused) {
                                        stringBuilder.Append("<color=#76b354>Running</color> ");
                                    }
                                }

                            }

                            _statusText = stringBuilder.ToString();

                            return;
                        }
                    }
                }

                _selectedText = NotApplicableString;
                _outputText = string.Empty;
            }
        }

        
        public override void Awake()
        {
            base.Awake();
            if ((bool)AccessibleProgrammableChipList)
            {
                AccessibleProgrammableChipList.ReplaceRaycasters();
            }
            if (_templateCanvas == null)
            {
                _templateCanvas = cboChipsItemTemplate.GetComponentInChildren<Canvas>(includeInactive: true);
            }
        }

        private IEnumerator SetDropdownTemplateLayer()
        {
            yield return Yielders.EndOfFrame;
            if (_templateCanvas == null)
            {
                _templateCanvas = cboChipsItemTemplate.GetComponentInChildren<Canvas>(includeInactive: true);
            }
            if (!(_templateCanvas == null))
            {
                cboChipsItemTemplate.gameObject.SetActive(value: true);
                _templateCanvas.enabled = true;
                _templateCanvas.overrideSorting = true;
                _templateCanvas.sortingOrder = 1;
                cboChipsItemTemplate.gameObject.SetActive(value: false);
            }
        }
        
        public override void UpdateEachFrame() {
            if (WorldManager.IsGamePaused)
                return;
            base.UpdateEachFrame();
            var compThing = ParentComputer as Thing;
            if (GameManager.IsBatchMode || ParentComputer == null || !compThing.OnOff || !compThing.Powered)
                return;
            OnScreenUpdate();
        }

        public void OnScreenUpdate() {
            if (_needTopScroll) {
                _needTopScroll = false;
                //var pos = txtDataText.rectTransform.anchoredPosition;
                //pos.y = 0;
                //txtDataText.rectTransform.anchoredPosition = pos;
                scpDataScrollpanel.SetScrollPosition(0.0f);
            }

            //SelectedTitle.text = _selectedText;
            txtDataText.text = _outputText;
            txtStatusField.text = _statusText;
            scpDataScrollpanel.SetContentHeight(txtDataText.preferredHeight);
        }

        private class ChangedStack {
            public short Offset;
            public double Data;

            public override string ToString() {
                return $"{nameof(Offset)}: {Offset}, {nameof(Data)}: {Data}";
            }
        }
        
        private static readonly int LabelHash = Animator.StringToHash("Label");
        private static readonly int LabelConfirmHash = Animator.StringToHash("LabelConfirm");
        private static readonly int LabelCancelHash = Animator.StringToHash("LabelCancel");
        private static float _dataAreaColumnCount = 5f;

        public void PlayCancelSound()
        {
            PlaySound(LabelCancelHash);
            InputWindow.OnCancel -= PlayCancelSound;
        }
        
        public void InputValue(string value, long thingID, IC10ValueType type, int index)
        {
            double result;
            double.TryParse(value, NumberStyles.Number, CultureInfo.CurrentCulture, out result);
            if (double.IsPositiveInfinity(result)) {
                result = double.MaxValue;
            }

            if (NetworkManager.IsClient) {
                NetworkClient.SendToServer(new SetIC10ValueMessage {
                    ThingId = GetSelectedDevice(SelectedDeviceIndex).ReferenceId,
                    Type = type,
                    Index = index,
                    Value = result
                });
            } else {
                Thing thing = Find<Thing>(thingID);
                SetValueOnChip(thing, type, index, result);
            }

            PlaySound(Labeller.LabelConfirmHash);
        }

        /*
        public class LInputWindow : InputWindow {



            public static bool ShowInputPanel(
                string title,
                string defaultText,
                int characterLimit = 256
                TMP_InputField.ContentType contentType = TMP_InputField.ContentType.Standard,
                int width = 600)
            {
                if (InputState != InputPanelState.None)
                    return false;
                _cursorDisabled = CursorManager.IsLocked;
                if (!_cursorDisabled)
                    CursorManager.SetCursor(false);
                InputState = InputPanelState.Waiting;
                Register(contentType);
                Instance._singleLineInputField.contentType = contentType;
                SetWidth(width);
                OutputRequired = true;
                Output2Required = false;
                Instance.SetVisible(true);
                Instance.TextUI.SetVisible(true);
                Instance.MultiTextUI.SetVisible(true);
                Instance.TitleText.text = title;
                Instance.InputText.text = defaultText ?? string.Empty;
                Instance.InputMultiText.text = string.Empty;
                Instance._singleLineInputField.text = defaultText ?? string.Empty;
                PreviousString = defaultText;
                Instance.InputType.text = _contentTypes.GetName(contentType) + " Input";
                WaitForInput().Forget();
                return true;
            }

        }*/
        
        public void SetRegister(Thing thing, int index, double currentValue) {
            if (!InputWindow.ShowInputPanel(string.Format("Set r{0:D}", index), currentValue.ToStringExact(), thing, InventoryManager.Parent, 32 /*0x20*/, TMP_InputField.ContentType.DecimalNumber))
                return;
            InputWindow.OnSubmit += (input, input2) => InputValue(input, thing.ReferenceId, IC10ValueType.Register, index);
            //PlaySound(LabelHash);
            //InputWindow.OnCancel += PlayCancelSound;
        }
        
        public void SetStack(Thing thing, int index, double currentValue) {
            if (!InputWindow.ShowInputPanel(string.Format("Set stack at addr {0:D}", index), currentValue.ToStringExact(), thing, InventoryManager.Parent, 32 /*0x20*/, TMP_InputField.ContentType.DecimalNumber))
                return;
            InputWindow.OnSubmit += (input, input2) => InputValue(input, thing.ReferenceId, IC10ValueType.Stack, index);
            //PlaySound(LabelHash);
            //InputWindow.OnCancel += PlayCancelSound;
        }

        public void OnScroll(Vector2 scrollDelta) {
            scpDataScrollpanel.OnScroll(scrollDelta);
        }
    }
}