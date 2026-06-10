# Changelog

## 1.3.0 (2026-06-10)

### Security
- Harden SecureDeleteDirectory against junction/TOCTOU arbitrary-delete (CVE-2022-21658 class) — handle-based reparse verification and delete-by-handle for Direct mode

### Bug Fixes
- Fix headless CLI exit code — was always 0; now returns 1 on scan/delete errors
- Correct OS requirement to Windows 10 20H2+ (4.8.1 does not support Windows 7/8.1)
- Stop classifying PathTooLongException as an infinite loop detection in scanner
- Fix app.manifest dpiAware value casing (true/pm -> true/PM)
- Dispose RED++.log StreamWriter on exit (RuntimeData now implements IDisposable)

### Features
- Add Enter-to-scan keyboard shortcut in path field
- Add Del-to-delete keyboard shortcut on selected tree node

## 1.2.0 (2026-06-10)

### Removed Dependencies
- Remove AlphaFS — all filesystem operations now use native System.IO with longPathAware manifest

### Accessibility
- Set AccessibleName on 25+ interactive controls for screen reader support (Narrator, NVDA)

### Infrastructure
- UNC/network share paths work natively (System.IO handles these with graceful error reporting)

## 1.1.0 (2026-06-10)

### Safety Fixes (P0)
- Fix format-string crash when min-folder-age filter skips a young directory
- Fix integer overflow in file size check — files >2GB no longer misidentified
- Add reparse-point check on start folder — junctions/symlinks/mount points refused as scan root
- Add defense-in-depth reparse guard in SecureDeleteDirectory
- Detect OneDrive/cloud-only placeholder files as real content (not treated as empty)
- Replace TOCTOU-vulnerable IsDirLocked rename-test with ACL-based permission check

### Features
- Rebrand from RED+ to RED++ across entire codebase (title, about, help, registry keys, URLs)
- Add PerMonitorV2 DPI awareness — crisp rendering at 125%/150%/200% scaling
- Add pre-deletion confirmation dialog with count and protected-skip summary
- Allow deletion on partial (cancelled) scan results
- Skip already-deleted child directories during deletion (topmost-dir optimization)
- Add headless CLI mode: `-silent -path "dir" -log "file"` for Task Scheduler/scripting
- Add "Move to specified folder" delete mode for safe review before permanent removal
- Expand environment variables (%TEMP%, %USERPROFILE%) in search directory input
- Enforce single-instance behavior via named Mutex
- Persist log to RED++.log file (survives crashes)
- Export scan results in CSV and JSON formats (in addition to TXT)
- Add GitHub Actions release workflow (workflow_dispatch)

### Bug Fixes
- Fix empty clipboard export for TreeView (was silently exporting to file instead)
- Fix pre-build event — use PowerShell instead of hardcoded author-specific date.exe path
- Remove forced rescan after adding directory to ignore filter (tree already updates in place)
- Improve "empty files" message to "ignored files" for clarity

### Performance
- Remove redundant IsDirLocked/FileIOPermission pre-checks from RecycleBin deletion (major speed improvement)
- Replace 7-pass filter matching with single-pass switch dispatch (fewer allocations)

### Infrastructure
- Retarget to .NET Framework 4.8.1
- Enable longPathAware, dpiAware, and dpiAwareness in app manifest
- Declare supported OS versions (Win7 through Win11) in manifest

## 1.0.0 (2026-06-10)
- Initial release of RED++, based on RED+ 25.3.0.0
