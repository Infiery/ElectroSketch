using System.Globalization;

namespace ElectroSketch.Core;

public sealed record MeterReading(string Display, string Unit, string Detail, bool Beep = false, bool Warning = false);

public sealed class CircuitSnapshot
{
    internal Dictionary<string, int> PortNets { get; init; } = [];
    internal Dictionary<int, double> Voltages { get; init; } = [];
    internal Dictionary<int, double> AcVoltages { get; init; } = [];
    internal List<PassiveEdge> PassiveEdges { get; init; } = [];
    internal HashSet<int> EnergizedNets { get; init; } = [];
    internal double? CurrentMeterAmps { get; init; }
    internal double? CurrentMeterAcAmps { get; init; }
    public bool HasShortCircuit { get; init; }
    public string? ShortCircuitDetail { get; init; }

    public bool HasLiveDcVoltage(double threshold = 0.5)
        => Voltages.Values.Any(value => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) >= threshold);

    public bool HasLiveAcVoltage(double threshold = 0.5)
        => AcVoltages.Values.Any(value => !double.IsNaN(value) && !double.IsInfinity(value) && Math.Abs(value) >= threshold);

    public double? VoltageAt(PortReference? port)
    {
        if (port is null || !PortNets.TryGetValue(Key(port), out var net) || !Voltages.TryGetValue(net, out var value) || double.IsNaN(value))
            return null;
        return value;
    }

    public double? AcVoltageAt(PortReference? port)
    {
        if (port is null || !PortNets.TryGetValue(Key(port), out var net) || !AcVoltages.TryGetValue(net, out var value) || double.IsNaN(value))
            return null;
        return value;
    }

    internal int? NetOf(PortReference? port)
        => port is not null && PortNets.TryGetValue(Key(port), out var net) ? net : null;

    public bool IsEnergized(PortReference? port)
        => NetOf(port) is { } net && EnergizedNets.Contains(net);

    internal static string Key(PortReference port) => $"{port.ComponentId}:{port.PortId}";
}

internal sealed record PassiveEdge(int A, int B, double Resistance, ComponentType Type, string ComponentId);

public sealed class CircuitEngine
{
    private readonly CircuitDocument _document;

    public CircuitEngine(CircuitDocument document) => _document = document;

    public CircuitSnapshot Analyze()
        => Analyze(_document.Meter.Mode == MeterMode.DcCurrent, _document.Meter.RedProbe, _document.Meter.BlackProbe);

