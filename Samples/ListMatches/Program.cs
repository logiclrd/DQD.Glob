using System;

namespace ListMatches;

using DQD.Glob;

class Program
{
	static void Main(string[] args)
	{
		if (args.Length == 0)
			Console.WriteLine("usage: ShowStat <path> [<path> [..]]");
		else
		{
			foreach (string pattern in args)
			{
				Console.WriteLine("Enumerating: {0}", pattern);

				var (baseDir, relativePattern) = Globber.SplitPattern(pattern);

				foreach (var matchingFile in Globber.GetMatches(baseDir, relativePattern))
					Console.WriteLine(matchingFile.FullName);
			}
		}
	}
}
