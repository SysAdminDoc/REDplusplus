# Changelog

## Unreleased

### Build / Runtime
- Migrate from .NET Framework 4.8.1 (old-style csproj + `packages.config`) to an SDK-style project targeting `net9.0-windows` (WPF + WinForms). The verified scan/delete engine and every P/Invoke struct are unchanged; the 50-test xUnit suite and the headless safety smoke (empty-dir/file deletion, reparse-point/junction protection, deny-ACL fail-closed, AutoProtectRoot, recycle→undo round-trip) pass identically on the new runtime.
- Replace the removed `Directory.GetAccessControl(path)` static with the `DirectoryInfo.GetAccessControl()` extension (same default ACL sections) in the deletion lock check.
- Make version reporting single-file safe: read the file version from `Environment.ProcessPath` / the embedded `AssemblyFileVersion` attribute instead of `Assembly.Location` (which is empty in a published single-file bundle and previously crashed `-version`/`-json`).
- Modern .NET fixes the .resx resource friction that forced a build-time workaround on 4.8.1: the WPF shell and WinForms fallback now build and render cleanly (icons and image resources load natively).
- Release builds are now a **self-contained single-file** `win-x64` artifact (`dotnet publish -p:PublishSingleFile=true --self-contained`) — no .NET runtime install required on the target machine, preserving the "unzip and run" portability. CI builds and tests with `dotnet` (Visual Studio / MSBuild no longer required).

### Documentation
- Drop the advertised "Enter to scan, Del to delete selected" keyboard shortcuts from the feature list: the default modern WPF shell is click-only by design, matching the project's no-keyboard-shortcuts convention.

