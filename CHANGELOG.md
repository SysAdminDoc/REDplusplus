# Changelog

## 1.5.12 (2026-06-13)

### Safety
- Reject unknown CLI switches with a clear error and exit code 1 instead of silently ignoring them (fail-closed parser).
- Error on missing values for `-path`, `-log`, `-export`, `-mode`, and `-moveto` instead of silently skipping the switch.
- Fix incomplete JSON escaping in GUI export: `\r`, `\n`, `\t`, and control characters are now properly escaped, matching the headless `-json` output.
- Cap imported dry-run files at 64 MB and 500K records; NDJSON imports now stream line-by-line from disk instead of loading the entire file into memory.
- Export/import empty-file candidates as first-class results: NDJSON (schema v2), JSON, and CSV include `kind=directory|file` for each record; GUI import loads file candidates into the empty-file deletion queue with a distinct status message. Old directory-only exports still import correctly.
- MFT turbo scan auto-falls back to standard scan when empty-file mode is enabled, since USN records lack file-size data needed for zero-byte detection.

### Gitignore
- Support nested `.gitignore` files: rules from `.gitignore` files inside the scan tree are discovered during traversal and applied with correct Git precedence (closer rules override ancestors).
- Fix rule precedence: ancestor `.gitignore` rules now load from farthest (git root) to closest so that closer rules win on conflict via last-match-wins.
- Fix path-pattern anchoring: patterns containing `/` are now anchored to their `.gitignore`'s directory instead of matching anywhere in the path.
- Fix `**` globstar to match zero path segments: `**/cache` now correctly ignores `cache` at the root level and at any depth.

### UX
- Forward Explorer context-menu launches to the already-running GUI instance: the new path is set and scan starts automatically instead of showing an "already running" dialog.

### Performance
- Batch empty-file recycle into a single IFileOperation transaction instead of per-file shell calls; non-recycle modes (Direct, Move) still process one-by-one with pre-delete re-verification.

### Automation
- Add `-exclude <name>` and `-protect <name>` CLI switches (repeatable) for per-run filter overrides that compose with config-file filters.
- Add headless scan progress reporting: periodic `Scanning: N directories examined...` to stderr (every 2 seconds), or `{"type":"progress",...}` in `-json` mode. `-quiet` suppresses progress.

## 1.5.11 (2026-06-12)

### UX Polish
- Refine the WinForms theme system with stronger focus, hover, disabled, tab, grid, toolstrip, and command-bar states.
- Improve the Search screen with a clearer path helper, responsive result legend, premium empty/no-results copy, and a stable bottom command bar that separates scan, review/delete, cancel, extras, and exit actions.
- Replace the redundant first-run defaults notification with a single clearer settings-location prompt that explains portable versus AppData storage.
- Restore `-autosearch <path>` as a GUI launch path so verification and Explorer integration can open directly into a scanned review state instead of being intercepted by headless mode.

## 1.5.10 (2026-06-12)

### Review Imports
- Add an Extras-menu import flow for saved dry-run JSON/NDJSON results so operators can review automation output in the GUI before acting.
- Keep imported non-empty/ignored/error rows review-only and load only `Empty` rows into the deletion queue.
- Re-check Move-to-folder deletions for stale files before moving, matching the final safety gate already used by direct and Recycle Bin modes.

## 1.5.9 (2026-06-12)

### Release Packaging
- Generate the final winget manifest during the release workflow after the zip SHA256 exists, validate it when `winget` is available, and attach it to the GitHub release

## 1.5.8 (2026-06-12)

### CLI Automation
- Add per-run CLI overrides for minimum age, maximum depth, `.gitignore`, MFT turbo scan, hidden directories, and system directories so scheduled jobs do not need to edit `RED+.cfg`

## 1.5.7 (2026-06-12)

### Help
- Update bundled help pages for current safety behavior, `.redkeep`, restore/undo, standalone empty-file cleanup, MFT turbo scan, headless CLI options, exit codes, logs, and undo manifest locations

## 1.5.6 (2026-06-12)

### Data Safety
- Detect Cloud Files and Azure File Sync directory reparse tags explicitly so cloud-placeholder directories are kept with a clear `cloud placeholder directory` reason instead of being reported as generic symbolic links

## 1.5.5 (2026-06-12)

### UX
- Add Advanced Settings checkboxes for respecting `.gitignore` rules and enabling the MFT turbo scan so both previously config-file-only scan options are discoverable in the GUI
- Preserve an existing MFT config value when the GUI is not elevated and disables the checkbox, avoiding accidental config loss during ordinary settings edits

## 1.5.4 (2026-06-12)

### CLI Automation
- Add `-quiet` for scheduler-friendly headless runs that suppress stdout/stderr while preserving exit codes and optional log files
- Add a versioned NDJSON `meta` record before result records and document the schema-bearing output contract
- Return exit code 11 when simulate/dry-run succeeds and finds empty directories or files, while keeping successful deletion runs at exit code 0

## 1.5.3 (2026-06-12)

### Reliability and Accessibility
- Rotate the persistent `RED++.log` at 5 MB with one retained `.1` generation so scheduled/headless runs do not grow logs without bound
- Localize the remaining settings read-only status string and the main accessibility labels, including a descriptive Search tab name for screen readers

## 1.5.2 (2026-06-12)

