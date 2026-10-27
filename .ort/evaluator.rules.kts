val allowedLicences = setOf(
    "MIT",
    "Apache-2.0",
    "BSD-2-Clause",
    "BSD-3-Clause",
    "ISC"
).map { it.toSpdx() as SpdxSingleLicenseExpression }.toSet()

// TUnit pulls this test-only coverage tool into the template. Microsoft .NET
// Library terms permit development and testing, but it is not an SPDX licence.
// Review new versions separately: https://www.nuget.org/packages/Microsoft.Testing.Extensions.CodeCoverage/18.10.0/license
val testCoverageException = "NuGet:Microsoft.Testing:Extensions.CodeCoverage:18.10.0"

fun PackageRule.LicenseRule.isOutsideAllowlist() = object : RuleMatcher {
    override val description = "isOutsideAllowlist($license)"
    override fun matches() = license !in allowedLicences
}

val licenceRules = ruleSet(ortResult, licenseInfoResolver, resolutionProvider) {
    dependencyRule("UNAPPROVED_PACKAGE_LICENCE") {
        require {
            -isExcluded()
        }

        // ORT also visits the repository's project entries, which are not consumed packages.
        val isTestProject = project.id.name.endsWith(".Tests.Unit.csproj") ||
            project.id.name.endsWith(".Tests.Integration.csproj")
        val isReviewedTestCoverage = isTestProject && pkg.metadata.id.toCoordinates() == testCoverageException

        if (pkg.metadata.id.type == "NuGet" && pkg.metadata.id.version.isNotEmpty() &&
            !isReviewedTestCoverage
        ) {
            if (pkg.metadata.declaredLicensesProcessed.spdxExpression == null) {
                error(
                    "No recognised licence was declared for ${pkg.metadata.id.toCoordinates()}.",
                    "Review the package licence and record a justified exception before approving it."
                )
            }

            licenseRule("UNAPPROVED_PACKAGE_LICENCE", LicenseView.CONCLUDED_OR_DECLARED_OR_DETECTED) {
                require {
                    -isExcluded()
                    +isOutsideAllowlist()
                }

                error(
                    "The licence $license for ${pkg.metadata.id.toCoordinates()} is outside the approved allowlist.",
                    "Review the licence before adding it to the allowlist or record a justified exception."
                )
            }
        }
    }
}

ruleViolations += licenceRules.violations
