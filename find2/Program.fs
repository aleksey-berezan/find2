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
    | :? FileNotFoundException as exc -> Debug.WriteLine(exc.Message)
                                         None
    | :? UnauthorizedAccessException as exc -> Debug.WriteLine(exc.Message)
                                               None

let internal getFilesByRegexPattern workingDirectory fileNameRegexPattern = 
    Directory.EnumerateFiles(workingDirectory, "*", SearchOption.AllDirectories)
    // no parallel so far.
    // TODO: add f# power pack for parallelism
    |> Seq.where (fun filePath -> Regex.IsMatch(filePath, fileNameRegexPattern, RegexOptions.Compiled ||| RegexOptions.IgnoreCase))
    |> Seq.map (fun filePath -> (tryGetFileInfo filePath))
    |> Seq.where Option.isSome
    |> Seq.map Option.get

let internal getFilesByWildcard workingDirectory fileNamePattern = 
    Directory.EnumerateFiles(workingDirectory, fileNamePattern, SearchOption.AllDirectories)
    |> Seq.map tryGetFileInfo
    |> Seq.where Option.isSome
    |> Seq.map Option.get
     
let internal joinLines (lines:seq<string>) = 
    String.Join(Environment.NewLine, lines)

let internal getFilesLines (filePath:string) = 
    try
        Some(File.ReadAllLines filePath)
    with 
    | exc -> let message = sprintf "Unhandled exception occurred on reading lines from '%s'. Details:\n%s"
                                    filePath (exc.ToString())
             Debug.WriteLine message
             Trace.WriteLine message
             None

let internal matches (line:string) (options:CommandLineOptions) = 
    let matchesRegex input pattern = Regex.IsMatch(input, pattern, RegexOptions.Compiled ||| RegexOptions.IgnoreCase)
    let satisfiesGreps (l:string) (grepsPattern:string) = 
        String.IsNullOrEmpty grepsPattern
        || grepsPattern.Split([|',';';'|]) |> Array.forall (fun x -> l.Contains(x))
    let hasLotsOfNonPrintableCharacters l = Regex.IsMatch(l, @"[^ -~\t\n]{3}")
    let up (s:string) = s.ToUpperInvariant()

    let pattern = if String.IsNullOrEmpty options.TextPattern
                      then options.TextRegexPattern
                      else options.TextPattern
    let isTextPatternRegex = options.IsTextPatternRegex

    if hasLotsOfNonPrintableCharacters line
    then false
    else
        if isTextPatternRegex
        then matchesRegex line pattern
        else (line |> up).Contains(pattern |> up)
             && satisfiesGreps (line |> up) (options.GrepsString |> up)

let internal getFileMatchInfo (fileInfo:FileInfo) (options:CommandLineOptions) = 
    let pattern = if String.IsNullOrEmpty options.TextPattern
                      then options.TextRegexPattern
                      else options.TextPattern
    let isTextPatternRegex = options.IsTextPatternRegex
    let skipLargeFiles = not(options.MatchLargeFiles)
    let isTooLarge (fileInfo:FileInfo) = fileInfo.Length > int64(64 * 1024 * 1024)// 64 mb
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

let rec readNumber() =
    match (Console.ReadKey true).Key with
    | ConsoleKey.Q -> None
    | k when k >= ConsoleKey.D0 && k <= ConsoleKey.D9 -> Some(int(k) - int(ConsoleKey.D0))
    | _ -> readNumber()

let CopyToClipBoard text = 
    System.Windows.Forms.Clipboard.SetText(text)
    printfn "Copied to clipboard: %s\n" text

let CopyFileNameToClipBoard (results: seq<int*FileInfo>) =
    match results.Count() with
    | 0 -> ();
    | 1 -> let _, file = results.First()
           CopyToClipBoard file.FullName
    | x when x <= 10 ->        
        printfn "\nEnter number of item  to copy to clipboard or 'q' ... "
        match readNumber() with
        | Some(num) ->
             let _, file = if num > results.Count() - 1
                           then results.Last()
                           else results.Skip(num).First()
             CopyToClipBoard file.FullName
        | None -> ();
    | _ -> ();

[<EntryPoint>]
[<STAThread>]
let main argv = 

    let mutable exitCode = 2
    // pure imperative code, not cool ...
    // TODO: rewrite in as declarative manner as possible
    // with all the monads and stuff ...

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

                if arguments.CopyPathToClip
                then CopyFileNameToClipBoard results

            else
                printfn "Looking for '%s' in '%s' ..." arguments.TextPattern arguments.FileNamePattern
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

                let toSelect = results |> Seq.map (fun (i, matchInfo) -> (i, matchInfo.FileInfo))
                if arguments.CopyPathToClip
                then CopyFileNameToClipBoard toSelect

                emphasize { do! printfn "%i matching file(s) found." (Seq.length results) }

            exitCode <- 0
    with
    | :? AssertException as exc -> error { do! printfn "%s" (exc.ToString()) }
                                   exitCode <- 2

    exitCode