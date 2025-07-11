using System.Xml.Serialization;
using Assets.Scripts.Objects.Electrical;
using Assets.Scripts.Objects.Items;

namespace Assets.Scripts.Objects.Motherboards
{
	[XmlInclude(typeof(ProgrammableChipSaveData))]
	public class ProgrammableChipMotherboardSaveData : MotherboardSaveData
	{
		[XmlElement]
		public string SourceCode;

		[XmlElement]
		public int DeviceIndex;
	}
}
