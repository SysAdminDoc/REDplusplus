using System;
using System.Threading;
using System.Windows.Forms;

namespace RED
{
	internal static class Program
	{
		private static Mutex singleInstanceMutex;

		[STAThread]
		private static void Main()
		{
			bool createdNew;
			singleInstanceMutex = new Mutex(true, "Global\\REDplusplus_SingleInstance", out createdNew);

			if (!createdNew)
			{
				MessageBox.Show("RED++ is already running.", "RED++", MessageBoxButtons.OK, MessageBoxIcon.Information);
				return;
			}

			try
			{
				Application.EnableVisualStyles();
				Application.SetCompatibleTextRenderingDefault(false);
				Application.Run(new UI.MainWindow());
			}
			finally
			{
				singleInstanceMutex.ReleaseMutex();
				singleInstanceMutex.Dispose();
			}
		}
	}
}
