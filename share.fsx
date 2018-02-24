#r @"System.Net"

#r @"packages/FAKE/tools/FakeLib.dll"
#r @"packages/Selenium.WebDriver/lib/net40/WebDriver.dll"
#r @"packages/canopy/lib/canopy.dll"

open System
open System.Net
open System.Net.Mail
open System.Text.RegularExpressions

open Fake
open canopy
open runner
open OpenQA.Selenium

let accounts =
    IO.File.ReadAllLines(__SOURCE_DIRECTORY__ </> "accounts.csv")
    |> Array.map (fun s -> s.Split(','))
    |> Array.map (fun a -> a.[0], (a.[1], a.[2]))
    |> Map.ofArray

let splitBy (c: char) (s: string) =
    s.Split([|c|], StringSplitOptions.RemoveEmptyEntries)

let who = 
    getBuildParam "with"
    |> splitBy ','
    |> Array.map (fun s -> Map.find s accounts)

let links =
    getBuildParam "links"
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

let fromMail = getBuildParam "mail"
let fromName = getBuildParam "name"

let fromAddress = MailAddress(fromMail, fromName)
let fromPassword = 
    UserInputHelper.getUserPassword (sprintf "gmail password for %s:" fromAddress.Address)

let sendMail (address, links) =
    let toAddress = MailAddress(address, address)
    let subject =
        match links with
        | [(_,name)] -> sprintf "Album '%s'" name
        | _ -> sprintf "Albumy (%d)" links.Length
    let body = constructBody links 
    printfn "\n\n\n%s\n\n\n" body
    use msg = new MailMessage(fromAddress, toAddress, Subject = subject, Body = body, IsBodyHtml = true);
    use smtp = 
        new SmtpClient(
            Host = "smtp.gmail.com",
            Port = 587,
            EnableSsl = true,
            DeliveryMethod = SmtpDeliveryMethod.Network)
    smtp.UseDefaultCredentials <- true
    smtp.Credentials <- NetworkCredential(fromAddress.Address, fromPassword)
    smtp.Timeout <- 10000
    smtp.Send(msg)

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
    tracefn "Link %s for %s" link account
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

Target "Share" (fun _ ->
    let who = 
        who 
        |> Array.map (fun (account,mail) ->
            let pass = 
                UserInputHelper.getUserPassword (sprintf "gmail password for %s:" account)
            account,pass,mail)
    
    canopy.configuration.chromeDir <- @"./packages/Selenium.WebDriver.ChromeDriver/driver/win32"

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
        if toSend.Length > 0 && hasBuildParam "mail" then sendMail (mail, toSend)
        
        url "https://accounts.google.com/Logout"

        quit()
)

Target "Discover" (fun _ ->
    canopy.configuration.chromeDir <- @"./packages/Selenium.WebDriver.ChromeDriver/driver/win32"
    start chrome
    url "https://accounts.google.com"
    pin FullScreen

    loginToGoogle(fromAddress.Address, fromPassword)
    
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
)

RunTargetOrDefault "Share"
