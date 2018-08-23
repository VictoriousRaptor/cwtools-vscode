// --------------------------------------------------------------------------------------
// FAKE build script
// --------------------------------------------------------------------------------------

// #I "packages/build/FAKE/tools"
// #r "FakeLib.dll"
// #r "paket: groupref build //"
#r "paket:
    nuget Fake.Core
    nuget Fake.Core.Target
    nuget Fake.IO.FileSystem
    nuget Fake.DotNet.Cli
    nuget Fake.DotNet.Paket
    nuget Fake.JavaScript.Npm
    nuget Fake.Core.UserInput //"
#load "./.fake/build.fsx/intellisense.fsx"

open System.Diagnostics
open Fake.Core
open Fake.DotNet
open Fake.JavaScript
open Fake.IO
open Fake.IO.Globbing.Operators

let run cmd args dir =
    if Process.execSimple( fun info ->
        let info = { info with FileName = cmd; Arguments = args }
        if not( String.isNullOrWhiteSpace dir) then
            { info with WorkingDirectory = dir } else info
    ) System.TimeSpan.MaxValue <> 0 then
        failwithf "Error while running '%s' with args: %s" cmd args


let platformTool tool path =
    match Environment.isUnix with
    | true -> tool
    | _ ->  match Process.tryFindFileOnPath path with
            | None -> failwithf "can't find tool %s on PATH" tool
            | Some v -> v

let npmTool =
    platformTool "npm"  "npm.cmd"

let vsceTool = lazy (platformTool "vsce" "vsce.cmd")


let releaseBin      = "release/bin"
let fsacBin         = "paket-files/github.com/fsharp/FsAutoComplete/bin/release"

let releaseBinNetcore = releaseBin + "_netcore"
let fsacBinNetcore = fsacBin + "_netcore"

let cwtoolsPath = ""
let cwtoolsProjectName = "Main.fsproj"

// --------------------------------------------------------------------------------------
// Build the Generator project and run it
// --------------------------------------------------------------------------------------

Target.create "Clean" (fun _ ->
    Shell.cleanDir "./temp"
    Shell.cleanDir "./out/server"
    // CopyFiles "release" ["README.md"; "LICENSE.md"]
    // CopyFile "release/CHANGELOG.md" "RELEASE_NOTES.md"
)

Target.create "YarnInstall" <| fun _ ->
    Npm.install id

Target.create "DotNetRestore" <| fun _ ->
    DotNet.restore (fun p -> { p with Common = { p.Common with WorkingDirectory = "src/Main" }} ) cwtoolsProjectName

let publishParams (framework : string) (release : bool) = 
    (fun (p : DotNet.PublishOptions) ->
        { p with 
            Common = 
                {
                    p.Common with
                        WorkingDirectory = "src/Main"
                        CustomParams = Some ("--self-contained true" + (if release then "" else " /p:LinkDuringPublish=false"))
                }
            OutputPath = Some ("../../out/server/" + framework)
            Runtime = Some framework
            Configuration = DotNet.BuildConfiguration.Release
        })

Target.create "BuildServer" <| fun _ ->
    // DotNetCli.Publish (fun p -> {p with WorkingDir = "src/Main"; AdditionalArgs = ["--self-contained"; "true"; "/p:LinkDuringPublish=false"]; Output = "../../out/server/win-x64"; Runtime = "win-x64"; Configuration = "Release"})
    DotNet.publish (publishParams "linux-x64" false) cwtoolsProjectName //(fun p -> {p with Common = { p.Common with WorkingDirectory = "src/Main"; CustomParams = Some "--self-contained true /p:LinkDuringPublish=false";}; OutputPath = Some "../../out/server/linux-x64"; Runtime =  Some "linux-x64"; Configuration = DotNet.BuildConfiguration.Release }) cwtoolsProjectName

Target.create "PublishServer" <| fun _ ->
    DotNet.publish (publishParams "win-x64" true) cwtoolsProjectName 
    DotNet.publish (publishParams "linux-x64" true) cwtoolsProjectName 
    DotNet.publish (publishParams "osx.10.11-x64" true) cwtoolsProjectName 

