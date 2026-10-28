# PackageCascade

[![CI](https://img.shields.io/github/actions/workflow/status/GoSimulate/PackageCascade/ci.yml?branch=main&style=for-the-badge&logo=github&label=CI)](https://github.com/GoSimulate/PackageCascade/actions/workflows/ci.yml)
[![CD](https://img.shields.io/github/actions/workflow/status/GoSimulate/PackageCascade/cd.yml?branch=main&style=for-the-badge&logo=github&label=CD)](https://github.com/GoSimulate/PackageCascade/actions/workflows/cd.yml)
[![NuGet](https://img.shields.io/nuget/v/PackageCascade?style=for-the-badge&logo=nuget)](https://www.nuget.org/packages/PackageCascade/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PackageCascade?style=for-the-badge)](https://www.nuget.org/packages/PackageCascade/)
[![License](https://img.shields.io/github/license/GoSimulate/PackageCascade?style=for-the-badge)](LICENSE)

PackageCascade is a .NET tool for planning releases in solutions containing multiple packable projects. It discovers package dependencies from evaluated MSBuild project references, assigns independent versions, and produces a JSON plan that includes transitive dependants in release order.

> [!WARNING]
> We developed this library for our own internal use and are sharing it because we believe it may be useful to the wider developer community. Issues and pull requests are welcome, but we make no support commitments or warranties, and we cannot guarantee that we will respond to issues or review or merge pull requests.

The `plan` command is read-only. It writes the release plan to standard output as JSON, or to a file when given `--outfile`. Execution is separate: callers can use the plan themselves, and dedicated execution commands are planned.

## Work locally

Install the SDK version in `global.json` (currently .NET 11 RC2), then run:

```sh
dotnet test --solution src/PackageCascade.slnx -c Release
```

The solution contains the core library and CLI, their TUnit unit test projects, and an integration test project. Tests cover release planning, JSON serialisation, the CLI command, and a CLI process running against a temporary Git repository.

CI runs the tests for source, SDK pin, or CI workflow changes. The workflows use Blacksmith runners.
