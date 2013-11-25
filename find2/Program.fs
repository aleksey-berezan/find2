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

let internal fileToUri (file:FileInfo) =
    sprintf "file://%s" (file.FullName.Replace(@"\", "/"))

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

    let textPattern = options.TextPattern
    let isTextPatternRegex = options.IsTextPatternRegex

    if hasLotsOfNonPrintableCharacters line
    then false
    else
        if isTextPatternRegex
        then matchesRegex line textPattern
        else (line |> up).Contains(textPattern |> up)
             && satisfiesGreps (line |> up) (options.GrepsString |> up)             

let internal getFileMatchInfo (fileInfo:FileInfo) (options:CommandLineOptions) = 
    let textPattern = options.TextPattern
    let isTextPatternRegex = options.IsTextPatternRegex
    let skipLargeFiles = not(options.MatchLargeFiles)
    let isTooLarge (fileInfo:FileInfo) = fileInfo.Length > int64(64 * 1024 * 1024)// 64 mb
    let isBinaryFile (fileInfo:FileInfo) =      
        [".exe"; ".dll"; ".pdb"; ".trc"]
        |> Seq.exists (fun ext -> ext = Path.GetExtension fileInfo.FullName)

    if String.IsNullOrEmpty textPattern
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

[<EntryPoint>]
let main argv = 
    printfn "%A" argv

    let mutable exitCode = 2
    // pure imperative code, not cool ...
    // TODO: rewrite in as declarative manner as possible
    // with all the monads and stuff ...

    try
        let arguments = CommandLineOptions.ParseArguments(argv)
        if arguments.IsNone
        then
            exitCode <- 1
        else
            let workingDirectory = if String.IsNullOrEmpty(arguments.Value.WorkingDirectory) 
                                   then Environment.NewLine
                                   else arguments.Value.WorkingDirectory
            let files = if arguments.Value.IsFileNamePatternRegex
                        then getFilesByRegexPattern workingDirectory (arguments.Value.FileNameRegexPattern)
                        else getFilesByWildcard workingDirectory (arguments.Value.FileNamePattern)                    

            ignore <|
            if String.IsNullOrEmpty(arguments.Value.TextPattern)
            then
                files                 
                |> Seq.sortBy (fun file -> file.FullName)
                // no 'lazy'-stuff here
                // TODO: add 'lazy'-stuff in here
                |> Seq.map (fun file -> (sprintf "> %s" (fileToUri file)))
                |> joinLines
                |> printfn "%s"
                printfn "%i file(s) found." (Seq.length files)
            else 
                let result = files |> Seq.map (fun file -> getFileMatchInfo file arguments.Value)
                                   |> Seq.where (fun matchInfo -> not(Seq.isEmpty matchInfo.MatchedLines))
                                   |> Seq.where (fun matchInfo -> not(matchInfo.ErrorOccurred))

                result |> Seq.iter (fun matchInfo -> 
                                        printfn "> %s" (fileToUri matchInfo.FileInfo)
                                        matchInfo.MatchedLines
                                        |> Seq.iter (fun (index, line) ->
                                                         printfn "    > line %i: %s" index (line.Trim())
                                                     )
                                    )
                printfn "%i matching file(s) foiund." (Seq.length result)

            exitCode <- 0
    with
    | :? AssertException as exc -> printfn "%s" (exc.ToString())
                                   exitCode <- 2

    exitCode