# DQD.Glob

A simple .NET filesystem globbing library. "But Microsoft already provides one!" Yes, but can it match directories? :-)

## Using

Add a reference to the `DQD.Glob` NuGet package, then call methods of the `Globber` class:

```
var pattern = "/home/username/**/*.txt";

var (baseDir, relativePattern) = Globber.SplitPattern(pattern);

foreach (var matchingFile in Globber.GetMatches(baseDir, relativePattern))
  if (matchingFile is DirectoryInfo)
    Console.WriteLine("Looks like a text file but is actually a directory: ", matchingFile.FullName);
```

You can create an instance of `Globber` to allow multiple tests against the same globbing expression:

```
var globber = new Globber("**/*.mp3", ignoreCase: true);

foreach (var relativeFilePath in archiveContents)
  if (globber.IsMatch(relativeFilePath))
    Console.WriteLine("Found an MP3 file: {0}", relativeFilePath);
```

You can supply multiple expressions to be applied simultaneously:

```
var globber = new Globber();

globber.AddExpression("**/*.mod", ignoreCase: true);
globber.AddExpression("**/*.xm", ignoreCase: true);
globber.AddExpression("**/*.s3m", ignoreCase: true);
globber.AddExpression("**/*.it", ignoreCase: true);

var modules = globber.GetMatches(modulesDirectory);
```

## Source Code

The repository for this library's source code is found at:

* https://github.com/logiclrd/DQD.Glob/

## License

The library is provided under the MIT Open Source license. See `LICENSE.md` for details.
