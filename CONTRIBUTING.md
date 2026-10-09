# Contributing

Issues are welcome for bugs, questions, and ideas. Pull requests are welcome
after discussing the proposed change in an issue first; unsolicited pull
requests may be closed if the scope or direction has not been agreed.

## Build and test

From a public-repository checkout, run:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\build.ps1 -Zip
```

The script discovers a .NET SDK on `PATH`, then the per-user SDK under
`%LOCALAPPDATA%\Microsoft\dotnet`, and then Visual Studio MSBuild via
`vswhere`. Unit tests run by default. Use `-SkipTests` only when you
intentionally need a build without the test run. A local Revit 2024
installation supplies the API references when available; otherwise the pinned
Nice3point reference packages are used.

Do not upload client or project files to issues or pull requests. This includes
RVT/XLSX files, screenshots containing project data, internal paths, and user
names. See [SECURITY.md](SECURITY.md) for private vulnerability reports.

Read [AGENTS.md](AGENTS.md) for architecture invariants, source boundaries,
transaction rules, and verification expectations.
