# Changelog

All notable changes to MetaGrid will be documented in this file.

## [0.1.1]

### Added

- Built-in MetaGrid application updater through GitHub Releases
- SHA-256 verification before application updates
- Separate MetaGrid.Updater replacement helper with rollback and relaunch support
- Application update controls across Dashboard, About, and Settings
- Optional OpenDota MY BEST HEROES personalization
- English and Russian interface support
- Local isolated self-update E2E regression harness

### Changed

- Improved installed-grid detection and user-facing status accuracy
- Improved Dashboard update UX and responsive layout polish
- Hardened backup restore, startup diagnostics, and update safety checks

### Security

- Enforced release ZIP traversal rejection during app-update staging
- Enforced unsafe install path rejection before updater replacement
- Kept updater replacement scoped to the MetaGrid installation tree only

## [0.1.0]

### Added

- Official Dota2ProTracker High Winrate grid retrieval
- WebView2-based official D2PT retrieval flow
- Steam installation and account detection
- Selected Steam account support
- Startup update checks
- Scheduled automatic updates
- Manual `Check for Updates` and `Force Refresh`
- Safe grid install and update workflow
- Automatic backup creation before MetaGrid-managed writes
- Backup restore flow
- Update history tracking
- System tray support
- Embedded application icon and polished desktop UI
