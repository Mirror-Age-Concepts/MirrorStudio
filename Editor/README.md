Starter Avalonia project for the video editor prototype.

To build and run (requires .NET 9 SDK):

1. Open a terminal in the repository root.
2. cd Editor
3. dotnet restore
4. dotnet run

Next steps:
- Replace placeholder UI with timeline, preview, and import controls.
- Integrate FFmpeg for decode/encode (e.g., via `FFmpeg.AutoGen` or `Xabe.FFmpeg`).
- Add project format and background workers for proxy generation.