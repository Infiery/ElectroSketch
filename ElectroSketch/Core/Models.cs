using System.Text.Json;
using System.Text.Json.Serialization;
using System.Windows;

namespace ElectroSketch.Core;

public enum ComponentType
{
    ArduinoHeader,
    Breadboard,
    Led,
    Resistor,
    AcSource,
    UsbA2,
    UsbB2,
    UsbMiniA,
    UsbMiniB,
    UsbMiniAb,
    UsbMicroA,
    UsbMicroB,
    UsbMicroAb,
    UsbA3,
    UsbB3,
    UsbMicroA3,
    UsbMicroB3,
    UsbMicroAb3,
    UsbCPlug,
    UsbCReceptacle,
    LightningPlug,
    LightningReceptacle,
    WallPlug,
    PowerStrip3,
    PowerStrip6,
    Speaker4Ohm2W,
    MihawkDriver,
    Adapter5V2AUsbA,
    Adapter5V2AUsbC,
    AdapterFast18WUsbA,
    AdapterFast20WUsbC,
    AdapterFast65WUsbC,
    Rj45Plug,
    Rj45Jack,
    EthernetCat5,
    EthernetCat5e,
    EthernetCat6,
    EthernetCat6a,
    EthernetCat7,
    EthernetCat8,
    UsbAToLightningCable,
    UsbCToUsbCCable,
    UsbAToUsbACable,
    UsbBToUsbBCable,
    UsbAToUsbBCable,
    UsbAToUsbCCable,
    UsbAToUsbBSuperSpeedCable,
    UsbCToLightningCable,
    DeviceUsbA,
    DeviceUsbB,
    DeviceUsbC,
    DeviceLightning
}

public enum EditorMode
{
    Select,
    Wire,
    Probe,
    AnnotationText,
    DrawLine,
    DrawArrow
}

public enum MeterMode
{
    DcVoltage,
    AcVoltage,
    DcCurrent,
    Continuity,
    Resistance,
    Diode
}

public enum MeterProfile { AutoRange, Mas830L }

public enum ComponentCategory
{
    Controllers,
    PowerSources,
    Prototyping,
    Indicators,
    PassiveComponents,
    Usb2Connectors,
    UsbSuperSpeedConnectors,
    LightningConnectors,
    PowerDistribution,
    Audio,
    PowerAdapters,
    Ethernet,
    ReadyCables,
    RechargeableDevices
}

public enum AnnotationType
{
    Text,
    Line,
    Arrow
}

public sealed class CircuitDocument
{
    public int Version { get; set; } = 1;
    public string Name { get; set; } = "Untitled topology";
    public List<ComponentModel> Components { get; set; } = [];
    public List<WireModel> Wires { get; set; } = [];
    public List<AnnotationModel> Annotations { get; set; } = [];
    public MeterState Meter { get; set; } = new();
}

public sealed class AnnotationModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public AnnotationType Type { get; set; }
    public double X1 { get; set; }
    public double Y1 { get; set; }
    public double X2 { get; set; }
    public double Y2 { get; set; }
    public string Text { get; set; } = "";
    public string Color { get; set; } = "#34495E";
    public double Rotation { get; set; }
}

public sealed class ComponentGroupModel
{
    public string Id { get; set; } = "";
    public double OffsetX { get; set; }
    public double OffsetY { get; set; }
    public double Rotation { get; set; }
}

public sealed record ContactGroupDefinition(string Id, string Label, IReadOnlyList<string> PortIds);

public sealed class ComponentModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public ComponentType Type { get; set; }
    public string Name { get; set; } = "Component";
    public double X { get; set; }
    public double Y { get; set; }
    public double Rotation { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
    public List<ComponentGroupModel> ContactGroups { get; set; } = [];
}

public sealed class WireModel
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public PortReference A { get; set; } = new();
    public PortReference B { get; set; } = new();
    public string Color { get; set; } = "#E53935";
    public bool HasOpenEnd { get; set; }
    public double OpenEndX { get; set; }
    public double OpenEndY { get; set; }
}

public sealed class PortReference
{
    public string ComponentId { get; set; } = "";
    public string PortId { get; set; } = "";

    public PortReference Copy() => new() { ComponentId = ComponentId, PortId = PortId };
}

public sealed class MeterState
{
    public MeterProfile Profile { get; set; } = MeterProfile.AutoRange;
    public MeterMode Mode { get; set; } = MeterMode.DcVoltage;
    public string Range { get; set; } = "Auto";
    public PortReference? RedProbe { get; set; }
    public PortReference? BlackProbe { get; set; }
}

public sealed record PortDefinition(string Id, string Label, Point Position, string Color = "#8D99A6");

public static class DocumentJson
{
    public static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true,
        Converters = { new JsonStringEnumConverter() }
    };
}

public static class ComponentCatalog
{
    private static readonly string[] Letters = ["a", "b", "c", "d", "e", "f", "g", "h", "i", "j"];

    public static Size GetSize(ComponentType type) => type switch
    {
        ComponentType.ArduinoHeader => new Size(250, 300),
        ComponentType.Breadboard => new Size(620, 310),
        ComponentType.Led => new Size(100, 140),
        ComponentType.Resistor => new Size(180, 76),
        ComponentType.AcSource => new Size(190, 150),
        ComponentType.WallPlug => new Size(220, 170),
        ComponentType.PowerStrip3 => new Size(500, 180),
        ComponentType.PowerStrip6 => new Size(800, 180),
        ComponentType.Speaker4Ohm2W => new Size(190, 165),
        ComponentType.MihawkDriver => new Size(210, 180),
        ComponentType.Adapter5V2AUsbA or ComponentType.AdapterFast18WUsbA => new Size(310, 220),
        ComponentType.Adapter5V2AUsbC or ComponentType.AdapterFast20WUsbC or ComponentType.AdapterFast65WUsbC => new Size(360, 398),
        ComponentType.Rj45Plug or ComponentType.Rj45Jack => new Size(270, 290),
        ComponentType.EthernetCat5 or ComponentType.EthernetCat5e or ComponentType.EthernetCat6 or ComponentType.EthernetCat6a or ComponentType.EthernetCat7 or ComponentType.EthernetCat8 => new Size(600, 250),
        _ when IsReadyCable(type) => new Size(430, 76 + CableSignals(type).Length * 21),
        _ when IsRechargeableDevice(type) => new Size(220, 170),
        _ when ConnectorCatalog.IsConnector(type) => ConnectorCatalog.GetSize(type),
        _ => new Size(160, 100)
    };

