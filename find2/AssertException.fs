namespace find2
type AssertException = 
    inherit System.Exception
    new(message) = { inherit System.Exception(message) }