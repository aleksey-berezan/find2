using System;
using System.Collections.Generic;
using System.Linq;

namespace findt
{
    internal struct Arguments
    {
        public string TextPattern { get; private set; }
        public bool IsTextPatternRegex { get; private set; }
        public string FileNamePattern { get; private set; }
        public bool IsFileNamePatternRegex { get; private set; }
        public string WorkingDirectory { get; private set; }

        public Arguments(string textPattern, bool isTextPatternRegex, string fileNamePattern, bool isFileNamePatternRegex, string workingDirectory=null)
            : this()
        {
            TextPattern = textPattern;
            IsTextPatternRegex = isTextPatternRegex;
            FileNamePattern = fileNamePattern;
            IsFileNamePatternRegex = isFileNamePatternRegex;
            WorkingDirectory = workingDirectory ?? Environment.CurrentDirectory;
        }

        public static Arguments Parse(IEnumerable<string> args)
        {
            string usage = "findt [/t:<text_pattern>] [/tr:<text_pattern_regex_flag>] [/f:<file_name_pattern>] [/fr:<file_name_regex_flag>] [/wd:<working_directory>]";
            Assert.Soft(args.Count() >= 1 && args.Count() <= 4, usage);
            return Parse(args, new Arguments(null, false, "*", false));
        }

        private static Arguments Parse(IEnumerable<string> args, Arguments current)
        {
            // TODO: add validation
            if (!args.Any())
            {
                return current;
            }

            Func<string, string, string> trim = (s, paramName) => s.Substring(paramName.Length, s.Length - paramName.Length);

            var map = new Dictionary<Func<string, bool>, Func<string, Arguments, Arguments>>
            {
                { s => s.StartsWith("/t:") ,  (s,a) => new Arguments(trim(s, "/t:"), a.IsTextPatternRegex, a.FileNamePattern, a.IsFileNamePatternRegex)}, 
                { s => s.StartsWith("/tr") ,  (s,a) => new Arguments(a.TextPattern, true, a.FileNamePattern, a.IsFileNamePatternRegex)}, 
                { s => s.StartsWith("/f:") ,  (s,a) => new Arguments(a.TextPattern, a.IsTextPatternRegex, trim(s,"/f:"), a.IsFileNamePatternRegex)}, 
                { s => s.StartsWith("/fr") ,  (s,a) => new Arguments(a.TextPattern, a.IsTextPatternRegex, a.FileNamePattern, true)}, 
                { s => s.StartsWith("/wd:") ,  (s,a) => new Arguments(a.TextPattern, a.IsTextPatternRegex, a.FileNamePattern, a.IsFileNamePatternRegex, trim(s,"/wd:"))}, 
            };

            string value = args.First();
            var matched = map.FirstOrDefault(pair => pair.Key(value));
            Assert.Soft(condition: !matched.Equals(default(KeyValuePair<Func<string, bool>, Func<string, Arguments, Arguments>>))
                        , message: string.Format("Unrecognized parameter '{0}'", value));

            Arguments current2 = matched.Value(value, current);
            return Parse(args.Skip(1), current2);
        }
    }
}