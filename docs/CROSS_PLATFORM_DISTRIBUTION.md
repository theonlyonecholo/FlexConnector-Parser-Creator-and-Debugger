# ArcSight Parser Designer 2.7.1 — native desktop application

This directory contains a self-contained application, including the .NET runtime.
Keep all files together. No separate .NET installation or PDF is required.

## Linux x64 / Ubuntu

Extract the archive into a directory you own and run from a graphical desktop:

```bash
chmod +x ArcSight.ParserDesigner.CrossPlatform
./ArcSight.ParserDesigner.CrossPlatform
```

The application uses X11; Wayland sessions need XWayland. On Ubuntu install:

```bash
sudo apt install libx11-6 libice6 libsm6 libfontconfig1
```

.NET also needs ICU: use `sudo apt install libicu78` on Ubuntu 26.04 or
`sudo apt install libicu74` on Ubuntu 24.04. Do not run the application with sudo.

## Windows x64

Run `ArcSight.ParserDesigner.CrossPlatform.exe`. This release is unsigned;
organizational application policies may require approval from your IT team.

## Getting started

Open a parser from `examples/`, paste a corresponding log into Message and press F5.
Use Tokens, Submessages, Event mapping and Severity / processors to edit the parser.
Test log file evaluates one parser against a file and reports selected submessage
patterns. Apply updates the editor; Save writes the `.properties` file.
Press F1 for searchable offline help. Ctrl+Z undoes up to 100 text edits.

The preview uses .NET regex, not Java, and does not execute general ArcSight
operations, token conversions or linked processors. Validate in the target connector.

`SHA256SUMS.txt` covers the distribution files. On Linux verify them with
`sha256sum -c SHA256SUMS.txt`. `licenses/` contains dependency notices; these do
not declare a license for this project's own code.

This release was tested on Windows and Ubuntu 26.04 x64 under WSLg. macOS can be
built from the source project but has not been runtime-tested or notarized.
