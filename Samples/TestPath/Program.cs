using System;
using System.Linq;

namespace TestPath;

using DQD.Glob;

class Program
{
	static void Main(string[] args)
	{
		if (args.Length < 2)
		{
			Console.WriteLine("usage: TestPath <glob expression> <path> [<path> [..]]");
			return;
		}

		var glob = new Globber(args[0]);

		foreach (string pathToTest in args.Skip(1))
		{
			bool isMatch = glob.IsMatch(pathToTest);

			if (isMatch)
			{
				Console.ForegroundColor = ConsoleColor.Green;
				Console.Write("MATCH");
			}
			else
			{
				Console.ForegroundColor = ConsoleColor.Red;
				Console.Write("NOT MATCH");
			}

			Console.ForegroundColor = ConsoleColor.White;
			Console.WriteLine(": {0}", pathToTest);
		}
	}
}