    public static Size GetSize(ComponentModel component)
        => IsImageMode(component) ? GetImageSize(component.Type) : GetSize(component.Type);

    public static bool SupportsViewMode(ComponentModel component)
        => ConnectorCatalog.IsConnector(component.Type) || IsPowerAdapter(component.Type) || component.Type is
            ComponentType.Rj45Plug or ComponentType.Rj45Jack or
            ComponentType.EthernetCat5 or ComponentType.EthernetCat5e or ComponentType.EthernetCat6 or
            ComponentType.EthernetCat6a or ComponentType.EthernetCat7 or ComponentType.EthernetCat8 || IsReadyCable(component.Type);

    public static bool IsImageMode(ComponentModel component)
        => SupportsViewMode(component) && component.Properties.TryGetValue("ViewMode", out var mode) && mode.Equals("Image", StringComparison.OrdinalIgnoreCase);

    public static void ToggleViewMode(ComponentModel component)
    {
        if (!SupportsViewMode(component)) return;
        component.Properties["ViewMode"] = IsImageMode(component) ? "Electronics" : "Image";
    }

    private static Size GetImageSize(ComponentType type)
        => type switch
        {
            ComponentType.LightningPlug or ComponentType.LightningReceptacle => new Size(190, 125),
            ComponentType.Adapter5V2AUsbA or ComponentType.Adapter5V2AUsbC or ComponentType.AdapterFast18WUsbA or ComponentType.AdapterFast20WUsbC or ComponentType.AdapterFast65WUsbC => new Size(250, 170),
            ComponentType.Rj45Plug or ComponentType.Rj45Jack => new Size(210, 135),
            ComponentType.EthernetCat5 or ComponentType.EthernetCat5e or ComponentType.EthernetCat6 or ComponentType.EthernetCat6a or ComponentType.EthernetCat7 or ComponentType.EthernetCat8 => new Size(420, 125),
            _ when IsReadyCable(type) => new Size(360, 115),
            _ => new Size(220, 125)
        };

    public static IEnumerable<PortDefinition> GetPorts(ComponentModel component)
    {
        var ports = GetBasePorts(component).ToList();
        IReadOnlyList<ContactGroupDefinition> groups = IsImageMode(component) ? [] : GetContactGroups(component);
        var assigned = groups.SelectMany(group => group.PortIds.Select(id => (id, group.Id)))
            .ToDictionary(pair => pair.id, pair => pair.Id, StringComparer.Ordinal);
        foreach (var group in groups) ConstrainContactGroup(component, group.Id);
        foreach (var port in ports)
        {
            if (assigned.TryGetValue(port.Id, out var groupId) && TryGetContactGroup(component, groupId, out var group))
                yield return port with { Position = ApplyContactGroupTransform(component, group, port.Position) };
            else
                yield return port;
        }
    }

