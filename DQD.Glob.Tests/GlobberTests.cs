using System;
using System.IO;

using NUnit.Framework;

using FluentAssertions;
using System.Linq;
using System.Collections.Generic;
using System.Security.Cryptography;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DQD.Glob.Tests;

[TestFixture]
public class GlobberTests
{
	[Test]
	public void SplitPattern_should_handle_empty_patterns()
	{
		// Act
		var result = Globber.SplitPattern("");

		// Assert
		result.Should().Be((".", ""));
	}

	[TestCase("test", ".", "test")]
	[TestCase("/test", "/", "test")]
	[TestCase("test/case", "test", "case")]
	[TestCase("/test/case", "/test", "case")]
	public void SplitPattern_should_handle_patterns_with_no_wildcard_characters(string path, string expectedBasePath, string expectedRelativePattern)
	{
		// Act
		var result = Globber.SplitPattern(path);

		// Assert
		result.Should().Be((expectedBasePath, expectedRelativePattern));
	}

	[TestCase("test/")]
	[TestCase("/test/")]
	[TestCase("test/case/")]
	[TestCase("/test/case/")]
	public void SplitPattern_should_trim_trailing_path_separators(string path)
	{
		// Act
		var result = Globber.SplitPattern(path);

		// Assert
		result.RelativePattern.Should().NotEndWith("/");
	}

	class TestDirectory : IDisposable
	{
		string _path;
		string _savedCWD;

		public string FullPath => _path;

		public TestDirectory()
		{
			_savedCWD = Environment.CurrentDirectory;

			_path = Path.GetTempFileName();

			File.Delete(_path);
			Directory.CreateDirectory(_path);

			Directory.CreateDirectory(Path.Combine(_path, "First"));
			Directory.CreateDirectory(Path.Combine(_path, "Second", "Nested"));
			Directory.CreateDirectory(Path.Combine(_path, "Third"));

			File.WriteAllText(Path.Combine(_path, "First", "A.txt"), "foo");
			File.WriteAllText(Path.Combine(_path, "First", "B.txt"), "foo");

			File.WriteAllText(Path.Combine(_path, "Second", "A.txt"), "foo");
			File.WriteAllText(Path.Combine(_path, "Second", "Nested", "B.txt"), "foo");

			File.WriteAllText(Path.Combine(_path, "Third", "B.txt"), "foo");
		}

		public void Dispose()
		{
			if (Directory.Exists(_savedCWD))
				Environment.CurrentDirectory = _savedCWD;

			if (Directory.Exists(_path))
				Directory.Delete(_path, recursive: true);
		}
	}

	[TestCase("First/A.txt", "First/A.txt")]
	[TestCase("*/A.txt", "First/A.txt|Second/A.txt")]
	[TestCase("*/*.txt", "First/A.txt|First/B.txt|Second/A.txt|Third/B.txt")]
	[TestCase("**/B.txt", "First/B.txt|Second/Nested/B.txt|Third/B.txt")]
	[TestCase("**/*.txt", "First/A.txt|First/B.txt|Second/A.txt|Second/Nested/B.txt|Third/B.txt")]
	public void GetMatches_should_find_matches_in_directory_tree(string testExpression, string expectedResultsPacked)
	{
		// Arrange
		using (var dir = new TestDirectory())
		{
			var expectedResults = expectedResultsPacked.Split('|').ToHashSet();

			// Act
			var matches = Globber.GetMatches(dir.FullPath, testExpression).ToArray();

			// Assert
			var actualResults = new HashSet<string>();

			foreach (var match in matches)
			{
				string actualFullPath = match.FullName;

				if (!actualFullPath.StartsWith(dir.FullPath))
					Assert.Fail("Got a match whose path didn't start with " + dir.FullPath + ": " + actualFullPath);

				string actualRelativePath = actualFullPath.Substring(dir.FullPath.Length).TrimStart(Path.DirectorySeparatorChar);

				if (!expectedResults.Contains(actualRelativePath))
					Assert.Fail("Got unexpected result: " + actualRelativePath);

				actualResults.Add(actualRelativePath);
			}

			foreach (var expected in expectedResults)
				if (!actualResults.Contains(expected))
					Assert.Fail("Did not get expected result: " + expected);
		}
	}

	[TestCase("*/*.txt", "First", "First/A.txt|First/B.txt")]
	[TestCase("*/*.txt", "Second", "Second/A.txt")]
	[TestCase("*/*/*.txt", "Second/Nested", "Second/Nested/B.txt")]
	[TestCase("**/*.txt", "Second", "Second/A.txt|Second/Nested/B.txt")]
	public void GetMatches_should_find_matches_in_subdirectory_of_rooted_expression(string testExpressionSuffix, string searchOrigin, string expectedResultsPacked)
	{
		// Arrange
		using (var dir = new TestDirectory())
		{
			string testExpression = Path.Combine(dir.FullPath, testExpressionSuffix);

			var expectedResults = expectedResultsPacked.Split('|').ToHashSet();

			var globber = new Globber(testExpression);

			var searchOriginDirectory = new DirectoryInfo(Path.Combine(dir.FullPath, searchOrigin));

			// Act
			var matches = globber.GetMatches(searchOriginDirectory).ToArray();

			// Assert
			var actualResults = new HashSet<string>();

			foreach (var match in matches)
			{
				string actualFullPath = match.FullName;

				if (!actualFullPath.StartsWith(dir.FullPath))
					Assert.Fail("Got a match whose path didn't start with " + dir.FullPath + ": " + actualFullPath);

				string actualRelativePath = actualFullPath.Substring(dir.FullPath.Length).TrimStart(Path.DirectorySeparatorChar);

				if (!expectedResults.Contains(actualRelativePath))
					Assert.Fail("Got unexpected result: " + actualRelativePath);

				actualResults.Add(actualRelativePath);
			}

			foreach (var expected in expectedResults)
				if (!actualResults.Contains(expected))
					Assert.Fail("Did not get expected result: " + expected);
		}
	}

