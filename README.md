# PackageCascade

[![CI](https://img.shields.io/github/actions/workflow/status/GoSimulate/PackageCascade/ci.yml?branch=main&style=for-the-badge&logo=github&label=CI)](https://github.com/GoSimulate/PackageCascade/actions/workflows/ci.yml)
[![CD](https://img.shields.io/github/actions/workflow/status/GoSimulate/PackageCascade/cd.yml?branch=main&style=for-the-badge&logo=github&label=CD)](https://github.com/GoSimulate/PackageCascade/actions/workflows/cd.yml)
[![NuGet](https://img.shields.io/nuget/v/PackageCascade?style=for-the-badge&logo=nuget)](https://www.nuget.org/packages/PackageCascade/)
[![NuGet Downloads](https://img.shields.io/nuget/dt/PackageCascade?style=for-the-badge)](https://www.nuget.org/packages/PackageCascade/)
[![License](https://img.shields.io/github/license/GoSimulate/PackageCascade?style=for-the-badge)](LICENSE)

PackageCascade plans and builds releases for .NET solutions whose packages have independent versions. When a dependency changes, it includes the packages that depend on it, so you can release a consistent set without giving every package the same version.

> [!WARNING]
> We developed this library for our own internal use and are sharing it because we believe it may be useful to the wider developer community. Issues and pull requests are welcome, but we make no support commitments or warranties, and we cannot guarantee that we will respond to issues or review or merge pull requests.

## Release workflow

Use `plan` to inspect what needs releasing, then either execute the JSON plan yourself or pass it to `apply`. Planning is read-only. Applying builds and packs the selected projects in dependency order, verifies the package identities and inter-package dependency versions, and creates local Git tags.

Your pipeline owns tests, package publication, pushing tags, and any recovery or rollback. Run plan and apply against the same checkout without changing it between steps: apply builds the current files and tags the current `HEAD`.

### Run the CLI

[GitHub releases](https://github.com/GoSimulate/PackageCascade/releases) containing these actions include a self-contained CLI for Linux x64, Windows x64, macOS x64, and macOS arm64. Download the binary for your runner; the tool itself does not require a .NET runtime. Planning and applying still require the SDK used by the consumer projects.

From the repository you want to release, point `cli` at the downloaded binary. These examples use Bash:

```sh
cli=/absolute/path/to/PackageCascade-linux-x64
chmod +x "$cli"

"$cli" plan src/Example.slnx --outfile release-plan.json
# Inspect release-plan.json before applying it.
"$cli" apply --infile release-plan.json --output artifacts/packages
```

For a single pipeline step, the plan can be streamed directly:

```sh
set -o pipefail
"$cli" plan src/Example.slnx |
  "$cli" apply --output artifacts/packages
```

### Use the GitHub Actions

The `plan` and `apply` composite actions download a specified PackageCascade release and expose paths to their JSON results. This example is a workflow in the repository being released. Replace `<release-tag>` with a published PackageCascade tag, and use that same tag in each `uses` reference and `version` input. Change the solution path to match your repository.

```yaml
name: Prepare packages
on: workflow_dispatch
jobs:
  prepare:
    runs-on: ubuntu-latest
    steps:
      - uses: actions/checkout@v7.0.1
        with:
          fetch-depth: 0
      - uses: actions/setup-dotnet@v6.0.0
        with:
          global-json-file: global.json
      - id: plan
        uses: GoSimulate/PackageCascade/.github/actions/plan@<release-tag>
        with:
          version: <release-tag>
          solution: src/Example.slnx
      - id: apply
        uses: GoSimulate/PackageCascade/.github/actions/apply@<release-tag>
        with:
          version: <release-tag>
          plan-path: ${{ steps.plan.outputs.plan-path }}
          output-directory: artifacts/packages
```

The example reads the SDK version from the consumer repository's `global.json`; configure SDK installation for your repository if it uses another method. Run your own tests before applying the plan. The apply action exposes `result-path` for subsequent publication or tag-push steps. Both actions accept an optional `repository` path; plan also accepts `configuration`, while apply accepts `include-symbols`.

To build the CLI from source instead, install the SDK specified in this repository's `global.json` and run `dotnet build src/PackageCascade.Cli/PackageCascade.Cli.csproj -c Release`.

Both commands default to the current directory as the Git repository; use `--repository` to select another. Planning defaults to `Release`; `plan --configuration Debug` or a custom configuration records that choice for apply. Add `apply --include-symbols` to generate `.snupkg` files. Apply writes JSON containing the output package paths and created tag names to standard output.

The SDK needed to build PackageCascade does not dictate the target frameworks of your packages. Apply uses the consumer solution's SDK selection and project settings. Versions are supplied during the build, without editing your project files; a reference to another package keeps that package's own version.

## How versions are chosen

Each package has its own release history, tracked by tags named `<PackageId>-v<version>`, such as `Example.Core-v2.3.1`. The planner uses the highest version tag reachable from `HEAD` for that package and examines commits since that tag. In CI, fetch the history and tags needed for those baselines; a shallow checkout with missing tags can make a previously released package appear new.

- A package with no matching reachable tag is included at `0.1.0`.
- `fix:` and `perf:` changes require a Patch bump; `feat:` requires Minor. A conventional commit with `!` or a `BREAKING CHANGE:` footer requires Major.
- Changes are attributed by project directory. When several commits affect a package, the strongest bump wins.
- If B changes and A depends on B, A gets at least a Patch bump, propagating through its dependants. A's own Minor or Major change takes precedence.
- A change to root-level `Directory.Build.props`, `Directory.Build.targets`, `Directory.Packages.props`, `global.json`, or `NuGet.Config` affects every package, with Patch as the minimum even for `build:` or `chore:` commits. Nested files retain ordinary directory-based handling.

For example, a feature in B can release B from `1.4.0` to `1.5.0` and its unchanged dependant A from `3.2.0` to `3.2.1`. A's package then refers to B at `1.5.0`, rather than inheriting A's version.

## Applying in CI

Apply verifies the generated archives before copying them to the requested output directory and creating tags. It does not run your tests, publish to a feed, or push to a remote. Use the result to drive those subsequent pipeline steps.

Apply stops on failure and does not undo completed steps. A failure during tagging can leave verified archives and some newly created local tags. Your pipeline decides whether to discard the checkout or clean up before retrying; existing tags cause tag creation to fail. Matching archive filenames in the output directory are overwritten, so a dedicated output directory per run makes it easier to identify that run's artefacts.

## Work locally

Install the SDK version in `global.json`, then run:

```sh
dotnet test --solution src/PackageCascade.slnx -c Release
```

The test suite covers planning and execution, public dependency injection, and the built CLI, including plan-to-apply flows through files and a pipe. Repository fixtures are created in temporary directories; tests do not publish packages or push tags.