    private static IEnumerable<PortDefinition> GetBasePorts(ComponentModel component)
    {
        if (ConnectorCatalog.TryGet(component.Type, out var connector))
        {
            if (component.Type == ComponentType.UsbA2)
            {
                var size = GetSize(component);
                foreach (var port in ConnectorCatalog.GetPorts(connector))
                {
                    if (IsImageMode(component))
                    {
                        yield return port with { Position = new Point(size.Width - 9, size.Height / 2), Label = $"solder {port.Label}" };
                        yield return port with { Id = "M_" + port.Id, Position = new Point(9, size.Height / 2), Label = $"socket {port.Label}" };
                    }
                    else
                    {
                        yield return port with { Label = $"solder {port.Label}" };
                        yield return port with { Id = "M_" + port.Id, Position = new Point(10, port.Position.Y), Label = $"socket {port.Label}" };
                    }
                }
                yield break;
            }
            if (IsImageMode(component))
            {
                var size = GetSize(component);
                foreach (var port in ConnectorCatalog.GetPorts(connector))
                    yield return port with { Position = new Point(size.Width - 9, size.Height / 2) };
            }
            else
            {
                foreach (var port in ConnectorCatalog.GetPorts(connector)) yield return port;
            }
            if (component.Type is ComponentType.LightningPlug or ComponentType.LightningReceptacle)
            {
                var size = GetSize(component);
                var x = IsImageMode(component) ? size.Width - 9 : 10;
                yield return new PortDefinition("PWR_VBUS", "VBUS power group · simplified model", new Point(x, size.Height - 43), "#E53935");
                yield return new PortDefinition("PWR_GND", "GND return group · simplified model", new Point(x, size.Height - 18), "#20262D");
            }
            yield break;
        }

        if (component.Type is ComponentType.Rj45Plug or ComponentType.Rj45Jack)
        {
            var colors = new[] { "#F5A623", "#E67E22", "#63C174", "#3285D5", "#80B7E5", "#27AE60", "#A06A42", "#6F4128" };
            var size = GetSize(component);
            for (var pin = 1; pin <= 8; pin++)
                yield return new(pin.ToString(), $"pin {pin}", IsImageMode(component) ? new Point(size.Width - 8, size.Height / 2) : new Point(size.Width - 10, 66 + (pin - 1) * 24), colors[pin - 1]);
            yield return new("SH", "shield", IsImageMode(component) ? new Point(size.Width - 8, size.Height / 2) : new Point(size.Width - 10, 258), "#AAB2B8");
            yield break;
        }

        if (IsEthernetCable(component.Type))
        {
            var size = GetSize(component);
            var colors = new[] { "#F5A623", "#E67E22", "#63C174", "#3285D5", "#80B7E5", "#27AE60", "#A06A42", "#6F4128" };
            for (var pin = 1; pin <= 8; pin++)
            {
                var y = 58 + (pin - 1) * 23;
                yield return new($"L{pin}", $"left pin {pin}", IsImageMode(component) ? new Point(8, size.Height / 2) : new Point(10, y), colors[pin - 1]);
                yield return new($"R{pin}", $"right pin {pin}", IsImageMode(component) ? new Point(size.Width - 8, size.Height / 2) : new Point(size.Width - 10, y), colors[pin - 1]);
            }
            yield break;
        }

        if (IsReadyCable(component.Type))
        {
            var size = GetSize(component);
            var image = IsImageMode(component);
            foreach (var signal in CableSignals(component.Type))
            {
                var color = SignalColor(signal);
                var y = 46 + Array.IndexOf(CableSignals(component.Type), signal) * 21;
                if (component.Type == ComponentType.UsbAToUsbCCable && signal == "CC")
                {
                    yield return new("R_CC", "USB-C CC1 · 56 kΩ VBUS bias", image ? new Point(size.Width - 8, size.Height / 2) : new Point(size.Width - 10, y), color);
                    continue;
                }
                yield return new($"L_{signal}", $"left {signal}", image ? new Point(8, size.Height / 2) : new Point(10, y), color);
                yield return new($"R_{signal}", $"right {signal}", image ? new Point(size.Width - 8, size.Height / 2) : new Point(size.Width - 10, y), color);
            }
            yield break;
        }

        if (IsRechargeableDevice(component.Type))
        {
            var size = GetSize(component);
            var image = IsImageMode(component);
            var signals = DeviceSignals(component.Type);
            foreach (var signal in signals)
            {
                var y = 72 + Array.IndexOf(signals, signal) * 27;
                yield return new(signal, $"charging {signal}", image ? new Point(8, size.Height / 2) : new Point(10, y), SignalColor(signal));
            }
            yield break;
        }

        if (IsPowerAdapter(component.Type))
        {
            var imageSize = GetSize(component);
            var imageMode = IsImageMode(component);
            yield return new("AC_L", "AC live · brown", imageMode ? new Point(8, imageSize.Height / 2) : new Point(10, 75), "#8B5A2B");
            yield return new("AC_N", "AC neutral", imageMode ? new Point(8, imageSize.Height / 2) : new Point(10, 110), "#3285D5");
            yield return new("AC_PE", "protective earth · green/yellow", imageMode ? new Point(8, imageSize.Height / 2) : new Point(10, 145), "#98B82F");
            if (component.Type is ComponentType.Adapter5V2AUsbA or ComponentType.AdapterFast18WUsbA)
            {
                var output = imageMode ? new Point(imageSize.Width - 8, imageSize.Height / 2) : (Point?)null;
                yield return new("USB_1", "USB-A 1 · VBUS", output ?? new Point(298, 72), "#E53935");
                yield return new("USB_2", "USB-A 2 · D−", output ?? new Point(298, 102), "#F2F2F2");
                yield return new("USB_3", "USB-A 3 · D+", output ?? new Point(298, 132), "#27AE60");
                yield return new("USB_4", "USB-A 4 · GND", output ?? new Point(298, 162), "#20262D");
                yield return new("USB_SH", "USB-A shield", output ?? new Point(298, 192), "#AAB2B8");
            }
            else
            {
                var usbC = ConnectorCatalog.Get(ComponentType.UsbCReceptacle);
                foreach (var port in ConnectorCatalog.GetPorts(usbC))
                {
                    var position = imageMode
                        ? new Point(imageSize.Width - 8, imageSize.Height / 2)
                        : new Point(port.Position.X < 140 ? 195 : 348, port.Position.Y);
                    yield return port with { Id = "USB_" + port.Id, Label = "USB-C " + port.Label, Position = position };
                }
            }
            yield break;
        }

        switch (component.Type)
        {
            case ComponentType.ArduinoHeader:
                yield return new("5V", "5V", new Point(238, 76), "#E53935");
                yield return new("GND", "GND", new Point(238, 112), "#20262D");
                for (var pin = 2; pin <= 7; pin++)
                    yield return new($"D{pin}", $"D{pin}", new Point(238, 140 + (pin - 2) * 27), "#F4A52C");
                break;

            case ComponentType.Breadboard:
                for (var number = 1; number <= 12; number++)
                {
                    var x = 68 + (number - 1) * 44;
                    yield return new($"top+{number}", "+ rail", new Point(x, 45), "#E53935");
                    yield return new($"top-{number}", "− rail", new Point(x, 65), "#3285D5");
                    for (var letterIndex = 0; letterIndex < 5; letterIndex++)
                        yield return new($"{Letters[letterIndex]}{number}", $"{Letters[letterIndex]}{number} · connected a–e row", new Point(x, 94 + letterIndex * 13), "#87929C");
                    for (var letterIndex = 5; letterIndex < 10; letterIndex++)
                        yield return new($"{Letters[letterIndex]}{number}", $"{Letters[letterIndex]}{number} · connected f–j row", new Point(x, 169 + (letterIndex - 5) * 13), "#87929C");
                    yield return new($"bottom+{number}", "+ rail", new Point(x, 270), "#E53935");
                    yield return new($"bottom-{number}", "− rail", new Point(x, 291), "#3285D5");
                }
                break;

            case ComponentType.Led:
                yield return new("A", "A  anode", new Point(28, 126), "#E53935");
                yield return new("K", "K  cathode", new Point(72, 126), "#20262D");
                break;

            case ComponentType.Resistor:
                yield return new("A", "A", new Point(10, 38), "#D6DCE1");
                yield return new("B", "B", new Point(170, 38), "#D6DCE1");
                break;
            case ComponentType.AcSource:
                yield return new("L", "L · AC · brown", new Point(178, 58), "#8B5A2B");
                yield return new("N", "N · return", new Point(178, 105), "#3285D5");
                break;
            case ComponentType.WallPlug:
                yield return new("L", "L · live · brown", new Point(208, 68), "#8B5A2B");
                yield return new("N", "N · neutral", new Point(208, 105), "#3285D5");
                yield return new("PE", "PE · earth · green/yellow", new Point(208, 142), "#98B82F");
                break;
            case ComponentType.PowerStrip3:
            case ComponentType.PowerStrip6:
                var sockets = component.Type == ComponentType.PowerStrip3 ? 3 : 6;
                yield return new("IN_L", "input L · brown", new Point(10, 70), "#8B5A2B");
                yield return new("IN_N", "input N", new Point(10, 105), "#3285D5");
                yield return new("IN_PE", "input PE · green/yellow", new Point(10, 140), "#98B82F");
                for (var socket = 1; socket <= sockets; socket++)
                {
                    var x = 175 + (socket - 1) * 100;
                    yield return new($"S{socket}_L", $"socket {socket} L · brown", new Point(x - 22, 166), "#8B5A2B");
                    yield return new($"S{socket}_N", $"socket {socket} N", new Point(x, 166), "#3285D5");
                    yield return new($"S{socket}_PE", $"socket {socket} PE · green/yellow", new Point(x + 22, 166), "#98B82F");
                }
                break;
            case ComponentType.Speaker4Ohm2W:
            case ComponentType.MihawkDriver:
                var width = GetSize(component.Type).Width;
                var height = GetSize(component.Type).Height;
                yield return new("+", "+", new Point(width * 0.35, height - 12), "#E53935");
                yield return new("-", "−", new Point(width * 0.65, height - 12), "#20262D");
                break;
        }
    }

