
open System
open System.Diagnostics
open System.IO
open System.Runtime.InteropServices
open System.Xml.Linq
open System.Linq

let exec = build.exec

[<EntryPoint>]
let main argv =
    let timer = Stopwatch.StartNew()

    let top =
        let cwd = Directory.GetCurrentDirectory()
        Path.GetFullPath(Path.Combine(cwd, ".."))
        // TODO maybe walk upward until we find the right directory

    exec "dotnet" "run .." (Path.Combine(top, "version_stamp"))

    exec "dotnet" "run" (Path.Combine(top, "gen_providers"))

    let dir_nupkgs = Path.Combine(top, "nupkgs")
    Directory.CreateDirectory(dir_nupkgs) |> ignore
    // Keep the native e_sqlcipher package: it is built by the build-sqlcipher
    // workflow (or by hand, see build_native/README.md), not by this script.
    for s in Directory.GetFiles(dir_nupkgs, "SQLitePCLRaw.*.nupkg") do
        File.Delete(s)

    // All the regular packages
    let pack_dirs = [
        "core"
        "ugly" 
        "provider.dynamic_cdecl" 
        "provider.dynamic_stdcall" 
        "provider.internal" 
        "provider.winsqlite3" 
        "provider.e_sqlite3" 
        "provider.sqlite3" 
        "provider.sqlcipher"
        "provider.e_sqlcipher"
    ]
    for s in pack_dirs do
        let dir_name = sprintf "SQLitePCLRaw.%s" s
        exec "dotnet" "pack -c Release" (Path.Combine(top, "src", dir_name))

    // These "batteries" assemblies are built but not packed in their own packages
    let build_dirs = [
        "batteries.e_sqlite3.internal"
        "batteries.e_sqlite3.dllimport"
        "batteries.e_sqlcipher.internal"
        "batteries.e_sqlcipher.dllimport"
    ]
    for s in build_dirs do
        let dir_name = sprintf "SQLitePCLRaw.%s" s
        exec "dotnet" "build -c Release" (Path.Combine(top, "src", dir_name))

    // These packages are built using a dummy csproj and a separate nuspec,
    // and the config packages contain the batteries assemblies built above.
    let pack_dirs = [
        "config.e_sqlite3"
        "bundle_e_sqlite3"
        "config.e_sqlcipher"
        "bundle_e_sqlcipher"
    ]
    for s in pack_dirs do
        let dir_name = sprintf "SQLitePCLRaw.%s" s
        exec "dotnet" "pack -c Release" (Path.Combine(top, "src", dir_name))

    let get_build_prop p =
        let path_xml = Path.Combine(top, "Directory.Build.props")
        let xml = XElement.Load(path_xml);
        let props = xml.Elements(XName.Get "PropertyGroup").First()
        let ver = props.Elements(XName.Get p).First()
        ver.Value

    let version = get_build_prop "Version"

    printfn "%s" version

    exec "dotnet" "run" (Path.Combine(top, "test_nupkgs", "smoke"))

    exec "dotnet" "run" (Path.Combine(top, "test_nupkgs", "fsmoke"))

    let real_xunit_dirs = [
        yield "e_sqlite3"
        //yield "e_sqlite3mc"
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then yield "winsqlite3"
        ]

    let fake_xunit_tfms = [
        yield "net11.0"
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then yield "net471"
        ]

    let fake_xunit_dirs = [
        yield "e_sqlite3"
        if Environment.GetEnvironmentVariable("SG_NUGET_TOKEN") |> isNull |> not then yield "e_see"
        //yield "e_sqlite3mc"
        if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then yield "winsqlite3"
        ]

    // real_xunit only targets $(tfm_net).
    for dir in real_xunit_dirs do
        exec "dotnet" "test --framework=net11.0" (Path.Combine(top, "test_nupkgs", dir, "real_xunit"))

    for tfm in fake_xunit_tfms do
        for dir in fake_xunit_dirs do
            let args = sprintf "run --framework=%s" tfm
            exec "dotnet" args (Path.Combine(top, "test_nupkgs", dir, "fake_xunit"))

    // The e_sqlcipher tests need the native e_sqlcipher package in nupkgs/.
    if Directory.GetFiles(dir_nupkgs, "e_sqlcipher.*.nupkg").Length > 0 then
        exec "dotnet" "run" (Path.Combine(top, "test_nupkgs", "e_sqlcipher_smoke"))
        for tfm in fake_xunit_tfms do
            let args = sprintf "run --framework=%s" tfm
            exec "dotnet" args (Path.Combine(top, "test_nupkgs", "e_sqlcipher", "fake_xunit"))
        exec "dotnet" "run" (Path.Combine(top, "test_nupkgs", "e_sqlcipher_tests", "desktop"))
    else
        printfn "skipping the e_sqlcipher tests: no e_sqlcipher.*.nupkg in %s" dir_nupkgs

#if not // TODO currently fails in the GitHub Action, not yet sure why
    if RuntimeInformation.IsOSPlatform(OSPlatform.Windows) then
        let args = "run -f net6.0-windows -r win-x86 --no-self-contained"
        exec "dotnet" args (Path.Combine(top, "test_nupkgs", "e_sqlite3", "fake_xunit"))
        exec "dotnet" args (Path.Combine(top, "test_nupkgs", "e_sqlite3mc", "fake_xunit"))
#endif

    printfn "generating push.bat"
    let bat = System.Collections.Generic.List<string>()
    for s in Directory.GetFiles(dir_nupkgs, "*.nupkg") do
        let fname = Path.GetFileName(s)
        let line = sprintf ".\\nuget push %s -Source https://api.nuget.org/v3/index.json -ApiKey %%1" fname
        bat.Add(line)
    File.WriteAllLines(Path.Combine(dir_nupkgs, "push.bat"), bat)

    timer.Stop()
    printfn "Total build time: %A milliseconds" timer.ElapsedMilliseconds

    0 // return an integer exit code

