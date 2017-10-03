open System
open System.Diagnostics
open System.IO
open System.Linq
open System.Text.RegularExpressions
open find2

let internal tryGetFileInfo filePath =
    try
        Some(FileInfo filePath)
    with
    | :? FileNotFoundException as exc -> Console.WriteLine(filePath + ": " + exc.Message); None
    | :? UnauthorizedAccessException as exc -> Console.WriteLine(filePath + ": " + exc.Message); None
    | :? PathTooLongException as exc -> Console.WriteLine(filePath + ": " + exc.Message); None
    | :? IOException as exc -> Console.WriteLine(filePath + ": " + exc.Message); None

let internal tryGetDirectoryFiles directory =
    try
        Some <| Directory.GetFiles (directory,  "*", SearchOption.TopDirectoryOnly)
    with
        | :? FileNotFoundException as exc -> Console.WriteLine(directory + ": " + exc.Message); None
        | :? UnauthorizedAccessException as exc -> Console.WriteLine(directory + ": " + exc.Message); None
        | :? PathTooLongException as exc -> Console.WriteLine(directory + ": " + exc.Message); None
        | :? IOException as exc -> Console.WriteLine(directory + ": " + exc.Message); None

let internal tryGetDirectorySubDirectories directory =
    try
        Some <| Directory.GetDirectories (directory,  "*", SearchOption.TopDirectoryOnly)
    with
        | :? FileNotFoundException as exc -> Console.WriteLine(directory + ": " + exc.Message); None
        | :? UnauthorizedAccessException as exc -> Console.WriteLine(directory + ": " + exc.Message); None
        | :? PathTooLongException as exc -> Console.WriteLine(directory + ": " + exc.Message); None
        | :? IOException as exc -> Console.WriteLine(directory + ": " + exc.Message); None

let rec internal enumerateAllFiles currentDirectory = seq {
        match tryGetDirectoryFiles currentDirectory with
        | Some files -> for f in files do yield f
                        match tryGetDirectorySubDirectories currentDirectory with
                        | Some dirs -> for d in dirs do
                                         for f in (enumerateAllFiles d) do yield f
                        | None -> ()
        | None -> ()
    }
let internal getFilesByRegexPattern directory regex =
    enumerateAllFiles directory
    // no parallel so far.
    // TODO: add f# power pack for parallelism
    |> Seq.where (fun filePath -> Regex.IsMatch(filePath, regex, RegexOptions.Compiled ||| RegexOptions.IgnoreCase))
    |> Seq.map (fun filePath -> (tryGetFileInfo filePath))
    |> Seq.where Option.isSome
    |> Seq.map Option.get

let internal getFilesByWildcard directory fileNamePattern =
    let regex = "^" + Regex.Escape(fileNamePattern)
                           .Replace("\\*", ".*")
                           .Replace("\\?", ".") + "$";
    getFilesByRegexPattern directory regex

let internal joinLines (lines:seq<string>) =
    String.Join(Environment.NewLine, lines)

let internal getFilesLines (filePath:string) =
    try
        Some(File.ReadAllLines filePath)
    with
    | exc -> let message = sprintf "Unhandled exception occurred on reading lines from '%s'. Details:\n%s"
                                    filePath (exc.ToString())
             Console.WriteLine exc.Message
             Trace.WriteLine message
             Debug.WriteLine message
             None

let internal getPattern (options: CommandLineOptions) =
    if String.IsNullOrEmpty options.TextPattern
       then options.TextRegexPattern
       else options.TextPattern

let internal matches (line:string) (options:CommandLineOptions) =
    let regexOptions = if options.CaseSensitive
                       then RegexOptions.Compiled
                       else RegexOptions.Compiled ||| RegexOptions.IgnoreCase
    let stringComparison = if options.CaseSensitive
                           then StringComparison.Ordinal
                           else StringComparison.OrdinalIgnoreCase
    let matchesRegex input pattern = Regex.IsMatch(input, pattern, regexOptions)
    let hasLotsOfNonPrintableCharacters l = Regex.IsMatch(l, @"[^ -~\t\n]{3}")

    if hasLotsOfNonPrintableCharacters line
    then false
    else
        let pattern = getPattern options
        if options.IsTextPatternRegex
        then matchesRegex line pattern
        else line.IndexOf(pattern, stringComparison) >= 0