    public static IReadOnlyList<ContactGroupDefinition> GetContactGroups(ComponentModel component)
    {
        var allPortIds = GetBasePorts(component).Select(port => port.Id).ToHashSet(StringComparer.Ordinal);
        if (component.Type is ComponentType.PowerStrip3 or ComponentType.PowerStrip6)
        {
            var groups = new List<ContactGroupDefinition>
            {
                new("inlet", "Input contacts", ["IN_L", "IN_N", "IN_PE"])
            };
            var count = component.Type == ComponentType.PowerStrip3 ? 3 : 6;
            for (var socket = 1; socket <= count; socket++)
                groups.Add(new($"socket-{socket}", $"Socket {socket}", [$"S{socket}_L", $"S{socket}_N", $"S{socket}_PE"]));
            return groups;
        }
        if (component.Type == ComponentType.UsbA2)
        {
            var pins = ConnectorCatalog.Get(ComponentType.UsbA2).Pins.Select(pin => pin.Id).ToArray();
            return [
                new("socket", "USB-A socket contacts", pins.Select(id => "M_" + id).ToArray()),
                new("solder", "Solder contacts", pins)
            ];
        }
        if (IsReadyCable(component.Type))
        {
            var signals = CableSignals(component.Type);
            return [
                new("left-end", $"{ReadyCableCatalog.Ends(component.Type).Left} end", signals.Select(id => "L_" + id).ToArray()),
                new("right-end", $"{ReadyCableCatalog.Ends(component.Type).Right} end", signals.Select(id => "R_" + id).ToArray())
            ];
        }
        if (IsEthernetCable(component.Type))
            return [new("left-end", "Left RJ45 end", Enumerable.Range(1, 8).Select(i => $"L{i}").ToArray()),
                new("right-end", "Right RJ45 end", Enumerable.Range(1, 8).Select(i => $"R{i}").ToArray())];
        if (IsPowerAdapter(component.Type))
        {
            var outputIds = allPortIds.Where(id => id.StartsWith("USB_", StringComparison.Ordinal)).ToArray();
            return [new("ac-input", "AC input contacts", ["AC_L", "AC_N", "AC_PE"]),
                new("usb-output", "USB output contacts", outputIds)];
        }
        return [];
    }

    public static bool TryGetContactGroup(ComponentModel component, string groupId, out ContactGroupDefinition group)
    {
        group = GetContactGroups(component).FirstOrDefault(item => item.Id == groupId)!;
        return group is not null;
    }

    public static ComponentGroupModel GetOrCreateContactGroup(ComponentModel component, string groupId)
    {
        component.ContactGroups ??= [];
        var group = component.ContactGroups.FirstOrDefault(item => item.Id == groupId);
        if (group is not null) return group;
        group = new ComponentGroupModel { Id = groupId };
        component.ContactGroups.Add(group);
        return group;
    }

    public static void ConstrainContactGroup(ComponentModel component, string groupId)
    {
        if (!TryGetContactGroup(component, groupId, out var definition)) return;
        var transform = GetOrCreateContactGroup(component, groupId);
        transform.Rotation = ((transform.Rotation % 360) + 360) % 360;

        var originalViewMode = component.Properties.GetValueOrDefault("ViewMode");
        if (IsImageMode(component)) component.Properties["ViewMode"] = "Electronics";
        try
        {
            ConstrainContactGroupInElectronicsView(component, definition, transform);
        }
        finally
        {
            if (originalViewMode is not null) component.Properties["ViewMode"] = originalViewMode;
        }
    }

    private static void ConstrainContactGroupInElectronicsView(ComponentModel component, ContactGroupDefinition definition, ComponentGroupModel transform)
    {

        var basePorts = GetBasePorts(component)
            .Where(port => definition.PortIds.Contains(port.Id, StringComparer.Ordinal))
            .Select(port => port.Position)
            .ToArray();
        if (basePorts.Length == 0) return;

        var anchor = new Point(basePorts.Average(point => point.X), basePorts.Average(point => point.Y));
        var radians = transform.Rotation * Math.PI / 180;
        var rotated = basePorts.Select(point =>
        {
            var dx = point.X - anchor.X;
            var dy = point.Y - anchor.Y;
            return new Point(anchor.X + dx * Math.Cos(radians) - dy * Math.Sin(radians),
                anchor.Y + dx * Math.Sin(radians) + dy * Math.Cos(radians));
        }).ToArray();

        var size = GetSize(component);
        transform.OffsetX = ClampGroupOffset(transform.OffsetX, rotated.Min(point => point.X), rotated.Max(point => point.X), size.Width);
        transform.OffsetY = ClampGroupOffset(transform.OffsetY, rotated.Min(point => point.Y), rotated.Max(point => point.Y), size.Height);
    }

    private static double ClampGroupOffset(double offset, double min, double max, double extent)
    {
        const double inset = 12;
        var lower = inset - min;
        var upper = extent - inset - max;
        if (lower <= upper) return Math.Clamp(offset, lower, upper);
        // If a legacy or unusually dense contact group cannot fit at this angle,
        // center it in the parent rather than allowing arbitrary drift.
        return (lower + upper) / 2;
    }

