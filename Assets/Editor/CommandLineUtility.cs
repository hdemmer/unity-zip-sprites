using System;
using System.IO;


namespace UnityEditor
{
	public class CommandLineUtility
	{
		public static string ExecuteProcess( string file, string arguments, string workingDirectory = "")
		{
			System.Diagnostics.Process p = new System.Diagnostics.Process();
			p.StartInfo.UseShellExecute = false;
			p.StartInfo.RedirectStandardOutput = true;
			p.StartInfo.RedirectStandardError = true;
			p.StartInfo.FileName = file;
			p.StartInfo.Arguments = arguments;
			p.StartInfo.WorkingDirectory = workingDirectory;
			p.StartInfo.CreateNoWindow = true;
			p.Start();

			var res = p.StandardOutput.ReadToEnd();

			p.WaitForExit();

			if (p.ExitCode != 0)
			{
				throw new Exception( string.Format( "ExitCode: {0} - {1}", p.ExitCode, p.StandardError.ReadToEnd() ) );
			}

			var err = p.StandardError.ReadToEnd();
			if (!string.IsNullOrEmpty( err ))
			{
				res += " ERRORS : " + err;
			}

			UnityEngine.Debug.Log( "CommandLine: (" + p.ExitCode + ") `" + file + " " + arguments + "` RES " + res );

			return res;
		}
	}
}