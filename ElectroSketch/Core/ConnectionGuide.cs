using System.Text;

namespace ElectroSketch.Core;

public static class ConnectionGuide
{
    public static string Describe(CircuitDocument document, ComponentModel cable)
    {
        if (!ComponentCatalog.IsReadyCable(cable.Type)) return DescribeComponent(document, cable);
        var (left, right) = ReadyCableCatalog.Ends(cable.Type);
        var signals = ComponentCatalog.CableSignals(cable.Type);
        var builder = new StringBuilder();
        builder.AppendLine($"{cable.Name}");
        builder.AppendLine($"{signals.Length} modeled internal paths · {left} ↔ {right}");
        builder.AppendLine();
        foreach (var (from, to) in ReadyCableCatalog.Connections(cable.Type))
        {
            builder.AppendLine($"{from}: {Pins(left, from)} → {to}: {Pins(right, to)}");
            builder.AppendLine($"  Canvas: {Attached(document, cable, $"L_{from}")} → {Attached(document, cable, $"R_{to}")}");
        }
        builder.AppendLine();
        if (cable.Type == ComponentType.UsbAToUsbCCable)
            builder.AppendLine($"56 kΩ CC bias to USB-C CC1 · Canvas: {Attached(document, cable, "R_CC")}");
        builder.AppendLine("Each listed route is connected inside the cable model. Canvas attachments can be changed independently.");
        if (cable.Type == ComponentType.UsbCToUsbCCable)
            builder.AppendLine("USB-C lanes are routed TX→RX in both directions. The drawn pin assignment uses the default plug orientation; a flipped plug swaps to its alternate contact set. The cable is modeled as a passive 3 A cable. USB-PD negotiation is not simulated, so a 65 W adapter remains at its 5 V default.");
        else if (cable.Type == ComponentType.UsbAToUsbCCable)
            builder.AppendLine("USB-A VBUS connects to the USB-C VBUS group, with a 56 kΩ pull-up from VBUS to the USB-C CC1 contact so a basic Type-C sink can detect the legacy source.");
        else if (left == "Lightning" || right == "Lightning")
            builder.AppendLine("The Lightning end exposes the simplified VBUS and GND power groups used in basic topology practice.");
        builder.AppendLine();
        builder.Append("USB pin names follow the USB connector definitions. USB 2.0 wire convention: red VBUS, black GND, green D+, white D−; SuperSpeed conductor colors vary by cable maker.");
        return builder.ToString();
    }

    private static string DescribeComponent(CircuitDocument document, ComponentModel component)
    {
        var builder = new StringBuilder();
        builder.AppendLine(component.Name);
        builder.AppendLine($"{ComponentCatalog.GetCategory(component.Type)} · modeled connection guide");
        builder.AppendLine();
        if (component.Type == ComponentType.Breadboard)
        {
            builder.AppendLine("Each numbered a–e row is internally continuous; each same-number f–j row is internally continuous. The center gap separates them. Each top/bottom + or − rail runs horizontally and is separate from the other three rails.");
            builder.AppendLine("A colored rail does not have voltage until you connect a source. The printed color is a visual convention, not a safety measurement.");
        }
        else
        {
            var ports = ComponentCatalog.GetPorts(component).ToList();
            builder.AppendLine($"{ports.Count} exposed contacts:");
            foreach (var port in ports)
                builder.AppendLine($"{port.Id} · {port.Label} · {Attached(document, component, port.Id)}");
            var internalLinks = ComponentCatalog.GetInternalConnections(component).ToList();
            if (internalLinks.Count > 0)
            {
                builder.AppendLine();
                builder.AppendLine($"{internalLinks.Count} direct internal links:");
                foreach (var (a, b) in internalLinks.Take(30)) builder.AppendLine($"{a} ↔ {b}");
                if (internalLinks.Count > 30) builder.AppendLine($"… and {internalLinks.Count - 30} more of the same pattern.");
            }
        }
        builder.AppendLine();
        builder.Append(component.Type switch
        {
            ComponentType.ArduinoHeader => "The 5V and GND pins are idealized power contacts. D2–D7 are fixed logic levels in this model, not a simulated Arduino program.",
            ComponentType.AcSource => $"AC source: {Property(component, "RmsVoltage", "5")} V RMS at {Property(component, "Frequency", "50")} Hz across L and N.",
            ComponentType.WallPlug => $"Türkiye outlet: {Property(component, "RmsVoltage", "230")} V RMS at {Property(component, "Frequency", "50")} Hz across L and N. PE is protective earth.",
            ComponentType.PowerStrip3 or ComponentType.PowerStrip6 => "ON connects inlet L/N/PE to each socket. OFF opens those three rails.",
            ComponentType.UsbCPlug or ComponentType.UsbCReceptacle => "Every USB-C contact is available as an individual topology connection.",
            ComponentType.LightningPlug or ComponentType.LightningReceptacle => "Every Lightning contact is available as an individual topology connection.",
            ComponentType.DeviceUsbC => "USB-C sink model: CC has a 5.1 kΩ pull-down to GND. Its charging input uses VBUS and GND.",
            _ when ComponentCatalog.IsPowerAdapter(component.Type) => AdapterDescription(component),
            _ => "Contacts and internal paths shown here are available for topology practice."
        });
        return builder.ToString();
    }

