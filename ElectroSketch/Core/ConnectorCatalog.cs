using System.Windows;

namespace ElectroSketch.Core;

public sealed record ConnectorPin(string Id, string Label, string Signal);

public sealed record ConnectorDefinition(
    ComponentType Type,
    string Title,
    string Subtitle,
    ComponentCategory Category,
    string Specification,
    IReadOnlyList<ConnectorPin> Pins);

public static class ConnectorCatalog
{
    private static readonly IReadOnlyDictionary<ComponentType, ConnectorDefinition> Definitions = BuildDefinitions();

    public static bool IsConnector(ComponentType type) => Definitions.ContainsKey(type);

    public static bool TryGet(ComponentType type, out ConnectorDefinition definition)
        => Definitions.TryGetValue(type, out definition!);

    public static ConnectorDefinition Get(ComponentType type) => Definitions[type];

    public static Size GetSize(ComponentType type)
    {
        if (type is ComponentType.UsbCPlug or ComponentType.UsbCReceptacle)
            return new Size(280, 398);
        var count = Get(type).Pins.Count;
        var rows = count > 12 ? (int)Math.Ceiling(count / 2.0) : count;
        return new Size(280, 86 + rows * 24);
    }

    public static IEnumerable<PortDefinition> GetPorts(ConnectorDefinition connector)
    {
        var size = GetSize(connector.Type);
        var pins = connector.Pins;
        if (connector.Type is ComponentType.UsbCPlug or ComponentType.UsbCReceptacle)
        {
            foreach (var pin in pins)
            {
                if (pin.Id == "SH")
                    yield return MakePort(pin, new Point(size.Width - 10, 70 + 12 * 24));
                else
                {
                    var sideA = pin.Id.StartsWith("A", StringComparison.Ordinal);
                    var row = int.Parse(pin.Id[1..]) - 1;
                    yield return MakePort(pin, new Point(sideA ? 10 : size.Width - 10, 70 + row * 24));
                }
            }
            yield break;
        }
        if (pins.Count <= 12)
        {
            for (var i = 0; i < pins.Count; i++)
                yield return MakePort(pins[i], new Point(size.Width - 10, 70 + i * 24));
            yield break;
        }

        var leftCount = (int)Math.Ceiling(pins.Count / 2.0);
        for (var i = 0; i < pins.Count; i++)
        {
            var left = i < leftCount;
            var row = left ? i : i - leftCount;
            yield return MakePort(pins[i], new Point(left ? 10 : size.Width - 10, 70 + row * 24));
        }
    }

    private static PortDefinition MakePort(ConnectorPin pin, Point position)
        => new(pin.Id, $"{pin.Id} · {pin.Label}", position, ColorFor(pin.Signal));

    private static string ColorFor(string signal)
    {
        if (signal.Contains("VBUS") || signal.Contains("POWER")) return "#E53935";
        if (signal.Contains("GND") || signal.Contains("RETURN")) return "#20262D";
        if (signal.Contains("D+")) return "#27AE60";
        if (signal.Contains("D-")) return "#F2F2F2";
        if (signal.Contains("TX")) return "#F4A52C";
        if (signal.Contains("RX")) return "#2DB7C4";
        if (signal.Contains("CC")) return "#F2C94C";
        if (signal.Contains("SBU")) return "#8E44AD";
        if (signal.Contains("SHIELD")) return "#AAB2B8";
        return "#7F8C98";
    }

    private static IReadOnlyDictionary<ComponentType, ConnectorDefinition> BuildDefinitions()
    {
        const string usb2 = "USB-IF USB 2.0 cable and connector specification";
        const string usb3 = "USB-IF USB 3.x legacy cable and connector specification";
        const string usbc = "USB-IF USB Type-C cable and connector specification";
        const string lightning = "Lightning contact layout for basic topology practice";

        var list = new List<ConnectorDefinition>
        {
            Legacy2(ComponentType.UsbA2, "USB Type-A female breakout", "socket contacts ↔ solder pins", ComponentCategory.Usb2Connectors, usb2, false),
            Legacy2(ComponentType.UsbB2, "USB Type-B 2.0", "Standard-B · 4 pins", ComponentCategory.Usb2Connectors, usb2, false),
            Legacy2(ComponentType.UsbMiniA, "USB Mini-A 2.0", "Mini-A · 5 pins", ComponentCategory.Usb2Connectors, usb2, true),
            Legacy2(ComponentType.UsbMiniB, "USB Mini-B 2.0", "Mini-B · 5 pins", ComponentCategory.Usb2Connectors, usb2, true),
            Legacy2(ComponentType.UsbMiniAb, "USB Mini-AB 2.0", "Mini-AB receptacle · 5 pins", ComponentCategory.Usb2Connectors, usb2, true),
            Legacy2(ComponentType.UsbMicroA, "USB Micro-A 2.0", "Micro-A · 5 pins", ComponentCategory.Usb2Connectors, usb2, true),
            Legacy2(ComponentType.UsbMicroB, "USB Micro-B 2.0", "Micro-B · 5 pins", ComponentCategory.Usb2Connectors, usb2, true),
            Legacy2(ComponentType.UsbMicroAb, "USB Micro-AB 2.0", "Micro-AB receptacle · 5 pins", ComponentCategory.Usb2Connectors, usb2, true),
            SuperSpeed(ComponentType.UsbA3, "USB Type-A SuperSpeed", "Standard-A · 9 pins", usb3, false),
            SuperSpeed(ComponentType.UsbB3, "USB Type-B SuperSpeed", "Standard-B · 9 pins", usb3, true),
            SuperSpeedMicro(ComponentType.UsbMicroA3, "USB Micro-A SuperSpeed", "Micro-A · 10 pins", usb3),
            SuperSpeedMicro(ComponentType.UsbMicroB3, "USB Micro-B SuperSpeed", "Micro-B · 10 pins", usb3),
            SuperSpeedMicro(ComponentType.UsbMicroAb3, "USB Micro-AB SuperSpeed", "Micro-AB receptacle · 10 pins", usb3),
            TypeC(ComponentType.UsbCPlug, "USB Type-C plug", "24 individual contacts", usbc, true),
            TypeC(ComponentType.UsbCReceptacle, "USB Type-C receptacle", "24 individual contacts", usbc, false),
            Lightning(ComponentType.LightningPlug, "Lightning plug", "8 reversible physical contacts", lightning),
            Lightning(ComponentType.LightningReceptacle, "Lightning receptacle", "8 physical contacts", lightning)
        };
        return list.ToDictionary(x => x.Type);
    }

