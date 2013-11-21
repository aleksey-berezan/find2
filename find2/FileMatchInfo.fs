namespace find2
open System
open System.IO

[<StructAttribute>]
type internal FileMatchInfo = 
    val public FileInfo:FileInfo  
    val public Matched:bool
    val public MatchedLines:seq<(int*string)>
    val public ErrorOccurred:bool

    private new (fileInfo:FileInfo, matchedLines:seq<(int*string)>, errorOccurred:bool) =
        {
            FileInfo = fileInfo;
            ErrorOccurred = errorOccurred;
            MatchedLines = matchedLines;
            Matched = (Seq.isEmpty matchedLines);
        }

    static member NoMatches fileInfo = new FileMatchInfo(fileInfo, Seq.empty, false)
    static member WithMatches fileInfo matchedLines = new FileMatchInfo(fileInfo, matchedLines, false)
    static member WithError fileInfo = new FileMatchInfo(fileInfo, Seq.empty, true)