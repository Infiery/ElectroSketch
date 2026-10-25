# ElectroSketch

A small Windows playground for drawing electronics connections, exploring simple circuits, and learning through experiments.

I am a student learning electronics. I built this project with extensive help from AI coding assistants: it is a **vibe-coded learning project** that grew from the connections and tools I wanted to understand. It helped me explore breadboards, USB connectors, and multimeter modes. The code and electrical models are still imperfect; bug reports and small improvements are welcome.

**Educational prototype:** readings, diagrams, sounds, and warnings can be incomplete or wrong. An absence of warnings does not mean a real circuit is safe. Do not use ElectroSketch to plan live mains work or decide whether a physical connection is safe. Read [Safety and limitations](docs/SAFETY.md).

## Try it

The current version is **0.8.5**, offered as an experimental Windows x64 release.

Download `ElectroSketch-0.8.5-win-x64.zip` from this repository's **Releases**, extract the whole archive, and run `ElectroSketch.exe`. Windows 10/11 x64 is the intended platform. The archive includes .NET, so a separate runtime installation is unnecessary. The executable is unsigned; Windows may show an unknown-publisher warning. If you prefer to inspect and build it yourself, use the commands below.

## What it can do

- Place a breadboard, Arduino-style connector board, LEDs, resistors, speakers, power strips, and simplified chargers/devices.
- Connect individual pins with colored wires, including open wire ends; save and load JSON-based `.ets` topologies.
- Explore USB, USB-C, Lightning, and Ethernet connector diagrams and ready-made cables in physical or electronics view.
- Use simulated DC/AC voltage, continuity, resistance, diode, and DC current modes, including a MAS830L-style manual-range profile.
- Move and rotate components, contact groups, and text; add arrows and lines; pan, zoom, and undo/redo five actions.
- Keep local notes and imported images in a side panel.

## Quick controls

| Action | Control |
| --- | --- |
| Select / Wire / Meter | S / W / D, outside text fields |
| Wire color | 1-9 while in Wire mode |
| Zoom | Mouse wheel |
| Pan | Drag empty canvas in Select mode, or middle-mouse drag |
| Multiple selection | Ctrl-drag |
| Clear selection | Click empty canvas |
| Clear meter probes | Right-click in Meter mode |
| Rotate | Drag the circular handle on a selected object |
| Edit text | Double-click the text label |
| Save / Save as | Ctrl+S / Ctrl+Shift+S |
| Undo / Redo | Ctrl+Z / Ctrl+Shift+Z |

In Wire mode, click two contacts to connect them, or click empty space for an open wire end. In Meter mode, place black then red probes; the reading updates automatically. Click **Connections** when you want to view the guide for the last selected item.

## Build and test

Use Windows with the **.NET 8 SDK**. Run these commands from the repository root:

```powershell
dotnet build .\ElectroSketch\ElectroSketch.csproj -c Release
dotnet run --project .\ElectroSketchSmoke\ElectroSketchSmoke.csproj -c Release
dotnet run --project .\ElectroSketch\ElectroSketch.csproj -c Release
```

To create a self-contained Windows executable:

```powershell
dotnet publish .\ElectroSketch\ElectroSketch.csproj -c Release -r win-x64 --self-contained true -o .\artifacts\win-x64
```

There are no explicit third-party application package dependencies. The SDK restores Microsoft's build tools and runtime packs. Public builds omit debug symbols and map compiler source paths to a generic location. The smoke suite checks selected modeled behaviors; passing it does not validate physical electrical safety.

## Local storage and Windows integration

The app works offline and has no account, telemetry, or upload feature. Notes, imported image copies, and diagnostic logs live under `%LOCALAPPDATA%\ElectroSketch`. Topology files are saved where you choose. On startup the app registers the `.ets` extension for the current Windows user, including an icon supplied by Windows itself. Starting the app does not require administrator privileges.

Do not include your personal notes, images, logs, or circuit files in a bug report unless you want to share their contents. Error logs can contain local file paths.

## Questions, ideas, and contributions

Found a bug, have an idea, or want to improve the code? Open an **Issue** using the bug-report or feature-request option. Small pull requests are welcome; please discuss larger changes in an issue first. See [Contributing](CONTRIBUTING.md) for a few practical guidelines.

I maintain this as a personal learning project in my spare time. Replies and updates may take a while, and I cannot promise a fix or a release date. Questions about the app are welcome, but I cannot verify the safety of a real electrical setup.

## Project information

- [Technical details](docs/TECHNICAL.md): source layout, simplified models, and known engineering limitations.
- [Safety and limitations](docs/SAFETY.md): what the simulator cannot establish.
- [Connector reference](docs/CONNECTOR_REFERENCE.md): modeled pin paths and reference links.
- [Change notes](CHANGELOG.md).
- [MIT license](LICENSE) for project code; [third-party notices](THIRD-PARTY-NOTICES.md) for the bundled runtime and product names.

This is an independent hobby project with no affiliation with the manufacturers whose connector or device names appear in the interface.
