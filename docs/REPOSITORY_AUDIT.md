# MetaGrid Repository Audit

This document records the local GitHub/open-source preparation audit completed before any public publication.

## Publication Candidate

### Safe To Publish

- `src/`
- `tests/`
- `scripts/`
- `build/heroes.json`
- `build/README.release.md`
- `MetaGrid.sln`
- `Directory.Build.props`
- `Directory.Build.rsp`
- `NuGet.Config`
- `README.md`
- `LICENSE`
- `CONTRIBUTING.md`
- `CHANGELOG.md`
- `.github/ISSUE_TEMPLATE/`
- `docs/`

### Excluded From Public Source

- `.appdata/`
- `.dotnet-home/`
- `.nuget/`
- `Release/`
- `artifacts/`
- `tmp/`
- `tools/`
- `**/bin/`
- `**/obj/`
- `.vs/`
- `tests/**/TestResults/`
- local verification screenshots in the repository root
- local log files such as `run-exact.log` and `run-ui.log`
- generated ZIP release artifacts
- local caches, backups, logs, and WebView2 profile data

### Requires Manual Review

- Product text referencing Valve, Dota 2, and Dota2ProTracker
  - Safe as compatibility/source references when accompanied by disclaimer language, but should continue to be reviewed for branding tone.
- `tools/`
  - Contains local probes and research utilities. Safe to keep locally, but not all of them should necessarily be highlighted in a future public-facing repository landing page.

## Security and Privacy Findings

### A. Safe Generic Code/Reference

- Generic Windows path handling via `%LOCALAPPDATA%`
- Generic Steam install path detection
- Generic references to `hero_grid_config.json`
- Generic WebView2 directory naming in source code

### B. Safe Test Fixture

- `tests/MetaGrid.Tests/Fixtures/dota2protracker_hero_grid_high_winrate_config.json`
  - Deterministic public test fixture used by automated tests

### C. Public Documentation Example

- `build/README.release.md`
  - Safe internal release documentation

### D. Sensitive or Local Data Not Suitable For Public Commit

- `.appdata/`, `.dotnet-home/`, `.nuget/`
- `Release/`
- `artifacts/`
- `tmp/`
- `tools/`
- local runtime logs
- root-level verification screenshots
- generated `bin/` and `obj/` trees containing absolute paths and machine-local build output

### E. Suspicious or Manual-Review Items

- Any future screenshots intended for README or Releases should be reviewed manually before publication

## Additional Notes

- A local probe source file previously contained a hard-coded user Downloads path. That default was replaced with repository-relative paths during this preparation pass.

## Git Status

At the time of this audit, the project is **not** initialized as a Git repository. No remote URLs were configured and no push was possible.
