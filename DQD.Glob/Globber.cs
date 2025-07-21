using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace DQD.Glob;

public class Globber
{
	static readonly char[] s_PatternChars = ['*', '?'];
	static readonly char[] s_PathSeparatorChars = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

	static readonly Regex s_MultiLevelRegex = new Regex("^.*.*$");

	public static ReadOnlySpan<char> PatternChars => s_PatternChars;

	public static (string BaseDirectory, string RelativePattern) SplitPattern(string pattern)
	{
		var baseDirectory = new StringBuilder();
		bool first = true;

		if (Path.IsPathRooted(pattern))
		{
			baseDirectory.Append(Path.GetPathRoot(pattern));
			pattern = RemovePathRoot(pattern);
		}

		while (true)
		{
			int separator = pattern.IndexOfAny(s_PathSeparatorChars);

			if (separator < 0)
				break;

			string token = pattern.Substring(0, separator);

			if (token.IndexOfAny(s_PatternChars) >= 0)
				break;

			if (first)
				first = false;
			else
				baseDirectory.Append(Path.DirectorySeparatorChar);
			baseDirectory.Append(token);

			pattern = pattern.Substring(separator + 1);
		}

		if (baseDirectory.Length == 0)
			return (".", pattern);
		else
			return (baseDirectory.ToString(), pattern);
	}