    public static Rect GetContactGroupBoundsLocal(ComponentModel component, string groupId)
    {
        if (!TryGetContactGroup(component, groupId, out var group)) return Rect.Empty;
        var points = GetPorts(component).Where(port => group.PortIds.Contains(port.Id, StringComparer.Ordinal)).Select(port => port.Position).ToList();
        if (points.Count == 0) return Rect.Empty;
        var bounds = new Rect(points[0], points[0]);
        foreach (var point in points.Skip(1)) bounds.Union(point);
        bounds.Inflate(15, 15);
        return bounds;
    }

    public static Point GetContactGroupCenterLocal(ComponentModel component, string groupId)
    {
        if (!TryGetContactGroup(component, groupId, out var group)) return new Point();
        var points = GetPorts(component).Where(port => group.PortIds.Contains(port.Id, StringComparer.Ordinal)).Select(port => port.Position).ToList();
        if (points.Count == 0) return new Point();
        return new Point(points.Average(point => point.X), points.Average(point => point.Y));
    }

    private static Point ApplyContactGroupTransform(ComponentModel component, ContactGroupDefinition group, Point position)
    {
        var basePorts = GetBasePorts(component).Where(port => group.PortIds.Contains(port.Id, StringComparer.Ordinal)).Select(port => port.Position).ToList();
        if (basePorts.Count == 0) return position;
        var anchor = new Point(basePorts.Average(point => point.X), basePorts.Average(point => point.Y));
        var transform = GetOrCreateContactGroup(component, group.Id);
        var radians = transform.Rotation * Math.PI / 180;
        var dx = position.X - anchor.X;
        var dy = position.Y - anchor.Y;
        var x = anchor.X + dx * Math.Cos(radians) - dy * Math.Sin(radians) + transform.OffsetX;
        var y = anchor.Y + dx * Math.Sin(radians) + dy * Math.Cos(radians) + transform.OffsetY;

        return new Point(x, y);
    }

    public static IEnumerable<(string A, string B)> GetInternalConnections(ComponentModel component)
    {
        if (component.Type != ComponentType.Breadboard)
        {
            if (component.Type == ComponentType.UsbA2)
            {
                foreach (var port in ConnectorCatalog.GetPorts(ConnectorCatalog.Get(ComponentType.UsbA2)))
                    yield return (port.Id, "M_" + port.Id);
            }
            else if (component.Type is ComponentType.PowerStrip3 or ComponentType.PowerStrip6)
            {
                if (component.Properties.TryGetValue("Power", out var state) && state.Equals("Off", StringComparison.OrdinalIgnoreCase)) yield break;
                var sockets = component.Type == ComponentType.PowerStrip3 ? 3 : 6;
                for (var socket = 1; socket <= sockets; socket++)
                {
                    yield return ("IN_L", $"S{socket}_L");
                    yield return ("IN_N", $"S{socket}_N");
                    yield return ("IN_PE", $"S{socket}_PE");
                }
            }
            else if (IsPowerAdapter(component.Type) &&
                     component.Type is not ComponentType.Adapter5V2AUsbA and not ComponentType.AdapterFast18WUsbA)
            {
                foreach (var pin in new[] { "USB_A9", "USB_B4", "USB_B9" }) yield return ("USB_A4", pin);
                foreach (var pin in new[] { "USB_A12", "USB_B1", "USB_B12" }) yield return ("USB_A1", pin);
            }
            else if (IsEthernetCable(component.Type))
            {
                for (var pin = 1; pin <= 8; pin++) yield return ($"L{pin}", $"R{pin}");
            }
            else if (IsReadyCable(component.Type))
            {
                foreach (var (left, right) in ReadyCableCatalog.Connections(component.Type))
                    yield return ($"L_{left}", $"R_{right}");
            }
            yield break;
        }

        for (var number = 1; number <= 12; number++)
        {
            for (var i = 1; i < 5; i++)
                yield return ($"a{number}", $"{Letters[i]}{number}");
            for (var i = 6; i < 10; i++)
                yield return ($"f{number}", $"{Letters[i]}{number}");
            if (number > 1)
            {
                yield return ("top+1", $"top+{number}");
                yield return ("top-1", $"top-{number}");
                yield return ("bottom+1", $"bottom+{number}");
                yield return ("bottom-1", $"bottom-{number}");
            }
        }
    }

