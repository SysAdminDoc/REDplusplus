using System;
using System.IO;

namespace RED
{
	/// <summary>
	/// A folder containing a <c>.redkeep</c> marker file is never treated as
	/// deletable. Unlike the per-config filter lists, the marker travels with the
	/// folder across copies and network shares — drop one in a share root once and
	/// every machine's RED++ honors it.
	/// </summary>
	internal static class RedKeepMarker
	{
		public const string MarkerFileName = ".redkeep";

		public static bool HasMarker(DirectoryInfo directory)
		{
			if (directory == null)
			{
				return false;
			}
			try
			{
				return File.Exists(Path.Combine(directory.FullName, MarkerFileName));
			}
			catch
			{
				return false;
			}
		}
	}
}
