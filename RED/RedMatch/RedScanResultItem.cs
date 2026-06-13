using System;
using System.IO;
using TXT = RED.RedGetText;

namespace RED.Match
{
	public enum ResultKind { Directory, File }

	public class RedScanResultItem
	{
		public RedScanResultItem(DirectoryInfo di, DirectorySearchStatusTypes status) : this(di, status, string.Empty) { }

		public RedScanResultItem(DirectoryInfo di, DirectorySearchStatusTypes status, string errorMsg)
		{
			Kind = ResultKind.Directory;
			Populate(di, status, errorMsg);
		}

		public RedScanResultItem(FileInfo fi, DirectorySearchStatusTypes status, string reason)
		{
			Kind = ResultKind.File;
			_filePath = fi.FullName;
			Directory = fi.Directory;
			SearchStatus = status;
			SearchStatusOriginal = status;
			ErrorMessage = reason;
		}

		public ResultKind Kind { get; private set; }
		private string _filePath;

		public DirectoryInfo Directory { get; private set; }
		public string FullPath { get { return Kind == ResultKind.File ? _filePath : Directory?.FullName; } }
		public string Name { get { return Kind == ResultKind.File ? Path.GetFileName(_filePath) : Directory?.Name; } }
		public DirectorySearchStatusTypes SearchStatus { get; private set; }
		public DirectorySearchStatusTypes SearchStatusOriginal { get; private set; }
		public string ErrorMessage { get; private set; }

		/// <summary>
		/// Extra detail for a kept folder (e.g. "contains 2 ignored files"). Set by
		/// the tree styler where the live file count is known; falls back to the
		/// status-derived reason when empty.
		/// </summary>
		public string KeptReasonDetail { get; set; }

		/// <summary>
		/// A concrete, human-readable reason this folder appears as it does — answers
		/// the category's top confusion ("why is this folder not deletable?").
		/// </summary>
		public string StatusReason
		{
			get
			{
				if (!string.IsNullOrEmpty(KeptReasonDetail))
				{
					return KeptReasonDetail;
				}
				switch (SearchStatus)
				{
					case DirectorySearchStatusTypes.Empty:
						return TXT.Translate("Empty - eligible for deletion");
					case DirectorySearchStatusTypes.Ignore:
						return TXT.Translate("Kept - matches an ignore filter rule");
					case DirectorySearchStatusTypes.NeverEmpty:
						return TXT.Translate("Kept - matches a never-empty rule");
					case DirectorySearchStatusTypes.Error:
						return string.IsNullOrWhiteSpace(ErrorMessage)
							? TXT.Translate("Kept - could not be read")
							: TXT.Translate("Kept - {0}", ErrorMessage.Replace("\r", " ").Replace("\n", " "));
					default:
						return SearchStatus.ToString();
				}
			}
		}

		private void Populate(DirectoryInfo di, DirectorySearchStatusTypes status, string errorMsg)
		{
			try
			{
				Directory = di;
				SearchStatus = status;
				SearchStatusOriginal = status;
				ErrorMessage = errorMsg;
			}
			catch (Exception ex)
			{
				ErrorMessage = errorMsg + Environment.NewLine + ex.Message;
			}
		}
	}
}