    private CircuitSnapshot Analyze(bool insertCurrentMeter, PortReference? redProbe, PortReference? blackProbe)
    {
        var union = new UnionFind();
        foreach (var component in _document.Components)
        {
            foreach (var port in ComponentCatalog.GetPorts(component))
                union.Add(Key(component.Id, port.Id));
            foreach (var (a, b) in ComponentCatalog.GetInternalConnections(component))
                union.Union(Key(component.Id, a), Key(component.Id, b));
        }

        foreach (var wire in _document.Wires)
        {
            var endpoint = $"{wire.Id}:OPEN";
            union.Add(endpoint);
            if (wire.HasOpenEnd)
                union.Union(CircuitSnapshot.Key(wire.A), endpoint);
            else
            {
                union.Union(CircuitSnapshot.Key(wire.A), CircuitSnapshot.Key(wire.B));
                union.Union(CircuitSnapshot.Key(wire.B), endpoint);
            }
        }

        var roots = union.Items.Select(union.Find).Distinct().ToList();
        var rootToNet = roots.Select((root, index) => (root, index)).ToDictionary(x => x.root, x => x.index);
        var portNets = union.Items.ToDictionary(key => key, key => rootToNet[union.Find(key)]);
        var passive = new List<PassiveEdge>();

        foreach (var component in _document.Components)
        {
            if (component.Type is ComponentType.Resistor or ComponentType.Speaker4Ohm2W or ComponentType.MihawkDriver)
            {
                var resistance = ReadDouble(component, "Resistance", 220);
                var (a, b) = component.Type == ComponentType.Resistor ? ("A", "B") : ("+", "-");
                AddEdge(component, a, b, Math.Max(0.01, resistance), passive, portNets);
            }
            else if (component.Type == ComponentType.Led)
            {
                // A compact linear approximation gives useful voltages for a beginner-friendly simulator.
                AddEdge(component, "A", "K", 150, passive, portNets);
            }
            else if (ComponentCatalog.IsRechargeableDevice(component.Type))
            {
                if (component.Type == ComponentType.DeviceUsbC)
                    AddEdge(component, "CC", "GND", 5100, passive, portNets); // USB-C sink Rd attachment model.
                var charge = ReadDouble(component, "ChargePercent", 0);
                var working = component.Properties.TryGetValue("WorkMode", out var mode) && mode.Contains("Working", StringComparison.OrdinalIgnoreCase);
                if (charge < 100 || working)
                    AddEdge(component, "VBUS", "GND", 25, passive, portNets); // approximately 200 mA at 5 V
            }
            else if (component.Type == ComponentType.UsbAToUsbCCable)
                AddEdge(component, "L_VBUS", "R_CC", 56_000, passive, portNets); // Legacy-A to Type-C cable's 56 kΩ Rp termination.
        }

        const double meterShuntOhms = 0.1;
        int? meterRedNet = null;
        int? meterBlackNet = null;
        var meterInserted = false;
        if (insertCurrentMeter && redProbe is not null && blackProbe is not null &&
            portNets.TryGetValue(CircuitSnapshot.Key(redProbe), out var currentRedNet) &&
            portNets.TryGetValue(CircuitSnapshot.Key(blackProbe), out var currentBlackNet))
        {
            meterRedNet = currentRedNet;
            meterBlackNet = currentBlackNet;
            if (currentRedNet != currentBlackNet)
            {
                // The current-mode meter is a low-resistance bridge across the break
                // between the two selected conductor ends, as a real series ammeter is.
                passive.Add(new PassiveEdge(currentRedNet, currentBlackNet, meterShuntOhms, ComponentType.Resistor, "$CURRENT_METER"));
                meterInserted = true;
            }
        }

        var fixedVoltages = new Dictionary<int, double>();
        var fixedAcVoltages = new Dictionary<int, double>();
        var conflicts = new List<string>();
        foreach (var component in _document.Components.Where(c => c.Type == ComponentType.ArduinoHeader))
        {
            SetFixed(component, "GND", 0);
            SetFixed(component, "5V", 5);
            for (var pin = 2; pin <= 7; pin++)
                SetFixed(component, $"D{pin}", ReadDouble(component, $"D{pin}", 0));
        }

        foreach (var component in _document.Components.Where(c => c.Type == ComponentType.AcSource))
        {
            SetAcFixed(component, "N", 0);
            SetAcFixed(component, "L", ReadDouble(component, "RmsVoltage", 5));
        }

        foreach (var component in _document.Components.Where(c => c.Type == ComponentType.WallPlug))
        {
            SetAcFixed(component, "N", 0);
            SetAcFixed(component, "PE", 0);
            SetAcFixed(component, "L", ReadDouble(component, "RmsVoltage", 230));
        }

        foreach (var component in _document.Components.Where(c => ComponentCatalog.IsPowerAdapter(c.Type)))
        {
            // USB chargers have a regulated isolated output only while their AC
            // input is supplied through a connected wall outlet and switched-on strip.
            if (!AdapterHasValidAcInput(component)) continue;

            var voltage = ReadDouble(component, "OutputVoltage", 5);
            if (component.Type is ComponentType.Adapter5V2AUsbA or ComponentType.AdapterFast18WUsbA)
            {
                SetFixed(component, "USB_1", voltage);
                SetFixed(component, "USB_4", 0);
            }
            else
            {
                foreach (var pin in new[] { "USB_A4", "USB_A9", "USB_B4", "USB_B9" }) SetFixed(component, pin, voltage);
                foreach (var pin in new[] { "USB_A1", "USB_A12", "USB_B1", "USB_B12" }) SetFixed(component, pin, 0);
                if (component.Type is ComponentType.Adapter5V2AUsbC or ComponentType.AdapterFast20WUsbC or ComponentType.AdapterFast65WUsbC)
                    AddEdge(component, "USB_A4", "USB_A5", ReadDouble(component, "CCPullupOhms", 10_000), passive, portNets);
            }
        }

        var mainsNets = new HashSet<int>();
        foreach (var source in _document.Components.Where(c => (c.Type is ComponentType.AcSource or ComponentType.WallPlug) &&
                     ReadDouble(c, "RmsVoltage", c.Type == ComponentType.WallPlug ? 230 : 5) >= 30))
        {
            foreach (var terminal in source.Type == ComponentType.WallPlug ? new[] { "L", "N", "PE" } : new[] { "L", "N" })
                if (portNets.TryGetValue(Key(source.Id, terminal), out var net)) mainsNets.Add(net);
        }
        foreach (var net in fixedVoltages.Keys.Intersect(mainsNets))
            conflicts.Add("Mains AC wiring is joined to a low-voltage DC circuit");

        var voltages = SolveVoltages(rootToNet.Count, passive, fixedVoltages);
        var acVoltages = SolveVoltages(rootToNet.Count, passive, fixedAcVoltages);
        var meterCurrentAmps = meterRedNet is { } redNet && meterBlackNet is { } blackNet
            ? meterInserted ? CurrentBetween(voltages, redNet, blackNet, meterShuntOhms) : 0
            : (double?)null;
        var meterCurrentAcAmps = meterRedNet is { } acRedNet && meterBlackNet is { } acBlackNet
            ? meterInserted ? CurrentBetween(acVoltages, acRedNet, acBlackNet, meterShuntOhms) : 0
            : (double?)null;
        var energizedNets = new HashSet<int>(fixedVoltages.Keys.Concat(fixedAcVoltages.Keys));
        // An energized source's 0 V return is part of the live circuit as well. A
        // voltage-to-absolute-zero test would incorrectly approve continuity there.
        var energizedQueue = new Queue<int>(energizedNets);
        var passiveNeighbors = new Dictionary<int, List<int>>();
        foreach (var edge in passive)
        {
            if (!passiveNeighbors.TryGetValue(edge.A, out var a)) passiveNeighbors[edge.A] = a = [];
            if (!passiveNeighbors.TryGetValue(edge.B, out var b)) passiveNeighbors[edge.B] = b = [];
            a.Add(edge.B);
            b.Add(edge.A);
        }
        while (energizedQueue.Count > 0)
            if (passiveNeighbors.TryGetValue(energizedQueue.Dequeue(), out var neighbors))
                foreach (var next in neighbors)
                    if (energizedNets.Add(next)) energizedQueue.Enqueue(next);
        return new CircuitSnapshot
        {
            PortNets = portNets,
            Voltages = voltages,
            AcVoltages = acVoltages,
            CurrentMeterAmps = meterCurrentAmps,
            CurrentMeterAcAmps = meterCurrentAcAmps,
            PassiveEdges = passive,
            EnergizedNets = energizedNets,
            HasShortCircuit = conflicts.Count > 0,
            ShortCircuitDetail = conflicts.FirstOrDefault()
        };

        bool AdapterHasValidAcInput(ComponentModel adapter)
        {
            if (!portNets.TryGetValue(Key(adapter.Id, "AC_L"), out var liveNet) ||
                !portNets.TryGetValue(Key(adapter.Id, "AC_N"), out var neutralNet) ||
                !fixedAcVoltages.TryGetValue(liveNet, out var liveVoltage) ||
                !fixedAcVoltages.TryGetValue(neutralNet, out var neutralVoltage)) return false;

            var inputVoltage = Math.Abs(liveVoltage - neutralVoltage);
            var minimum = ReadDouble(adapter, "MinInputVoltage", 100);
            var maximum = ReadDouble(adapter, "MaxInputVoltage", 240);
            return inputVoltage >= minimum && inputVoltage <= maximum;
        }

        void SetFixed(ComponentModel component, string port, double value)
        {
            if (!portNets.TryGetValue(Key(component.Id, port), out var net)) return;
            if (fixedVoltages.TryGetValue(net, out var existing) && Math.Abs(existing - value) > 0.05)
                conflicts.Add($"{component.Name}: {port} is shorted to a {existing:0.##} V net");
            else
                fixedVoltages[net] = value;
        }


        void SetAcFixed(ComponentModel component, string port, double value)
        {
            if (!portNets.TryGetValue(Key(component.Id, port), out var net)) return;
            if (fixedAcVoltages.TryGetValue(net, out var existing) && Math.Abs(existing - value) > 0.05)
                conflicts.Add($"{component.Name}: {port} is shorted to a {existing:0.##} V AC net");
            else
                fixedAcVoltages[net] = value;
        }
    }

