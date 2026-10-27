# PackageCascade

PackageCascade is being developed as a release planner for solutions containing packable .NET projects. The repository currently contains a .NET 11 class library with TUnit unit and integration tests.

## Work locally

Install the SDK version in `global.json` (currently .NET 11 RC2), then run:

```sh
dotnet test --solution src/PackageCascade.slnx -c Release
```

The projects are `PackageCascade`, `PackageCascade.Tests.Unit`, and `PackageCascade.Tests.Integration`. The unit test project can access the library's internal members through `InternalsVisibleTo`; the integration test uses its public surface.

CI runs the tests for source, SDK pin, or CI workflow changes. The workflows use Blacksmith runners.