### Modern UI
- Name the specific cause when a directory cannot be read during a scan — access denied, in use by another process, path too long, or no longer exists (mapped from the native Win32 error) — in the result's Reason and the run log, instead of a generic "Failed to access". The modern shell's Reason column already surfaces every result's reason; this makes the kept-because-unreadable rows actually say *why*.
- Add two export formats to the GUI Export dialog and the `-export` CLI (chosen by extension): a reviewable **PowerShell removal script** (`.ps1`) that lists the eligible directories, defaults to a fail-safe `$Execute = $false` no-op, and re-checks each directory is still file-free before removing it (Recycle Bin by default) — the rmlint-style "decide now, run later" workflow; and a **self-contained HTML report** (`.html`) of the full result set with run metadata and each row's status reason. The existing `.json` export remains the re-importable replay for GUI review.
- Colour-code the review list: each result's Status is shown as a coloured word (eligible = red, kept = muted, deleted = green, protected = blue, failed = amber), so the destructive list now reflects the legend instead of leaving every row the same colour. The colour reinforces the status word and is never the only signal.
- Give the path box, deletion-mode dropdown, filter lists, and result list a visible keyboard-focus ring (they previously showed only WPF's near-invisible dotted default on the dark surface) — WCAG 2.4.7.
- Report a dry run honestly: it now says "… would be removed. Nothing was changed." instead of claiming directories and files were "changed", and the completion line matches the chosen mode (deleted / recycled / moved). Distinguish a user **Canceled** run from one **Stopped after an error**.
- Use a legible red for the "eligible" status text, give result rows a comfortable minimum height, raise the idle status indicator to green / working to amber, and expose the plain state word ("Ready"/"Working") to screen readers instead of the decorative bullet.
- Add a positive "no empty directories found" state for a completed scan with nothing to remove, and normalize one-off button colours and corner radii onto the shared palette.
- Add **Restore deletion** to the modern WPF shell's Extras menu: pick any kept undo manifest (newest first, with timestamp, mode, and item count) and restore it on a background thread with live status. Recovery no longer requires `-classic`.
- Add **Import saved dry-run results...** to the modern WPF shell's Extras menu: load a saved `.json`/`.ndjson`/`.csv`/`.txt` dry-run and review the records in the results list (review/export only; re-scan to delete, and the engine re-checks every directory before acting). Review of saved runs no longer requires `-classic`.

### CLI
- Clamp `-minage` (to 100 years) and `-maxdepth` (to a sane ceiling), warning on stderr instead of accepting an implausibly large value that would silently disable the run — no directory is ever "old enough" — or drive pathological recursion bookkeeping.
- Validate `-saveprofile` options before writing: an unknown `-mode`, or `-mode move` without an absolute `-moveto`, is now rejected with exit code 1 instead of silently saving a profile that fails every later `-profile` run. Profile names are trimmed and rejected if they exceed 128 characters or contain control characters (which would corrupt the `-listprofiles` output).
- Add saved profiles: `-saveprofile <name>` stores the current options (paths, mode, empty-files, age/depth, gitignore/MFT/hidden/system toggles) as a named profile; `-profile <name>` runs it (command-line options still override); `-listprofiles` lists them. A scheduled task can now reference `-profile nightly` instead of a long argument list. Profiles live in a dedicated `RED+.profiles.json`, separate from the XML config.
- Headless runs now print a "Run complete" summary line with total empty directories, empty files, deleted, failed, and wall-clock duration. The `-json` `meta` record (now schema 3) carries the same totals plus `elapsedMs`. (The modern GUI already shows the current directory being scanned in its status strip.)

### Reliability
- Be honest about a Recycle-Bin delete on a UNC share / network / removable drive: those locations have no Recycle Bin, so the shell deletes permanently. The headless run now logs a one-time note and the modern shell says so at delete start, instead of implying the items were recycled. Undo still recreates the empty directories regardless.
- Treat `/` and `\` as the same separator in directory filter rules, so a path rule written with forward slashes (e.g. `temp/cache`) matches the same as one written with backslashes instead of silently degrading to name-only matching and never firing.
- Derive the classic tree's exported deletion list from each node's status icon rather than its theme-dependent `ForeColor`, so a restyle or theme refresh can no longer silently drop eligible nodes from the export or include kept ones.
- Make the classic tree's protect/unprotect symmetric: unprotecting a folder now also releases the ancestors that protecting it had marked (when no other protected descendant remains) and strips the `[Protected]` label, instead of leaving ancestors stuck visually and on the protected list.
- Derive batch-recycle outcomes (success/fail and the undo manifest) authoritatively from `Directory.Exists` rather than the shell sink's submission-order fallback. `IFileOperation` does not guarantee one `PostDeleteItem` per `DeleteItem` in order, so a coalesced/skipped callback could previously record an undo entry against the wrong path; the filesystem is now the ground truth.
- Apply the same ground-truth rule to the empty-files recycle batch: each file's success and undo entry now come from `!File.Exists` rather than the sink's positional result list, so a coalesced callback can no longer log or record the wrong file as deleted.
- Scope a per-directory `.gitignore`'s bare-name rule (e.g. `dist`) to that directory's own subtree, matching Git semantics. Previously such a rule was applied tree-wide, so directories of that name elsewhere were wrongly reported as ignored (and skipped) in the dry-run the user reviews before deleting.

### Security & data safety
- Validate undo-manifest restore targets. A recreated directory or moved-back payload is now refused unless its path is fully-qualified, free of `..`, and — using a new `roots` field that records the cleaned scan root — inside the originally-cleaned tree. A tampered or corrupt manifest sitting next to the portable exe can therefore no longer be turned into an arbitrary directory-create or file-move primitive; legacy manifests without the field still restore under the structural checks. Following an adversarial review, the move-back **source** (`movedTo`) is constrained to the recorded roots too — the manifest now records the Move-mode destination folder — so a manifest cannot name an arbitrary system file as the move source and have `Directory.Move`/`File.Move` relocate (and destroy) it.
- Extend the CSV formula-injection guard (CWE-1236) to a leading-whitespace payload: a cell whose first non-whitespace character is `=`, `+`, `-`, or `@` (e.g. `" =1+1"`) is now quote-prefixed too, not just one whose very first character is a formula trigger.
- Never delete a directory that contains a user-protected descendant. Protecting a folder only guarded that exact path, so an empty-eligible **ancestor** would still be deleted recursively and take the protected child with it (in both the direct and recycle-batch delete paths). A directory is now treated as protected when it, or anything beneath it, is on the protected list, and the protected-folder list is matched case-insensitively to follow Windows path semantics.
- Harden the cross-volume Move-to-folder fallback: after copying the directory to the other volume, re-verify it is file-free and remove the source bottom-up by handle (which refuses a non-empty directory) instead of a blind recursive `Directory.Delete`, so content that appears after the copy can never be destroyed.
- Bound user-supplied filter regexes (`RegExName`/`RegExPath`) with a one-second match timeout, so a catastrophic-backtracking pattern (e.g. `(a+)+$`) can no longer hang the scan thread; a timed-out match is treated as no-match.
- Clamp the C-style (`SpecialFormatters`) printf width and precision to a bounded ceiling so a crafted format such as `%9999999d` can no longer drive a multi-megabyte allocation (memory-amplification DoS for callers that opt into the format callback).
- Guard `Translator.RegisterTranslationsByCulture` against a malformed caller-supplied search pattern: a bad `string.Format` pattern is now skipped instead of throwing an uncaught `FormatException`.
- Stop truncating the live run log when rotation cannot move it (e.g. the log is held open). The existing log is now preserved and appended to, keeping it intact as a forensic/undo aid.
- Restore a default sub-object after loading a config whose child element was explicitly nil'd (e.g. a hand-edited `<Options xsi:nil="true"/>`), which would otherwise NullReference inside the dirty-state check — including in the load's `finally`, crashing a headless run with a non-deterministic exit code.
- Run the scanned paths through the same bidi/zero-width/control-character sanitizer before writing the `-eventlog` summary, so a crafted folder name cannot reorder or corrupt the Windows Event Viewer entry.
- Make the MFT/USN turbo scanner fail closed on an incomplete enumeration. The volume walk now treats any termination other than the EOF sentinel as truncated, and rejects a result set in which a directory referenced as a parent is missing its own record (the signature of a dropped USN record). In either case the scan falls back to the standard recursive walker instead of risking a non-empty directory being reported empty because its children were dropped.

### Developer / CI
- Strengthen the supply-chain story for the unsigned binary: every release now attaches a CycloneDX software bill of materials (`bom.json`, checksummed in `SHA256SUMS` and covered by the build-provenance attestation); CI fails on any known-vulnerable direct or transitive NuGet package (`dotnet list package --vulnerable`); and Dependabot watches NuGet and GitHub Actions for updates.
- Add a first-class automated test project (`RED.Tests`, xUnit) covering the scan/delete engine's safety-critical parsers and undo logic: `.mo` catalog bounds and corrupt-header rejection, `Plural-Forms` divide/modulo-by-zero and deep-nesting hardening, USN/MFT record-parser bounds checks, filter-rule matching, `.gitignore` anchoring/negation/scoping, and undo restore round-trips plus corrupt-manifest rejection. Wired into CI so every push runs the suite (40 tests). Locks in the v1.5.18 P0 input-hardening fixes against regression.

## 1.5.18 (2026-06-14)

### Security & data safety
- Reject malformed `.mo` translation catalogs shorter than the 28-byte header instead of throwing an uncaught `IndexOutOfRangeException` (a crafted/truncated catalog dropped beside the portable exe could crash the app).
- Treat division and modulo by zero in `Plural-Forms` expressions as `0` so a malformed or hostile plural rule (e.g. `plural=(n%0)`) can no longer crash a translation call at evaluation time.
- Neutralize spreadsheet formula injection (CWE-1236) in CSV exports: directory names beginning with `=`, `+`, `-`, `@`, tab, or CR are quote-prefixed so they cannot execute when the export is opened in Excel/LibreOffice.
- Require `-moveto` to be an absolute path; a relative target previously resolved against the process working directory (often `C:\Windows\System32` under Task Scheduler).
- Harden the MFT/USN record parser against truncated or corrupt records: overflow-safe length checks, version-specific header-size validation, and name fields validated against the record length rather than only the buffer.

### Reliability
- Never show a modal dialog in the headless config load/save path: the redirect-limit, read-only, and save-failure messages now write to stderr in `-silent` mode, removing two ways a scheduled task could hang forever.
- Set silent mode in the `-undo` CLI path so a scripted/scheduled restore cannot block on a config dialog.
- Write undo manifests atomically (temp file + replace) so a crash or power loss mid-write can no longer leave a truncated, unusable manifest — the only recovery path for a deletion run.
- Re-verify a directory is still empty before the cross-volume move fallback's recursive delete, closing a TOCTOU window where newly created content could be removed.
- Release per-item shell COM objects during batch recycle so large batches no longer accumulate live runtime callable wrappers.
- Resolve an ambiguous `-undo <token>` to a single manifest only when the substring match is unique, preventing restoration of the wrong run; clean up the latest-pointer after a successful restore so it cannot recreate already-restored directories.
- Escape control characters when writing undo-manifest JSON.

### Modern UI & accessibility
- Restore a visible keyboard focus ring on every button (tabs, title-bar controls, primary actions, Browse, Extras, Exit). Focus was previously invisible because each button set its colors as local values that overrode the style triggers (WCAG 2.4.7).
- Add consistent hover and pressed feedback to all buttons regardless of their base color, including the previously feedback-less window controls and the colored Scan/Delete actions.
- Enable the Delete action only when results are genuinely eligible, not when only kept/protected rows are present.

## 1.5.17 (2026-06-13)

### Branding
- Replace the legacy project icon resource with the new RED++ folder/search/check logo across the executable, WPF title bar, WinForms fallback, dialogs, and bundled help navigation.
- Add the new RED++ banner artwork as a tracked README/help asset so the project page uses repo-owned branding instead of a hosted mockup image.

## 1.5.16 (2026-06-13)

### Modern UI
- Replace mixed bitmap/text shell icons with a cohesive vector icon system for tabs, titlebar controls, primary actions, empty-state guidance, and the Result Legend.
- Update the Search magnifying-glass icon everywhere it appears so the Search tab, Scan button, and onboarding guidance share the same premium line-icon treatment.
- Refine the default WPF shell density to keep the right-side legend, empty state, command bar, and status strip polished at the portable default size.

## 1.5.15 (2026-06-13)

### Modern UI
- Make the WPF Search surface responsive at the default portable-app size: path input, Browse, review pane, and Result Legend now use adaptive grid columns instead of mockup-era fixed margins.
- Tighten the title bar, tabs, command bar, empty state, legend, and status strip so the app feels intentional and complete at 1180x760 without clipping.
- Add pressed-state feedback for WPF buttons and improve review-list scrolling/column density for long paths and narrow windows.
- Wrap Settings and Filters content in scrollable shells so larger DPI/text settings do not hide controls.

## 1.5.14 (2026-06-13)

### Modern UI
- Replace the oversized WPF startup/DPI workaround with screen-aware default bounds so the reference-style shell opens at a practical size on normal and scaled displays.
- Restore Explorer/context-menu forwarding in the modern WPF shell so a second launch with `-path` brings the existing window forward and starts the requested scan.
- Fix WPF settings lifecycle issues: saved settings now populate when the Settings tab is opened, folder edits survive tab switches, and standalone zero-byte file review is exposed as its own setting.
- Show standalone zero-byte file candidates in the modern WPF review surface and export the same visible review list from the Extras menu.
- Harden WPF deletion review: delete-mode changes made after scanning now apply before deletion starts, Move-to-folder prompts for a destination, and confirmation copy clearly calls out Recycle, Direct, and Move behavior.
- Turn Extras into a real menu with log, export-to-file, and copy-to-clipboard actions, plus accessible names/help text across primary commands, tabs, result list, settings, and progress.

### Packaging
- Sync portable package metadata to v1.5.14 and fix the Chocolatey installer download URL so it no longer points at the older v1.5.12 ZIP.

## 1.5.13 (2026-06-13)

### Modern UI
- Add a modern WPF shell as the default GUI while preserving the existing WinForms interface behind `-classic`.
- Recreate the Search experience with the dark navy visual language from the design reference: custom title bar, large icon tabs, framed search panel, review-focused empty state, fixed result legend, command bar, and status/progress strip.
- Add DPI-aware startup sizing so the default window targets the reference-sized 1584x992 physical composition instead of becoming oversized on 125% scaled displays.
- Replace default WPF button chrome with a custom dark button template so disabled, hover, and focus states remain visually consistent with the shell.
- Wire the WPF shell to the existing `REDCore`/`RuntimeData` scan and deletion pipeline; no scan/delete engine replacement.

### Compatibility
- Keep `-autosearch -path <dir>` opening the GUI and starting a scan in the modern shell.
- Keep the legacy WinForms UI available with `RED+.exe -classic`.

## 1.5.12 (2026-06-13)

### Safety
- Reject unknown CLI switches with a clear error and exit code 1 instead of silently ignoring them (fail-closed parser).
- Error on missing values for `-path`, `-log`, `-export`, `-mode`, and `-moveto` instead of silently skipping the switch.
- Fix incomplete JSON escaping in GUI export: `\r`, `\n`, `\t`, and control characters are now properly escaped, matching the headless `-json` output.
- Cap imported dry-run files at 64 MB and 500K records; NDJSON imports now stream line-by-line from disk instead of loading the entire file into memory.
- Export/import empty-file candidates as first-class results: NDJSON (schema v2), JSON, and CSV include `kind=directory|file` for each record; GUI import loads file candidates into the empty-file deletion queue with a distinct status message. Old directory-only exports still import correctly.
- MFT turbo scan auto-falls back to standard scan when empty-file mode is enabled, since USN records lack file-size data needed for zero-byte detection.

### Accessibility
- Add accessible names (localized) to all Settings, Filter, and sub-tab pages.
- Replace hardcoded Color.White and Color.DarkGray with HC-aware palette colors (SystemColors.HighlightText in High Contrast, DarkTheme.DisabledText for muted labels).
- Draw dotted focus rectangles on all flat buttons when focused; 2px solid in High Contrast mode.
- Localize LogWindow title, accessible name, and empty-state text via TXT.Translate.
- Enforce WCAG 2.2 SC 2.5.8 minimum target sizes: TreeView rows 22→24px, checkboxes 24px minimum height, ToolStrip buttons 24px minimum height.

### Gitignore
- Support nested `.gitignore` files: rules from `.gitignore` files inside the scan tree are discovered during traversal and applied with correct Git precedence (closer rules override ancestors).
- Fix rule precedence: ancestor `.gitignore` rules now load from farthest (git root) to closest so that closer rules win on conflict via last-match-wins.
- Fix path-pattern anchoring: patterns containing `/` are now anchored to their `.gitignore`'s directory instead of matching anywhere in the path.
- Fix `**` globstar to match zero path segments: `**/cache` now correctly ignores `cache` at the root level and at any depth.
- Load `.git/info/exclude` and the global gitignore (`core.excludesFile` or `~/.config/git/ignore`) with correct Git-spec precedence: global lowest, then exclude, then per-directory `.gitignore` highest.

### UX
- Forward Explorer context-menu launches to the already-running GUI instance: the new path is set and scan starts automatically instead of showing an "already running" dialog.
- Multi-run undo history: the last 5 deletion runs are preserved as timestamped manifests instead of a single overwritten file. GUI Extras → Restore Deletion shows all available runs with date, mode, and entry count. CLI `-undo` without arguments restores the most recent; `-undo <timestamp>` restores a specific one.

### Enterprise
- Windows Event Log integration: `-eventlog` flag writes a summary event (source "RED++", Application log) with scan paths, mode, counts, and exit code after headless runs. Event source registration requires admin on first use.

### Discoverability
- Set GitHub topics (empty-directories, disk-cleanup, sysadmin, cli, portable, mft, etc.) for search visibility.
- README now leads with a one-line value proposition, hero screenshot, and quick-start install commands (portable, Scoop, headless).

### Distribution
- Add Chocolatey portable package manifest (`packaging/chocolatey/`): nuspec, install/uninstall scripts ready for `choco pack` and community submission.

### Documentation
- Add Code Signing & SmartScreen strategy section to README: documents the unsigned-by-design decision, explains why OV/EV/self-signing are each unsuitable, and points users to attestation-based verification.
- Add benchmark harness (`packaging/bench/`): `Generate-Tree.ps1` creates reproducible synthetic trees; `Run-Benchmark.ps1` times RED++ standard, MFT, PowerShell, and robocopy scanners with hardware/methodology context.

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
