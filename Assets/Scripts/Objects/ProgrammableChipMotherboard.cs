using System;
using System.Collections;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading;
using Assets.Scripts.GridSystem;
using Assets.Scripts.Inventory;
using Assets.Scripts.Networking;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;
using Assets.Scripts.Objects.Pipes;
using Assets.Scripts.Serialization;
using Assets.Scripts.UI;
using Assets.Scripts.Util;
using Cysharp.Threading.Tasks;
using Trading;
using UnityEngine;
using UnityEngine.UI;

namespace Assets.Scripts.Objects.Motherboards
{
	public class ProgrammableChipMotherboard : Motherboard, ISourceCode, IReferencable, IEvaluable
	{
		[Header("Programmable Chip Motherboard")]
		private bool _DevicesChanged;

		private readonly List<ICircuitHolder> _circuitHolders = new List<ICircuitHolder>();

		private Canvas _templateCanvas;

		[SerializeField]
		private Dropdown AccessibleProgrammableChipList;

		[SerializeField]
		private GameObject DropDownTemplate;

		[SerializeField]
		private Text SourceCode;

		[SerializeField]
		private Text SourceCodeLines;

		[SerializeField]
		private Camera ScreenshotCamera;

		[SerializeField]
		private Text ScreenTitle;

		[SerializeField]
		private GameObject ChipList;

		[SerializeField]
		private GameObject ImportButton;

		[SerializeField]
		private GameObject ExportButton;

		public List<string> AcceptedStrings = new List<string>();

		public List<string> AcceptedJumps = new List<string>();

		private AsciiString _sourceCode = AsciiString.Empty;

		public char[] SourceCodeCharArray { get; set; }

		public int SourceCodeWritePointer { get; set; }

		public override bool IsOperable => _circuitHolders.Count > 0;

		public override ThingSaveData SerializeSave()
		{
			ThingSaveData savedData;
			ThingSaveData result = (savedData = new ProgrammableChipMotherboardSaveData());
			InitialiseSaveData(ref savedData);
			return result;
		}

		public override void DeserializeSave(ThingSaveData savedData)
		{
			base.DeserializeSave(savedData);
			if (savedData is ProgrammableChipMotherboardSaveData programmableChipMotherboardSaveData)
			{
				SetSourceCode(programmableChipMotherboardSaveData.SourceCode);
				AccessibleProgrammableChipList.value = programmableChipMotherboardSaveData.DeviceIndex;
			}
		}

		protected override void InitialiseSaveData(ref ThingSaveData savedData)
		{
			base.InitialiseSaveData(ref savedData);
			if (savedData is ProgrammableChipMotherboardSaveData programmableChipMotherboardSaveData)
			{
				programmableChipMotherboardSaveData.SourceCode = GetSourceCode();
				programmableChipMotherboardSaveData.DeviceIndex = AccessibleProgrammableChipList.value;
			}
		}

		public override void SerializeOnJoin(RocketBinaryWriter writer)
		{
			base.SerializeOnJoin(writer);
			writer.WriteAscii(_sourceCode);
		}

		public override void DeserializeOnJoin(RocketBinaryReader reader)
		{
			base.DeserializeOnJoin(reader);
			SetSourceCode(reader.ReadAscii());
		}

		public override void BuildUpdate(RocketBinaryWriter writer, ushort networkUpdateType)
		{
			base.BuildUpdate(writer, networkUpdateType);
			if (Thing.IsNetworkUpdateRequired(256u, networkUpdateType))
			{
				writer.WriteBoolean(_DevicesChanged);
			}
			if (Thing.IsNetworkUpdateRequired(512u, networkUpdateType))
			{
				writer.WriteAscii(_sourceCode);
			}
		}

		public override void ProcessUpdate(RocketBinaryReader reader, ushort networkUpdateType)
		{
			base.ProcessUpdate(reader, networkUpdateType);
			if (Thing.IsNetworkUpdateRequired(256u, networkUpdateType))
			{
				_DevicesChanged = reader.ReadBoolean();
				if (_DevicesChanged)
				{
					HandleDeviceListChange().Forget();
				}
			}
			if (Thing.IsNetworkUpdateRequired(512u, networkUpdateType))
			{
				SetSourceCode(reader.ReadAscii());
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
				_templateCanvas = DropDownTemplate.GetComponentInChildren<Canvas>(includeInactive: true);
			}
		}

		private IEnumerator SetDropdownTemplateLayer()
		{
			yield return Yielders.EndOfFrame;
			if (_templateCanvas == null)
			{
				_templateCanvas = DropDownTemplate.GetComponentInChildren<Canvas>(includeInactive: true);
			}
			if (!(_templateCanvas == null))
			{
				DropDownTemplate.gameObject.SetActive(value: true);
				_templateCanvas.enabled = true;
				_templateCanvas.overrideSorting = true;
				_templateCanvas.sortingOrder = 1;
				DropDownTemplate.gameObject.SetActive(value: false);
			}
		}