    public MeterReading Measure(MeterMode mode, PortReference? red, PortReference? black, CircuitSnapshot? currentSnapshot = null)
    {
        if (red is null || black is null)
            return new MeterReading("— — — —", "", "Place both the red and black probes on connection points.");

        var snapshot = mode == MeterMode.DcCurrent
            ? currentSnapshot?.CurrentMeterAmps is not null ? currentSnapshot : Analyze(true, red, black)
            : currentSnapshot ?? Analyze();
        if (snapshot.HasShortCircuit)
            return new MeterReading("SHORT", "!", snapshot.ShortCircuitDetail ?? "5 V and GND are connected together.", Warning: true);

        var redNet = snapshot.NetOf(red);
        var blackNet = snapshot.NetOf(black);
        if (redNet is null || blackNet is null)
            return new MeterReading("OL", "", "One of the probe points no longer exists.", Warning: true);

        if (mode is MeterMode.Continuity or MeterMode.Resistance or MeterMode.Diode)
        {
            if (snapshot.IsEnergized(red) || snapshot.IsEnergized(black))
                return new MeterReading("LIVE", "!", "A probe touches an energized circuit, including its 0 V return. Remove power and verify it is off before continuity, resistance, or diode testing.", Warning: true);
        }

        switch (mode)
        {
            case MeterMode.DcVoltage:
            {
                var redV = snapshot.VoltageAt(red);
                var blackV = snapshot.VoltageAt(black);
                if (redV is null || blackV is null)
                    return new MeterReading("OL", "V DC", "The probes are on a floating or unpowered part of the circuit.");
                var delta = redV.Value - blackV.Value;
                return new MeterReading(delta.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture), "V DC", $"Red is {Math.Abs(delta):0.###} V {(delta >= 0 ? "above" : "below")} black.");
            }

            case MeterMode.AcVoltage:
            {
                var redV = snapshot.AcVoltageAt(red);
                var blackV = snapshot.AcVoltageAt(black);
                if (redV is null || blackV is null)
                    return new MeterReading("0.000", "V AC", "No simulated AC source is connected across these probes.");
                var rms = Math.Abs(redV.Value - blackV.Value);
                return new MeterReading(rms.ToString("0.000", CultureInfo.InvariantCulture), "V AC", $"True RMS of the simulated resistive AC network: {rms:0.###} V.");
            }

            case MeterMode.DcCurrent:
            {
                var acCurrent = Math.Abs(snapshot.CurrentMeterAcAmps ?? 0);
                if (acCurrent > 0.0005)
                    return new MeterReading("MODE", "A DC", "These probes complete an AC circuit. DC current mode is the wrong function; use a suitable AC current instrument inserted in series. Do not use this simulation to work on mains.", Warning: true);
                var current = snapshot.CurrentMeterAmps ?? 0;
                var range = GetCurrentRange();
                if (Math.Abs(current) > range)
                    return new MeterReading("OL", "A DC", $"The simulated series current is {Math.Abs(current):0.000} A, above the selected {range:0.###} A range. The real meter fuse could open. Power down and choose a suitable fused range.", Warning: true);
                return new MeterReading(current.ToString("+0.000;-0.000;0.000", CultureInfo.InvariantCulture), "A DC",
                    Math.Abs(current) < 0.0005
                        ? "No current flows through the inserted meter link (unpowered circuit, open path, or no voltage difference)."
                        : $"Current flows from red to black through the meter's simulated 0.1 Ω shunt. The probes complete the broken series path; they must be placed at the two ends of a break, not across the supply or a component.");
            }

            case MeterMode.Continuity:
            {
                var resistance = EquivalentResistance(snapshot, redNet.Value, blackNet.Value, includeLed: false);
                if (resistance is null || resistance > 50)
                    return new MeterReading("OL", "", "Open circuit — no low-resistance path between probes.");
                return new MeterReading(resistance < 0.1 ? "0.0" : resistance.Value.ToString("0.0", CultureInfo.InvariantCulture), "Ω  BEEP", "Continuity found. The path is below 50 Ω.", Beep: true);
            }

            case MeterMode.Resistance:
            {
                var resistance = EquivalentResistance(snapshot, redNet.Value, blackNet.Value, includeLed: false);
                if (resistance is null || resistance > 20_000_000)
                    return new MeterReading("OL", "Ω", "Open circuit or resistance is above the simulated range.");
                return FormatResistance(resistance.Value);
            }

            case MeterMode.Diode:
                return MeasureDiode(snapshot, redNet.Value, blackNet.Value);

            default:
                return new MeterReading("OL", "", "Unsupported meter mode.");
        }
    }

    private double GetCurrentRange()
    {
        if (double.TryParse(_document.Meter.Range, NumberStyles.Float, CultureInfo.InvariantCulture, out var range) && range > 0)
            return range;
        return 10;
    }

    private static double CurrentBetween(Dictionary<int, double> voltages, int redNet, int blackNet, double resistance)
    {
        if (!voltages.TryGetValue(redNet, out var redVoltage) || !voltages.TryGetValue(blackNet, out var blackVoltage) ||
            double.IsNaN(redVoltage) || double.IsNaN(blackVoltage) || double.IsInfinity(redVoltage) || double.IsInfinity(blackVoltage))
            return 0;
        return (redVoltage - blackVoltage) / resistance;
    }

    public bool IsLedLit(ComponentModel led, CircuitSnapshot? snapshot = null)
    {
        snapshot ??= Analyze();
        var anode = new PortReference { ComponentId = led.Id, PortId = "A" };
        var cathode = new PortReference { ComponentId = led.Id, PortId = "K" };
        var a = snapshot.VoltageAt(anode);
        var k = snapshot.VoltageAt(cathode);
        var threshold = ReadDouble(led, "ForwardVoltage", 1.8) * 0.82;
        return a is not null && k is not null && a - k >= threshold;
    }

    public IReadOnlyList<string> GetHealthMessages(CircuitSnapshot? snapshot = null)
    {
        snapshot ??= Analyze();
        if (snapshot.HasShortCircuit)
            return [$"⚠ Short circuit: {snapshot.ShortCircuitDetail}"];

        var messages = new List<string>();
        foreach (var led in _document.Components.Where(c => c.Type == ComponentType.Led))
        {
            var a = snapshot.VoltageAt(new PortReference { ComponentId = led.Id, PortId = "A" });
            var k = snapshot.VoltageAt(new PortReference { ComponentId = led.Id, PortId = "K" });
            if (a is not null && k is not null)
            {
                var delta = a.Value - k.Value;
                if (IsLedLit(led, snapshot))
                    messages.Add($"● {led.Name} is powered and glowing.");
            }
        }
        if (messages.Count == 0)
            messages.Add("No powered output detected by the simplified model.");
        return messages;
    }

    private MeterReading MeasureDiode(CircuitSnapshot snapshot, int redNet, int blackNet)
    {
        foreach (var led in _document.Components.Where(c => c.Type == ComponentType.Led))
        {
            var a = snapshot.NetOf(new PortReference { ComponentId = led.Id, PortId = "A" });
            var k = snapshot.NetOf(new PortReference { ComponentId = led.Id, PortId = "K" });
            if (a == redNet && k == blackNet)
            {
                var drop = ReadDouble(led, "ForwardVoltage", 1.8);
                return new MeterReading(drop.ToString("0.000", CultureInfo.InvariantCulture), "V", $"Forward-biased {led.Name}; junction looks healthy.");
            }
            if (a == blackNet && k == redNet)
                return new MeterReading("OL", "", $"{led.Name} blocks current in this direction. Swap the probes.");
        }
        return new MeterReading("OL", "", "No LED is directly connected between the probes.");
    }

    private static MeterReading FormatResistance(double resistance)
    {
        if (resistance >= 1_000_000)
            return new MeterReading((resistance / 1_000_000).ToString("0.###", CultureInfo.InvariantCulture), "MΩ", "Equivalent resistance between the probes.");
        if (resistance >= 1_000)
            return new MeterReading((resistance / 1_000).ToString("0.###", CultureInfo.InvariantCulture), "kΩ", "Equivalent resistance between the probes.");
        return new MeterReading(resistance.ToString("0.##", CultureInfo.InvariantCulture), "Ω", "Equivalent resistance between the probes.");
    }

    private static double? EquivalentResistance(CircuitSnapshot snapshot, int source, int ground, bool includeLed)
    {
        if (source == ground) return 0;
        var edges = snapshot.PassiveEdges.Where(e => includeLed || e.Type != ComponentType.Led).ToList();
        var reachable = new HashSet<int> { source };
        var queue = new Queue<int>();
        queue.Enqueue(source);
        while (queue.Count > 0)
        {
            var node = queue.Dequeue();
            foreach (var edge in edges.Where(e => e.A == node || e.B == node))
            {
                var next = edge.A == node ? edge.B : edge.A;
                if (reachable.Add(next)) queue.Enqueue(next);
            }
        }
        if (!reachable.Contains(ground)) return null;

        var unknown = reachable.Where(n => n != ground).ToList();
        var index = unknown.Select((net, i) => (net, i)).ToDictionary(x => x.net, x => x.i);
        var matrix = new double[unknown.Count, unknown.Count];
        var current = new double[unknown.Count];
        current[index[source]] = 1;
        foreach (var edge in edges.Where(e => reachable.Contains(e.A) && reachable.Contains(e.B)))
        {
            var g = 1.0 / Math.Max(edge.Resistance, 0.0001);
            if (edge.A != ground)
            {
                matrix[index[edge.A], index[edge.A]] += g;
                if (edge.B != ground) matrix[index[edge.A], index[edge.B]] -= g;
            }
            if (edge.B != ground)
            {
                matrix[index[edge.B], index[edge.B]] += g;
                if (edge.A != ground) matrix[index[edge.B], index[edge.A]] -= g;
            }
        }
        var solution = SolveLinear(matrix, current);
        return solution is null ? null : Math.Abs(solution[index[source]]);
    }

    private static Dictionary<int, double> SolveVoltages(int netCount, List<PassiveEdge> edges, Dictionary<int, double> fixedVoltages)
    {
        var result = Enumerable.Range(0, netCount).ToDictionary(i => i, _ => double.NaN);
        foreach (var pair in fixedVoltages) result[pair.Key] = pair.Value;

        var adjacency = Enumerable.Range(0, netCount).ToDictionary(i => i, _ => new List<int>());
        foreach (var edge in edges)
        {
            adjacency[edge.A].Add(edge.B);
            adjacency[edge.B].Add(edge.A);
        }

        var visited = new HashSet<int>();
        for (var start = 0; start < netCount; start++)
        {
            if (!visited.Add(start)) continue;
            var group = new List<int>();
            var queue = new Queue<int>();
            queue.Enqueue(start);
            while (queue.Count > 0)
            {
                var node = queue.Dequeue();
                group.Add(node);
                foreach (var next in adjacency[node]) if (visited.Add(next)) queue.Enqueue(next);
            }

            if (!group.Any(fixedVoltages.ContainsKey)) continue;
            var unknown = group.Where(n => !fixedVoltages.ContainsKey(n)).ToList();
            if (unknown.Count == 0) continue;
            var index = unknown.Select((net, i) => (net, i)).ToDictionary(x => x.net, x => x.i);
            var matrix = new double[unknown.Count, unknown.Count];
            var vector = new double[unknown.Count];
            foreach (var edge in edges.Where(e => group.Contains(e.A) && group.Contains(e.B)))
            {
                var g = 1.0 / Math.Max(edge.Resistance, 0.0001);
                Stamp(edge.A, edge.B, g);
                Stamp(edge.B, edge.A, g);
            }
            var solution = SolveLinear(matrix, vector);
            if (solution is null) continue;
            for (var i = 0; i < unknown.Count; i++) result[unknown[i]] = solution[i];

            void Stamp(int node, int other, double conductance)
            {
                if (!index.TryGetValue(node, out var row)) return;
                matrix[row, row] += conductance;
                if (index.TryGetValue(other, out var column))
                    matrix[row, column] -= conductance;
                else if (fixedVoltages.TryGetValue(other, out var fixedValue))
                    vector[row] += conductance * fixedValue;
            }
        }
        return result;
    }

    private static double[]? SolveLinear(double[,] matrix, double[] vector)
    {
        var n = vector.Length;
        if (n == 0) return [];
        var augmented = new double[n, n + 1];
        for (var row = 0; row < n; row++)
        {
            for (var col = 0; col < n; col++) augmented[row, col] = matrix[row, col];
            augmented[row, n] = vector[row];
        }
        for (var pivot = 0; pivot < n; pivot++)
        {
            var best = pivot;
            for (var row = pivot + 1; row < n; row++)
                if (Math.Abs(augmented[row, pivot]) > Math.Abs(augmented[best, pivot])) best = row;
            if (Math.Abs(augmented[best, pivot]) < 1e-12) return null;
            if (best != pivot)
                for (var col = pivot; col <= n; col++) (augmented[pivot, col], augmented[best, col]) = (augmented[best, col], augmented[pivot, col]);
            var divisor = augmented[pivot, pivot];
            for (var col = pivot; col <= n; col++) augmented[pivot, col] /= divisor;
            for (var row = 0; row < n; row++)
            {
                if (row == pivot) continue;
                var factor = augmented[row, pivot];
                for (var col = pivot; col <= n; col++) augmented[row, col] -= factor * augmented[pivot, col];
            }
        }
        return Enumerable.Range(0, n).Select(i => augmented[i, n]).ToArray();
    }

    private static void AddEdge(ComponentModel component, string a, string b, double resistance, List<PassiveEdge> edges, Dictionary<string, int> portNets)
    {
        if (portNets.TryGetValue(Key(component.Id, a), out var netA) && portNets.TryGetValue(Key(component.Id, b), out var netB) && netA != netB)
            edges.Add(new PassiveEdge(netA, netB, resistance, component.Type, component.Id));
    }

    private static double ReadDouble(ComponentModel component, string key, double fallback)
        => component.Properties.TryGetValue(key, out var text) && double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) ? value : fallback;

    private static string Key(string componentId, string portId) => $"{componentId}:{portId}";

    private sealed class UnionFind
    {
        private readonly Dictionary<string, string> _parent = new();
        public IEnumerable<string> Items => _parent.Keys;

        public void Add(string item)
        {
            if (!_parent.ContainsKey(item)) _parent[item] = item;
        }

        public string Find(string item)
        {
            Add(item);
            if (_parent[item] != item) _parent[item] = Find(_parent[item]);
            return _parent[item];
        }

        public void Union(string a, string b)
        {
            var rootA = Find(a);
            var rootB = Find(b);
            if (rootA != rootB) _parent[rootB] = rootA;
        }
    }
}
