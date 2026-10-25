# Safety and limitations

ElectroSketch is an experimental, AI-assisted educational drawing and simulation project. Its results have not been independently validated as electrical safety guidance.

**A connection accepted by this app may be dangerous in real life. A displayed value, continuity beep, or absence of a warning is not evidence that a physical circuit is safe.** Do not rely on the app for live mains work, protective-earth design, battery charging safety, or the selection of real multimeter leads, jacks, ranges, and ratings.

## What the model covers

The app checks selected modeled hazards, including source shorts/conflicts, some component overloads, mains applied to low-voltage contacts, energized resistance/continuity/diode measurements, and current-meter overloads. These checks are incomplete and may contain bugs.

The wall outlet represents an ideal 230 V RMS, 50 Hz source. There is no actual AC waveform, realistic source impedance, breaker/RCD behavior, fuse-clearing time, shock-current calculation, insulation model, or fault-energy calculation. Protective earth is only a modeled node. Power-strip OFF currently disconnects all modeled rails, including PE; that simplification must not be used as a design for physical protective-earth wiring.

## Important simplifications

- LEDs use a simple resistance approximation rather than a full nonlinear model. Speakers are modeled as resistive loads. Heat, tolerances, transients, and component failure are not reproduced.
- DC current mode inserts a simulated 0.1-ohm shunt between the probes. It can complete an opened series path. Real meters have different burdens, jack arrangements, fuses, and safety ratings; those are not reproduced.
- USB-C cable paths and a basic CC attachment model are represented, but USB data signaling, full orientation behavior, e-markers, and Power Delivery negotiation are not. A charger labeled 65 W stays at its modeled 5 V default.
- Lightning power terminals are abstract VBUS/GND model terminals, not verified instructions for wiring the physical numbered contacts.
- Batteries use a simplified charge/work percentage. Battery chemistry, charging protection, temperature, and thermal runaway are not simulated.
- Wire colors aid illustration. They cannot identify an unknown physical conductor or verify its current capacity, insulation, connector orientation, or compatibility.

## Using the project responsibly

Use diagrams to ask questions and explore concepts. For real hardware, verify the actual pinout, part specifications, meter manual, and connection orientation. Work within your knowledge and seek qualified help for mains-powered equipment. The program does not replace practical training or suitable test equipment.

## Reporting a problem

Open a GitHub issue with the app version, reproduction steps, expected behavior, and observed behavior. Include a minimal topology or screenshot only after checking it for personal information. Never demonstrate a simulator bug by reproducing a hazardous circuit on real hardware.

The software is provided under the warranty disclaimer in [LICENSE](../LICENSE).
