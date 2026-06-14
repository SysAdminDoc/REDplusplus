![Version](https://img.shields.io/badge/version-1.5.18-blue)
![License](https://img.shields.io/badge/license-LGPL--3.0-green)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-lightgrey)

# RED++ : Remove Empty Directories

**Fast, portable empty-directory scanner and cleaner for Windows** — GUI + headless CLI, filter rules, MFT turbo scan, one-click undo, network/UNC support, and zero installation required.

<img width="2084" height="460" alt="RED++ banner showing the folder search and review logo" src="help/img/banner.png" />


### Quick Start

**Portable** — download, unzip, run:

```
curl -Lo RED++.zip https://github.com/SysAdminDoc/REDplusplus/releases/latest/download/RED++_v1.5.18.zip
tar -xf RED++.zip
RED+.exe
```

**Scoop:**

```
scoop bucket add redplusplus https://github.com/SysAdminDoc/REDplusplus
scoop install redplusplus
```

**Headless (Task Scheduler, scripts):**

```
RED+.exe -silent -path "D:\Shares" -log cleanup.log
```

[All releases](https://github.com/SysAdminDoc/REDplusplus/releases)

## Features

- Modern WPF user interface with screen-aware sizing, cohesive vector iconography, and a legacy WinForms fallback
- Shows empty directories before deleting them with confirmation summary
- Supports multiple delete modes (Recycle Bin, Direct, Simulate, Move to folder)
- Allows whitelisting and blacklisting of directories by using filter lists
- Can detect directories with empty files as empty
- Detects OneDrive/cloud-only placeholder files as real content
- Handle-based reparse-point safety (junctions, symlinks, mount points)
- One-click restore of the last deletion (GUI button or `-undo`)
- `.redkeep` marker file protects a folder (and its subtree) anywhere it travels
- Opt-in MFT turbo scan for whole-volume scans in seconds — NTFS and ReFS/Dev Drive (admin required)
- Fully portable (local config file, no %APPDATA% required)
- Extended directory and file name matching with sophisticated filter syntax
- Dedicated grid for display and editing of filter rules
- Translation-ready via .po files (template included, community translations welcome)
- Export scan results to TXT, CSV, or JSON; import saved dry-run results for GUI review
- Headless CLI mode for scripted/scheduled operation
- Single-instance enforcement
- Persistent log file (RED++.log)
- Explorer context menu integration
- Environment variable expansion in path input

## Why RED++?

| Feature | RED++ | TreeSize Pro | FolderSizes | Store apps |
|---------|:-----:|:----------:|:-----------:|:----------:|
| Empty-dir scan + preview | Free | ~$50/yr | $60+ | Free |
| Network/UNC share support | Free | Paid only | Paid only | No |
| CLI / Task Scheduler | Free | Paid only | No | No |
| CSV/JSON export | Free | Paid only | Paid only | No |
| Custom filter rules | Free | No | No | No |
| "Select All" results | Free | Free | Free | Paid |
| Portable (no install) | Yes | No | No | No |

## System Requirements

- Windows 10 22H2, Windows 11, or Windows Server 2022/2025 (64-bit / x64)
- No runtime to install — the release is a self-contained single-file build that bundles .NET 9. Just unzip and run.
- No installer required.

## Verify Your Download

Every release ships a `SHA256SUMS` file, a CycloneDX software bill of materials (`bom.json`) that inventories every bundled dependency, and a signed build-provenance attestation. To verify the zip really came from this repository's CI:

```
gh attestation verify RED++_v1.5.18.zip -R SysAdminDoc/REDplusplus
```

## Code Signing & SmartScreen

RED++ is **unsigned**. Windows SmartScreen may show "Windows protected your PC" on first run of each new version. Click **More info → Run anyway** to proceed.

**Why unsigned?** Standard OV code-signing certificates ($200–400/yr) do not bypass SmartScreen — Microsoft resets reputation per version regardless of certificate. EV certificates ($400–600/yr with hardware token) build reputation faster but are disproportionate for a free utility. Self-signing is actively harmful: Microsoft treats self-signed executables as a malware indicator.

**Verification instead of signing:** every release includes a `SHA256SUMS` file and a GitHub build-provenance attestation (see [Verify Your Download](#verify-your-download)). These prove the binary came from this repository's CI, not a third party.

**Future path:** if download volume grows, OV or EV signing may be adopted. The `release.yml` workflow already has a VirusTotal submission step ready to activate.

## Usage

### GUI Mode

RED++ now opens the modern WPF interface by default. The original Windows Forms interface is still available with `RED+.exe -classic` for compatibility testing or fallback.

If the config file (**RED+.cfg**) isn't found, you'll be prompted to create one:
- **Portable Mode** stores the config in the same folder as the executable
- **%APPDATA%** stores the config in a subfolder of Windows %APPDATA%
- If RED++ is in a protected folder (Program Files, etc.), select %APPDATA% instead

Dry-run JSON from the CLI can be reviewed later in the GUI with **Extras -> Import Saved Dry-Run Results**. Import shows every valid record in the tree, but only `Empty` records are eligible for deletion; destructive modes still re-check that directories are file-free and not reparse points before deleting or moving them.

### CLI / Headless Mode

```
RED+.exe [-silent] -path "C:\target" [-path "D:\other" ...] [options]
```

Scans and deletes (per configured delete mode) without showing a window. Useful for Task Scheduler and batch scripts.

Options:

| Option | Description |
|--------|-------------|
| `-path <dir>` | Scan root (repeatable). A bare path argument also works. |
| `-dryrun` | Scan and report only; never delete (forces simulate mode). |
| `-emptyfiles` | Also delete standalone zero-byte files (opt-in sister mode). |
| `-mode <mode>` | Override delete mode: `recycle` \| `direct` \| `move` \| `simulate`. |
| `-moveto <dir>` | Required with `-mode move`; moves empty directories and empty files to `<dir>`. |
| `-minage <hours>` | Override minimum directory age for this run. |
| `-maxdepth <n>` | Override maximum scan depth for this run (`-1` = infinite). |
| `-gitignore`, `-no-gitignore` | Enable or disable `.gitignore` rules for this run. |
| `-mft`, `-no-mft` | Enable or disable MFT turbo scan for this run; MFT still requires administrator rights. |
| `-hidden`, `-ignore-hidden` | Include or ignore hidden directories for this run. |
| `-system`, `-ignore-system` | Include or ignore system directories for this run. |
| `-export <file>` | Write results to `.txt` / `.csv` / `.json` (chosen by extension). |
| `-json` | Emit NDJSON to stdout: one `meta` record (version, run totals, and elapsed time), then one `result` record per directory. |
| `-quiet` | Suppress stdout/stderr; use only the process exit code and optional `-log`. |
| `-log <file>` | Write a timestamped run log. |
| `-undo [manifest]` | Restore directories from the most recent (or specified) run. Up to 5 undo manifests are kept. |
| `-profile <name>` | Run a saved profile. Any other command-line options still override the profile's values. |
| `-saveprofile <name>` | Save the current options (paths, mode, toggles) as a named profile and exit without scanning. |
| `-listprofiles` | List saved profiles and exit. |
| `-eventlog` | Write a summary event to the Windows Application Event Log (source "RED++"). |
| `-classic` | Open the legacy Windows Forms GUI instead of the modern WPF shell. |
| `-help`, `-version` | Show usage / version and exit. |

Exit codes:

| Code | Meaning |
|------|---------|
| `0` | Success, or simulate/dry-run found nothing. |
| `1` | Error, invalid argument, failed deletion, or failed undo. |
| `11` | Simulate/dry-run succeeded and found empty directories or files. |

Preview a cleanup without touching anything, machine-readable:

```
RED+.exe -path "D:\Shares" -dryrun -json
```

Works with UNC paths directly — no mapped drive needed:

```
RED+.exe -silent -path "\\server\share\folder" -log "cleanup.log"
```

### Undo

Every deletion run writes a timestamped undo manifest. The last 5 manifests are kept so you can undo earlier runs, not just the most recent one. Restore via the GUI (Extras → Restore Deletion → pick a run) or headlessly:

```
RED+.exe -undo                              # restore the most recent run
RED+.exe -undo 2026-06-13_14-30-00          # restore a specific run by timestamp
RED+.exe -undo RED++.undo.2026-06-13_14-30-00.json  # or by filename
```

Deleted directories were empty, so recreating them is a complete restore; Move-to-folder deletions are moved back to their original location.

### Post-Migration Cleanup

After a `robocopy /MIR` or file-server migration, clean up leftover empty directories:

```
RED+.exe -silent -path "\\server\share" -log "migration-cleanup.log"
```

RED++ handles Thumbs.db and desktop.ini correctly (treats them as ignored files), unlike `robocopy "X" "X" /S /move` which chokes on them.

### Task Scheduler

Create a scheduled task to clean a directory nightly:

```xml
<Actions>
  <Exec>
    <Command>C:\Tools\RED+.exe</Command>
    <Arguments>-silent -path "D:\Shares\Home" -log "D:\Logs\red-cleanup.log"</Arguments>
  </Exec>
</Actions>
```

Exit code 0 = success, 1 = errors occurred. Configure the task to use UNC paths directly — mapped drives are not available in task context.

### Saved Profiles

Save a reusable set of options once, then reference it by name from a scheduled task or script instead of repeating a long argument list:

```
RED+.exe -saveprofile nightly -path "D:\Shares\Home" -mode recycle -emptyfiles
RED+.exe -listprofiles
RED+.exe -silent -profile nightly -log "D:\Logs\red-cleanup.log"
```

Any options passed alongside `-profile` still override the profile's stored values. Profiles are stored in `RED+.profiles.json` next to the config file.

## Credits

RED++ is based on [RED+](https://github.com/BookOfBeasts/Remove-Empty-Directories-Plus) by [Robert 'NotBob' Bookerby](https://github.com/BookOfBeasts), which is itself based on [RED](https://github.com/hxseven/Remove-Empty-Directories) by [Jonas John](http://www.jonasjohn.de/).

### Icon sources
- Nuvola icons (GNU LGPL 2.1)
- NuoveXT icons (GPL)
- [famfamfam silk icons](https://github.com/legacy-icons/famfamfam-silk) (CC BY 2.5)
- [FatCow free-icons](https://github.com/gammasoft/fatcow) (CC BY 3.0)

## License

RED++ is free software under the [GNU Lesser General Public License v3](http://www.gnu.org/licenses/lgpl.html) or later.
