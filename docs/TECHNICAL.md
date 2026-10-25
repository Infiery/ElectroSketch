# Technical details

## Platform and source layout

ElectroSketch 0.8.5 is a C# desktop application targeting `net8.0-windows` with WPF. Artwork is drawn with WPF geometry, text, and brushes; sounds are generated locally. The project uses Windows APIs for its per-user file association. It has no explicit third-party application packages.

| Location | Responsibility |
| --- | --- |
| `ElectroSketch/MainWindow.xaml` and `.xaml.cs` | Window, component browser, panels, keyboard input, file operations, and UI state |
| `ElectroSketch/Controls/CircuitCanvas.cs` | Drawing, hit testing, selection, wiring, transforms, and workspace navigation |
| `ElectroSketch/Core/Models.cs` | Document types, component definitions, contacts, and sample circuit |
| `Core/ConnectorCatalog.cs`, `ReadyCableCatalog.cs`, `ConnectionGuide.cs` | Connector definitions, cable matching, and connection explanations |
| `Core/CircuitEngine.cs` | Net construction, simplified DC/AC analysis, and meter readings |
| `Core/SafetyAnalyzer.cs` | Findings for the limited set of modeled hazards |
| `Core/DocumentValidator.cs` | Topology input checks and normalization |
| `Core/NotesStore.cs`, `CrashReporter.cs`, `SafetySounds.cs`, `FileAssociation.cs` | Local notes, diagnostics, sound, and Windows integration |
| `ElectroSketchSmoke/Program.cs` | Automated WPF render, interaction, persistence, and electrical-model smoke checks |

Paths starting with `Core/` in the table are relative to `ElectroSketch/`.

## Documents and state

`.ets` files are readable JSON with format version 1. They store components, contacts/group transforms, wires, annotations, and meter configuration. Loading enforces a 10 MiB file limit and validates versions, identifiers, positions, and contacts. Saving writes a temporary file before replacing the destination. Undo/redo retain five topology snapshots per direction in memory.

Notes and imported images are independent of topology files and stored under `%LOCALAPPDATA%\ElectroSketch`. Notes JSON contains local image paths; it should be sanitized before sharing. Crash logs remain local and may contain paths. The app does not transmit these files.

## Electrical model

Contacts and internal/wire connections form graph nodes. Simplified passive elements and ideal DC/AC sources determine modeled net values. AC values represent RMS magnitudes rather than a time-domain waveform. The meter reads the chosen contacts continuously.

DC current mode temporarily adds a 0.1-ohm shunt between probe contacts during analysis. It reports the resulting modeled branch current, gives zero on unpowered/disconnected paths, and feeds overload conditions into the safety system. Continuity/resistance/diode checks reject contacts classified as energized, including source returns. A separate unconnected source elsewhere on the canvas does not by itself make a resistor live.

Connector models identify contacts and routes. They do not simulate USB/Ethernet data transfer. USB-C chargers remain at 5 V without implemented PD negotiation. Lightning ready cables show abstract power routes. See [connector reference](CONNECTOR_REFERENCE.md) and [safety limitations](SAFETY.md).

## Tests and current limitations

Run the smoke project on Windows as shown in the root README. It checks representative rendering, cable routes/mating, document round trips, open wire endpoints, meters, selected hazard cases, contact-group transforms, text controls, window sizing, and probe-tone clearing. It generates screenshots only under its ignored build output. These are regression checks, not an independent electrical validation or an exhaustive security audit.

The canvas, catalog, and main window are relatively large and still mix some responsibilities. Device charge timing remains in the canvas, and some model definitions depend on WPF types. Performance on large topologies and all combinations of malformed files have not been exhaustively tested. Exceptions in the interface are logged and handled, but recovery does not prove the state is correct. This is a reasonable starting point for learning and incremental contributions.

## Release packaging

`Directory.Build.props` enables deterministic compilation, disables debugging symbols, and maps source paths to a generic prefix. Build output, personal topology files, notes, and credentials are excluded by `.gitignore`. Windows releases are self-contained x64 builds, accompanied by the project license and Microsoft's runtime notices. The public executable is unsigned.
