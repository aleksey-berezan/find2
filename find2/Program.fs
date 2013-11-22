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

            if String.IsNullOrEmpty(arguments.Value.TextPattern)
            then
                files                 
                |> Seq.sortBy (fun file -> file.FullName)
                // no 'lazy'-stuff here
                // TODO: add 'lazy'-stuff in here
                |> Seq.map (fun file -> (sprintf "> %s" (fileToUri file)))
                |> joinLines
                |> printfn "%s"
            else
                // TODO: implement more stuff here
            ()
            |> ignore

            exitCode <- 0
    with
    | :? AssertException as exc -> printfn "%s" (exc.ToString())
                                   exitCode <- 2

    exitCode