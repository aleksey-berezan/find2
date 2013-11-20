module Assert
    open System     
    open find2

    let Critical condition message = 
        if not(condition)
            then raise (Exception(message))
            else () |> ignore

    let Soft condition message = 
        if not(condition)
            then raise (AssertException(message))
            else () |> ignore