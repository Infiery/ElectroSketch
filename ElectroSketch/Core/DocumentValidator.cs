using System.IO;

namespace ElectroSketch.Core;

public static class DocumentValidator
{
    public const long MaxFileBytes = 10 * 1024 * 1024;

    public static CircuitDocument Normalize(CircuitDocument document)
    {
        if (document.Version > 1) throw new InvalidDataException("This topology was saved by a newer, unsupported format version.");
        document.Name = string.IsNullOrWhiteSpace(document.Name) ? "Untitled topology" : document.Name;
        document.Components ??= [];
        document.Wires ??= [];
        document.Annotations ??= [];
        document.Meter ??= new MeterState();
        if (document.Components.Count > 2000 || document.Wires.Count > 10000 || document.Annotations.Count > 5000)
            throw new InvalidDataException("The topology exceeds ElectroSketch's supported size.");

        var portsByComponent = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        var usedIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var component in document.Components)
        {
            if (!Enum.IsDefined(component.Type)) throw new InvalidDataException("The topology contains an unknown component type.");
            component.Id = string.IsNullOrWhiteSpace(component.Id) ? Guid.NewGuid().ToString("N") : component.Id;
            if (!usedIds.Add(component.Id)) throw new InvalidDataException("The topology contains duplicate component identifiers.");
            component.Name = string.IsNullOrWhiteSpace(component.Name) ? component.Type.ToString() : component.Name;
            component.Properties ??= new Dictionary<string, string>();
            component.ContactGroups ??= [];
            if (component.ContactGroups.Count > 1000) throw new InvalidDataException("A component has too many movable contact groups.");
            var groupIds = new HashSet<string>(StringComparer.Ordinal);
            foreach (var group in component.ContactGroups)
            {
                if (group is null || string.IsNullOrWhiteSpace(group.Id) || !groupIds.Add(group.Id) ||
                    !double.IsFinite(group.OffsetX) || !double.IsFinite(group.OffsetY) || !double.IsFinite(group.Rotation))
                    throw new InvalidDataException($"{component.Name} has an invalid contact group transform.");
                if (Math.Abs(group.OffsetX) > 100_000 || Math.Abs(group.OffsetY) > 100_000)
                    throw new InvalidDataException($"{component.Name} has a contact group outside the supported canvas range.");
                group.Rotation = ((group.Rotation % 360) + 360) % 360;
                if (!ComponentCatalog.TryGetContactGroup(component, group.Id, out _))
                    throw new InvalidDataException($"{component.Name} has an unknown contact group.");
                ComponentCatalog.ConstrainContactGroup(component, group.Id);
            }
            if (!WithinCanvas(component.X) || !WithinCanvas(component.Y) || !double.IsFinite(component.Rotation))
                throw new InvalidDataException($"{component.Name} has an invalid location or rotation.");
            component.Rotation = ((component.Rotation % 360) + 360) % 360;
            portsByComponent.Add(component.Id, ComponentCatalog.GetPorts(component).Select(p => p.Id).ToHashSet(StringComparer.Ordinal));
        }

        foreach (var wire in document.Wires)
        {
            wire.Id = string.IsNullOrWhiteSpace(wire.Id) ? Guid.NewGuid().ToString("N") : wire.Id;
            if (!usedIds.Add(wire.Id)) throw new InvalidDataException("The topology contains duplicate component or wire identifiers.");
        }
        var wireEndpoints = document.Wires.Select(w => w.Id).ToHashSet(StringComparer.Ordinal);
        foreach (var wire in document.Wires)
        {
            if (!ValidPort(wire.A, portsByComponent, wireEndpoints) ||
                (!wire.HasOpenEnd && !ValidPort(wire.B, portsByComponent, wireEndpoints)) ||
                (wire.A.ComponentId == wire.Id && wire.A.PortId == "OPEN") ||
                (!wire.HasOpenEnd && wire.B.ComponentId == wire.Id && wire.B.PortId == "OPEN") ||
                (wire.HasOpenEnd && (!WithinCanvas(wire.OpenEndX) || !WithinCanvas(wire.OpenEndY))))
                throw new InvalidDataException("The topology contains a wire with a missing or invalid contact.");
            wire.Color = string.IsNullOrWhiteSpace(wire.Color) ? "#20262D" : wire.Color;
        }
        if (!ValidPort(document.Meter.RedProbe, portsByComponent, wireEndpoints)) document.Meter.RedProbe = null;
        if (!ValidPort(document.Meter.BlackProbe, portsByComponent, wireEndpoints)) document.Meter.BlackProbe = null;
        foreach (var annotation in document.Annotations)
        {
            if (!WithinCanvas(annotation.X1) || !WithinCanvas(annotation.Y1) ||
                !WithinCanvas(annotation.X2) || !WithinCanvas(annotation.Y2) || (annotation.Text?.Length ?? 0) > 10000)
                throw new InvalidDataException("The topology contains an invalid annotation.");
            annotation.Id = string.IsNullOrWhiteSpace(annotation.Id) ? Guid.NewGuid().ToString("N") : annotation.Id;
            annotation.Text ??= "";
            annotation.Color = string.IsNullOrWhiteSpace(annotation.Color) ? "#34495E" : annotation.Color;
            if (!double.IsFinite(annotation.Rotation)) throw new InvalidDataException("The topology contains an invalid text rotation.");
            annotation.Rotation = ((annotation.Rotation % 360) + 360) % 360;
        }
        return document;
    }

    private static bool WithinCanvas(double value) => double.IsFinite(value) && Math.Abs(value) <= 10_000_000;

    private static bool ValidPort(PortReference? port, Dictionary<string, HashSet<string>> ports, HashSet<string> wireEndpoints)
    {
        if (port is null) return false;
        if (ports.TryGetValue(port.ComponentId, out var valid)) return valid.Contains(port.PortId);
        return port.PortId == "OPEN" && wireEndpoints.Contains(port.ComponentId);
    }
}
