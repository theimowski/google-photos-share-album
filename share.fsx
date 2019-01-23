#r """paket: nuget canopy
nuget Selenium.WebDriver.ChromeDriver
nuget Fake.Core.Environment
nuget Fake.Core.Target
nuget Fake.IO.FileSystem
nuget Fake.Core.UserInput //"""
#load "./.fake/share.fsx/intellisense.fsx"

#if !FAKE
#r "netstandard"
#r "Facades/netstandard" // https://github.com/ionide/ionide-vscode-fsharp/issues/839#issuecomment-396296095
#endif

open System
open System.Net
open System.Net.Mail
open System.Text.RegularExpressions

open canopy.runner.classic
open canopy.configuration
open canopy.classic
open canopy.types
open OpenQA.Selenium

open Fake.Core

let accounts =
    IO.File.ReadAllLines("accounts.csv")
    |> Array.map (fun s -> s.Split(','))
    |> Array.map (fun a -> a.[0], (a.[1], a.[2]))
    |> Map.ofArray

let splitBy (c: char) (s: string) =
    s.Split([|c|], StringSplitOptions.RemoveEmptyEntries)

let who = 
    Environment.environVar "with"
    |> splitBy ','
    |> Array.map (fun s -> Map.find s accounts)

let links =
    Environment.environVar "links"
    |> splitBy ','
    |> Array.collect (fun x -> 
        [| sprintf "https://goo.gl/photos/%s" x
           sprintf "https://photos.app.goo.gl/%s" x |])


let constructBody links =
    let linksMarkup =
        links
        |> List.map (fun (url,name) -> sprintf """<li><a href="%s">%s</a></li>""" url name)
        |> String.concat Environment.NewLine

    sprintf """Udostępniłem albumy:<br/><ul>%s</ul>""" linksMarkup

// let fromMail = getBuildParam "mail"
// let fromName = getBuildParam "name"


let loginToGoogle(account,password) =
    match someElement "#headingText" with
    | Some _ ->
        // new sign in page
        "#identifierId" << account
        click "#identifierNext"
        "input[aria-label=\"Enter your password\"]" << password
        click "#passwordNext"
    | None ->
        // old sign in page
        "#Email" << account
        click "#next"
        "#Passwd" << password
        click "#signIn"
    sleep 3

let collect link account =
    printfn "Link %s for %s" link account
    url link
    try
        if elementsWithText "p" "404" |> List.isEmpty |> not then
            None // 404 - not found, one of 2 prefixes doesn't work
        else
            //sleep 3
            click ".f8eGGd" // options
            try
                waitFor (fun () -> (elements ".z80M1").Length > 3)
            with _ ->

                // sometimes opens "activity", navigate back and forth to fix
                navigate back
                sleep 3
                navigate forward
                sleep 3
            let elems = 
                (elementsWithText ".z80M1" "Pokaż w albumach" )
                |> List.append (elementsWithText ".z80M1" "Show in Albums" )
            match elems with
            | [elem] ->
                click elem
                //sleep 3
                press enter
                //waitFor (fun () -> (elementsWithText ".aGJE1b" "Wyświetlam w Albumach").Length = 1)
                let name = (element ".cL5NYb").Text
                sleep 3
                Some (link,name)
            | _ ->
                let elems = 
                    (elementsWithText ".z80M1" "Ukryj w albumach" )
                if elems.Length > 0 then 
                    () // already shared
                else
                    printfn "--> ERR: No elems found for %s" link
                None
    with _ ->
        printfn "--> ERR: Other error for %s" link
        None

let share () =
    let who = 
        who 
        |> Array.map (fun (account,mail) ->
            let pass = 
                
                UserInput.getUserPassword (sprintf "gmail password for %s:" account)
            account,pass,mail)
    
    canopy.configuration.chromeDir <- @"/Users/theimowski/.nuget/packages/selenium.webdriver.chromedriver/2.45.0/driver/mac64"

    for (account,password,mail) in who do
        
        start chrome
        
        url "https://accounts.google.com"
        pin Right
        loginToGoogle(account,password)
        
        let toSend =
            links
            |> Array.choose (fun link -> collect link account)
            |> Array.toList

        sleep 3
        
        url "https://accounts.google.com/Logout"

        quit()


let discover () =
    canopy.configuration.chromeDir <- @"/Users/theimowski/.nuget/packages/selenium.webdriver.chromedriver/2.45.0/driver/mac64"
    start chrome
    url "https://accounts.google.com"
    pin FullScreen

    //loginToGoogle(fromAddress.Address, fromPassword)
    
    url "https://photos.google.com/albums"

    let clickAndWait button waitSelector =
        let rec trial left =
            if left = 1 then
                element waitSelector
            else
                click button
                try 
                    waitForElement waitSelector
                    element waitSelector
                with _ -> 
                    trial (left - 1)

        trial 10

    elementTimeout <- 5.0
 
    //printfn "Scroll and press enter to start"
    //Console.ReadLine() |> ignore
                  

    let skip =
        IO.File.ReadAllLines "skip.txt"
        |> Set.ofArray

    let albums =
        [for i in [1..50] do
            let albums = elements ".MTmRkb" |> List.take 40
            printfn "got %d albums" albums.Length
            let links = 
                albums
                |> List.choose (fun album ->
                    try 
                        let title = (elementWithin ".mfQCMe" album).Text
                        try
                            Some <| (title, album.GetAttribute "href")
                        with _ ->
                            printfn "can't get link for %s" title
                            None
                    with _ ->
                        printfn "can't get"
                        None)
            for _ in [1..2] do
                press Keys.PageDown
                sleep ()
            yield! links]
        |> List.distinct
        |> List.filter (snd >> (<>) null)
        //for album in List.rev albums do ctrlClick album

    printfn "LIST:\n"
    for (title, _) in albums do
        printfn "%s" title

    let ok = ResizeArray<_>()

    for (title, album) in albums do
        try 
            url album
            let owner, sharedWith =
                match someElement ".MMYVu" with
                | Some e  ->
                    let owner = e.GetAttribute "title"
                    let people =
                        elements ".MMYVu"
                        |> List.skip 1
                        |> List.map (fun e -> e.GetAttribute "title")
                    owner, people
                | None -> "Not Shared", []
            let sharebutton = element "div[jscontroller=\"YafD9d\"]"
            let linkbutton = clickAndWait sharebutton ".ex6r4d"
            let ahref = clickAndWait linkbutton "a[jsname=\"s7JrIc\"]"
            let uri = ahref.Text.Substring(ahref.Text.LastIndexOf "/" + 1)
            let title = 
                try (element ".IqUHod").Text
                with _ -> title
            let ch (c: string) =
                if sharedWith |> List.exists (fun s -> s.ToLower().Contains (c.ToLower())) then "JEST" else ""
            let peps = 
                accounts 
                |> Map.toList 
                |> List.map (fst >> ch)
                |> String.concat ","
            printfn "\"%s\",%s,%s,%s" 
                    title owner uri  
                    peps
            ok.Add (title)
        with e ->
            try
                let title = (element ".IqUHod").Text        
                printfn ",,%s,,,,\n%s" title e.Message
            with _ ->
                printfn "ERROR: %s" e.Message

    url "https://accounts.google.com/Logout"

    quit ()

    let oks = Set.ofSeq ok
    let all = Set.ofSeq (albums |> List.map fst)
    let fails = all - oks
    for ok in oks do printfn "OK: '%s'" ok
    for fail in fails do printfn "FAILED: '%s'" fail


share ()
