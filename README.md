# PackageCascade

[![CI](https://img.shields.io/github/actions/workflow/status/GoSimulate/PackageCascade/ci.yml?branch=main&style=for-the-badge&logo=github&label=CI)](https://github.com/GoSimulate/PackageCascade/actions/workflows/ci.yml)
[![CD](https://img.shields.io/github/actions/workflow/status/GoSimulate/PackageCascade/cd.yml?branch=main&style=for-the-badge&logo=github&label=CD)](https://github.com/GoSimulate/PackageCascade/actions/workflows/cd.yml)
[![NuGet](https://img.shields.io/nuget/v/PackageCascade?style=for-the-badge&logo=nuget)](https://www.nuget.org/packages/PackageCascade/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PackageCascade?style=for-the-badge)](https://www.nuget.org/packages/PackageCascade/)
[![License](https://img.shields.io/github/license/GoSimulate/PackageCascade?style=for-the-badge)](LICENSE)

PackageCascade is a .NET tool under development for planning releases in solutions containing any number of packable projects. It is intended to discover package dependencies from evaluated MSBuild project references, assign independent versions, and produce a deterministic JSON plan that includes transitive dependants in the release set.

> [!WARNING]
> We developed this library for our own internal use and are sharing it because we believe it may be useful to the wider developer community. Issues and pull requests are welcome, but we make no support commitments or warranties, and we cannot guarantee that we will respond to issues or review or merge pull requests.

Planning will be read-only. Separate CLI commands are intended to apply, validate, and roll back a saved plan; callers may instead execute the plan themselves. The current repository contains the core and CLI scaffolding, but release planning and execution are not yet implemented.

## Work locally

Install the SDK version in `global.json` (currently .NET 11 RC2), then run:

```sh
dotnet test --solution src/PackageCascade.slnx -c Release
```

The solution contains the core library and CLI, their TUnit unit test projects, and an integration test project. The unit tests currently contain placeholders; the integration test launches the CLI and checks `--help`.

CI runs the tests for source, SDK pin, or CI workflow changes. The workflows use Blacksmith runners.
