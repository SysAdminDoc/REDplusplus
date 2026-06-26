using System;
using TXT = RED.RedGetText;

namespace RED
{
	/// <summary>
	/// List box container class thingy
	/// </summary>
	public class DeleteModeItem
	{
		public DeleteModes DeleteMode { get; set; }

		public DeleteModeItem(DeleteModes Mode)
		{
			this.DeleteMode = Mode;
		}

		public static DeleteModes[] GetList()
		{
			return new DeleteModes[] {
				DeleteModes.RecycleBin,
				DeleteModes.RecycleBinShowErrors,
				DeleteModes.RecycleBinWithQuestion,
				DeleteModes.Direct,
				DeleteModes.Simulate,
				DeleteModes.MoveToFolder
			};
		}

		public override string ToString()
		{
			switch (this.DeleteMode)
			{
				case DeleteModes.RecycleBin:
					return TXT.Translate("Recycle Bin (default, safest)");

				case DeleteModes.RecycleBinShowErrors:
					return TXT.Translate("Recycle Bin, show errors");

				case DeleteModes.RecycleBinWithQuestion:
					return TXT.Translate("Recycle Bin, ask per item");

				case DeleteModes.Direct:
					return TXT.Translate("Direct delete (bypasses Recycle Bin)");

				case DeleteModes.Simulate:
					return TXT.Translate("Simulate only (no changes)");

				case DeleteModes.MoveToFolder:
					return TXT.Translate("Move to folder");

				default:
					throw new Exception(TXT.Translate("Unknown delete mode"));
			}
		}
	}
}
