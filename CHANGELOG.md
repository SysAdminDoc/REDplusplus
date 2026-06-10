# Changelog

## 1.4.0 (2026-06-10)

### Critical Fixes
- Fix broken WIN32_FIND_DATAW struct layout in the FindFirstFileExW enumerator — FILETIME fields declared as `long` inserted 4 alignment-padding bytes, shifting every field so file names marshaled 2 characters short and "."/".." escaped the dot-filter; the v1.3.0 scanner recursed into itself on every directory. Same misalignment fixed in BY_HANDLE_FILE_INFORMATION (SystemFunctions, MftScanner)
- Fail closed on directory-enumeration failure — an access-denied/vanished directory previously enumerated as an empty list and was classified Empty (delete-eligible); enumeration errors now throw and the directory is marked Error at scan time and aborts deletion at delete time (verified with a deny-ACL test)
- Fix MFT turbo scan root lookup — the NTFS volume root was addressed by literal FRN 5, but USN records carry full 64-bit FRNs with sequence bits, so path resolution could never match; the real root FRN is now read via GetFileInformationByHandle

### Data Safety
- Re-verify the entire subtree is still file-free immediately before any recursive delete (Direct and Recycle modes) — content created between scan and delete now aborts the deletion instead of being destroyed
- Refuse to recursively delete through reparse points discovered during re-verification, and add the handle-based reparse guard to manual tree-node deletion
- Headless mode now honors AutoProtectRoot — a fully-empty target tree is cleaned out but the start folder itself survives
- Apply the minimum-directory-age rule in the MFT turbo scan (was standard-scan only); a too-young directory also keeps its parent non-empty
- Apply .gitignore rules in the MFT turbo scan (was standard-scan only)
- Move-to-folder mode works across volumes (replicate-then-delete fallback) and refuses a move target located inside the directory being moved
- Write the undo manifest on cancelled and error-stopped runs, not just clean completions; manifest and RED++.log fall back to %APPDATA% when the install directory is not writable

### Reliability
- Corrupt or unreadable RED+.cfg no longer crashes at startup — falls back to read-only defaults
- Headless (-silent) mode never shows dialogs (config prompts and message boxes previously hung Task Scheduler runs) and no longer hangs forever if an unexpected error fires during the delete phase
- Deletion error-continue no longer corrupts the run: the results list was re-sorted and the deleted-parents set reset on resume, skipping pending items and re-deleting vanished ones
- Multi-path drag-and-drop scans accumulate results across all roots — previously each root's scan wiped the previous results, so Delete only processed the last root while the tree showed all of them; the last queued root also no longer resets the tree
- Wire up the infinite-loop detector (the counter existed but nothing ever incremented it); pathologically deep nesting (>256 levels) now trips it
- Regex filter rules match case-insensitively like every other match method (uppercase patterns could never match the lowercased names)
- Deletion statistics no longer double-count: the session counter was folded into the persisted total on every scan, not once
- Guard against crashes from: null tree nodes during icon updates, vanished directories during node styling, incomplete filter-grid rows, out-of-range config values (delete mode index, numeric settings now clamped), and a missing deletion worker on abort/continue

### UX
- Dropping folders onto the window starts the scan immediately (drops while busy are ignored)
- Dark owner-drawn tab headers — the tab strip no longer renders system light-gray on the dark theme
- Icon legend renders at full contrast (was disabled-gray)
- Browse dialog starts from the path currently in the search box
- Adding a directory to the ignore filter no longer resets the search path box
- Deletion-error and log dialogs open centered on the main window
- Calmer filter-grid error message instead of raw row/column diagnostics

### Documentation
- All version strings synced to 1.4.0 (assembly was still 1.0.0.0 — the title bar showed v1.0.0.0)
- Correct config filename in README (RED+.cfg, not RED++.cfg)

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
- Add default NeverEmpty list: AUDIO_TS, VIDEO_TS, .git, .svn, .hg
- Annotate tree nodes with status text for screen readers (Empty, Ignored, Protected)

### Theme
- Catppuccin Mocha dark theme applied by default across all forms (MainWindow, DeletionError, LogWindow, FormLanguage, FormRtfHelp, NBMsgBox)
- Dark title bar via DwmSetWindowAttribute (Win10 1809+)
- TreeView status colors updated for dark backgrounds (Catppuccin Red, Blue, Subtext0)
- Custom ToolStrip/ContextMenu renderer, UCMenuButton arrow glyph, DataGridView cell styles
- Fast-mode TreeView background uses Catppuccin Surface0/Mantle

### Features (MFT turbo scan)
- Opt-in MFT turbo scan via FSCTL_ENUM_USN_DATA — scans entire NTFS volumes in seconds by reading the Master File Table directly
- Requires admin elevation; auto-detects NTFS via GetVolumeInformation and falls back to standard scan on non-NTFS, network paths, or insufficient permissions
- Respects all existing filters (ignore lists, never-empty, hidden/system, reparse points, OneDrive cloud placeholders)
- New UseMftScan config option (default off)

### Features (multi-path)
- Multi-path drag-and-drop: drop multiple folders onto RED++ to scan them sequentially with results under separate root nodes
- TreeManager supports multi-root append mode for sequential scans

### Performance (continued)
- Replace Directory.GetFiles/GetDirectories with FindFirstFileExW + FindExInfoBasic + FIND_FIRST_EX_LARGE_FETCH P/Invoke enumerator — significantly faster scan on large directory trees

### Distribution
- Add Scoop manifest (packaging/redplusplus.json) and winget manifest (packaging/winget/) for package-manager submissions

### Features (continued)
- Add .gitignore-aware scan filter — when enabled, directories matching .gitignore patterns are skipped during scan (first GUI empty-dir tool with this capability)
- New GitIgnoreParser loads rules from ancestor .gitignore files up to the .git root
- New RespectGitIgnore config option (default off, persisted in RED++.cfg)

### Safety
- Write JSON undo manifest (RED++.undo.json) after each deletion run recording paths, mode, and move targets

### Performance
- Topmost-subtree single-pass deletion — parents processed before children; one recursive delete handles the whole empty subtree instead of individual leaf-by-leaf removal

### Infrastructure
- Ship .pot translation template (130 strings) in language/ folder; release workflow now bundles language/ in the zip
- README claim downgraded from "Translation support" to "Translation-ready" (template only, no translations yet)
- Exact/wildcard protect-list matching already implemented (NameExact match type does strict equality; no code change needed)

### Documentation
- Add competitive comparison table (vs TreeSize Pro, FolderSizes, Store apps)
- Add UNC path, post-migration cleanup, and Task Scheduler examples to README

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
