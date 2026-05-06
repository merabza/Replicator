# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

Replicator is the host process for a "Daily Tools Launch Server" — an ASP.NET Core Web app (`net10.0`) that also runs as a Windows Service. It boots a Swagger-documented HTTP surface plus a periodic background worker that drives a cancellable process queue.

## Multi-repo solution

`Replicator.slnx` references sibling repositories at `..\` (i.e. peers of this repo, **not** submodules). The full clone layout is documented in [README.md](README.md) and looks like:

```
ReServer/
  ConnectionTools/  DatabaseTools/  ParametersManagement/  Replicator/  (this repo)
  ReplicatorShared/ SystemTools/    ToolsManagement/        WebAgentContracts/  WebSystemTools/
```

Opening `Replicator.slnx` without those siblings present will fail to load most projects. The `Replicator.csproj` itself only depends on a subset (SystemTools, WebSystemTools), so it builds standalone if those two sibling repos are present.

## Common commands

Run from the working tree root (`D:\1WorkDotnet\Replicator\Replicator`):

```bash
# Build only this project (requires SystemTools + WebSystemTools siblings)
dotnet build Replicator/Replicator.csproj

# Build whole solution (requires ALL siblings listed above)
dotnet build Replicator.slnx

# Run locally (Development profile launches Swagger at http://localhost:5041/swagger)
dotnet run --project Replicator/Replicator.csproj
```

There is no test project in this repo; `dotnet test` has nothing to run here.

## Architecture

The runtime is a thin shell built on shared infrastructure projects from sibling repos:

- **[Program.cs](Replicator/Program.cs)** — top-level statements. Order matters:
  1. `WebApplication.CreateBuilder` with `ContentRootPath = AppContext.BaseDirectory` (so the Windows-service install path resolves correctly).
  2. `UseSerilogLogger` from `WebSystemTools.SerilogLogger` — writes `appsettings.json:Serilog` config to console + rolling file.
  3. `UseWindowsServiceOnWindows` from `WebSystemTools.WindowsServiceTools` — auto-detects service vs interactive run.
  4. `AddSwagger` (versioned, `versionCount = 1`), `AddHostedServices`, `AddHttpClient`.
  5. After build: `UseSwaggerServices`, `UseApiExceptionHandler`, `UseTestToolsApiEndpoints` (test/health endpoints from `WebSystemTools.TestToolsApi`).

- **[HostedServiceDependencyInjection.cs](Replicator/DependencyInjection/HostedServiceDependencyInjection.cs)** registers two things:
  - `IProcesses` (singleton, from `SystemTools.BackgroundTasks`) — the cancellable background process queue used by the rest of the system.
  - `TimedHostedService` — the periodic driver.

- **[TimedHostedService.cs](Replicator/HostedServices/TimedHostedService.cs)** — a `System.Threading.Timer`-based `IHostedService` that ticks every minute starting at `TimeSpan.Zero`. Hooks `IHostApplicationLifetime.ApplicationStopping` to call `_processes.CancelProcesses()` so in-flight work cancels cleanly on shutdown. The actual job-dispatch logic (`_jobStarter`, `JobStarter`, `InstructionsFileName` from `AppSettings`) is currently **commented out** — this is a scaffold awaiting the JobStarter wiring. When implementing, the pattern to restore is: read `AppSettings.InstructionsFileName`, construct a `JobStarter` with the injected `IProcesses` + `IHttpClientFactory`, call `Run()` on first tick, then `DoTimerEventAnswer()` on subsequent ticks.

- **[AppSettings.cs](Replicator/Models/AppSettings.cs)** — single-property config (`InstructionsFileName`), bound from the `AppSettings` section of `appsettings.json`. The instructions file path is what eventually drives JobStarter.

A note on comments: existing source mixes Georgian and English (e.g. the DI file's comments describing `IProcesses` and `TimedHostedService` are in Georgian). Preserve the original language when editing nearby code; don't translate comments unless asked.

## Build conventions

[Directory.Build.props](Directory.Build.props) applies to every project in the solution and enforces a strict bar:

- `TargetFramework=net10.0`, `Nullable=enable`, `ImplicitUsings=disable` (you must write `using` directives explicitly).
- `AnalysisMode=All`, `TreatWarningsAsErrors=true`, `CodeAnalysisTreatWarningsAsErrors=true`, `EnforceCodeStyleInBuild=true`. Warnings break the build.
- `SonarAnalyzer.CSharp` is auto-included on every non-`.dcproj` project.
- Only `NU1608` is suppressed.

Package versions are pinned centrally in [Directory.Packages.props](Directory.Packages.props) (`ManagePackageVersionsCentrally=true`) — add new packages there, not in individual `.csproj` files.

## Code style ([.editorconfig](.editorconfig))

The .editorconfig promotes many style rules to **`error`** severity, so violations fail the build:

- File-scoped namespaces (`csharp_style_namespace_declarations = file_scoped:error`).
- `using` directives **outside** the namespace.
- Braces required even for single-statement bodies (`csharp_prefer_braces = true:error`).
- Explicit accessibility modifiers required on non-interface members.
- Predefined types (`int`, `string`) over BCL names (`Int32`, `String`).
- No `this.`/`Me.` qualification.
- Expression-bodied members required for properties, indexers, accessors, operators, lambdas, local functions — but **not** methods or constructors (those use block bodies).
- System `using`s sorted first, no separate import groups.
- CRLF line endings, final newline required.

When in doubt, match the surrounding file — the analyzers will tell you fast.