    private static ConnectorDefinition Legacy2(ComponentType type, string title, string subtitle, ComponentCategory category, string spec, bool hasId)
    {
        var pins = new List<ConnectorPin>
        {
            new("1", "VBUS +5 V", "VBUS"),
            new("2", "D−", "D-"),
            new("3", "D+", "D+"),
        };
        if (hasId) pins.Add(new("4", "ID", "ID"));
        pins.Add(new(hasId ? "5" : "4", "GND", "GND"));
        pins.Add(new("SH", "shield / shell", "SHIELD"));
        return new ConnectorDefinition(type, title, subtitle, category, spec, pins);
    }

    private static ConnectorDefinition SuperSpeed(ComponentType type, string title, string subtitle, string spec, bool typeB)
    {
        var prefix = typeB ? "B" : "A";
        var firstPair = typeB ? "TX" : "RX";
        var secondPair = typeB ? "RX" : "TX";
        return new ConnectorDefinition(type, title, subtitle, ComponentCategory.UsbSuperSpeedConnectors, spec,
        [
            new("1", "VBUS +5 V", "VBUS"), new("2", "D−", "D-"), new("3", "D+", "D+"), new("4", "GND", "GND"),
            new("5", $"SS{prefix}_{firstPair}−", firstPair), new("6", $"SS{prefix}_{firstPair}+", firstPair), new("7", "GND drain", "GND"),
            new("8", $"SS{prefix}_{secondPair}−", secondPair), new("9", $"SS{prefix}_{secondPair}+", secondPair), new("SH", "shield / shell", "SHIELD")
        ]);
    }

    private static ConnectorDefinition SuperSpeedMicro(ComponentType type, string title, string subtitle, string spec)
        => new(type, title, subtitle, ComponentCategory.UsbSuperSpeedConnectors, spec,
        [
            new("1", "VBUS +5 V", "VBUS"), new("2", "D−", "D-"), new("3", "D+", "D+"), new("4", "ID", "ID"), new("5", "GND", "GND"),
            new("6", "SS_TX−", "TX"), new("7", "SS_TX+", "TX"), new("8", "GND drain", "GND"), new("9", "SS_RX−", "RX"), new("10", "SS_RX+", "RX"),
            new("SH", "shield / shell", "SHIELD")
        ]);

    private static ConnectorDefinition TypeC(ComponentType type, string title, string subtitle, string spec, bool plug)
    {
        var cc2 = plug ? "VCONN / CC2 contact" : "CC2";
        var pins = new List<ConnectorPin>
        {
            new("A1", "GND", "GND"), new("A2", "SSTX1+", "TX"), new("A3", "SSTX1−", "TX"), new("A4", "VBUS", "VBUS"),
            new("A5", "CC1", "CC"), new("A6", "D+", "D+"), new("A7", "D−", "D-"), new("A8", "SBU1", "SBU"),
            new("A9", "VBUS", "VBUS"), new("A10", "SSRX2−", "RX"), new("A11", "SSRX2+", "RX"), new("A12", "GND", "GND"),
            new("B1", "GND", "GND"), new("B2", "SSTX2+", "TX"), new("B3", "SSTX2−", "TX"), new("B4", "VBUS", "VBUS"),
            new("B5", cc2, "CC")
        };
        if (!plug)
        {
            pins.Add(new("B6", "D+", "D+"));
            pins.Add(new("B7", "D−", "D-"));
        }
        pins.AddRange([
            new("B8", "SBU2", "SBU"),
            new("B9", "VBUS", "VBUS"), new("B10", "SSRX1−", "RX"), new("B11", "SSRX1+", "RX"), new("B12", "GND", "GND"),
            new("SH", "shield / shell", "SHIELD")
        ]);
        return new ConnectorDefinition(type, title, plug ? "22 contacts + shield" : subtitle, ComponentCategory.UsbSuperSpeedConnectors, spec, pins);
    }

    private static ConnectorDefinition Lightning(ComponentType type, string title, string subtitle, string spec)
        => new(type, title, subtitle, ComponentCategory.LightningConnectors, spec,
        [
            new("L1", "physical contact 1", "CONTACT"), new("L2", "physical contact 2", "CONTACT"),
            new("L3", "physical contact 3", "CONTACT"), new("L4", "physical contact 4", "CONTACT"),
            new("L5", "physical contact 5", "CONTACT"), new("L6", "physical contact 6", "CONTACT"),
            new("L7", "physical contact 7", "CONTACT"), new("L8", "physical contact 8", "CONTACT"),
            new("SH", "shield / shell", "SHIELD")
        ]);
}
