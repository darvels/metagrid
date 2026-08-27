# MetaGrid - Auto-Updating Hero Grid from Dota 2 Pro Tracker

Auto-updating Dota 2 hero grid powered by Dota2ProTracker High Winrate data.

MetaGrid is a free Windows utility that retrieves the official Dota2ProTracker High Winrate hero grid, compares it with the currently managed Dota 2 hero grid for the selected Steam account, and safely installs updates when a real change exists.

## Features

- Official Dota2ProTracker High Winrate grid retrieval
- Resilient provider pipeline with bounded source diagnostics
- Startup update checks
- Scheduled automatic updates
- Manual `Check for Updates` and `Force Refresh`
- `Install Grid` / `Update Grid` workflow
- Steam installation and account detection
- Selected Steam account support
- Safe backup before MetaGrid-managed changes
- Backup restore from inside the app
- Update history and latest-grid status tracking
- System tray support
- Preservation of unrelated custom layouts where supported

## How It Works

MetaGrid follows a simple user-facing workflow:

1. MetaGrid checks Dota2ProTracker for the latest High Winrate hero grid.
2. It validates the downloaded grid.
3. It compares that grid with the MetaGrid-managed grid for the selected Steam account.
4. If a real change exists, MetaGrid creates a backup first.
5. MetaGrid then safely installs the updated hero grid.

## Requirements

- Windows x64
- Steam
- Dota 2
- Internet connection for Dota2ProTracker updates
- Microsoft Edge WebView2 Runtime

The distributed v0.1.0 Windows build is self-contained with respect to .NET, so a separate .NET Desktop Runtime installation is not required for the packaged release.

## Installation

1. Download the latest Windows release ZIP.
2. Extract the ZIP.
3. Open the extracted `MetaGrid` folder.
4. Run `MetaGrid.exe`.

## Usage

1. Launch MetaGrid.
2. Select the correct Steam account if needed.
3. Click `Check for Updates`, or enable `Automatic Updates`.
4. Review the current source and grid status.
5. Install or update the managed hero grid when MetaGrid indicates that a real change is available.

## Safety and Backups

- MetaGrid creates a backup before changing the MetaGrid-managed Dota hero grid configuration.
- MetaGrid uses the selected Steam account only.
- Unrelated custom layouts are preserved where supported.
- Previous backups can be restored from inside the app.

MetaGrid is designed to reduce risk around hero-grid updates, but it does not make absolute guarantees. Review your selected account and current status before installing changes.

## Data Source

MetaGrid prefers the official Dota2ProTracker High Winrate hero-grid source when it is available. When that live source cannot be reached reliably, MetaGrid can fall back to other validated provider paths and clearly labels the active source inside the UI instead of pretending all data came from Dota2ProTracker.

## Open Source and License

MetaGrid's open-source code is licensed under the Mozilla Public License 2.0. See [LICENSE](LICENSE).

In practical terms, MPL-2.0 requires modifications to MPL-covered files to remain available under MPL when distributed, while still allowing MetaGrid to be combined with separate code under different terms. This is a summary only and not legal advice.

Copyright (c) 2026 Darvel

## Contributions

GitHub Issues are welcome for:

- Bug reports
- Feature requests

MetaGrid is currently **not accepting external code contributions or pull requests**. See [CONTRIBUTING.md](CONTRIBUTING.md).

## Disclaimer

MetaGrid is an independent community tool. It is not affiliated with, endorsed by, sponsored by, or officially associated with Valve Corporation, Dota 2, or Dota2ProTracker. Names and trademarks are used only to identify compatibility and data sources.

## Repository Layout

- `src/` - application source
- `tests/` - deterministic automated tests
- `scripts/` - local build and packaging helpers
- `build/` - safe build-time assets and internal release notes
