#if SMOKE_TESTS
using System.Text.Json;
using ElectroSketch.Core;

var document = SampleCircuitFactory.Create();
var engine = new CircuitEngine(document);
var reading = engine.Measure(MeterMode.DcVoltage, document.Meter.RedProbe, document.Meter.BlackProbe);
if (reading.Display != "+5.000") throw new Exception($"Expected +5.000 V, got {reading.Display}");
var led = document.Components.Single(c => c.Type == ComponentType.Led);
if (!engine.IsLedLit(led)) throw new Exception("Starter LED should be lit.");
var json = JsonSerializer.Serialize(document, DocumentJson.Options);
var roundTrip = JsonSerializer.Deserialize<CircuitDocument>(json, DocumentJson.Options) ?? throw new Exception("Save round-trip failed.");
if (roundTrip.Components.Count != 4 || roundTrip.Wires.Count != 5) throw new Exception("Save round-trip changed circuit content.");
Console.WriteLine("Electrical engine: PASS");
Console.WriteLine("Starter voltage: " + reading.Display + " " + reading.Unit);
Console.WriteLine("Starter LED: ON");
Console.WriteLine("Save/load round-trip: PASS");
#endif
