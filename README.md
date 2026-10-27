# PackageCascade

This private GoSim template starts a .NET 11 class library with TUnit unit and integration tests and an empty C# Pulumi stack.

## Create a repository

1. In `GoSimulate/github-infra`, declare a new class deriving from `CSharpRepository` under `infra/Github.Infrastructure/Resources/Repositories/Active/CSharp/`. Give the repository a PascalCase name, optionally with dotted segments, such as `GoSim.Motor.Core`.
2. Merge the infrastructure change. Pulumi creates a private repository from this template, applies its canonical files, runs the rename script, and commits the initialised project. The rename script, its test and its workflow are then removed from the new repository.
3. Replace the new repository's copy of this README with project-specific instructions when the project takes shape.

The rename script updates the source and infrastructure project identities, namespaces, solution paths, Pulumi project name, and CI references. Repository creation and initialisation are managed by `github-infra`; do not create a repository from the GitHub template interface directly.

## Work locally

Install the SDK version in `global.json` (currently .NET 11 RC2), then run:

```sh
dotnet test --solution src/PackageCascade.slnx -c Release
```

The projects are `PackageCascade`, `PackageCascade.Tests.Unit`, and `PackageCascade.Tests.Integration`. The unit test project can access the library's internal members through `InternalsVisibleTo`; the integration test uses its public surface.

The `infra/` Pulumi program declares no resources. To preview it without a cloud account, create a disposable local backend and stack:

```sh
export PULUMI_BACKEND_URL="file://$(mktemp -d)"
export PULUMI_CONFIG_PASSPHRASE=local-preview
cd infra
pulumi stack init local --non-interactive
pulumi preview --stack local --non-interactive
```

In this template, CI runs the tests for source, SDK pin, or test workflow changes. Infrastructure changes run the empty Pulumi preview. Changes to the rename utility run its own check before they reach new repositories. All workflows use Blacksmith runners.
