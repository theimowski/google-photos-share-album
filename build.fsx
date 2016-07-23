#r @"packages/FAKE/tools/FakeLib.dll"
#r @"packages/Selenium.WebDriver/lib/net40/WebDriver.dll"
#r @"packages/canopy/lib/canopy.dll"

open System
open Fake
open canopy
open runner

Target "Share" (fun _ ->
    canopy.configuration.chromeDir <- @"./packages/Selenium.WebDriver.ChromeDriver/driver"
    start chrome

    Console.ReadKey() |> ignore

    quit()
)

RunTargetOrDefault "Share"
