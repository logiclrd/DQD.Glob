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

	public static IEnumerable<FileSystemInfo> GetMatches(string path, string glob, bool ignoreCase = false)
	{
		return GetMatches(path, glob.Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries), ignoreCase);
	}

	public static IEnumerable<FileSystemInfo> GetMatches(string path, ArraySegment<string> components, bool ignoreCase = false)
	{
		if (components.Count > 0)
		{
			var directory = new DirectoryInfo(path);

			return GetMatches(directory, components, ignoreCase);
		}

		return Array.Empty<FileSystemInfo>();
	}

	public static IEnumerable<FileSystemInfo> GetMatches(string path, ArraySegment<Regex> components)
	{
		if (components.Count > 0)
		{
			var directory = new DirectoryInfo(path);

			return GetMatches(directory, components);
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

	public static IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory, string glob, bool ignoreCase = false)
	{
		if (Path.IsPathRooted(glob))
		{
			string[] leader = RemovePathRoot(directory.FullName)
				.Trim(s_PathSeparatorChars)
				.Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries);

			return GetMatches(new ArraySegment<string>(leader), directory, glob.Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries), ignoreCase);
		}
		else
			return GetMatches(directory, glob.Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries), ignoreCase);

			throw new ArgumentException("Cannot enumerate matches starting from a specified subdirectory with a rooted glob expression");

	}

	public static IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory, ArraySegment<string> components, bool ignoreCase = false)
		=> GetMatches(Array.Empty<string>(), directory, components, ignoreCase);

	static IEnumerable<FileSystemInfo> GetMatches(ArraySegment<string> leader, DirectoryInfo directory, ArraySegment<string> components, bool ignoreCase)
	{
		var componentExpressions = new List<Regex>();

		foreach (var pattern in components)
			componentExpressions.Add(MakeRegularExpressionForPattern(pattern, ignoreCase));

		return GetMatches(leader, directory, componentExpressions.ToArray());
	}

	static Regex MakeRegularExpressionForPattern(string pattern, bool ignoreCase)
	{
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
		=> GetMatches(leader.Trim(s_PathSeparatorChars).Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries), directory, components);

	static IEnumerable<FileSystemInfo> GetMatches(ArraySegment<string> leader, DirectoryInfo directory, ArraySegment<Regex> components)
	{
		if (components.Count == 0)
			yield break;

		var thisComponent = components[0];
		var subcomponents = components.Slice(1);

		bool isMultiLevel = (thisComponent.ToString() == "^.*.*$");

		if (leader.Count > 0)
		{
			var remainingLeader = leader.Slice(1);

			if (thisComponent.IsMatch(leader[0]))
			{
				foreach (var match in GetMatches(remainingLeader, directory, subcomponents))
					yield return match;

				if (isMultiLevel)
					foreach (var match in GetMatches(remainingLeader, directory, components))
						yield return match;
			}
		}
		else
		{
			foreach (var entry in directory.EnumerateFileSystemInfos())
			{
				if (subcomponents.Count == 0)
					if (thisComponent.IsMatch(entry.Name))
						yield return entry;

				if (entry is DirectoryInfo subdirectory)
				{
					if (thisComponent.IsMatch(entry.Name))
					{
						foreach (var match in GetMatches(leader, subdirectory, subcomponents))
							yield return match;

						if (isMultiLevel)
							foreach (var match in GetMatches(leader, subdirectory, components))
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

	public static bool IsMatch(string path, ArraySegment<Regex> components, bool isRooted)
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

		if (components.Count == 0)
			return false;

		var thisComponent = components[0];
		var subcomponents = components.Slice(1);

		bool isMultiLevel = (thisComponent.ToString() == ".*.*");

		int separatorIndex = path.IndexOfAny(s_PathSeparatorChars);

		if (subcomponents.Count == 0)
			if ((subcomponents.Count == 0) && (separatorIndex < 0))
				return thisComponent.IsMatch(path);

		if ((separatorIndex >= 0) && thisComponent.IsMatch(path.Substring(0, separatorIndex)))
		{
			path = path.Substring(separatorIndex + 1).TrimStart(s_PathSeparatorChars);

			return IsMatch(path, subcomponents, isRooted: false) || (isMultiLevel && IsMatch(path, components, isRooted: false));
		}

		return false;
	}

	//////////////////////////////////////////////////////////////////////////////////

	Regex[] _components;
	bool _isRooted;

	public Globber(string glob, bool ignoreCase = false)
		: this(glob.Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries), Path.IsPathRooted(glob), ignoreCase)
	{
	}

	public Globber(string[] components, bool isRooted, bool ignoreCase = false)
		: this(components.Select(component => MakeRegularExpressionForPattern(component, ignoreCase)).ToArray(), isRooted)
	{
	}

	public Globber(Regex[] components, bool isRooted)
	{
		_components = components;
		_isRooted = isRooted;
	}

	public IEnumerable<FileSystemInfo> GetMatches(string path)
	{
		if (_isRooted && !Path.IsPathRooted(path))
			path = Path.Combine(Path.GetFullPath(Environment.CurrentDirectory), path);

		if (_components.Length > 0)
		{
			var directory = new DirectoryInfo(path);

			return GetMatches(directory);
		}

		return Array.Empty<FileSystemInfo>();
	}

	public IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory)
	{
		if (_isRooted)
			return GetMatches(directory.FullName, directory, _components);
		else
			return GetMatches(directory, _components);
	}

	public bool IsMatch(string path)
		=> IsMatch(path, _components, _isRooted);
}