### Release Hardening
- Add push/PR CI for Windows Release builds plus headless safety smoke coverage for root protection, non-empty directory survival, junction survival, empty-file deletion, and deny-ACL fail-closed behavior
- Fix a headless scan-result race where an inaccessible child could make Direct mode report zero found directories while still deleting queued empty directories
- Correct the documented supported platform floor to Windows 10 22H2, Windows 11, or Windows Server 2022/2025 while keeping the tested .NET Framework 4.8.1 target
- Fix the winget portable manifest shape for the zip release artifact and add the `red++` portable command alias

## 1.5.1 (2026-06-12)

### Bug Fixes
- Empty-files mode now clears stale file candidates between fresh scans, enables Delete for file-only GUI results, reports directory and file deletion counts separately, and moves zero-byte files correctly in Move-to-folder mode; headless Move mode now requires `-moveto <dir>` so `-emptyfiles -mode move` is verifiable and undoable
- Manual tree-node deletion now uses the same file-free stale-scan safety gate as batch Recycle deletion, refuses non-empty subtrees, and writes an undo manifest that restores the full empty subtree

## 1.5.0 (2026-06-10)

### Theme
- Light theme option (Catppuccin Latte) plus Dark and System (follows the Windows app-theme registry value); switch live from Extras → Theme, persisted in config. Dark remains the default; the title bar follows the theme via DwmSetWindowAttribute

### Features
- Empty-files sister mode (opt-in, off by default): also deletes standalone zero-byte files (including in the scan root, excluding ignore-list trash) via the active delete mode, with one-click restore that recreates them losslessly. Toggle from Extras → "Delete empty files too" or the `-emptyfiles` CLI flag; isolated from the directory deletion pipeline as a pre-pass
- `.redkeep` marker file protects a folder and its entire subtree from deletion on every scan path (standard and MFT) — unlike per-config filter lists, the marker travels with the folder across copies and network shares
- CLI v2: repeatable `-path` for multiple scan roots in one run, `-dryrun` (report without deleting), `-mode recycle|direct|move|simulate` to override the configured delete mode, `-export <file>` (.txt/.csv/.json by extension), `-json` NDJSON to stdout for piping, and `-help`/`-version`; bare path arguments are accepted as scan roots
- "Why kept?" reasons: each surviving folder now carries a concrete reason (empty / N ignored files / matches ignore rule / never-empty rule / could not be read) shown on hover and added as a Reason column to CSV/JSON exports and `-json` output
- One-click restore of the last deletion run: Extras menu → "Restore Last Deletion" in the GUI, `RED+.exe -undo [-log file]` headless — recreates every deleted directory (lossless, they were empty) and moves Move-to-folder deletions back; the undo manifest now records children of recursively-deleted subtrees and the actual Move destination (collision suffixes included)

### Bug Fixes
- Honor an explicit filter match-method code even when the text contains a wildcard — a rule like `C|*.tmp` (literal Contains) is no longer silently rewritten to a name regex; codeless entries (`*.tmp`) still auto-detect as regex

### Security
- Harden the bundled gettext catalog parser against malicious .po/.mo files placed beside the portable exe: bound the Plural-Forms expression length and recursion depth (a deeply-nested expression would otherwise StackOverflow and kill the process — uncatchable), bounds-check every .mo string-table offset/length against the file size, and cap catalog size at 32 MB — any malformed catalog falls back to untranslated English
- Restrict unmanaged DLL resolution to System32 and drop the current directory from the search path at startup (`SetDefaultDllDirectories`) — a DLL planted next to a portable exe in Downloads can no longer be loaded (CVE-2024-11859 class)
- Bidi/zero-width control characters (RLO and friends, MITRE T1036.002) in folder names render as visible \uXXXX escapes in the tree, logs, and headless output — a crafted name can no longer reorder the displayed path in confirmations or logs

### Performance
- MFT turbo scan now supports ReFS / Dev Drive volumes (USN_RECORD_V3 / 128-bit file IDs via MFT_ENUM_DATA_V1 and FILE_ID_INFO), not just NTFS — file reference numbers widened to 128-bit throughout the scanner; NTFS uses the original 64-bit V0/V2 path unchanged. (ReFS path compiles and the NTFS path is regression-tested; end-to-end ReFS verification still requires an elevated session on a Dev Drive — see ROADMAP.)
- All Recycle Bin deletions run through one batched IFileOperation shell transaction with per-item result reporting — 320 directories recycle in under a second (the legacy per-call VisualBasic path took minutes at that scale and could raise modal shell dialogs even in headless mode); headless runs force-silence all shell UI; Microsoft.VisualBasic dependency removed
- Recycle-mode failures no longer stop the run mid-batch for an error prompt — every item is attempted, failures are logged and counted individually

### Reliability
- Direct delete is now three-tier: POSIX delete semantics with read-only override (NTFS, Win10 1607+) → legacy delete-on-close (FAT32/exFAT/SMB) → error; read-only empty directories delete without a separate attribute pass
- Long-path support independent of the OS LongPathsEnabled policy: directory enumeration, reparse verification, and Direct-mode deletes use extended-length (`\\?\`) paths; wholly-empty subtrees are deleted bottom-up by handle instead of DirectoryInfo.Delete (verified with a 499-character tree)

### Distribution
- Release workflow now publishes a SHA256SUMS asset, attests build provenance via GitHub Sigstore (`gh attestation verify` documented in README), and optionally scans the zip on VirusTotal (when `VIRUSTOTAL_API_KEY` secret is set) and auto-submits winget version bumps (when `WINGET_TOKEN` secret is set)
- Scoop manifest: fix persisted config filename (RED+.cfg) and extract update hashes from the SHA256SUMS release asset

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
