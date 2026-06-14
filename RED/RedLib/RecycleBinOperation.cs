using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace RED
{
	/// <summary>
	/// Batched Recycle Bin deletion via IFileOperation. One shell transaction
	/// recycles any number of directories with per-item result reporting through
	/// IFileOperationProgressSink — replacing the legacy per-call
	/// Microsoft.VisualBasic FileSystem.DeleteDirectory, which could raise modal
	/// shell dialogs even in headless mode and paid full shell setup per item.
	/// Callers must reparse-verify every path BEFORE queueing: the shell recycles
	/// whatever it is handed, recursively.
	/// </summary>
	internal static class RecycleBinOperation
	{
		#region COM interop

		private const uint FOF_SILENT = 0x0004;          // no progress dialog
		private const uint FOF_NOCONFIRMATION = 0x0010;  // no "are you sure"
		private const uint FOF_ALLOWUNDO = 0x0040;
		private const uint FOF_NOERRORUI = 0x0400;       // no error dialogs
		private const uint FOFX_RECYCLEONDELETE = 0x00080000;

		private const int SIGDN_FILESYSPATH = unchecked((int)0x80058000);

		[DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
		private static extern IShellItem SHCreateItemFromParsingName(
			string pszPath, IntPtr pbc, [In] ref Guid riid);

		private static Guid IID_IShellItem = new Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe");
		private static readonly Guid CLSID_FileOperation = new Guid("3ad05575-8857-4850-9277-11b85bdb8e09");

		[ComImport, Guid("43826d1e-e718-42ee-bc55-a1e261c37bfe"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IShellItem
		{
			void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
			void GetParent(out IShellItem ppsi);
			void GetDisplayName(int sigdnName, out IntPtr ppszName);
			void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
			void Compare(IShellItem psi, uint hint, out int piOrder);
		}

		[ComImport, Guid("04b0f1a7-9490-44bc-96e1-4296a31252e2"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IFileOperationProgressSink
		{
			[PreserveSig] int StartOperations();
			[PreserveSig] int FinishOperations(int hrResult);
			[PreserveSig] int PreRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
			[PreserveSig] int PostRenameItem(uint dwFlags, IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrRename, IShellItem psiNewlyCreated);
			[PreserveSig] int PreMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
			[PreserveSig] int PostMoveItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrMove, IShellItem psiNewlyCreated);
			[PreserveSig] int PreCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
			[PreserveSig] int PostCopyItem(uint dwFlags, IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, int hrCopy, IShellItem psiNewlyCreated);
			[PreserveSig] int PreDeleteItem(uint dwFlags, IShellItem psiItem);
			[PreserveSig] int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem psiNewlyCreated);
			[PreserveSig] int PreNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
			[PreserveSig] int PostNewItem(uint dwFlags, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, uint dwFileAttributes, int hrNew, IShellItem psiNewItem);
			[PreserveSig] int UpdateProgress(uint iWorkTotal, uint iWorkSoFar);
			[PreserveSig] int ResetTimer();
			[PreserveSig] int PauseTimer();
			[PreserveSig] int ResumeTimer();
		}

		[ComImport, Guid("947aab5f-0a5c-4c13-b4d6-4bf7836fc9f8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
		private interface IFileOperation
		{
			void Advise(IFileOperationProgressSink pfops, out uint pdwCookie);
			void Unadvise(uint dwCookie);
			void SetOperationFlags(uint dwOperationFlags);
			void SetProgressMessage([MarshalAs(UnmanagedType.LPWStr)] string pszMessage);
			void SetProgressDialog([MarshalAs(UnmanagedType.IUnknown)] object popd);
			void SetProperties([MarshalAs(UnmanagedType.IUnknown)] object pproparray);
			void SetOwnerWindow(IntPtr hwndOwner);
			void ApplyPropertiesToItem(IShellItem psiItem);
			void ApplyPropertiesToItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
			void RenameItem(IShellItem psiItem, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink pfopsItem);
			void RenameItems([MarshalAs(UnmanagedType.IUnknown)] object pUnkItems, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName);
			void MoveItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszNewName, IFileOperationProgressSink pfopsItem);
			void MoveItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
			void CopyItem(IShellItem psiItem, IShellItem psiDestinationFolder, [MarshalAs(UnmanagedType.LPWStr)] string pszCopyName, IFileOperationProgressSink pfopsItem);
			void CopyItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems, IShellItem psiDestinationFolder);
			void DeleteItem(IShellItem psiItem, IFileOperationProgressSink pfopsItem);
			void DeleteItems([MarshalAs(UnmanagedType.IUnknown)] object punkItems);
			void NewItem(IShellItem psiDestinationFolder, uint dwFileAttributes, [MarshalAs(UnmanagedType.LPWStr)] string pszName, [MarshalAs(UnmanagedType.LPWStr)] string pszTemplateName, IFileOperationProgressSink pfopsItem);
			[PreserveSig] int PerformOperations();
			void GetAnyOperationsAborted([MarshalAs(UnmanagedType.Bool)] out bool pfAnyOperationsAborted);
		}

		#endregion COM interop

		internal class ItemResult
		{
			public string Path;
			public int HResult;
			public bool Succeeded { get { return HResult >= 0; } }
		}

		/// <summary>
		/// Per-item results arrive through PostDeleteItem during PerformOperations
		/// (synchronously, on the calling thread).
		/// </summary>
		[ClassInterface(ClassInterfaceType.None)]
		private class DeleteSink : IFileOperationProgressSink
		{
			public readonly List<ItemResult> Results = new List<ItemResult>();
			public Func<bool> CancelRequested;
			public Action<ItemResult> OnItemDone;

			private const int E_FAIL = unchecked((int)0x80004005);

			public int StartOperations() { return 0; }
			public int FinishOperations(int hrResult) { return 0; }
			public int PreRenameItem(uint f, IShellItem i, string n) { return 0; }
			public int PostRenameItem(uint f, IShellItem i, string n, int hr, IShellItem c) { return 0; }
			public int PreMoveItem(uint f, IShellItem i, IShellItem d, string n) { return 0; }
			public int PostMoveItem(uint f, IShellItem i, IShellItem d, string n, int hr, IShellItem c) { return 0; }
			public int PreCopyItem(uint f, IShellItem i, IShellItem d, string n) { return 0; }
			public int PostCopyItem(uint f, IShellItem i, IShellItem d, string n, int hr, IShellItem c) { return 0; }

			public int PreDeleteItem(uint dwFlags, IShellItem psiItem)
			{
				// Returning a failure HRESULT makes the shell skip this item —
				// honoring a cancellation request mid-batch
				if (CancelRequested != null && CancelRequested())
				{
					return E_FAIL;
				}
				return 0;
			}

			public int PostDeleteItem(uint dwFlags, IShellItem psiItem, int hrDelete, IShellItem psiNewlyCreated)
			{
				var result = new ItemResult { Path = GetPath(psiItem), HResult = hrDelete };
				Results.Add(result);
				OnItemDone?.Invoke(result);
				return 0;
			}

			public int PreNewItem(uint f, IShellItem d, string n) { return 0; }
			public int PostNewItem(uint f, IShellItem d, string n, string t, uint a, int hr, IShellItem i) { return 0; }
			public int UpdateProgress(uint total, uint soFar) { return 0; }
			public int ResetTimer() { return 0; }
			public int PauseTimer() { return 0; }
			public int ResumeTimer() { return 0; }

			private static string GetPath(IShellItem item)
			{
				if (item == null) return null;
				IntPtr pszPath = IntPtr.Zero;
				try
				{
					item.GetDisplayName(SIGDN_FILESYSPATH, out pszPath);
					return Marshal.PtrToStringUni(pszPath);
				}
				catch
				{
					return null;
				}
				finally
				{
					if (pszPath != IntPtr.Zero) Marshal.FreeCoTaskMem(pszPath);
				}
			}
		}

		private static uint FlagsFor(bool allowConfirmation, bool allowErrorUi)
		{
			uint flags = FOF_ALLOWUNDO | FOFX_RECYCLEONDELETE | FOF_SILENT;
			if (!allowConfirmation)
			{
				flags |= FOF_NOCONFIRMATION;
			}
			if (!allowErrorUi)
			{
				flags |= FOF_NOERRORUI;
			}
			return flags;
		}

		/// <summary>
		/// Recycles all paths in one shell transaction. Headless mode must pass
		/// allowConfirmation=false and allowErrorUi=false. onItemDone fires per item
		/// during the operation (same thread). A path that no longer exists is
		/// reported as failed by the shell, not thrown.
		/// </summary>
		internal static List<ItemResult> RecycleBatch(
			IList<string> paths,
			bool allowConfirmation,
			bool allowErrorUi,
			Func<bool> cancelRequested,
			Action<ItemResult> onItemDone)
		{
			var sink = new DeleteSink { CancelRequested = cancelRequested, OnItemDone = onItemDone };
			if (paths == null || paths.Count == 0)
			{
				return sink.Results;
			}

			var fileOp = (IFileOperation)Activator.CreateInstance(Type.GetTypeFromCLSID(CLSID_FileOperation));
			try
			{
				uint cookie;
				fileOp.Advise(sink, out cookie);
				try
				{
					fileOp.SetOperationFlags(FlagsFor(allowConfirmation, allowErrorUi));

					int queued = 0;
					foreach (string path in paths)
					{
						IShellItem item = SHCreateItemFromParsingName(path, IntPtr.Zero, ref IID_IShellItem);
						try
						{
							fileOp.DeleteItem(item, null);
							queued++;
						}
						finally
						{
							// DeleteItem AddRefs the item internally, so releasing our
							// reference now keeps the batch from accumulating live shell
							// RCWs across hundreds/thousands of queued directories.
							Marshal.ReleaseComObject(item);
						}
					}

					if (queued > 0)
					{
						// Per-item failures surface through PostDeleteItem; the
						// aggregate HRESULT alone would hide which items failed
						fileOp.PerformOperations();
					}
				}
				finally
				{
					fileOp.Unadvise(cookie);
				}
			}
			finally
			{
				Marshal.ReleaseComObject(fileOp);
			}

			return sink.Results;
		}

		/// <summary>Recycles a single file or directory; throws on failure.</summary>
		internal static void RecycleSingle(string path, bool allowConfirmation, bool allowErrorUi)
		{
			List<ItemResult> results = RecycleBatch(new[] { path }, allowConfirmation, allowErrorUi, null, null);
			if (results.Count == 0)
			{
				throw new System.IO.IOException("Recycle Bin operation reported no result for: " + path);
			}
			if (!results[0].Succeeded)
			{
				Marshal.ThrowExceptionForHR(results[0].HResult);
			}
		}
	}
}