    public static ComponentModel Create(ComponentType type, double x, double y)
    {
        var component = new ComponentModel { Type = type, X = x, Y = y };
        if (ConnectorCatalog.TryGet(type, out var connector))
        {
            component.Name = connector.Title;
            component.Properties["Category"] = connector.Category.ToString();
            component.Properties["Specification"] = connector.Specification;
            component.Properties["ViewMode"] = "Electronics";
            return component;
        }
        switch (type)
        {
            case ComponentType.ArduinoHeader:
                component.Name = "Arduino connectors";
                for (var pin = 2; pin <= 7; pin++) component.Properties[$"D{pin}"] = "0";
                break;
            case ComponentType.Breadboard:
                component.Name = "Mini breadboard";
                break;
            case ComponentType.Led:
                component.Name = "Red LED";
                component.Properties["Color"] = "#FF3B30";
                component.Properties["ForwardVoltage"] = "1.8";
                break;
            case ComponentType.Resistor:
                component.Name = "220 Ω resistor";
                component.Properties["Resistance"] = "220";
                component.Properties["PowerWatts"] = "0.25";
                break;
            case ComponentType.AcSource:
                component.Name = "5 V AC source";
                component.Properties["RmsVoltage"] = "5";
                component.Properties["Frequency"] = "50";
                break;
            case ComponentType.WallPlug:
                component.Name = "Türkiye wall outlet · 230 V AC";
                component.Properties["RmsVoltage"] = "230";
                component.Properties["Frequency"] = "50";
                break;
            case ComponentType.PowerStrip3:
                component.Name = "3-socket power strip";
                component.Properties["Sockets"] = "3";
                component.Properties["Power"] = "On";
                break;
            case ComponentType.PowerStrip6:
                component.Name = "6-socket power strip";
                component.Properties["Sockets"] = "6";
                component.Properties["Power"] = "On";
                break;
            case ComponentType.Speaker4Ohm2W:
                component.Name = "4 Ω 2 W speaker";
                component.Properties["Resistance"] = "4";
                component.Properties["PowerWatts"] = "2";
                break;
            case ComponentType.MihawkDriver:
                component.Name = "Mihawk 50 mm driver";
                component.Properties["Resistance"] = "32";
                component.Properties["Tolerance"] = "±5 Ω";
                component.Properties["Driver"] = "50 mm neodymium";
                component.Properties["Sensitivity"] = "112 dB ±3 dB";
                component.Properties["FrequencyRange"] = "20 Hz–20 kHz";
                break;
            case ComponentType.Adapter5V2AUsbA:
                ConfigureAdapter(component, "5 V 2 A USB-A adapter", "USB-A female", "5", "2", "10", "5V/2A");
                break;
            case ComponentType.Adapter5V2AUsbC:
                ConfigureAdapter(component, "5 V 2 A USB-C adapter", "USB-C female", "5", "2", "10", "5V/2A");
                break;
            case ComponentType.AdapterFast18WUsbA:
                ConfigureAdapter(component, "18 W fast USB-A adapter", "USB-A female", "5", "3", "18", "5V/3A · 9V/2A · 12V/1.5A");
                break;
            case ComponentType.AdapterFast20WUsbC:
                ConfigureAdapter(component, "20 W USB-C PD adapter", "USB-C female", "5", "3", "20", "5V/3A · 9V/2.22A");
                break;
            case ComponentType.AdapterFast65WUsbC:
                ConfigureAdapter(component, "65 W USB-C PD adapter", "USB-C female", "5", "3", "65", "5V/3A · 9V/3A · 15V/3A · 20V/3.25A");
                break;
            case ComponentType.Rj45Plug:
                component.Name = "RJ45 plug";
                component.Properties["ViewMode"] = "Electronics";
                component.Properties["Wiring"] = "T568B colors";
                break;
            case ComponentType.Rj45Jack:
                component.Name = "RJ45 female jack";
                component.Properties["ViewMode"] = "Electronics";
                component.Properties["Wiring"] = "T568B colors";
                break;
            case ComponentType.EthernetCat5:
            case ComponentType.EthernetCat5e:
            case ComponentType.EthernetCat6:
            case ComponentType.EthernetCat6a:
            case ComponentType.EthernetCat7:
            case ComponentType.EthernetCat8:
                component.Name = type switch
                {
                    ComponentType.EthernetCat5 => "Cat5 Ethernet cable",
                    ComponentType.EthernetCat5e => "Cat5e Ethernet cable",
                    ComponentType.EthernetCat6 => "Cat6 Ethernet cable",
                    ComponentType.EthernetCat6a => "Cat6A Ethernet cable",
                    ComponentType.EthernetCat7 => "Cat7 Ethernet cable",
                    _ => "Cat8 Ethernet cable"
                };
                component.Properties["ViewMode"] = "Electronics";
                component.Properties["Wiring"] = "Straight-through · T568B";
                break;
            case ComponentType.UsbAToLightningCable:
            case ComponentType.UsbCToUsbCCable:
            case ComponentType.UsbAToUsbACable:
            case ComponentType.UsbBToUsbBCable:
            case ComponentType.UsbAToUsbBCable:
            case ComponentType.UsbAToUsbCCable:
            case ComponentType.UsbAToUsbBSuperSpeedCable:
            case ComponentType.UsbCToLightningCable:
                component.Name = ReadyCableName(type);
                component.Properties["ViewMode"] = "Image";
                component.Properties["AutoConnect"] = "Match compatible contacts on drop";
                component.Properties["Wiring"] = "Factory-wired; individual links remain editable";
                break;
            case ComponentType.DeviceUsbA:
            case ComponentType.DeviceUsbB:
            case ComponentType.DeviceUsbC:
            case ComponentType.DeviceLightning:
                component.Name = type switch
                {
                    ComponentType.DeviceUsbA => "USB-A rechargeable light",
                    ComponentType.DeviceUsbB => "USB-B rechargeable gadget",
                    ComponentType.DeviceUsbC => "USB-C rechargeable device",
                    _ => "Lightning rechargeable device"
                };
                component.Properties["ViewMode"] = "Image";
                component.Properties["ChargePercent"] = "45";
                component.Properties["BatteryCapacitymAh"] = "1200";
                component.Properties["WorkMode"] = "Idle";
                break;
        }
        return component;
    }

    public static ComponentCategory GetCategory(ComponentType type)
    {
        if (ConnectorCatalog.TryGet(type, out var connector)) return connector.Category;
        return type switch
        {
            ComponentType.ArduinoHeader => ComponentCategory.Controllers,
            ComponentType.AcSource => ComponentCategory.PowerSources,
            ComponentType.Breadboard => ComponentCategory.Prototyping,
            ComponentType.Led => ComponentCategory.Indicators,
            ComponentType.Resistor => ComponentCategory.PassiveComponents,
            ComponentType.WallPlug or ComponentType.PowerStrip3 or ComponentType.PowerStrip6 => ComponentCategory.PowerDistribution,
            ComponentType.Speaker4Ohm2W or ComponentType.MihawkDriver => ComponentCategory.Audio,
            ComponentType.Adapter5V2AUsbA or ComponentType.Adapter5V2AUsbC or ComponentType.AdapterFast18WUsbA or ComponentType.AdapterFast20WUsbC or ComponentType.AdapterFast65WUsbC => ComponentCategory.PowerAdapters,
            ComponentType.Rj45Plug or ComponentType.Rj45Jack or ComponentType.EthernetCat5 or ComponentType.EthernetCat5e or ComponentType.EthernetCat6 or ComponentType.EthernetCat6a or ComponentType.EthernetCat7 or ComponentType.EthernetCat8 => ComponentCategory.Ethernet,
            _ when IsReadyCable(type) => ComponentCategory.ReadyCables,
            _ when IsRechargeableDevice(type) => ComponentCategory.RechargeableDevices,
            _ => ComponentCategory.PassiveComponents
        };
    }

