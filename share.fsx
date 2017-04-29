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


let constructBody links =
    let linksMarkup =
        links
        |> List.map (fun (url,name) -> sprintf """<li><a href="%s">%s</a></li>""" url name)
        |> String.concat Environment.NewLine

    sprintf """Udostępniłem albumy:<br/><ul>%s</ul>""" linksMarkup

let fromAddress = MailAddress("tomekheimowski@gmail.com", "Tomek Heimowski")
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
        "#Email" << account
        click "#next"
        "#Passwd" << password
        click "#signIn"
        
        let toSend =
            [ for link in links do
                tracefn "Link %s for %s" link account
                url link
                sleep 3
                click ".f8eGGd" // options
                waitFor (fun () -> (elements ".z80M1").Length > 3)
                let elems = 
                    (elementsWithText ".z80M1" "Pokaż w albumach" )
                    |> List.append (elementsWithText ".z80M1" "Show in Albums" )
                match elems with
                | [elem] ->
                    click elem
                    sleep 3
                    press enter
                    //waitFor (fun () -> (elementsWithText ".aGJE1b" "Wyświetlam w Albumach").Length = 1)
                    let name = (element ".cL5NYb").Text
                    sleep 3
                    yield (link,name)
                | _ -> ()
            ]

        sleep 3
        if toSend.Length > 0 then sendMail (mail, toSend)
        
        url "https://accounts.google.com/Logout"

        quit()
)

Target "Discover" (fun _ ->
    canopy.configuration.chromeDir <- @"./packages/Selenium.WebDriver.ChromeDriver/driver/win32"
    start chrome
    url "https://accounts.google.com"
    pin FullScreen

    match someElement "#headingText" with
    | Some _ ->
        // new sign in page
        "#identifierId" << fromAddress.Address
        click "#identifierNext"
        "input[aria-label=\"Enter your password\"]" << fromPassword
        click "#passwordNext"
    | None ->
        // old sign in page
        "#Email" << fromAddress.Address
        click "#next"
        "#Passwd" << fromPassword
        click "#signIn"

    sleep ()
    
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

    elementTimeout <- 1.0
 
    //printfn "Scroll and press enter to start"
    //Console.ReadLine() |> ignore
                  

    let skip =
        IO.File.ReadAllLines "skip.txt"
        |> Set.ofArray

    let albums =
        [for i in [1..10] do
            let albums = elements ".MTmRkb" |> List.take 40
            let links = 
                [for album in albums do
                    let title = (elementWithin ".mfQCMe" album).Text
                    yield album.GetAttribute("href") ]
                    //IO.File.AppendAllText ("out", sprintf "%s %s\n" title (album.GetAttribute("href")))
            for _ in [1..3] do
                press Keys.PageDown
                sleep ()
            yield! links]
        |> List.distinct
        //for album in List.rev albums do ctrlClick album

    printfn "%A\n\n\n" albums

    for album in albums do
        try 
            url album
            let owner = elements ".MMYVu" |> Seq.head |> fun e -> e.GetAttribute "title"
            let sharebutton = element "div[jscontroller=\"YafD9d\"]"
            let linkbutton = clickAndWait sharebutton ".ex6r4d"
            let ahref = clickAndWait linkbutton "a[jsname=\"s7JrIc\"]"
            let title = element ".IqUHod"
            let uri = ahref.Text.Substring(ahref.Text.LastIndexOf "/" + 1)
            printfn "%s %s %s" uri owner title.Text
        with e ->
            printfn "ERROR: %s" e.Message

    url "https://accounts.google.com/Logout"

    quit ()
)

RunTargetOrDefault "Share"