		public void SendUpdate()
		{
			if (NetworkManager.IsClient)
			{
				ISourceCode.SendSourceCodeToServer(_sourceCode, base.ReferenceId);
			}
			else if (NetworkManager.IsServer)
			{
				base.NetworkUpdateFlags |= 256;
			}
		}

		public override void FlashCircuit()
		{
			base.FlashCircuit();
			SetSourceCode(string.Empty);
			AccessibleProgrammableChipList.options.Clear();
			AccessibleProgrammableChipList.RefreshShownValue();
			_circuitHolders.Clear();
			_DevicesChanged = false;
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
			list.Sort((ILogicable a, ILogicable b) => a.DisplayName.CompareTo(b.DisplayName));
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

		public override void OnDeviceListChanged()
		{
			base.OnDeviceListChanged();
			if (!_DevicesChanged)
			{
				_DevicesChanged = true;
				HandleDeviceListChange().Forget();
			}
		}

		public override void OnInsertedToComputer(IComputer computer)
		{
			base.OnInsertedToComputer(computer);
			if (!_DevicesChanged)
			{
				_DevicesChanged = true;
				HandleDeviceListChange().Forget();
				StartCoroutine(SetDropdownTemplateLayer());
				if (GameManager.RunSimulation)
				{
					SendUpdate();
				}
			}
		}

		public override void OnRemovedFromComputer(IComputer computer)
		{
			base.OnRemovedFromComputer(computer);
			AccessibleProgrammableChipList.options.Clear();
			if (GameManager.RunSimulation)
			{
				SendUpdate();
			}
		}

		public void Import()
		{
			if (_circuitHolders.Count > AccessibleProgrammableChipList.value)
			{
				ICircuitHolder circuitHolder = _circuitHolders[AccessibleProgrammableChipList.value];
				SetSourceCode(circuitHolder.GetSourceCode());
			}
		}

		public void Export()
		{
			if (_circuitHolders.Count > AccessibleProgrammableChipList.value)
			{
				ICircuitHolder circuitHolder = _circuitHolders[AccessibleProgrammableChipList.value];
				string text = _sourceCode.ToString();
				ulong lastEditedBy = InventoryManager.ParentBrain?.ClientId ?? 0;
				circuitHolder.SetSourceCode(text);
				circuitHolder.LastEditedBy = lastEditedBy;
				if (!string.IsNullOrEmpty(text))
				{
					Achievements.AchieveSomeAssemblyRequired();
				}
			}
		}

		public void SetSourceCode(string sourceCode)
		{
			if (sourceCode == null)
			{
				sourceCode = string.Empty;
			}
			_sourceCode = AsciiString.Parse(sourceCode);
			_ = string.Empty;
			sourceCode = Regex.Replace(sourceCode, "([<])", "$1<b></b>");
			AcceptedStrings.Clear();
			AcceptedJumps.Clear();
			Localization.ParseDefines(sourceCode, ref AcceptedStrings, ref AcceptedJumps);
			SourceCode.text = Localization.ParseScript(sourceCode, ref AcceptedStrings, ref AcceptedJumps);
			SourceCodeLines.text = InputSourceCode.GetLineText(sourceCode);
			SourceCode.raycastTarget = false;
			SourceCodeLines.raycastTarget = false;
			if (NetworkManager.IsServer)
			{
				base.NetworkUpdateFlags |= 512;
			}
		}

		public void OnEdit()
		{
			//InputSourceCode.Instance.PCM = this;
			//if (InputSourceCode.ShowInputPanel("Edit Script", _sourceCode))
			//{
			//	InputSourceCode.OnSubmit += InputFinished;
			//}
		}

		public void InputFinished(string result)
		{
			SetSourceCode(result);
			SendUpdate();
			InputSourceCode.Instance.PCM = null;
		}

		public AsciiString GetSourceCode()
		{
			return _sourceCode;
		}

		public void SaveCodeScreenshot(string path, string title)
		{
			ScreenshotCamera.gameObject.SetActive(value: true);
			ImportButton.SetActive(value: false);
			ExportButton.SetActive(value: false);
			ChipList.SetActive(value: false);
			string text = ScreenTitle.text;
			ScreenTitle.text = title;
			try
			{
				GameManager.SaveScreenShot(path, XmlSaveLoad.WorkShopPreviewFileName, 400, 400, ScreenshotCamera);
				GameManager.SaveScreenShot(path, XmlSaveLoad.LoadWordScreenShot, 613, 266, ScreenshotCamera);
			}
			catch (Exception ex)
			{
				Debug.Log(ex.Message);
			}
			ScreenshotCamera.gameObject.SetActive(value: false);
			ImportButton.SetActive(value: true);
			ExportButton.SetActive(value: true);
			ChipList.SetActive(value: true);
			ScreenTitle.text = text;
		}
	}
}