    public static bool IsPowerAdapter(ComponentType type)
        => type is ComponentType.Adapter5V2AUsbA or ComponentType.Adapter5V2AUsbC or ComponentType.AdapterFast18WUsbA or ComponentType.AdapterFast20WUsbC or ComponentType.AdapterFast65WUsbC;

    public static bool IsEthernetCable(ComponentType type)
        => type is ComponentType.EthernetCat5 or ComponentType.EthernetCat5e or ComponentType.EthernetCat6 or ComponentType.EthernetCat6a or ComponentType.EthernetCat7 or ComponentType.EthernetCat8;

    public static bool IsReadyCable(ComponentType type)
        => type is ComponentType.UsbAToLightningCable or ComponentType.UsbCToUsbCCable or ComponentType.UsbAToUsbACable or
            ComponentType.UsbBToUsbBCable or ComponentType.UsbAToUsbBCable or ComponentType.UsbAToUsbCCable or
            ComponentType.UsbAToUsbBSuperSpeedCable or ComponentType.UsbCToLightningCable;

    public static bool IsRechargeableDevice(ComponentType type)
        => type is ComponentType.DeviceUsbA or ComponentType.DeviceUsbB or ComponentType.DeviceUsbC or ComponentType.DeviceLightning;

    public static string[] CableSignals(ComponentType type) => type switch
    {
        ComponentType.UsbAToLightningCable or ComponentType.UsbCToLightningCable => ["VBUS", "GND"],
        ComponentType.UsbCToUsbCCable => [
            "VBUS", "GND", "CC", "D+", "D-",
            "TX1+", "TX1-", "TX2+", "TX2-", "RX1+", "RX1-", "RX2+", "RX2-",
            "SBU1", "SBU2", "SH"],
        ComponentType.UsbAToUsbBSuperSpeedCable => ["VBUS", "D-", "D+", "GND", "RX1+", "RX1-", "GND_DRAIN", "TX1+", "TX1-", "SH"],
        ComponentType.UsbAToUsbCCable => ["VBUS", "D-", "D+", "GND", "SH", "CC"],
        _ => ["VBUS", "D-", "D+", "GND", "SH"]
    };

    public static string[] DeviceSignals(ComponentType type)
        => type == ComponentType.DeviceUsbC ? ["VBUS", "GND", "CC"] : ["VBUS", "GND"];

    public static string ReadyCableName(ComponentType type) => type switch
    {
        ComponentType.UsbAToLightningCable => "USB-A to Lightning cable",
        ComponentType.UsbCToUsbCCable => "USB-C to USB-C cable",
        ComponentType.UsbAToUsbACable => "USB-A to USB-A cable",
        ComponentType.UsbBToUsbBCable => "USB-B to USB-B cable",
        ComponentType.UsbAToUsbBCable => "USB-A to USB-B cable",
        ComponentType.UsbAToUsbCCable => "USB-A to USB-C cable",
        ComponentType.UsbAToUsbBSuperSpeedCable => "USB-A to USB-B SuperSpeed cable",
        _ => "USB-C to Lightning cable"
    };

    public static string SignalColor(string signal) => signal switch
    {
        "VBUS" => "#E53935", "GND" => "#20262D", "GND_DRAIN" => "#59636C", "D+" => "#27AE60", "D-" => "#F2F2F2", "SH" => "#AAB2B8",
        "CC" => "#F2C94C", "SBU1" => "#8E44AD", "SBU2" => "#B565A7",
        "TX1+" or "TX1-" => "#F4A52C", "TX2+" or "TX2-" => "#63C174",
        "RX1+" or "RX1-" => "#3285D5", "RX2+" or "RX2-" => "#2DB7C4", _ => "#7F8C98"
    };

    public static string? GetPortSignal(ComponentModel component, string portId)
    {
        if (portId.StartsWith("L_") || portId.StartsWith("R_")) return portId[2..];
        if (portId is "VBUS" or "GND" or "D+" or "D-" or "SH") return portId;
        if (portId == "CC" && component.Type == ComponentType.DeviceUsbC) return "CC";
        if (portId is "PWR_VBUS" or "PWR_GND") return portId == "PWR_VBUS" ? "VBUS" : "GND";
        if (component.Type is ComponentType.LightningPlug or ComponentType.LightningReceptacle)
            return portId == "SH" ? "SH" : null; // Physical Lightning contacts are not assigned by this model.
        var connectorType = component.Type;
        var connectorPortId = portId;
        if (IsPowerAdapter(component.Type) && portId.StartsWith("USB_", StringComparison.Ordinal))
        {
            connectorType = component.Type is ComponentType.Adapter5V2AUsbA or ComponentType.AdapterFast18WUsbA
                ? ComponentType.UsbA2 : ComponentType.UsbCReceptacle;
            connectorPortId = portId[4..];
        }
        if (ConnectorCatalog.IsConnector(connectorType))
        {
            var raw = connectorPortId.StartsWith("M_", StringComparison.Ordinal) ? connectorPortId[2..] : connectorPortId;
            if (connectorType is ComponentType.UsbCPlug or ComponentType.UsbCReceptacle)
            {
                var cc2 = raw == "B5" && connectorType == ComponentType.UsbCReceptacle;
                if (raw == "SH") return "SH";
                if (raw is "A1" or "A12" or "B1" or "B12") return "GND";
                if (raw is "A4" or "A9" or "B4" or "B9") return "VBUS";
                if (raw == "A5") return "CC";
                if (cc2) return "CC2";
                if (raw is "A6" or "B6") return "D+";
                if (raw is "A7" or "B7") return "D-";
                return raw switch
                {
                    "A2" => "TX1+", "A3" => "TX1-", "B2" => "TX2+", "B3" => "TX2-",
                    "A10" => "RX2-", "A11" => "RX2+", "B10" => "RX1-", "B11" => "RX1+",
                    "A8" => "SBU1", "B8" => "SBU2",
                    "B5" => "VCONN", _ => null
                };
            }
            if (ConnectorCatalog.TryGet(connectorType, out var definition))
            {
                var pin = definition.Pins.FirstOrDefault(p => p.Id == raw);
                if (pin is null) return null;
                if (pin.Label.Contains("drain", StringComparison.OrdinalIgnoreCase)) return "GND_DRAIN";
                return pin.Signal switch
                {
                    "TX" => pin.Label.Contains("+", StringComparison.Ordinal) ? "TX1+" : "TX1-",
                    "RX" => pin.Label.Contains("+", StringComparison.Ordinal) ? "RX1+" : "RX1-",
                    "SBU" => pin.Id == "A8" ? "SBU1" : "SBU2",
                    "CC" => pin.Id == "B5" && connectorType == ComponentType.UsbCPlug ? "VCONN" : "CC",
                    "SHIELD" => "SH",
                    _ => pin.Signal is "VBUS" or "GND" or "D+" or "D-" ? pin.Signal : null
                };
            }
        }
        var port = GetPorts(component).FirstOrDefault(p => p.Id == portId);
        if (port is null) return null;
        var label = port.Label.ToUpperInvariant();
        if (label.Contains("VBUS") || label.Contains("+5 V")) return "VBUS";
        if (label.Contains("GND")) return "GND";
        if (label.Contains("D+")) return "D+";
        if (label.Contains("D−") || label.Contains("D-")) return "D-";
        if (label.Contains("SHIELD") || label.Contains("SHELL")) return "SH";
        return null;
    }