	public void GetMatches_should_ignore_case_when_asked()
	{
		// Arrange
		using (var dir = new TestDirectory())
		{
			string[] expectedResults = ["First/A.txt", "First/B.txt", "Second/A.txt", "Third/B.txt"];

			// Act
			var results = Globber.GetMatches(dir.FullPath, "*/*.TXT", ignoreCase: true);

			// Assert
			var actualResults = new List<string>();

			foreach (var match in results)
			{
				string actualFullPath = match.FullName;

				if (!actualFullPath.StartsWith(dir.FullPath))
					Assert.Fail("Got a match whose path didn't start with " + dir.FullPath + ": " + actualFullPath);

				string actualRelativePath = actualFullPath.Substring(dir.FullPath.Length).TrimStart(Path.DirectorySeparatorChar);
			}
		}
	}

	[TestCase("**/A.txt", "**/B.txt", "First/A.txt|First/B.txt|Second/A.txt|Second/Nested/B.txt|Third/B.txt")]
	public void GetMatches_should_support_multiple_expressions(string firstExpression, string secondExpression, string expectedResultsPacked)
	{
		// Arrange
		using (var dir = new TestDirectory())
		{
			var expectedResults = expectedResultsPacked.Split('|').ToHashSet();

			// Act
			var matches = Globber.GetMatches(dir.FullPath, [firstExpression, secondExpression]);

			// Assert
			var actualResults = new HashSet<string>();

			foreach (var match in matches)
			{
				string actualFullPath = match.FullName;

				if (!actualFullPath.StartsWith(dir.FullPath))
					Assert.Fail("Got a match whose path didn't start with " + dir.FullPath + ": " + actualFullPath);

				string actualRelativePath = actualFullPath.Substring(dir.FullPath.Length).TrimStart(Path.DirectorySeparatorChar);

				if (!expectedResults.Contains(actualRelativePath))
					Assert.Fail("Got unexpected result: " + actualRelativePath);

				actualResults.Add(actualRelativePath);
			}

			foreach (var expected in expectedResults)
				if (!actualResults.Contains(expected))
					Assert.Fail("Did not get expected result: " + expected);
		}
	}

	// TODO: possible to make generalized tests from root??

	[TestCase("/test", "/test")]
	[TestCase("/*", "/test")]
	[TestCase("C:/test", "C:/test")]
	public void IsMatch_should_match(string testExpression, string testPath)
	{
		// Arrange
		var globber = new Globber(testExpression);

		// Act
		var result = globber.IsMatch(testPath);

		// Assert
		result.Should().BeTrue();
	}

	[TestCase("test", "/test")]
	[TestCase("*", "/test")]
	[TestCase("C:/test", "B:/test")]
	[TestCase("C:/test", "/test")]
	public void IsMatch_should_not_match(string testExpression, string testPath)
	{
		// Arrange
		var globber = new Globber(testExpression);

		// Act
		var result = globber.IsMatch(testPath);

		// Assert
		result.Should().BeFalse();
	}

	[TestCase("First/*.txt", "First/A.txt", true, true)]
	[TestCase("First/*.txt", "First/A.txt", false, false)]
	public void IsMatch_should_use_context_when_expression_is_rooted(string testExpressionSuffix, string testPath, bool setCurrentDirectoryToTestPath, bool expectedResult)
	{
		// Arrange
		using (var dir = new TestDirectory())
		{
			string testExpression = Path.Combine(dir.FullPath, testExpressionSuffix);

			var globber = new Globber(testExpression);

			if (setCurrentDirectoryToTestPath)
				Environment.CurrentDirectory = dir.FullPath;

			// Act
			var result = globber.IsMatch(testPath);

			// Assert
			result.Should().Be(expectedResult);
		}
	}

	[Test]
	public void IsMatch_with_empty_expression_should_never_match()
	{
		// Act
		var result = Globber.IsMatch("foo", Array.Empty<Regex>(), false);

		// Assert
		result.Should().BeFalse();
	}

	[Test]
	public void IsMatch_should_ignore_case_when_asked()
	{
		// Act
		var result = Globber.IsMatch("FILE.TXT", "*.txt", ignoreCase: true);

		// Assert
		result.Should().BeTrue();
	}

	[Test]
	public void IsMatch_should_support_multiple_expressions()
	{
		// Arrange
		var globber = new Globber();

		globber.AddExpression("a.txt");
		globber.AddExpression("b.m*");

		// Act
		var result1 = globber.IsMatch("a.txt");
		var result2 = globber.IsMatch("b.mod");
		var result3 = globber.IsMatch("c.xls");

		// Assert
		result1.Should().BeTrue();
		result2.Should().BeTrue();
		result3.Should().BeFalse();
	}
}
