![Version](https://img.shields.io/badge/version-1.5.3-blue)
![License](https://img.shields.io/badge/license-LGPL--3.0-green)
![Platform](https://img.shields.io/badge/platform-Windows%2010%2B-lightgrey)

# RED++ : Remove Empty Directories

[Download RED++](https://github.com/SysAdminDoc/REDplusplus/releases)

RED++ finds, displays, and deletes empty directories recursively below a given start folder. Create custom rules for keeping and deleting folders (e.g. treat directories with empty files as empty).

![screenshot](help/img/screen-M02.png)

## Features

- Simple user interface with per-monitor DPI awareness
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
- Export scan results to TXT, CSV, or JSON
- Headless CLI mode for scripted/scheduled operation
- Keyboard shortcuts (Enter to scan, Del to delete selected)
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

- Windows 10 22H2, Windows 11, or Windows Server 2022/2025
- Microsoft .NET Framework 4.8.1
- No installer required. Unzip and run.

## Verify Your Download

Every release ships a `SHA256SUMS` file and a signed build-provenance attestation. To verify the zip really came from this repository's CI:

```
gh attestation verify RED++_v1.5.3.zip -R SysAdminDoc/REDplusplus
```

## Usage

### GUI Mode

If the config file (**RED+.cfg**) isn't found, you'll be prompted to create one:
- **Portable Mode** stores the config in the same folder as the executable
- **%APPDATA%** stores the config in a subfolder of Windows %APPDATA%
- If RED++ is in a protected folder (Program Files, etc.), select %APPDATA% instead

### CLI / Headless Mode

```
RED+.exe [-silent] -path "C:\target" [-path "D:\other" ...] [options]
```

Scans and deletes (per configured delete mode) without showing a window. Exits with code 0 (success) or 1 (errors). Useful for Task Scheduler and batch scripts.

Options:

| Option | Description |
|--------|-------------|
| `-path <dir>` | Scan root (repeatable). A bare path argument also works. |
| `-dryrun` | Scan and report only; never delete (forces simulate mode). |
| `-emptyfiles` | Also delete standalone zero-byte files (opt-in sister mode). |
| `-mode <mode>` | Override delete mode: `recycle` \| `direct` \| `move` \| `simulate`. |
| `-moveto <dir>` | Required with `-mode move`; moves empty directories and empty files to `<dir>`. |
| `-export <file>` | Write results to `.txt` / `.csv` / `.json` (chosen by extension). |
| `-json` | Emit one NDJSON object per result to stdout (for piping). |
| `-log <file>` | Write a timestamped run log. |
| `-undo` | Restore the directories deleted by the last run. |
| `-help`, `-version` | Show usage / version and exit. |

Preview a cleanup without touching anything, machine-readable:

```
RED+.exe -path "D:\Shares" -dryrun -json
```

Works with UNC paths directly — no mapped drive needed:

```
RED+.exe -silent -path "\\server\share\folder" -log "cleanup.log"
```

### Undo

Every deletion run writes an undo manifest (`RED++.undo.json`). Restore the last run's directories with one click (Extras menu → Restore Last Deletion) or headlessly:

```
RED+.exe -undo [-log "restore.log"]
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

## Credits

RED++ is based on [RED+](https://github.com/BookOfBeasts/Remove-Empty-Directories-Plus) by [Robert 'NotBob' Bookerby](https://github.com/BookOfBeasts), which is itself based on [RED](https://github.com/hxseven/Remove-Empty-Directories) by [Jonas John](http://www.jonasjohn.de/).

### Icon sources
- Nuvola icons (GNU LGPL 2.1)
- NuoveXT icons (GPL)
- [famfamfam silk icons](https://github.com/legacy-icons/famfamfam-silk) (CC BY 2.5)
- [FatCow free-icons](https://github.com/gammasoft/fatcow) (CC BY 3.0)

## License

RED++ is free software under the [GNU Lesser General Public License v3](http://www.gnu.org/licenses/lgpl.html) or later.
