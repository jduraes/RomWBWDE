# RomWBW Disk Explorer

RomWBWDE is a spiritual evolution of [RC2014 CP/M Disk Explorer](https://github.com/ForNextSoftwareDevelopment/RC2014CpmDskExp) by Dirk Prins (ForNext Software Development, 2023). His program remains at that address.

It reads and writes a RomWBW card or disk image, one slice at a time. A modern hd1k slice is 8 MiB. A classic hd512 slice is a little larger. Two panes sit side by side, in the manner of a split file manager. One pane can be a Windows folder, the other the card. Files copy in either direction. A slice can also be written, extracted, or erased on its own. The partition table and the other slices are left alone.

## Writing to a disk

This program can write to a real card or disk. That can destroy what is on it. It is meant to change only the slice you picked, but the wrong drive, a fault, or a bug can still wipe that card, or another disk entirely. Making a backup is your responsibility.

Before you write anything, take an image of the whole card and keep it somewhere else. [Rufus](https://rufus.ie/) will do this. Select the card, click **Show advanced drive properties**, then click the save icon beside the drive and store the image. Rufus can write that image back onto a card later. Do not point this program at a drive you cannot afford to lose.

## Running it

The program needs the .NET 8 desktop runtime, and Windows administrator rights, because it opens the card as a raw disk.

A built copy is attached to the [releases](https://github.com/jduraes/RomWBWDE/releases). That download is optional. Unzip it and run `RomWBWDE.exe`. Windows will ask for permission.

To build it yourself, double-click `RomWBWDE.cmd` in this directory. It publishes the program into `Release` and starts it. From a terminal:

```
dotnet run --project RomWBWDE
```

## Version 1.0.2

A slice is named from the label stored in its third sector, and a write of a whole slice now stays on the card.

- The 16-byte name is read even when the boot signature is absent. A name inferred from the files is marked with a star, for example `NZ-COM*`. MBASIC on its own is not enough to name the slice.
- **Rename slice** writes that name and nothing else. A Min-eZ partition has no such name.
- Classic hd512 disks are opened. An hd512 slice is 8,519,680 bytes and cannot be written over an hd1k slice.
- A Min-eZ partition has 768 directory entries, three 8 KB blocks, as the BIOS disk parameter block defines. Files whose entries sit in the third block are shown, and a write does not use that block for file data.
- In a folder, an hd1k slice or disk, an hd512 slice or disk, or a Min-eZ / eZT-RCB image is shown in blue.
- **Write slice** uses the one file highlighted in the other pane. If none is highlighted, or more than one is, it asks you to choose a file.
- The slice is read back after the write. Windows keeps the RomWBW partition mounted even with no drive letter, and a write is then refused as "the device is not ready". The program locks that volume, takes it offline, brings it back, and then writes.

## The card

A modern RomWBW hard disk has a partition of type `0x2E`. After a 1 MiB prefix, the slices are laid out one after another. Each slice is 8,388,608 bytes: 1,024 tracks of 16 sectors of 512 bytes. The first 16 KB of a slice are the system tracks. The CP/M directory follows.

Only whole slices that fit on the media are offered. A partial slice at the end of the card is not shown, and neither is a partition that starts past the end of the card.

A file that is itself one slice is opened as a single slice. A larger RomWBW disk image is opened as the set of slices inside it.

A classic RomWBW disk has no type `0x2E` partition. Its slices start at the first sector. Each one is 8,519,680 bytes: 1,040 tracks of 16 sectors of 512 bytes, with 128 KB of system tracks, 4 KB blocks, and 512 directory entries. That is the hd512 layout. Up to 256 whole slices are offered. An hd512 slice cannot be written over an hd1k slice, because the two sizes differ.

A Min-eZ card keeps CP/M on its own partition, type `0x52`, of the same 8,388,608 bytes. That filesystem has no system tracks. Its blocks are 8 KB and its directory has 768 entries, which is three blocks. File data starts at the fourth block. The FAT32 partition on the card is not a slice and is left alone. The CP/M partition is labelled Min-eZ unless the files belong to a more specific system. On the card this was checked against, the FAT32 partition entry still covers the first 4 MB of the CP/M partition, so filling that card from Windows can reach the CP/M area.

Each slice is shown under the name stored in its third sector, whether or not the boot signature is present. `hd1k_combo.img` therefore shows CP/M 2.2 and ZSDOS 1.1 for its first two slices, rather than a program that happens to be on them. A slice with no name there is labelled from the files that distinguish it, and that guess is shown with a star, for example `NZ-COM*`. One program that happens to be present, such as MBASIC, is not enough. The names that come out of that guess are NZ-COM, ZPM3, Z3PLUS, QP/M, DOS/65, Turbo Pascal, SLR Z80, WordStar, dBASE, MBASIC, Fortran, Cowgol, Hi-Tech C, Aztec C, COBOL, BPBIOS, Infocom, Games, Z-System, MSX, ROM images, Music, CP/M 3, ZSDOS, CP/M 2.2, or CP/M. A slice with no files and no stored name is labelled unused.

## The two panes

Each pane has Folder, Image, and Drive.

- Folder opens a Windows directory. A file that is an hd1k slice or disk, an hd512 slice or disk, or a Min-eZ / eZT-RCB image is shown in blue.
- Image opens a RomWBW disk image or a single slice file.
- Drive lists removable disks that currently have media. Each line shows the drive letter, the model, the size, and the `PHYSICALDRIVEn` name.

Choose the slice from the list, or with Previous and Next. The user list shows All users, users 0 to 15, and any higher user that already has files. All users shows one row per occupied user area. Open a user to see only that user's files. Double-click a user row, a folder, or `..` to move in or out. Up and Backspace do the same.

The same slice cannot be open on both panes.

## Copying files

The tick boxes are the files that will be copied. The box on the `..` row selects every row underneath. When only some rows are ticked it shows the faded state. Click it again to clear them. Unticking any other row clears only that row.

The Copy button sits on the toolbar of each pane and points at the other pane. F5 copies from the active pane to the other one. Copy does not delete the source. You can also drag one or more files onto a pane.

A file from Windows is stored as user 0, with an 8.3 name in capitals, unless a user area is already open or you drop the files onto a user row. A file copied from one slice to another keeps its user number, unless the destination pane has a user area open.

The program asks before it writes, and the question lists the names. Afterwards the destination pane refreshes and the new files are shown.

A copy onto the card rewrites only the changed 4 KB blocks inside the selected slice, and only after the 16 KB system tracks are checked to be unchanged. The partition table and the other slices are not written. Before the first write, the program locks the Windows volume on that disk, takes it offline, and brings it back, so the card keeps the bytes.

If the slice has no CP/M directory, you are asked before it is initialised. That fills the directory of that slice only.

## Deleting files

On a slice, Delete and F8 remove the ticked CP/M files from that slice. They do not delete files in a Windows folder.

On a Windows folder, Delete moves the ticked files and folders to the Recycle Bin. The `..` row is never included.

## Writing, extracting, renaming, and erasing a slice

These buttons sit under the slice list.

**Write slice** copies a RomWBW slice image into the slice you are looking at. The slice must be unused. When the other pane has exactly one file highlighted, that file is the image. Otherwise you are asked to choose one. The image can be one slice, or a larger RomWBW disk, in which case you choose which of its slices to write. The whole slice is replaced, system tracks included. The partition table and every other slice stay as they are. The slice is read back afterwards, and the write is reported as failed if the disk did not keep it. Windows mounts the RomWBW partition even when that partition has no drive letter. While it is in that state the card answers a write with "the device is not ready". The program locks the volume, takes it offline and brings it back, and then writes. The lock is held until the card is closed.

**Extract slice** saves the slice you are looking at as an `.img` file. The suggested name is the slice number and its label, for example `00-NZ-COM.img`. That file is the slice itself. RomWBW, and Write slice, open it as a single slice. Nothing on the card is written.

**Rename slice** changes the name stored in the slice. The write is the one sector that holds the name. Files are left as they are, and so are the partition table and the other slices. A Min-eZ partition has no such name.

**Delete slice** erases a slice that has files, including its system tracks, and leaves it unused. The partition table and the other slices are not changed.

## Building

```
dotnet build RomWBWDE.sln -c Release
```

`RomWBWDE.Core` is the slice and CP/M filesystem library. `RomWBWDE` is the Windows program. `RomWBWDE.Tests` checks the library against a saved slice image at `%TEMP%\rc2014-slice0.img`:

```
dotnet run --project RomWBWDE.Tests -c Release
```

A build writes `bin` and `obj` under the project it compiles, and a publish writes `Release` at the top of this directory. Those folders are listed in `.gitignore`. They stay on your machine. The copy offered for download is the one attached to a GitHub release.

## Licence

RC2014 CP/M Disk Explorer, Copyright (c) 2023 Dirk Prins.
https://github.com/ForNextSoftwareDevelopment/RC2014CpmDskExp



Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
