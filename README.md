![Version](https://img.shields.io/badge/version-1.0.0-blue)
![License](https://img.shields.io/badge/license-LGPL--3.0-green)
![Platform](https://img.shields.io/badge/platform-Windows-lightgrey)

# RED++ : Remove Empty Directories

[Download RED++](https://github.com/SysAdminDoc/REDplusplus/releases)

RED++ finds, displays, and deletes empty directories recursively below a given start folder. Create custom rules for keeping and deleting folders (e.g. treat directories with empty files as empty).

![screenshot](help/img/screen-M02.png)

## Features

- Simple user interface
- Shows empty directories before deleting them
- Supports multiple delete modes (including Delete to recycle bin)
- Allows whitelisting and blacklisting of directories by using filter lists
- Can detect directories with empty files as empty
- Fully portable (local config file, no %APPDATA% required)
- Extended directory and file name matching with sophisticated filter syntax
- Dedicated grid for display and editing of filter rules
- Translation support via .po files

## System Requirements

- Windows 7 or later
- Microsoft .NET Framework 4.8
- No installer required. Unzip and run.

## Usage

If the config file (**RED++.cfg**) isn't found, you'll be prompted to create one:
- **Portable Mode** stores the config in the same folder as the executable
- **%APPDATA%** stores the config in a subfolder of Windows %APPDATA%
- If RED++ is in a protected folder (Program Files, etc.), select %APPDATA% instead

## Credits

RED++ is based on [RED+](https://github.com/BookOfBeasts/Remove-Empty-Directories-Plus) by [Robert 'NotBob' Bookerby](https://github.com/BookOfBeasts), which is itself based on [RED](https://github.com/hxseven/Remove-Empty-Directories) by [Jonas John](http://www.jonasjohn.de/).

### Third-party components
- [AlphaFS](https://github.com/alphaleonis/AlphaFS) for file system calls

### Icon sources
- Nuvola icons (GNU LGPL 2.1)
- NuoveXT icons (GPL)
- [famfamfam silk icons](https://github.com/legacy-icons/famfamfam-silk) (CC BY 2.5)
- [FatCow free-icons](https://github.com/gammasoft/fatcow) (CC BY 3.0)

## License

RED++ is free software under the [GNU Lesser General Public License v3](http://www.gnu.org/licenses/lgpl.html) or later.
