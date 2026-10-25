using System.Globalization;

namespace ElectroSketch.Core;

public enum SafetySeverity { Caution, Danger, Unknown }

public sealed record SafetyFinding(string Code, SafetySeverity Severity, string Message);

/// <summary>
/// Rules for the small subset of circuit behaviour that ElectroSketch actually models.
/// A missing finding is not a certificate that a physical build is safe.
/// </summary>
public sealed class SafetyAnalyzer
{
    private readonly CircuitDocument _document;
    private readonly CircuitSnapshot _snapshot;

    public SafetyAnalyzer(CircuitDocument document, CircuitSnapshot snapshot)
    {
        _document = document;
        _snapshot = snapshot;
    }

    public IReadOnlyList<SafetyFinding> CheckTopology()
    {
        var findings = new List<SafetyFinding>();
        var lineNeutralShort = _document.Components.FirstOrDefault(component =>
            (component.Type is ComponentType.WallPlug or ComponentType.AcSource) && SameNet(component, "L", "N"));
        if (lineNeutralShort is not null)
            findings.Add(new("AC_LINE_NEUTRAL_SHORT", SafetySeverity.Danger,
                $"{lineNeutralShort.Name}: L (live) and N (neutral) are directly connected. In a real outlet this is a mains short circuit: fault current can cause an arc, intense heat, fire, or a breaker/fuse trip. It is not a valid way to test the outlet. Disconnect power and remove the connection."));
        else if (_snapshot.HasShortCircuit)
            findings.Add(new("SOURCE_SHORT", SafetySeverity.Danger,
                $"Direct source conflict: {_snapshot.ShortCircuitDetail}. Disconnect power and inspect the wiring before continuing."));
        foreach (var component in _document.Components)
        {
            if (component.Type is ComponentType.AcSource or ComponentType.WallPlug or ComponentType.PowerStrip3 or ComponentType.PowerStrip6)
            {
                var live = component.Type switch
                {
                    ComponentType.AcSource or ComponentType.WallPlug => "L",
                    _ => "IN_L"
                };
                var earth = component.Type switch
                {
                    ComponentType.AcSource => null,
                    ComponentType.WallPlug => "PE",
                    _ => "IN_PE"
                };
                var neutral = component.Type is ComponentType.AcSource or ComponentType.WallPlug ? "N" : "IN_N";
                if (earth is not null && SameNet(component, live, earth))
                    findings.Add(new("LIVE_TO_EARTH", SafetySeverity.Danger,
                        $"{component.Name}: live and protective earth are directly connected. In real wiring this can cause electric shock or trip protection. Disconnect power and correct the connection."));
                if (earth is not null && SameNet(component, neutral, earth))
                    findings.Add(new("NEUTRAL_TO_EARTH", SafetySeverity.Danger,
                        $"{component.Name}: neutral and protective earth are joined in the modeled outlet or strip wiring. Keep these conductors separate at equipment connections."));
            }

            if (component.Type is ComponentType.UsbA2 or ComponentType.UsbB2 or ComponentType.UsbCPlug or ComponentType.UsbCReceptacle ||
                ComponentCatalog.IsPowerAdapter(component.Type) || ComponentCatalog.IsRechargeableDevice(component.Type))
            {
                var ports = ComponentCatalog.GetPorts(component).ToList();
                var grouped = ports.Select(port => (port.Id, Signal: ComponentCatalog.GetPortSignal(component, port.Id), Net: Net(component, port.Id)))
                    .Where(p => p.Net is not null).GroupBy(p => p.Net);
                foreach (var group in grouped)
                {
                    var signals = group.Select(p => p.Signal).ToHashSet(StringComparer.Ordinal);
                    if (signals.Contains("VBUS") && (signals.Contains("GND") || signals.Contains("D+") || signals.Contains("D-")))
                        findings.Add(new("USB_POWER_SIGNAL_SHORT", SafetySeverity.Danger,
                            $"{component.Name}: USB power shares a net with ground or a USB data contact. This may short a supply or damage a connected device."));
                    else if (signals.Contains("D+") && signals.Contains("D-"))
                        findings.Add(new("USB_DATA_SHORT", SafetySeverity.Caution,
                            $"{component.Name}: USB D+ and D− are connected together. Data transfer is unlikely to work."));
                }
            }

            if (component.Type is ComponentType.Speaker4Ohm2W or ComponentType.MihawkDriver)
            {
                var a = new PortReference { ComponentId = component.Id, PortId = "+" };
                var b = new PortReference { ComponentId = component.Id, PortId = "-" };
                var dc = Math.Abs((_snapshot.VoltageAt(a) ?? 0) - (_snapshot.VoltageAt(b) ?? 0));
                var ac = Math.Abs((_snapshot.AcVoltageAt(a) ?? 0) - (_snapshot.AcVoltageAt(b) ?? 0));
                var resistance = GetPositive(component, "Resistance", component.Type == ComponentType.Speaker4Ohm2W ? 4 : 32);
                var watts = (dc * dc + ac * ac) / resistance;
                var rating = GetPositive(component, "PowerWatts", component.Type == ComponentType.Speaker4Ohm2W ? 2 : double.PositiveInfinity);
                if (watts > rating)
                    findings.Add(new("LOAD_OVERPOWER", SafetySeverity.Danger,
                        $"{component.Name}: the simplified load model estimates {watts:0.##} W, above its {rating:0.##} W rating. Reduce the applied voltage or choose a correctly rated load."));
            }

            if (component.Type == ComponentType.Resistor)
            {
                var a = new PortReference { ComponentId = component.Id, PortId = "A" };
                var b = new PortReference { ComponentId = component.Id, PortId = "B" };
                var dc = Math.Abs((_snapshot.VoltageAt(a) ?? 0) - (_snapshot.VoltageAt(b) ?? 0));
                var ac = Math.Abs((_snapshot.AcVoltageAt(a) ?? 0) - (_snapshot.AcVoltageAt(b) ?? 0));
                var resistance = component.Properties.TryGetValue("Resistance", out var ohmsText) &&
                                 double.TryParse(ohmsText, NumberStyles.Float, CultureInfo.InvariantCulture, out var ohms) && ohms >= 0
                    ? Math.Max(0.01, ohms) : 220;
                var watts = (dc * dc + ac * ac) / resistance;
                var rating = GetPositive(component, "PowerWatts", 0.25);
                if (watts > rating)
                    findings.Add(new("RESISTOR_OVERPOWER", SafetySeverity.Danger,
                        $"{component.Name}: estimated dissipation is {watts:0.##} W, above the assumed {rating:0.##} W rating. Verify the actual resistor rating before applying power."));
            }

            if (component.Type == ComponentType.Led)
            {
                var a = new PortReference { ComponentId = component.Id, PortId = "A" };
                var k = new PortReference { ComponentId = component.Id, PortId = "K" };
                var voltage = (_snapshot.VoltageAt(a) ?? 0) - (_snapshot.VoltageAt(k) ?? 0);
                if (voltage / 150 > 0.02)
                    findings.Add(new("LED_OVERCURRENT", SafetySeverity.Danger,
                        $"{component.Name}: the simplified model estimates over 20 mA through the LED. Add an appropriate series current-limiting resistor."));
                if (voltage < -0.5)
                    findings.Add(new("LED_REVERSED", SafetySeverity.Caution,
                        $"{component.Name}: the LED is reverse biased; check its polarity."));
            }
        }

        foreach (var cable in _document.Components.Where(c =>
                     c.Type is ComponentType.UsbAToUsbACable or ComponentType.UsbBToUsbBCable))
        {
            var cableVbus = _snapshot.NetOf(new PortReference { ComponentId = cable.Id, PortId = "L_VBUS" });
            if (cableVbus is null) continue;
            var poweredSources = new HashSet<string>(StringComparer.Ordinal);
            foreach (var source in _document.Components)
            {
                var outputPorts = source.Type switch
                {
                    ComponentType.ArduinoHeader => new[] { "5V" },
                    ComponentType.Adapter5V2AUsbA or ComponentType.AdapterFast18WUsbA => new[] { "USB_1" },
                    ComponentType.Adapter5V2AUsbC or ComponentType.AdapterFast20WUsbC or ComponentType.AdapterFast65WUsbC =>
                        new[] { "USB_A4", "USB_A9", "USB_B4", "USB_B9" },
                    _ => Array.Empty<string>()
                };
                if (outputPorts.Any(port =>
                        _snapshot.NetOf(new PortReference { ComponentId = source.Id, PortId = port }) == cableVbus &&
                        _snapshot.VoltageAt(new PortReference { ComponentId = source.Id, PortId = port }) is > 0.5))
                    poweredSources.Add(source.Id);
            }
            if (poweredSources.Count > 1)
                findings.Add(new("USB_SOURCE_BACKFEED", SafetySeverity.Danger,
                    $"{cable.Name}: this connection joins {poweredSources.Count} powered 5 V outputs. Disconnect one source to prevent backfeeding."));
        }

        foreach (var adapter in _document.Components.Where(c => ComponentCatalog.IsPowerAdapter(c.Type)))
        {
            foreach (var input in new[] { "AC_L", "AC_N" })
            {
                var dcInput = _snapshot.VoltageAt(new PortReference { ComponentId = adapter.Id, PortId = input });
                if (dcInput is > 0.5)
                    findings.Add(new("DC_ON_ADAPTER_AC_INPUT", SafetySeverity.Danger,
                        $"{adapter.Name}: DC voltage is connected to its AC input. Move the supply to the USB output contacts."));
            }

            var positivePorts = ComponentCatalog.GetPorts(adapter).Where(p =>
                adapter.Type is ComponentType.Adapter5V2AUsbA or ComponentType.AdapterFast18WUsbA
                    ? p.Id == "USB_1"
                    : p.Id is "USB_A4" or "USB_A9" or "USB_B4" or "USB_B9");
            foreach (var output in positivePorts)
            {
                if (Net(adapter, output.Id) is not { } net || !_snapshot.Voltages.ContainsKey(net)) continue;
                var current = CurrentLeavingNet(net);
                var maximum = GetPositive(adapter, "MaxCurrent", 2);
                if (current > maximum * 1.01)
                    findings.Add(new("ADAPTER_OVERLOAD", SafetySeverity.Danger,
                        $"{adapter.Name}: the simplified load draws about {current:0.##} A, above its {maximum:0.##} A output rating. Reduce the load."));
            }
        }

        var acSources = _document.Components
            .Where(c => c.Type is ComponentType.AcSource or ComponentType.WallPlug)
            .Select(c => (Net: Net(c, "L"), Rms: GetPositive(c, "RmsVoltage", c.Type == ComponentType.WallPlug ? 230 : 5)))
            .Where(x => x.Net is not null).ToList();
        var mainsLiveNets = acSources.Where(source => source.Rms >= 30).Select(source => source.Net!.Value).ToHashSet();
        foreach (var wire in _document.Wires.Where(w => w.HasOpenEnd))
        {
            var endpointHasConnection = _document.Wires.Any(other => other.Id != wire.Id &&
                ((other.A.ComponentId == wire.Id && other.A.PortId == "OPEN") ||
                 (!other.HasOpenEnd && other.B.ComponentId == wire.Id && other.B.PortId == "OPEN")));
            if (endpointHasConnection) continue;
            if (NetOf(wire.A) is { } net && mainsLiveNets.Contains(net))
                findings.Add(new("EXPOSED_LIVE_WIRE", SafetySeverity.Danger,
                    "An open wire end is connected to a live mains conductor. Disconnect the outlet supply before changing the wiring."));
        }

        foreach (var component in _document.Components.Where(c => c.Type is not ComponentType.AcSource and not ComponentType.WallPlug and
                     not ComponentType.PowerStrip3 and not ComponentType.PowerStrip6))
        {
            var ports = ComponentCatalog.IsPowerAdapter(component.Type)
                ? ComponentCatalog.GetPorts(component).Where(p => p.Id.StartsWith("USB_", StringComparison.Ordinal))
                : ComponentCatalog.GetPorts(component);
            var acSensitive = component.Type is ComponentType.ArduinoHeader or ComponentType.Led ||
                              ConnectorCatalog.IsConnector(component.Type) || ComponentCatalog.IsRechargeableDevice(component.Type);
            if (acSources.Any(source =>
                    (source.Rms >= 30 || acSensitive) &&
                    ports.Any(p => Net(component, p.Id) == source.Net)))
            {
                findings.Add(new("AC_ON_LOW_VOLTAGE", SafetySeverity.Danger,
                    $"{component.Name}: mains voltage reaches a low-voltage contact. Disconnect power and correct the wiring."));
            }
        }

        return findings.DistinctBy(f => (f.Code, f.Message)).ToList();
    }

