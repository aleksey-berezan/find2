using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Net.Mime;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace findt
{
    internal class Program
    {
        #region Nested types

        private struct FileMatchInfo
        {
            public FileInfo FileInfo { get; private set; }
            public bool Matched { get; private set; }
            public IEnumerable<Tuple<int, string>> MatchedLines { get; set; }

            public FileMatchInfo(FileInfo fileInfo)
                : this(fileInfo, Enumerable.Empty<Tuple<int, string>>())
            {
                Matched = true;
            }

            public FileMatchInfo(FileInfo fileInfo, IEnumerable<Tuple<int, string>> matchedLines)
                : this()
            {
                FileInfo = fileInfo;
                MatchedLines = matchedLines;
                Matched = matchedLines.Any();
            }
        }

        #endregion

        private static void Main(string[] args)
        {

            try
            {
                var arguments = Arguments.Parse(args);
                var files = (arguments.IsFileNamePatternRegex
                                     ? GetFilesByRegexPattern(arguments.WorkingDirectory, arguments.FileNamePattern)
                                     : GetFilesByWildcard(arguments.WorkingDirectory, arguments.FileNamePattern)
                            ).ToList();

                if (string.IsNullOrEmpty(arguments.TextPattern))
                {
                    files.ForEach(x => Console.WriteLine("file: {0}", x));
                    Console.WriteLine("{0} files found.", files.Count);
                    return;
                }

                Console.WriteLine("{0} files to look up for '{1}'...", files.Count(), arguments.TextPattern);
                var matchedFileInfos = files.AsParallel()
                     .Select(x => GetFileMatchInfo(x, arguments.TextPattern, arguments.IsTextPatternRegex))
                     .Where(y => y.MatchedLines.Any())
                     .ToList();


                foreach (var matchedFileInfo in matchedFileInfos)
                {
                    Console.WriteLine("> file: {0}", matchedFileInfo.FileInfo.FullName);
                    foreach (var matchedLine in matchedFileInfo.MatchedLines)
                    {
                        var lineNumber = matchedLine.Item1;
                        var line = matchedLine.Item2;
                        Console.WriteLine("    > line {0}: {1}", lineNumber, line.Trim());
                    }
                }
                Console.WriteLine("{0} files found.", matchedFileInfos.Count);
            }
            catch (AssertException a_exc)
            {
                Console.WriteLine(a_exc);
                Environment.Exit(1);
            }
        }

        private static FileMatchInfo GetFileMatchInfo(FileInfo fileInfo, string textPattern, bool isTextPatternRegex)
        {
            if (string.IsNullOrEmpty(textPattern))
                return new FileMatchInfo(fileInfo);

            var matchedLines = File.ReadAllLines(fileInfo.FullName)
                .Select((line, index) =>
                {
                    bool matches = isTextPatternRegex
                                    ? Regex. IsMatch(line, textPattern)
                                    : line.ToUpperInvariant().Contains(textPattern.ToUpperInvariant());
                    return matches ? Tuple.Create(index, line) : null;
                })
                .Where(matchedLine => matchedLine != null)
                .ToList();

            return new FileMatchInfo(fileInfo, matchedLines);
        }

        private static IEnumerable<FileInfo> GetFilesByRegexPattern(string namePattern, string fileNamePattern)
        {
            // TODO: do not check binary files
            return Directory.GetFiles(namePattern, "*", SearchOption.AllDirectories)
                            .AsParallel()
                            .Where(x => Regex.IsMatch(Path.GetFileName(x), fileNamePattern))
                            .Select(x => new FileInfo(x));
        }

        private static IEnumerable<FileInfo> GetFilesByWildcard(string currentDirectory, string fileNamePattern)
        {
            // TODO: do not check binary files

            return Directory.GetFiles(currentDirectory, fileNamePattern, SearchOption.AllDirectories)
                .Select(x => new FileInfo(x));
        }
    }
}