    private static void ConfigureAdapter(ComponentModel component, string name, string port, string voltage, string current, string watts, string profiles)
    {
        component.Name = name;
        component.Properties["Port"] = port;
        component.Properties["OutputVoltage"] = voltage;
        component.Properties["MaxCurrent"] = current;
        component.Properties["MaxPower"] = watts;
        component.Properties["Profiles"] = profiles;
        component.Properties["MinInputVoltage"] = "100";
        component.Properties["MaxInputVoltage"] = "240";
        component.Properties["InputFrequency"] = "50/60";
        component.Properties["OutputIsolation"] = "Isolated";
        component.Properties["ViewMode"] = "Electronics";
        if (port == "USB-C female")
            component.Properties["CCPullupOhms"] = double.TryParse(current, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var amps) && amps >= 3 ? "10000" : "22000";
    }

    public static Point GetAbsolutePortPosition(CircuitDocument document, PortReference port)
    {
        return Resolve(port, new HashSet<string>(StringComparer.Ordinal));

        Point Resolve(PortReference reference, HashSet<string> visitedWires)
        {
            var wire = document.Wires.FirstOrDefault(w => w.Id == reference.ComponentId && reference.PortId == "OPEN");
            if (wire is not null)
            {
                if (!visitedWires.Add(wire.Id)) return new Point();
                return wire.HasOpenEnd ? new Point(wire.OpenEndX, wire.OpenEndY) : Resolve(wire.B, visitedWires);
            }
            var component = document.Components.FirstOrDefault(c => c.Id == reference.ComponentId);
            if (component is null) return new Point();
            var definition = GetPorts(component).FirstOrDefault(p => p.Id == reference.PortId);
            return definition is null ? new Point(component.X, component.Y) : LocalToWorld(component, definition.Position);
        }
    }

    public static Point LocalToWorld(ComponentModel component, Point local)
    {
        var size = GetSize(component);
        var center = new Point(size.Width / 2, size.Height / 2);
        var radians = component.Rotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = local.X - center.X;
        var dy = local.Y - center.Y;
        return new Point(component.X + center.X + dx * cos - dy * sin,
            component.Y + center.Y + dx * sin + dy * cos);
    }

    public static Point WorldToLocal(ComponentModel component, Point world)
    {
        var size = GetSize(component);
        var center = new Point(size.Width / 2, size.Height / 2);
        var radians = -component.Rotation * Math.PI / 180.0;
        var cos = Math.Cos(radians);
        var sin = Math.Sin(radians);
        var dx = world.X - component.X - center.X;
        var dy = world.Y - component.Y - center.Y;
        return new Point(center.X + dx * cos - dy * sin, center.Y + dx * sin + dy * cos);
    }

    public static string GetPortLabel(CircuitDocument document, PortReference? port)
    {
        if (port is null) return "Not connected";
        var wire = document.Wires.FirstOrDefault(w => w.Id == port.ComponentId && port.PortId == "OPEN");
        if (wire is not null) return wire.HasOpenEnd ? "Open wire endpoint" : "Wire endpoint";
        var component = document.Components.FirstOrDefault(c => c.Id == port.ComponentId);
        if (component is null) return "Not connected";
        var definition = GetPorts(component).FirstOrDefault(p => p.Id == port.PortId);
        return definition is null ? "Not connected" : $"{component.Name} · {definition.Label}";
    }
}

public static class SampleCircuitFactory
{
    public static CircuitDocument Create()
    {
        var document = new CircuitDocument { Name = "LED starter circuit" };
        var arduino = ComponentCatalog.Create(ComponentType.ArduinoHeader, 45, 265);
        var breadboard = ComponentCatalog.Create(ComponentType.Breadboard, 350, 250);
        var resistor = ComponentCatalog.Create(ComponentType.Resistor, 505, 105);
        var led = ComponentCatalog.Create(ComponentType.Led, 735, 70);
        document.Components.AddRange([arduino, breadboard, resistor, led]);

        AddWire(document, arduino, "5V", breadboard, "a1", "#E53935");
        AddWire(document, breadboard, "e1", resistor, "A", "#E53935");
        AddWire(document, resistor, "B", led, "A", "#F4A52C");
        AddWire(document, led, "K", breadboard, "j4", "#20262D");
        AddWire(document, breadboard, "f4", arduino, "GND", "#20262D");

        document.Meter.BlackProbe = new PortReference { ComponentId = arduino.Id, PortId = "GND" };
        document.Meter.RedProbe = new PortReference { ComponentId = breadboard.Id, PortId = "e1" };
        return document;
    }

    private static void AddWire(CircuitDocument document, ComponentModel a, string aPort, ComponentModel b, string bPort, string color)
    {
        document.Wires.Add(new WireModel
        {
            A = new PortReference { ComponentId = a.Id, PortId = aPort },
            B = new PortReference { ComponentId = b.Id, PortId = bPort },
            Color = color
        });
    }
}
