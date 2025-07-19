using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;

namespace DQD.Glob;

public static class Globber
{
	static readonly char[] s_PatternChars = ['*', '?'];
	static readonly char[] s_PathSeparatorChars = [Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar];

	public static ReadOnlySpan<char> PatternChars => s_PatternChars;

	public static (string BaseDirectory, string RelativePattern) SplitPattern(string pattern)
	{
		var baseDirectory = new StringBuilder();
		bool first = true;

		if (Path.IsPathRooted(pattern))
			baseDirectory.Append(Path.GetPathRoot(pattern));

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

	public static IEnumerable<FileSystemInfo> GetMatches(string path, string glob)
	{
		return GetMatches(path, glob.Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries));
	}

	public static IEnumerable<FileSystemInfo> GetMatches(string path, ArraySegment<string> components)
	{
		if (components.Count > 0)
		{
			var directory = new DirectoryInfo(path);

			return GetMatches(directory, components);
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

	public static IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory, string glob)
	{
		return GetMatches(directory, glob.Split(s_PathSeparatorChars, StringSplitOptions.RemoveEmptyEntries));
	}

	public static IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory, ArraySegment<string> components)
	{
		var componentExpressions = new List<Regex>();

		foreach (var pattern in components)
			componentExpressions.Add(MakeRegularExpressionForPattern(pattern));

		return GetMatches(directory, componentExpressions.ToArray());
	}

	public static IEnumerable<FileSystemInfo> GetMatches(DirectoryInfo directory, ArraySegment<Regex> components)
	{
		if (components.Count == 0)
			yield break;

		var thisComponent = components[0];
		var subcomponents = components.Slice(1);

		bool isMultiLevel = (thisComponent.ToString() == ".*.*");

		foreach (var entry in directory.EnumerateFileSystemInfos())
		{
			if (subcomponents.Count == 0)
				if (thisComponent.IsMatch(entry.Name))
					yield return entry;

			if (entry is DirectoryInfo subdirectory)
			{
				foreach (var match in GetMatches(subdirectory, subcomponents))
					yield return match;

				if (isMultiLevel)
					foreach (var match in GetMatches(subdirectory, components))
						yield return match;
			}
		}
	}

	static Regex MakeRegularExpressionForPattern(string pattern)
	{
		if (pattern.IndexOfAny(s_PatternChars) < 0)
			return new Regex(Regex.Escape(pattern));

		var expression = new StringBuilder();

		foreach (char ch in pattern)
		{
			if (ch == '?')
				expression.Append('.');
			else if (ch == '*')
				expression.Append(".*");
			else
				expression.Append(Regex.Escape(ch.ToString()));
		}

		return new Regex(expression.ToString());
	}
}
