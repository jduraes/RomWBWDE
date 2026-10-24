# RomWBW Disk Explorer

RomWBWDE is a spiritual evolution of [RC2014 CP/M Disk Explorer](https://github.com/ForNextSoftwareDevelopment/RC2014CpmDskExp) by Dirk Prins (ForNext Software Development, 2023).

It reads and writes a RomWBW hd1k card or disk image, one 8 MiB slice at a time. Two panes sit side by side, in the manner of a split file manager. One pane can be a Windows folder, the other the card. Files copy in either direction. A slice can also be written, extracted, or erased on its own. The partition table and the other slices are left alone.

Dirk's original .NET Framework program is still in `Src`. The pictures at the bottom of this file are from that program.

## Running it

The program needs the .NET 8 desktop runtime, and Windows administrator rights, because it opens the card as a raw disk. Double-click `RomWBWDE.cmd` in this directory. It builds the Release program and starts it. Windows will ask for permission.

From a terminal:

```
dotnet run --project RomWBWDE
```

## The card

A modern RomWBW hard disk has a partition of type `0x2E`. After a 1 MiB prefix, the slices are laid out one after another. Each slice is 8,388,608 bytes: 1,024 tracks of 16 sectors of 512 bytes. The first 16 KB of a slice are the system tracks. The CP/M directory follows.

Only whole slices that fit on the media are offered. A partial slice at the end of the card is not shown, and neither is a partition that starts past the end of the card.

A file that is itself one 8 MiB slice is opened as a single slice. A larger RomWBW disk image is opened as the set of slices inside it.

Slices that contain a recognisable system are labelled from their files: NZ-COM, ZPM3, Z3PLUS, DOS/65, Turbo Pascal, SLR Z80, Games, Z-System, MSX, ROM images, CP/M 3, WordStar, dBASE, QP/M, MBASIC, Music, ZSDOS, CP/M 2.2, or CP/M. A slice with no files is labelled unused.

## The two panes

Each pane has Folder, Image, and Drive.

- Folder opens a Windows directory.
- Image opens a RomWBW disk image or a single slice file.
- Drive lists removable disks that currently have media. Each line shows the drive letter, the model, the size, and the `PHYSICALDRIVEn` name.

Choose the slice from the list, or with Previous and Next. The user list shows All users, users 0 to 15, and any higher user that already has files. All users shows one row per occupied user area. Open a user to see only that user's files. Double-click a user row, a folder, or `..` to move in or out. Up and Backspace do the same.

The same slice cannot be open on both panes.

## Copying files

The tick boxes are the files that will be copied. The box on the `..` row selects every row underneath. When only some rows are ticked it shows the faded state. Click it again to clear them. Unticking any other row clears only that row.

The Copy button sits on the toolbar of each pane and points at the other pane. F5 copies from the active pane to the other one. Copy does not delete the source. You can also drag one or more files onto a pane.

A file from Windows is stored as user 0, with an 8.3 name in capitals, unless a user area is already open or you drop the files onto a user row. A file copied from one slice to another keeps its user number, unless the destination pane has a user area open.

The program asks before it writes, and the question lists the names. Afterwards the destination pane refreshes and the new files are shown.

A copy onto the card rewrites only the changed 4 KB blocks inside the selected slice, and only after the 16 KB system tracks are checked to be unchanged. The partition table and the other slices are not written. Before the first write, Windows volumes on that disk are dismounted.

If the slice has no CP/M directory, you are asked before it is initialised. That fills the directory of that slice only.

## Deleting files

On a slice, Delete and F8 remove the ticked CP/M files from that slice. They do not delete files in a Windows folder.

On a Windows folder, Delete moves the ticked files and folders to the Recycle Bin. The `..` row is never included.

## Writing, extracting, and erasing a slice

These three buttons sit under the slice list.

**Write slice** copies a RomWBW slice image into the slice you are looking at. The slice must be unused. The image can be one 8 MiB slice, or a larger RomWBW disk, in which case you choose which of its slices to write. The whole slice is replaced, system tracks included. The partition table and every other slice stay as they are.

**Extract slice** saves the slice you are looking at as an 8 MiB `.img` file. The suggested name is the slice number and its label, for example `00-NZ-COM.img`. That file is the slice itself. RomWBW, and Write slice, open it as a single slice. Nothing on the card is written.

**Delete slice** erases a slice that has files, including its system tracks, and leaves it unused. The partition table and the other slices are not changed.

## Building

```
dotnet build RomWBWDE.sln -c Release
```

`RomWBWDE.Core` is the slice and CP/M filesystem library. `RomWBWDE` is the Windows program. `RomWBWDE.Tests` checks the library against a saved slice image at `%TEMP%\rc2014-slice0.img`:

```
dotnet run --project RomWBWDE.Tests -c Release
```

## The 2023 program

`Src` is RC2014 CP/M Disk Explorer 1.2, unchanged. It was written for a layout of 16 drives, A to P, of 8 MB each, which is why it stopped at 128 MB. Dirk found 256 MB cards the safer choice, because some cards sold as 128 MB were physically smaller. The program had to be run as administrator, and drag and drop had been removed.

The notes below, and the three screenshots, are his.

---

# RC2014CpmDskExp

RC2014 CPM Disk Explorer

This is a disk image(container) browser/editor/writer for the RC2014.
Disk images for the CP/M system can be read/altered and written again to a Compact Flash card.

I found it to be better to work with 256MB CF cards.
The 128MB cards I had were from china and in fact less then 128MB so it booted ok but produced problems accessing the files on the RC2014.

Remember to start Visual Studio in Admin mode if working on the program yourself.
This is because the program needs access to certain disk routines in the kernel32.dll.
That is also the reason why I had to kick out the drag & drop posibility to insert files and use a filebrowser dialog.

Copyright (c) 2023 Dirk Prins

![Screenshot](ScreenShot1.png)

![Screenshot](ScreenShot2.png)

![Screenshot](ScreenShot3.png)

## Licence

Permission is hereby granted, free of charge, to any person obtaining a copy of this software and associated documentation files (the "Software"), to deal in the Software without restriction, including without limitation the rights to use, copy, modify, merge, publish, distribute, sublicense, and/or sell copies of the Software, and to permit persons to whom the Software is furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY, FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM, OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE SOFTWARE.