    private static string Attached(CircuitDocument document, ComponentModel cable, string port)
    {
        var ends = new List<string>();
        foreach (var wire in document.Wires)
        {
            if (wire.A.ComponentId == cable.Id && wire.A.PortId == port)
                ends.Add(wire.HasOpenEnd ? $"open end at {wire.OpenEndX:0}, {wire.OpenEndY:0}" : ComponentCatalog.GetPortLabel(document, wire.B));
            else if (!wire.HasOpenEnd && wire.B.ComponentId == cable.Id && wire.B.PortId == port)
                ends.Add(ComponentCatalog.GetPortLabel(document, wire.A));
        }
        return ends.Count == 0 ? "open" : string.Join(", ", ends);
    }

    private static string Pins(string connector, string signal)
    {
        if (connector == "Lightning") return signal switch
        {
            "VBUS" => "VBUS power contact group (model)",
            "GND" => "GND return contact group (model)",
            _ => signal
        };
        if (connector == "USB-C") return signal switch
        {
            "VBUS" => "A4/A9/B4/B9 VBUS",
            "GND" => "A1/A12/B1/B12 GND",
            "CC" => "A5 CC1 (default orientation)",
            "D+" => "A6/B6 D+ contacts",
            "D-" => "A7/B7 D− contacts",
            "TX1+" => "A2 SSTX1+", "TX1-" => "A3 SSTX1−",
            "TX2+" => "B2 SSTX2+", "TX2-" => "B3 SSTX2−",
            "RX2-" => "A10 SSRX2−", "RX2+" => "A11 SSRX2+",
            "RX1-" => "B10 SSRX1−", "RX1+" => "B11 SSRX1+",
            "SBU1" => "A8 SBU1", "SBU2" => "B8 SBU2",
            "SH" => "shell/shield",
            _ => signal
        };
        if (connector == "USB-A" && (signal.StartsWith("RX1", StringComparison.Ordinal) || signal.StartsWith("TX1", StringComparison.Ordinal)))
            return $"pin {(signal.StartsWith("RX1", StringComparison.Ordinal) ? (signal.EndsWith("+", StringComparison.Ordinal) ? "6" : "5") : (signal.EndsWith("+", StringComparison.Ordinal) ? "9" : "8"))} {signal}";
        if (connector == "USB-B" && (signal.StartsWith("RX1", StringComparison.Ordinal) || signal.StartsWith("TX1", StringComparison.Ordinal)))
            return $"pin {(signal.StartsWith("TX1", StringComparison.Ordinal) ? (signal.EndsWith("+", StringComparison.Ordinal) ? "6" : "5") : (signal.EndsWith("+", StringComparison.Ordinal) ? "9" : "8"))} {signal}";
        if (signal == "GND_DRAIN" && connector is "USB-A" or "USB-B") return "pin 7 GND drain";
        return signal switch
        {
            "VBUS" => "pin 1 VBUS",
            "D-" => "pin 2 D−",
            "D+" => "pin 3 D+",
            "GND" => "pin 4 GND",
            "SH" => "shell/shield",
            _ => signal
        };
    }

    private static string Property(ComponentModel component, string key, string fallback)
        => component.Properties.TryGetValue(key, out var value) ? value : fallback;

    private static string AdapterDescription(ComponentModel component)
    {
        var current = Property(component, "MaxCurrent", "2");
        var description = $"Generic isolated charger model: 100–240 V AC, 50/60 Hz input. With live AC on L/N, its USB output supplies {Property(component, "OutputVoltage", "5")} V DC up to {current} A.";
        if (component.Type is ComponentType.Adapter5V2AUsbC or ComponentType.AdapterFast20WUsbC or ComponentType.AdapterFast65WUsbC)
            description += $" CC1 uses a {Property(component, "CCPullupOhms", "10000")} Ω source pull-up; higher USB-PD voltages require negotiation, which this basic model does not simulate.";
        return description;
    }
}
