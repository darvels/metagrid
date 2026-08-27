# Third-Party Dependency License Audit

This summary is limited to the dependencies used by the shipping `MetaGrid.UI` application and its immediate shipped dependency chain.

## Packages

| Package | Version | Detected License | Notes |
| --- | --- | --- | --- |
| Microsoft.Extensions.DependencyInjection | 8.0.1 | MIT | Permissive license; appears compatible with distribution alongside MPL-covered MetaGrid source. |
| Microsoft.Extensions.DependencyInjection.Abstractions | 8.0.2 | MIT | Transitive dependency of DI package; permissive and appears compatible. |
| Microsoft.Web.WebView2 | 1.0.3065.39 | Bundled `LICENSE.txt` in package | Permissive Microsoft redistribution terms were detected in the package. Review attribution/redistribution requirements before public binary distribution. |

## Notes

- The MetaGrid MPL-2.0 license applies only to MetaGrid-owned code.
- Third-party dependencies retain their own licenses.
- The self-contained release also redistributes Microsoft .NET runtime components and Windows Desktop runtime files. Those are not relicensed by MetaGrid and remain subject to Microsoft's terms.
- This file is a preparation audit, not legal advice.

## Manual Review Suggested

- Confirm whether future public binary releases should ship a dedicated third-party notices file alongside the ZIP.
- Re-check third-party license texts whenever package versions change.

