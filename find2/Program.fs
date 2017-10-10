open System
open System.IO
open System.Text.RegularExpressions
open find2
open System.Threading.Tasks
open FileUtils

let internal Trace (message:string) = 
    Console.WriteLine message
    System.Diagnostics.Debug.WriteLine message
    System.Diagnostics.Trace.WriteLine message

let internal tryFsOperation f x =
    let excToString (exc:Exception) = x.ToString() + ": " + exc.Message
    try
        f x |> Some
    with
    | :? FileNotFoundException as exc -> exc |> excToString |> Trace; None
    | :? UnauthorizedAccessException as exc -> exc |> excToString |> Trace; None
    | :? PathTooLongException as exc -> exc |> excToString |> Trace; None
    | :? IOException as exc -> exc |> excToString |> Trace; None

let internal tryGetFileInfo =
    tryFsOperation (fun filePath -> FileInfo filePath)

let internal tryGetDirectoryFiles =
    tryFsOperation (fun directory -> Directory.GetFiles (directory,  "*", SearchOption.TopDirectoryOnly))

let internal tryGetSubDirectories =
    tryFsOperation (fun directory -> Directory.GetDirectories (directory,  "*", SearchOption.TopDirectoryOnly))

let tryOpenSr (filePath: string) =
    match filePath |> (tryFsOperation (fun f -> new FileStream(f, FileMode.Open, FileAccess.Read, FileShare.ReadWrite))) with
    | None -> None
    | Some x -> x |> (tryFsOperation (fun fs -> new StreamReader (fs)))

let readFileLinesFrom (sr: StreamReader) = seq {
    while not sr.EndOfStream do
        yield sr.ReadLine ()
}

let rec internal enumerateAllFiles currentDirectory = seq {
        match tryGetDirectoryFiles currentDirectory with
        | Some files -> for f in files do yield f
                        match tryGetSubDirectories currentDirectory with
                        | Some dirs -> for d in dirs do
                                         for f in (enumerateAllFiles d) do
                                            yield f
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
    let skipLargeFiles = not(options.MatchLargeFiles)
    let isTooLarge (fileInfo:FileInfo) = fileInfo.Length > int64(16 * 1024 * 1024)// 16 mb
    async {
        return (if String.IsNullOrEmpty pattern
                        || skipLargeFiles && isTooLarge fileInfo
                        || isBinary fileInfo
                then FileMatchInfo.NoMatches fileInfo
                else
                    match tryOpenSr fileInfo.FullName with
                    | None -> FileMatchInfo.NoMatches fileInfo
                    | Some x ->
                        use sr = x
                        readFileLinesFrom sr
                        |> Seq.mapi (fun index line -> if matches line options then Some(index, line) else None)
                        |> Seq.where Option.isSome
                        |> Seq.map Option.get
                        |> Seq.toArray
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