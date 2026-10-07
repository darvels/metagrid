# MetaGrid

Dota 2 Hero Grid Updater

Version 0.2.0 adds Auto Guides powered by live Dota2ProTracker builds. Choose a hero
and enable available roles in Guides. Starting purchases, item groups, skills and
highest-Pick-Rate talents are synchronized; disabling a role removes its owned guide.
Not every hero/role always has a build. Keep Steam running on your selected account.

MetaGrid retrieves the official Dota2ProTracker High Winrate hero grid, checks for updates, and safely installs or updates the grid for the selected Steam account.

What it does:
- Retrieves the official Dota2ProTracker High Winrate hero grid
- Checks for updates
- Safely installs or updates the grid for the selected Steam account
- Checks GitHub Releases for newer MetaGrid application builds
- Includes the MetaGrid self-update helper for future in-place application updates
- Preserves unrelated custom hero grids where supported
- Creates backups before changes
- Starting with MetaGrid v0.1.1, future compatible MetaGrid application releases can be installed from inside the app
- Users on MetaGrid v0.1.0 must manually download the latest version once; v0.1.1 can update in-app

Basic usage:
1. Run MetaGrid.
2. Select the correct Steam account.
3. Click `Check for Updates`.
4. Review the status.
5. Install or update the grid when appropriate.

Requirements:
- Windows x64
- Steam and Dota 2
- Microsoft Edge WebView2 Runtime