	static string[] SplitPath(string path)
		=> path.Trim(s_PathSeparatorChars).Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries);

	public static IEnumerable<FileSystemInfo> GetMatches(string path, string glob, bool ignoreCase = false)
	{
		return GetMatches(path, [glob], ignoreCase);
	}

	public static IEnumerable<FileSystemInfo> GetMatches(string path, IEnumerable<string> globs, bool ignoreCase = false)
	{
		List<ArraySegment<Regex>> componentSequences = new List<ArraySegment<Regex>>();

		foreach (string glob in globs)
		{
			var sequence = MakeRegularExpressionsForGlob(glob, ignoreCase);

			if (sequence.Length > 0)
				componentSequences.Add(sequence);
		}

		if (componentSequences.Count > 0)
		{
			var directory = new DirectoryInfo(path);

			return GetMatches(directory, componentSequences);
		}

		return Array.Empty<FileSystemInfo>();
	}

	static IEnumerable<FileSystemInfo> GetMatches(string path, ArraySegment<Regex> components)
		=> GetMatches(path, new List<ArraySegment<Regex>>() { components });

	static IEnumerable<FileSystemInfo> GetMatches(string path, IEnumerable<ArraySegment<Regex>> componentSequences)
	{
		var sequences = componentSequences.Where(seq => seq.Count > 0).ToList();

		if (sequences.Count > 0)
		{
			var directory = new DirectoryInfo(path);

			return GetMatches(directory, sequences);
		}

		return Array.Empty<FileSystemInfo>();
	}

	public static IEnumerable<FileSystemInfo> GetMatchesFromRoot(string glob, bool ignoreCase = false)
	{
		glob = glob.TrimStart(s_PathSeparatorChars);

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			foreach (var driveInfo in DriveInfo.GetDrives())
				foreach (var match in GetMatches(driveInfo.RootDirectory, driveInfo.RootDirectory + glob, ignoreCase = false))
					yield return match;
		}
		else
		{
			var root = new DirectoryInfo(Path.DirectorySeparatorChar.ToString());

			foreach (var match in GetMatches(root, root + glob, ignoreCase))
				yield return match;
		}
	}

	public static IEnumerable<FileSystemInfo> GetMatchesFromRoot(IEnumerable<string> globs, bool ignoreCase = false)
	{
		string[] trimmedGlobs = globs.Select(glob => glob.TrimStart(s_PathSeparatorChars)).ToArray();

		var rootedGlobs = new string[trimmedGlobs.Length];

		if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
		{
			foreach (var driveInfo in DriveInfo.GetDrives())
			{
				for (int i = 0; i < trimmedGlobs.Length; i++)
					rootedGlobs[i] = driveInfo.RootDirectory + trimmedGlobs[i];

				foreach (var match in GetMatches(driveInfo.RootDirectory, rootedGlobs, ignoreCase = false))
					yield return match;
			}
		}
		else
		{
			var root = new DirectoryInfo(Path.DirectorySeparatorChar.ToString());

			for (int i = 0; i < trimmedGlobs.Length; i++)
				rootedGlobs[i] = root + trimmedGlobs[i];

			foreach (var match in GetMatches(root, rootedGlobs, ignoreCase))
				yield return match;
		}
	}

	public static IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory, string glob, bool ignoreCase = false)
	{
		if (Path.IsPathRooted(glob))
		{
			string[] leader = SplitPath(RemovePathRoot(directory.FullName));

			return GetMatches(new ArraySegment<string>(leader), directory, [glob], ignoreCase);
		}
		else
			return GetMatches(directory, [glob], ignoreCase);

		throw new ArgumentException("Cannot enumerate matches starting from a specified subdirectory with a rooted glob expression");
	}

	public static IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory, IEnumerable<string> globs, bool ignoreCase = false)
		=> GetMatches(Array.Empty<string>(), directory, globs, ignoreCase);

	static IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory, IEnumerable<ArraySegment<Regex>> componentSequences)
		=> GetMatches(Array.Empty<string>(), directory, componentSequences.ToList());

	static IEnumerable<FileSystemInfo> GetMatches(ArraySegment<string> leader, DirectoryInfo directory, IEnumerable<string> globs, bool ignoreCase)
	{
		var componentSequences = new List<ArraySegment<Regex>>();

		foreach (var glob in globs)
		{
			var componentExpressions = new List<Regex>();

			foreach (var pattern in SplitPath(glob))
				componentExpressions.Add(MakeRegularExpressionForPattern(pattern, ignoreCase));

			componentSequences.Add(componentExpressions.ToArray());
		}

		return GetMatches(leader, directory, componentSequences);
	}

	static Regex[] MakeRegularExpressionsForGlob(string glob, bool ignoreCase)
	{
		string[] components = glob.Trim(s_PathSeparatorChars).Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries);

		Regex[] expressions = new Regex[components.Length];

		for (int i = 0; i < components.Length; i++)
			expressions[i] = MakeRegularExpressionForPattern(components[i], ignoreCase);

		return expressions;
	}

	static Regex MakeRegularExpressionForPattern(string pattern, bool ignoreCase)
	{
		if (pattern == "**")
			return s_MultiLevelRegex;

		if (pattern.IndexOfAny(s_PatternChars) < 0)
			return new Regex("^" + Regex.Escape(pattern) + "$", ignoreCase ? RegexOptions.IgnoreCase : default);

		var expression = new StringBuilder();

		expression.Append('^');

		foreach (char ch in pattern)
		{
			if (ch == '?')
				expression.Append('.');
			else if (ch == '*')
				expression.Append(".*");
			else
				expression.Append(Regex.Escape(ch.ToString()));
		}

		expression.Append('$');

		return new Regex(expression.ToString(), ignoreCase ? RegexOptions.IgnoreCase : default);
	}

	public static IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory, ArraySegment<Regex> components)
		=> GetMatches(Array.Empty<string>(), directory, components);

	public static IEnumerable<FileSystemInfo> GetMatches(string leader, DirectoryInfo directory, ArraySegment<Regex> components)
		=> GetMatches(SplitPath(leader), directory, new List<ArraySegment<Regex>>() { components });

	public static IEnumerable<FileSystemInfo> GetMatches(string leader, DirectoryInfo directory, List<ArraySegment<Regex>> componentSequences)
		=> GetMatches(SplitPath(leader), directory, componentSequences);

	static IEnumerable<FileSystemInfo> GetMatches(ArraySegment<string> leader, DirectoryInfo directory, ArraySegment<Regex> components)
		=> GetMatches(leader, directory, new List<ArraySegment<Regex>>() { components });

	static IEnumerable<FileSystemInfo> GetMatches(ArraySegment<string> leader, DirectoryInfo directory, List<ArraySegment<Regex>> componentSequences)
	{
		var subComponentSequences = componentSequences.ToList();

		subComponentSequences.RemoveAll(seq => seq.Count == 0);

		if (subComponentSequences.Count == 0)
			yield break;

		var headComponents = new List<Regex>();

		for (int i = 0; i < subComponentSequences.Count; i++)
		{
			headComponents.Add(subComponentSequences[i][0]);
			subComponentSequences[i] = subComponentSequences[i].Slice(1);
		}

		for (int i = 0, l = headComponents.Count; i < l; i++)
		{
			if (headComponents[i] == s_MultiLevelRegex)
			{
				headComponents.Add(headComponents[i]);
				subComponentSequences.Add(componentSequences[i]);
			}
		}

		if (leader.Count > 0)
		{
			var remainingLeader = leader.Slice(1);

			for (int i = subComponentSequences.Count - 1; i >= 0; i--)
			{
				if (!headComponents[i].IsMatch(leader[0]))
				{
					headComponents.RemoveAt(i);
					subComponentSequences.RemoveAt(i);
				}
			}

			if (subComponentSequences.Any())
			{
				foreach (var match in GetMatches(remainingLeader, directory, subComponentSequences))
					yield return match;
			}
		}
		else
		{
			var activeSubComponentSequences = new List<ArraySegment<Regex>>();

			foreach (var entry in directory.EnumerateFileSystemInfos())
			{
				activeSubComponentSequences.Clear();

				for (int i = 0; i < subComponentSequences.Count; i++)
					if (headComponents[i].IsMatch(entry.Name))
						activeSubComponentSequences.Add(subComponentSequences[i]);

				if (activeSubComponentSequences.Any(seq => seq.Count == 0))
					yield return entry;

				if (entry is DirectoryInfo subdirectory)
				{
					if (activeSubComponentSequences.Any())
					{
						foreach (var match in GetMatches(leader, subdirectory, activeSubComponentSequences))
							yield return match;
					}
				}
			}
		}
	}

	static string RemovePathRoot(string path)
	{
		// Deliberately leave the text of a Windows root (drive letter) so that it can be matched.
		return path.TrimStart(s_PathSeparatorChars);
	}

	public static bool IsMatch(string path, string globExpression, bool ignoreCase = false)
	{
		var globber = new Globber(globExpression, ignoreCase);

		return globber.IsMatch(path);
	}

	public static bool IsMatch(string path, IEnumerable<string> globs, bool ignoreCase = false)
	{
		var globber = new Globber(globs, ignoreCase);

		return globber.IsMatch(path);
	}

	public static bool IsMatch(string path, ArraySegment<Regex> components, bool isRooted)
		=> IsMatch(path, new List<ArraySegment<Regex>>() { components }, isRooted);

	public static bool IsMatch(string path, List<ArraySegment<Regex>> componentSequences, bool isRooted)
	{
		if (Path.IsPathRooted(path))
		{
			if (isRooted)
				path = RemovePathRoot(path);
			else
				return false;
		}
		else
		{
			if (isRooted)
				path = RemovePathRoot(Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), path));
		}

		var subComponentSequences = componentSequences.ToList();

		subComponentSequences.RemoveAll(seq => seq.Count == 0);

		if (subComponentSequences.Count == 0)
			return false;

		var headComponents = new List<Regex>();

		for (int i = 0; i < subComponentSequences.Count; i++)
		{
			headComponents.Add(subComponentSequences[i][0]);
			subComponentSequences[i] = subComponentSequences[i].Slice(1);
		}

		for (int i = 0, l = headComponents.Count; i < l; i++)
		{
			if (headComponents[i] == s_MultiLevelRegex)
			{
				headComponents.Add(headComponents[i]);
				subComponentSequences.Add(componentSequences[i]);
			}

			if ((subComponentSequences[i].Count == 0) && headComponents[i].IsMatch(path))
				return true;
		}

		int separatorIndex = path.IndexOfAny(s_PathSeparatorChars);

		if (separatorIndex >= 0)
		{
			string head = path.Substring(0, separatorIndex);

			for (int i = subComponentSequences.Count - 1; i >= 0; i--)
				if (!headComponents[i].IsMatch(head))
				{
					headComponents.RemoveAt(i);
					subComponentSequences.RemoveAt(i);
				}

			if (subComponentSequences.Any())
			{
				path = path.Substring(separatorIndex + 1).TrimStart(s_PathSeparatorChars);

				return IsMatch(path, subComponentSequences, isRooted: false);
			}
		}

		return false;
	}

	//////////////////////////////////////////////////////////////////////////////////

	List<Regex[]> _componentSequences = new List<Regex[]>();
	bool _isRooted;

	public Globber()
	{
	}

	public Globber(string glob, bool ignoreCase = false)
	{
		AddExpression(glob, ignoreCase);
	}

	public Globber(IEnumerable<string> globs, bool isRooted, bool ignoreCase = false)
		: this()
	{
		_isRooted = isRooted;

		foreach (string glob in globs)
			AddExpression(glob);
	}

	public Globber(Regex[] components, bool isRooted)
	{
		_componentSequences.Add(components);
		_isRooted = isRooted;
	}

	public void AddExpression(string glob, bool ignoreCase = false)
	{
		if (_componentSequences.Count == 0)
			_isRooted = Path.IsPathRooted(glob);
		else if (Path.IsPathRooted(glob) != _isRooted)
			Console.WriteLine("Cannot mix rooted and unrooted paths in the same Globber instance");

		AddExpression(glob.Trim(s_PathSeparatorChars).Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries), ignoreCase);
	}

	public void AddExpression(IEnumerable<string> components, bool ignoreCase = false)
	{
		AddExpression(components.Select(component => MakeRegularExpressionForPattern(component, ignoreCase)));
	}

	public void AddExpression(IEnumerable<Regex> components)
	{
		_componentSequences.Add(components.ToArray());
	}

	public IEnumerable<FileSystemInfo> GetMatches(string path)
	{
		if (_isRooted && !Path.IsPathRooted(path))
			path = Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), path);

		if (_componentSequences.Any(seq => seq.Length > 0))
		{
			var directory = new DirectoryInfo(path);

			return GetMatches(directory);
		}

		return Array.Empty<FileSystemInfo>();
	}

	public IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory)
	{
		var componentSequences = _componentSequences.Select(sequence => new ArraySegment<Regex>(sequence)).ToList();

		if (_isRooted)
			return GetMatches(directory.FullName, directory, componentSequences);
		else
			return GetMatches(directory, componentSequences);
	}

	public bool IsMatch(string path)
	{
		var rootComponentSequences = new List<ArraySegment<Regex>>();

		foreach (var componentSequence in _componentSequences)
			rootComponentSequences.Add(componentSequence);

		return IsMatch(path, rootComponentSequences, _isRooted);
	}
}