let runTsc additionalArgs noTimeout =
    let cmd = "tsc"
    // let timeout = if noTimeout then System.TimeSpan.MaxValue else System.TimeSpan.FromMinutes 30.
    run cmd additionalArgs ""
Target.create "RunScript" (fun _ ->
    Shell.Exec @"tsc" |> ignore
)

// Target "Watch" (fun _ ->
//     runFable "--watch" true
// )

// Target "CompileTypeScript" (fun _ ->
//     // !! "**/*.ts"
//     //     |> TypeScriptCompiler (fun p -> { p with OutputPath = "./out/client", Projec }) 
//     //let cmd = "tsc -p ./"
//     //DotNetCli.RunCommand id cmd
//     ExecProcess (fun p -> p. <- "tsc" ;p.Arguments <- "-p ./") (TimeSpan.FromMinutes 5.0) |> ignore
// )
Target.create "PaketRestore" (fun _ ->
    Shell.replaceInFiles ["../cwtools",Path.getFullName("../cwtools")] ["paket.lock"]
    Paket.PaketRestoreDefaults |> ignore
    Shell.replaceInFiles [Path.getFullName("../cwtools"),"../cwtools"] ["paket.lock"]
    )

Target.create "CopyFSAC" (fun _ ->
    Directory.ensure releaseBin
    Shell.cleanDir releaseBin

    !!(fsacBin + "/*")
    |> Shell.copyFiles releaseBin
)

Target.create "CopyFSACNetcore" (fun _ ->
    Directory.ensure releaseBinNetcore
    Shell.cleanDir releaseBinNetcore

    Shell.copyDir releaseBinNetcore fsacBinNetcore (fun _ -> true)
)



Target.create "InstallVSCE" ( fun _ ->
    Process.killAllByName "npm"
    run npmTool "install -g vsce" ""
)


Target.create "BuildPackage" ( fun _ ->
    Process.killAllByName "vsce"
    run vsceTool.Value "package" ""
    Process.killAllByName "vsce"
    !!("*.vsix")
    |> Seq.iter(Shell.moveFile "./temp/")
)


Target.create "PublishToGallery" ( fun _ ->
    let token =
        match Environment.environVarOrDefault "vsce-token" System.String.Empty with
        | s when not (String.isNullOrWhiteSpace s) -> s
        | _ -> UserInput.getUserPassword "VSCE Token: "

    Process.killAllByName "vsce"
    run vsceTool.Value (sprintf "publish patch --pat %s" token) ""
)



// --------------------------------------------------------------------------------------
// Run generator by default. Invoke 'build <Target>' to override
// --------------------------------------------------------------------------------------

Target.create "Build" ignore
Target.create "Release" ignore
Target.create "DryRelease" ignore

//"YarnInstall" ?=> "RunScript"
//"DotNetRestore" ?=> "RunScript"

// "Clean"
// //==> "RunScript"
// ==> "Default"

open Fake.Core.TargetOperators

"Clean"
//==> "RunScript"
//==> "CopyFSAC"
//==> "CopyFSACNetcore"
//==> "CopyForge"
//==> "CopyGrammar"
//==> "CopySchemas"
==> "BuildServer"
==> "Build"

"RunScript" ==> "Build"
"RunScript" ==> "PublishServer"
"PaketRestore" ==> "BuildServer"
"PaketRestore" ==> "PublishServer"
// "CompileTypeScript" ==> "Build"
//"DotNetRestore" ==> "BuildServer"
//"DotNetRestore" ==> "Build"

"Clean"
//==> "SetVersion"
// ==> "InstallVSCE"
==> "PublishServer"
==> "BuildPackage"
//==> "ReleaseGitHub"
==> "PublishToGallery"
==> "Release"

"BuildPackage"
==> "DryRelease"

Target.runOrDefaultWithArguments "Build"