    public SafetyFinding? CheckMeter()
    {
        var meter = _document.Meter;
        var red = meter.RedProbe;
        var black = meter.BlackProbe;
        if ((meter.Mode is MeterMode.Continuity or MeterMode.Resistance or MeterMode.Diode) &&
            (_snapshot.IsEnergized(red) || _snapshot.IsEnergized(black)))
            return new("LIVE_OHMS", SafetySeverity.Danger,
                "A meter probe touches an energized circuit, including a 0 V return or continuous wire. Continuity, resistance and diode modes inject test current. Remove power, verify zero voltage with a suitable meter, then test.");

        if (red is null || black is null) return null;
        var redDc = _snapshot.VoltageAt(red);
        var blackDc = _snapshot.VoltageAt(black);
        var redAc = _snapshot.AcVoltageAt(red);
        var blackAc = _snapshot.AcVoltageAt(black);
        var dc = redDc is not null && blackDc is not null ? Math.Abs(redDc.Value - blackDc.Value) : 0;
        var ac = redAc is not null && blackAc is not null ? Math.Abs(redAc.Value - blackAc.Value) : 0;

        if (meter.Mode == MeterMode.DcCurrent)
        {
            var acAmps = Math.Abs(_snapshot.CurrentMeterAcAmps ?? 0);
            if (acAmps > 0.0005)
            {
                var acAcrossMeter = acAmps * 0.1;
                var severity = acAcrossMeter >= 30 ? SafetySeverity.Danger : SafetySeverity.Caution;
                return new("AC_CURRENT_MODE", severity,
                    acAcrossMeter >= 30
                        ? $"The current-mode meter bridge is across about {acAcrossMeter:0.#} V AC. That is a dangerous mains path and could cause severe shock, an arc, or meter damage. Do not connect an ammeter across an outlet; disconnect power and correct the topology."
                        : "The current-mode meter is completing an AC path, but this is a DC current function. Use an appropriate AC-current instrument connected in series.");
            }
            var amps = Math.Abs(_snapshot.CurrentMeterAmps ?? 0);
            var maxCurrent = double.TryParse(meter.Range, NumberStyles.Float, CultureInfo.InvariantCulture, out var currentRange) && currentRange > 0
                ? currentRange : 10;
            if (amps > maxCurrent)
                return new("CURRENT_OVER_RANGE", SafetySeverity.Danger,
                    $"The simulated meter link would carry {amps:0.###} A, above the selected {maxCurrent:0.###} A range. A real meter fuse could open or the meter/leads could be damaged. Power down; never bridge a supply in current mode.");
        }
        if (meter.Mode == MeterMode.DcVoltage && ac > 0.5 && dc < 0.5)
            return new("WRONG_AC_DC", SafetySeverity.Caution, "The probes span an AC source while the meter is in DC voltage mode. Select the AC function and a suitable range.");
        if (meter.Mode == MeterMode.AcVoltage && dc > 0.5 && ac < 0.5)
            return new("WRONG_AC_DC", SafetySeverity.Caution, "The probes span a DC source while the meter is in AC voltage mode. Select the DC function and a suitable range.");
        if (meter.Profile == MeterProfile.Mas830L &&
            double.TryParse(meter.Range, NumberStyles.Float, CultureInfo.InvariantCulture, out var range))
        {
            var measured = meter.Mode == MeterMode.DcVoltage ? dc : meter.Mode == MeterMode.AcVoltage ? ac : 0;
            if (measured > range)
                return new("METER_OVER_RANGE", SafetySeverity.Caution,
                    $"The expected {measured:0.###} V exceeds the selected {range:0.###} V range. Select a higher range before measuring. Over-range indication does not by itself prove meter damage.");
        }
        return null;
    }

    private int? Net(ComponentModel component, string portId)
        => _snapshot.NetOf(new PortReference { ComponentId = component.Id, PortId = portId });

    private bool SameNet(ComponentModel component, string first, string second)
        => Net(component, first) is { } a && Net(component, second) == a;

    private int? NetOf(PortReference port) => _snapshot.NetOf(port);

    private double CurrentLeavingNet(int net)
    {
        if (!_snapshot.Voltages.TryGetValue(net, out var voltage) || double.IsNaN(voltage) || double.IsInfinity(voltage))
            return 0;
        var total = 0d;
        foreach (var edge in _snapshot.PassiveEdges.Where(e => e.A == net || e.B == net))
        {
            var other = edge.A == net ? edge.B : edge.A;
            if (!_snapshot.Voltages.TryGetValue(other, out var otherVoltage) || double.IsNaN(otherVoltage) || double.IsInfinity(otherVoltage))
                continue;
            var current = (voltage - otherVoltage) / Math.Max(edge.Resistance, 0.0001);
            if (current > 0) total += current;
        }
        return total;
    }

    private static double GetPositive(ComponentModel component, string key, double fallback)
        => component.Properties.TryGetValue(key, out var text) &&
           double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && value > 0
            ? value : fallback;
}
