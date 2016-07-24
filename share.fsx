#r @"System.Net"

#r @"packages/FAKE/tools/FakeLib.dll"
#r @"packages/Selenium.WebDriver/lib/net40/WebDriver.dll"
#r @"packages/canopy/lib/canopy.dll"

open System
open System.Net
open System.Net.Mail

open Fake
open canopy
open runner

let accounts =
    IO.File.ReadAllLines(__SOURCE_DIRECTORY__ </> "accounts.csv")
    |> Array.map (fun s -> s.Split(','))
    |> Array.map (fun a -> a.[0], (a.[1], a.[2], a.[3]))
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

let fromPassword = UserInputHelper.getUserPassword "gmail password:"

let constructBody links =
    let linksMarkup =
        links
        |> List.map (fun (url,name) -> sprintf """<li><a href="%s">%s</a></li>""" url name)
        |> String.concat Environment.NewLine

    sprintf """Udostępniłem albumy:<br/><ul>%s</ul>""" linksMarkup

let sendMail (address, links) =
    let fromAddress = MailAddress("tomekheimowski@gmail.com", "Tomek Heimowski")
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
    smtp.Timeout <- 3000
    smtp.Send(msg)

Target "Share" (fun _ ->
    canopy.configuration.chromeDir <- @"./packages/Selenium.WebDriver.ChromeDriver/driver"

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
                url link
                click ".f8eGGd" // options
                waitFor (fun () -> (elements ".z80M1").Length > 3)
                let elems = (elementsWithText ".z80M1" "Pokaż w albumach" )
                match elems with
                | [elem] ->
                    click elem
                    sleep 3
                    press enter
                    //waitFor (fun () -> (elementsWithText ".aGJE1b" "Wyświetlam w Albumach").Length = 1)
                    let name = (element ".cL5NYb").Text
                    yield (link,name)
                | _ -> ()
            ]

        if toSend.Length > 0 then sendMail (mail, toSend)
        
        url "https://accounts.google.com/Logout"

        quit()
)

RunTargetOrDefault "Share"