let internal getFileMatchInfo (fileInfo:FileInfo) (options:CommandLineOptions) =
    let pattern = getPattern options
    let isTextPatternRegex = options.IsTextPatternRegex
    let skipLargeFiles = not(options.MatchLargeFiles)
    let isTooLarge (fileInfo:FileInfo) = fileInfo.Length > int64(16 * 1024 * 1024)// 16 mb
    let isBinaryFile (fileInfo:FileInfo) =
        [".exe"; ".dll"; ".pdb"; ".trc"]
        |> Seq.exists (fun ext -> ext = Path.GetExtension fileInfo.FullName)
    async {
        return (if String.IsNullOrEmpty pattern
                        || isBinaryFile fileInfo
                        || skipLargeFiles && isTooLarge fileInfo
                then FileMatchInfo.NoMatches fileInfo
                else
                    match getFilesLines fileInfo.FullName with
                    | None -> FileMatchInfo.NoMatches fileInfo
                    | Some(fileLines) ->
                        fileLines
                        |> Seq.mapi (fun index line -> if matches line options then Some(index, line) else None)
                        |> Seq.where Option.isSome
                        |> Seq.map Option.get
                        |> (fun matchedLines -> FileMatchInfo.WithMatches fileInfo matchedLines)
                ) // return
    }// async

[<EntryPoint>]
[<STAThread>]
let main argv =

    let mutable exitCode = 2
    // pure imperative code, not cool ...
    // TODO: rewrite in as declarative manner as possible
    // with all the monads and stuff ...
    // 
    // 2013's called, they want their monads back

    // no 'lazy'-stuff here
    // TODO: add 'lazy'-stuff in here

    try
        match CommandLineOptions.ParseArguments(argv) with
        | None -> exitCode <- 1
        | Some(arguments) ->
            let workingDirectory = if String.IsNullOrEmpty(arguments.WorkingDirectory)
                                   then Environment.CurrentDirectory
                                   else arguments.WorkingDirectory
            let files = if arguments.IsFileNamePatternRegex
                        then getFilesByRegexPattern workingDirectory (arguments.FileNameRegexPattern)
                        else getFilesByWildcard workingDirectory (arguments.FileNamePattern)

            ignore <|
            if String.IsNullOrEmpty arguments.TextPattern
                && String.IsNullOrEmpty arguments.TextRegexPattern
            then
                printfn "Looking for '%s' ..." arguments.FileNamePattern
                let results = files
                              |> Seq.sortBy (fun file -> file.FullName)
                              |> Seq.mapi (fun i file -> i, file)
                              |> Seq.toArray

                results
                |> Seq.map (fun (i, file) -> sprintf "%i > %s" i file.FullName)
                |> joinLines
                |> (fun line -> emphasize { do! printfn  "%s" line } )

                emphasize { do! printfn "%i file(s) found." (Seq.length files) }

            else
                printfn "Looking for '%s' in '%s' ..." (getPattern arguments) arguments.FileNamePattern
                let results = files |> Seq.map (fun file -> getFileMatchInfo file arguments)
                                    |> Async.Parallel
                                    |> Async.StartAsTask
                                    |> (fun t -> t.Result)
                                    |> Array.toSeq
                                    |> Seq.where (fun matchInfo -> not(Seq.isEmpty matchInfo.MatchedLines))
                                    |> Seq.where (fun matchInfo -> not(matchInfo.ErrorOccurred))
                                    |> Seq.mapi (fun i matchInfo -> (i, matchInfo))

                results |> Seq.iter (fun (i, matchInfo) ->
                                        emphasize { do! printfn "%i > %s" i matchInfo.FileInfo.FullName }
                                        matchInfo.MatchedLines
                                        |> Seq.iter (fun (index, line) ->
                                                         printfn "    > line %i: %s" index (line.Trim())))

                emphasize { do! printfn "%i matching file(s) found." (Seq.length results) }

            exitCode <- 0
    with
    | :? AssertException as exc -> error { do! printfn "%s" (exc.ToString()) }
                                   exitCode <- 2

    exitCode