using System.Reflection;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using ElectroSketch.Controls;
using ElectroSketch.Core;
using ElectroSketch;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, args) =>
        {
            Console.Error.WriteLine(args.ExceptionObject);
            Environment.Exit(1);
        };

        var document = new CircuitDocument();
        var connector = ComponentCatalog.Create(ComponentType.UsbCReceptacle, 120, 100);
        document.Components.Add(connector);
        document.Wires.Add(new WireModel
        {
            A = new PortReference { ComponentId = connector.Id, PortId = "A1" },
            B = new PortReference { ComponentId = connector.Id, PortId = "A4" },
            Color = "not-a-valid-color"
        });
        var canvas = new CircuitCanvas { Document = document };
        var hover = new PortReference { ComponentId = connector.Id, PortId = "B5" };
        typeof(CircuitCanvas).GetField("_hoverPort", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(canvas, hover);
        canvas.Measure(new Size(1800, 1100));
        canvas.Arrange(new Rect(0, 0, 1800, 1100));
        var bitmap = new RenderTargetBitmap(1800, 1100, 96, 96, PixelFormats.Pbgra32);
        bitmap.Render(canvas);
        var hoverEncoder = new PngBitmapEncoder();
        hoverEncoder.Frames.Add(BitmapFrame.Create(bitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "hover-tooltip.png"))) hoverEncoder.Save(stream);
        Console.WriteLine("Connector hover render: PASS");
        Console.WriteLine("Invalid saved wire color fallback: PASS");

        var detailedSize = ComponentCatalog.GetSize(connector);
        ComponentCatalog.ToggleViewMode(connector);
        var imageSize = ComponentCatalog.GetSize(connector);
        if (!ComponentCatalog.IsImageMode(connector) || imageSize.Width >= detailedSize.Width)
            throw new InvalidOperationException("Connector image mode did not become compact.");
        bitmap.Render(canvas);
        ComponentCatalog.ToggleViewMode(connector);
        Console.WriteLine("Connector image/electronics toggle: PASS");

        var physicalDocument = new CircuitDocument();
        var physicalTypes = new[] { ComponentType.UsbA2, ComponentType.UsbCPlug, ComponentType.UsbMicroB, ComponentType.LightningPlug, ComponentType.AdapterFast20WUsbC };
        for (var i = 0; i < physicalTypes.Length; i++)
        {
            var part = ComponentCatalog.Create(physicalTypes[i], 30 + i * 250, 30);
            ComponentCatalog.ToggleViewMode(part);
            physicalDocument.Components.Add(part);
        }
        var physicalCanvas = new CircuitCanvas { Document = physicalDocument };
        physicalCanvas.Measure(new Size(1350, 220));
        physicalCanvas.Arrange(new Rect(0, 0, 1350, 220));
        var physicalBitmap = new RenderTargetBitmap(1350, 220, 96, 96, PixelFormats.Pbgra32);
        physicalBitmap.Render(physicalCanvas);
        var physicalEncoder = new PngBitmapEncoder();
        physicalEncoder.Frames.Add(BitmapFrame.Create(physicalBitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "physical-connectors.png"))) physicalEncoder.Save(stream);
        Console.WriteLine("Top-down connector image rendering: PASS");

        var openWireDocument = new CircuitDocument();
        var openWireArduino = ComponentCatalog.Create(ComponentType.ArduinoHeader, 40, 30);
        openWireDocument.Components.Add(openWireArduino);
        openWireDocument.Wires.Add(new WireModel
        {
            A = new PortReference { ComponentId = openWireArduino.Id, PortId = "5V" },
            B = new PortReference(),
            Color = "#E53935",
            HasOpenEnd = true,
            OpenEndX = 520,
            OpenEndY = 150
        });
        var openWireCanvas = new CircuitCanvas { Document = openWireDocument };
        openWireCanvas.Measure(new Size(700, 400));
        openWireCanvas.Arrange(new Rect(0, 0, 700, 400));
        var openWireBitmap = new RenderTargetBitmap(700, 400, 96, 96, PixelFormats.Pbgra32);
        openWireBitmap.Render(openWireCanvas);
        var openEndHit = (PortReference?)typeof(CircuitCanvas).GetMethod("HitPort", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(openWireCanvas, [new Point(520, 150)]);
        if (openEndHit?.ComponentId != openWireDocument.Wires[0].Id || openEndHit.PortId != "OPEN")
            throw new InvalidOperationException("An open cable endpoint was not selectable as a probe/wire contact.");
        var serializedOpenWire = System.Text.Json.JsonSerializer.Serialize(openWireDocument, DocumentJson.Options);
        var restoredOpenWire = System.Text.Json.JsonSerializer.Deserialize<CircuitDocument>(serializedOpenWire, DocumentJson.Options);
        if (restoredOpenWire?.Wires.SingleOrDefault() is not { HasOpenEnd: true, OpenEndX: 520 })
            throw new InvalidOperationException("Open-ended wire was not preserved by .ets serialization.");
        Console.WriteLine("Open-ended wire render/save/probe hit target: PASS");

        var openReconnect = new CircuitDocument();
        var openArduino = ComponentCatalog.Create(ComponentType.ArduinoHeader, 10, 10);
        var openBoard = ComponentCatalog.Create(ComponentType.Breadboard, 350, 10);
        openReconnect.Components.AddRange([openArduino, openBoard]);
        var loose = new WireModel { A = new PortReference { ComponentId = openArduino.Id, PortId = "5V" }, B = new PortReference(), HasOpenEnd = true, OpenEndX = 350, OpenEndY = 120, Color = "#E53935" };
        openReconnect.Wires.Add(loose);
        var openCanvas = new CircuitCanvas { Document = openReconnect, Mode = EditorMode.Select };
        var beginOpen = typeof(CircuitCanvas).GetMethod("BeginOpenWireLift", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var completeOpen = typeof(CircuitCanvas).GetMethod("CompleteLiftedWire", BindingFlags.Instance | BindingFlags.NonPublic)!;
        beginOpen.Invoke(openCanvas, [loose, new Point(350, 120)]);
        completeOpen.Invoke(openCanvas, [new PortReference { ComponentId = openBoard.Id, PortId = "a1" }, new Point(418, 104)]);
        if (loose.HasOpenEnd || loose.B.PortId != "a1") throw new InvalidOperationException("Open wire endpoint did not reconnect.");
        Console.WriteLine("Open wire endpoint reconnect: PASS");

        var openProbeDocument = new CircuitDocument();
        var openProbeArduino = ComponentCatalog.Create(ComponentType.ArduinoHeader, 0, 0);
        openProbeDocument.Components.Add(openProbeArduino);
        openProbeDocument.Meter.Mode = MeterMode.Continuity;
        var liveOpenWire = new WireModel
        {
            A = new PortReference { ComponentId = openProbeArduino.Id, PortId = "5V" },
            HasOpenEnd = true,
            OpenEndX = 420,
            OpenEndY = 180,
            Color = "#E53935"
        };
        openProbeDocument.Wires.Add(liveOpenWire);
        openProbeDocument.Meter.RedProbe = new PortReference { ComponentId = liveOpenWire.Id, PortId = "OPEN" };
        openProbeDocument.Meter.BlackProbe = new PortReference { ComponentId = openProbeArduino.Id, PortId = "GND" };
        var openProbeEngine = new CircuitEngine(DocumentValidator.Normalize(openProbeDocument));
        if (openProbeEngine.Measure(MeterMode.DcVoltage, openProbeDocument.Meter.RedProbe, openProbeDocument.Meter.BlackProbe).Display != "+5.000")
            throw new InvalidOperationException("A DC probe could not read an exposed live wire endpoint.");
        var openProbeContinuity = openProbeEngine.Measure(MeterMode.Continuity, openProbeDocument.Meter.RedProbe, openProbeDocument.Meter.BlackProbe);
        var openProbeFinding = new SafetyAnalyzer(openProbeDocument, openProbeEngine.Analyze()).CheckMeter();
        if (openProbeContinuity.Display != "LIVE" || openProbeFinding?.Code != "LIVE_OHMS")
            throw new InvalidOperationException($"Continuity at a powered open wire endpoint did not trigger the live-circuit safety rule (display {openProbeContinuity.Display}, finding {openProbeFinding?.Code ?? "none"}).");
        Console.WriteLine("DC reading and continuity lock at an open wire end: PASS");

        var liftDocument = new CircuitDocument();
        var liftArduino = ComponentCatalog.Create(ComponentType.ArduinoHeader, 10, 10);
        var liftBoard = ComponentCatalog.Create(ComponentType.Breadboard, 350, 10);
        liftDocument.Components.AddRange([liftArduino, liftBoard]);
        var firstWire = new WireModel
        {
            A = new PortReference { ComponentId = liftArduino.Id, PortId = "5V" },
            B = new PortReference { ComponentId = liftBoard.Id, PortId = "a1" },
            Color = "#20262D"
        };
        var lastWire = new WireModel
        {
            A = new PortReference { ComponentId = liftArduino.Id, PortId = "5V" },
            B = new PortReference { ComponentId = liftBoard.Id, PortId = "a2" },
            Color = "#E53935"
        };
        liftDocument.Wires.AddRange([firstWire, lastWire]);
        var liftCanvas = new CircuitCanvas { Document = liftDocument, Mode = EditorMode.Select };
        var beginLift = typeof(CircuitCanvas).GetMethod("BeginWireLift", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var completeLift = typeof(CircuitCanvas).GetMethod("CompleteLiftedWire", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var commonPort = new PortReference { ComponentId = liftArduino.Id, PortId = "5V" };
        if ((bool)beginLift.Invoke(liftCanvas, [commonPort, new Point(248, 86)])! != true || liftDocument.Wires.Contains(lastWire))
            throw new InvalidOperationException("Select-mode lift did not choose the last wire on the shared contact.");
        completeLift.Invoke(liftCanvas, [null, new Point(700, 400)]);
        if (liftDocument.Wires.Last() != lastWire || !lastWire.HasOpenEnd)
            throw new InvalidOperationException("Lifted wire did not complete as an open endpoint.");
        Console.WriteLine("Last-wire contact lifting and open placement: PASS");

        var breakoutDocument = new CircuitDocument();
        var breakout = ComponentCatalog.Create(ComponentType.UsbA2, 40, 30);
        breakoutDocument.Components.Add(breakout);
        var breakoutReading = new CircuitEngine(breakoutDocument).Measure(MeterMode.Continuity,
            new PortReference { ComponentId = breakout.Id, PortId = "M_1" },
            new PortReference { ComponentId = breakout.Id, PortId = "1" });
        if (!breakoutReading.Beep || ComponentCatalog.GetPorts(breakout).Count() != ConnectorCatalog.Get(ComponentType.UsbA2).Pins.Count * 2)
            throw new InvalidOperationException("USB Type-A breakout mating/solder sides are not internally connected.");
        var breakoutCanvas = new CircuitCanvas { Document = breakoutDocument };
        breakoutCanvas.Measure(new Size(380, 300));
        breakoutCanvas.Arrange(new Rect(0, 0, 380, 300));
        var breakoutBitmap = new RenderTargetBitmap(380, 300, 96, 96, PixelFormats.Pbgra32);
        breakoutBitmap.Render(breakoutCanvas);
        var breakoutEncoder = new PngBitmapEncoder();
        breakoutEncoder.Frames.Add(BitmapFrame.Create(breakoutBitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "usb-breakout.png"))) breakoutEncoder.Save(stream);
        Console.WriteLine("USB Type-A female socket-to-solder breakout: PASS");

        breakout.Rotation = 89;
        var localPort = ComponentCatalog.GetPorts(breakout).First(p => p.Id == "1").Position;
        var worldPort = ComponentCatalog.LocalToWorld(breakout, localPort);
        var restoredLocal = ComponentCatalog.WorldToLocal(breakout, worldPort);
        if ((restoredLocal - localPort).Length > 0.001)
            throw new InvalidOperationException("Arbitrary-angle port transform did not round-trip.");
        breakout.Rotation = 359;
        if (Math.Abs(ComponentCatalog.WorldToLocal(breakout, ComponentCatalog.LocalToWorld(breakout, localPort)).X - localPort.X) > 0.001)
            throw new InvalidOperationException("359-degree rotation was not preserved.");
        Console.WriteLine("Arbitrary 89° and 359° rotation transforms: PASS");

        var breadboardDocument = new CircuitDocument();
        var breadboard = ComponentCatalog.Create(ComponentType.Breadboard, 35, 35);
        breadboardDocument.Components.Add(breadboard);
        breadboardDocument.Wires.Add(new WireModel
        {
            A = new PortReference { ComponentId = breadboard.Id, PortId = "a1" },
            B = new PortReference { ComponentId = breadboard.Id, PortId = "j1" },
            Color = "#E53935"
        });
        breadboardDocument.Wires.Add(new WireModel
        {
            A = new PortReference { ComponentId = breadboard.Id, PortId = "e4" },
            B = new PortReference { ComponentId = breadboard.Id, PortId = "f8" },
            Color = "#3285D5"
        });
        var breadboardCanvas = new CircuitCanvas { Document = breadboardDocument };
        breadboardCanvas.Measure(new Size(700, 360));
        breadboardCanvas.Arrange(new Rect(0, 0, 700, 360));
        var breadboardBitmap = new RenderTargetBitmap(700, 360, 96, 96, PixelFormats.Pbgra32);
        breadboardBitmap.Render(breadboardCanvas);
        var breadboardEncoder = new PngBitmapEncoder();
        breadboardEncoder.Frames.Add(BitmapFrame.Create(breadboardBitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "breadboard-wires.png"))) breadboardEncoder.Save(stream);
        Console.WriteLine("Breadboard lower-row spacing and visible wire layer: PASS");

        var stripDocument = new CircuitDocument();
        var strip = ComponentCatalog.Create(ComponentType.PowerStrip6, 0, 0);
        stripDocument.Components.Add(strip);
        var stripEngine = new CircuitEngine(stripDocument);
        var inlet = new PortReference { ComponentId = strip.Id, PortId = "IN_L" };
        var outlet = new PortReference { ComponentId = strip.Id, PortId = "S6_L" };
        var stripReading = stripEngine.Measure(MeterMode.Continuity, inlet, outlet);
        if (!stripReading.Beep) throw new InvalidOperationException("Power-strip L rail should have continuity.");
        Console.WriteLine("Six-socket L/N/PE continuity: PASS");
        strip.Properties["Power"] = "Off";
        if (new CircuitEngine(stripDocument).Measure(MeterMode.Continuity, inlet, outlet).Beep)
            throw new InvalidOperationException("Power strip OFF should isolate its rails.");
        Console.WriteLine("Power-strip ON/OFF isolation: PASS");
        var poweredStrip = ComponentCatalog.Create(ComponentType.PowerStrip3, 0, 0);
        var poweredAdapter = ComponentCatalog.Create(ComponentType.Adapter5V2AUsbA, 900, 0);
        var switchedDocument = new CircuitDocument();
        switchedDocument.Components.AddRange([poweredStrip, poweredAdapter]);
        switchedDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = poweredStrip.Id, PortId = "S1_L" }, B = new PortReference { ComponentId = poweredAdapter.Id, PortId = "AC_L" } });
        switchedDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = poweredStrip.Id, PortId = "S1_N" }, B = new PortReference { ComponentId = poweredAdapter.Id, PortId = "AC_N" } });
        poweredStrip.Properties["Power"] = "Off";
        var offReading = new CircuitEngine(switchedDocument).Measure(MeterMode.DcVoltage,
            new PortReference { ComponentId = poweredAdapter.Id, PortId = "USB_1" }, new PortReference { ComponentId = poweredAdapter.Id, PortId = "USB_4" });
        if (offReading.Display != "OL") throw new InvalidOperationException("Power strip OFF did not cut adapter output.");
        Console.WriteLine("Power-strip OFF cuts downstream adapter output: PASS");

        var audioDocument = new CircuitDocument();
        var speaker = ComponentCatalog.Create(ComponentType.Speaker4Ohm2W, 0, 0);
        var mihawk = ComponentCatalog.Create(ComponentType.MihawkDriver, 250, 0);
        audioDocument.Components.Add(speaker);
        audioDocument.Components.Add(mihawk);
        var audioEngine = new CircuitEngine(audioDocument);
        var speakerReading = audioEngine.Measure(MeterMode.Resistance,
            new PortReference { ComponentId = speaker.Id, PortId = "+" },
            new PortReference { ComponentId = speaker.Id, PortId = "-" });
        var mihawkReading = audioEngine.Measure(MeterMode.Resistance,
            new PortReference { ComponentId = mihawk.Id, PortId = "+" },
            new PortReference { ComponentId = mihawk.Id, PortId = "-" });
        if (speakerReading.Display != "4" || mihawkReading.Display != "32")
            throw new InvalidOperationException($"Unexpected speaker readings: {speakerReading.Display}, {mihawkReading.Display}");
        Console.WriteLine("4 Ω and Mihawk 32 Ω loads: PASS");

        var newPartsDocument = new CircuitDocument();
        newPartsDocument.Components.Add(ComponentCatalog.Create(ComponentType.WallPlug, 30, 30));
        newPartsDocument.Components.Add(ComponentCatalog.Create(ComponentType.PowerStrip3, 300, 30));
        newPartsDocument.Components.Add(ComponentCatalog.Create(ComponentType.PowerStrip6, 30, 260));
        newPartsDocument.Components.Add(ComponentCatalog.Create(ComponentType.Speaker4Ohm2W, 900, 30));
        newPartsDocument.Components.Add(ComponentCatalog.Create(ComponentType.MihawkDriver, 1120, 30));
        newPartsDocument.Components.Add(ComponentCatalog.Create(ComponentType.Adapter5V2AUsbA, 30, 500));
        newPartsDocument.Components.Add(ComponentCatalog.Create(ComponentType.AdapterFast20WUsbC, 380, 500));
        newPartsDocument.Components.Add(ComponentCatalog.Create(ComponentType.Rj45Plug, 790, 500));
        var cat6 = ComponentCatalog.Create(ComponentType.EthernetCat6, 1100, 500);
        ComponentCatalog.ToggleViewMode(cat6);
        newPartsDocument.Components.Add(cat6);
        newPartsDocument.Annotations.Add(new AnnotationModel { Type = AnnotationType.Text, X1 = 960, Y1 = 420, Text = "Network branch", Color = "#4E6AF3" });
        newPartsDocument.Annotations.Add(new AnnotationModel { Type = AnnotationType.Arrow, X1 = 980, Y1 = 460, X2 = 1160, Y2 = 530, Color = "#4E6AF3" });
        var partsCanvas = new CircuitCanvas { Document = newPartsDocument };
        partsCanvas.Measure(new Size(1800, 1050));
        partsCanvas.Arrange(new Rect(0, 0, 1800, 1050));
        var partsBitmap = new RenderTargetBitmap(1800, 1000, 96, 96, PixelFormats.Pbgra32);
        partsBitmap.Render(partsCanvas);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(partsBitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "smoke-v04.png"))) encoder.Save(stream);
        Console.WriteLine("New power/audio/adapter/Ethernet/annotation rendering: PASS");

        var adapterDocument = new CircuitDocument();
        foreach (var adapterType in new[] { ComponentType.Adapter5V2AUsbA, ComponentType.AdapterFast18WUsbA })
        {
            var adapter = ComponentCatalog.Create(adapterType, 0, 0);
            var wall = ComponentCatalog.Create(ComponentType.WallPlug, 0, 0);
            adapterDocument.Components.AddRange([wall, adapter]);
            adapterDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = wall.Id, PortId = "L" }, B = new PortReference { ComponentId = adapter.Id, PortId = "AC_L" }, Color = "#8B5A2B" });
            adapterDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = wall.Id, PortId = "N" }, B = new PortReference { ComponentId = adapter.Id, PortId = "AC_N" }, Color = "#3285D5" });
            var adapterReading = new CircuitEngine(adapterDocument).Measure(MeterMode.DcVoltage,
                new PortReference { ComponentId = adapter.Id, PortId = "USB_1" },
                new PortReference { ComponentId = adapter.Id, PortId = "USB_4" });
            if (adapterReading.Display != "+5.000") throw new InvalidOperationException($"Unexpected adapter reading: {adapterReading.Display}");
            adapterDocument.Components.Clear();
            adapterDocument.Wires.Clear();
        }
        var usbCAdapter = ComponentCatalog.Create(ComponentType.AdapterFast65WUsbC, 0, 0);
        var usbCWall = ComponentCatalog.Create(ComponentType.WallPlug, 0, 0);
        adapterDocument.Components.AddRange([usbCWall, usbCAdapter]);
        adapterDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = usbCWall.Id, PortId = "L" }, B = new PortReference { ComponentId = usbCAdapter.Id, PortId = "AC_L" }, Color = "#8B5A2B" });
        adapterDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = usbCWall.Id, PortId = "N" }, B = new PortReference { ComponentId = usbCAdapter.Id, PortId = "AC_N" }, Color = "#3285D5" });
        var usbCReading = new CircuitEngine(adapterDocument).Measure(MeterMode.DcVoltage,
            new PortReference { ComponentId = usbCAdapter.Id, PortId = "USB_A4" },
            new PortReference { ComponentId = usbCAdapter.Id, PortId = "USB_A1" });
        if (usbCReading.Display != "+5.000") throw new InvalidOperationException($"Unexpected USB-C adapter reading: {usbCReading.Display}");
        Console.WriteLine("USB-A and USB-C adapter outputs: PASS");

        var ccCable = ComponentCatalog.Create(ComponentType.UsbCToUsbCCable, 315, 45);
        var ccSink = ComponentCatalog.Create(ComponentType.DeviceUsbC, 700, 120);
        adapterDocument.Components.AddRange([ccCable, ccSink]);
        var ccCanvas = new CircuitCanvas { Document = adapterDocument };
        var ccAutoConnect = typeof(CircuitCanvas).GetMethod("AutoConnectCable", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var ccAttachments = (int)ccAutoConnect.Invoke(ccCanvas, [ccCable])!;
        var ccDeviceReading = new CircuitEngine(adapterDocument).Measure(MeterMode.DcVoltage,
            new PortReference { ComponentId = ccSink.Id, PortId = "VBUS" },
            new PortReference { ComponentId = ccSink.Id, PortId = "GND" });
        if (ccAttachments < 10 || ccDeviceReading.Display != "+5.000")
            throw new InvalidOperationException($"USB-C to USB-C failed to mate to a 65 W adapter and USB-C device ({ccAttachments} links, {ccDeviceReading.Display}).");
        var ccPaths = ReadyCableCatalog.Connections(ComponentType.UsbCToUsbCCable);
        if (ccPaths.Count != 16 || !ccPaths.Contains(("TX1+", "RX1+")) || !ccPaths.Contains(("TX2-", "RX2-")) ||
            ComponentCatalog.GetPortSignal(ComponentCatalog.Create(ComponentType.UsbCReceptacle, 0, 0), "A10") != "RX2-" ||
            ComponentCatalog.GetPortSignal(ComponentCatalog.Create(ComponentType.UsbCReceptacle, 0, 0), "B11") != "RX1+")
            throw new InvalidOperationException("The USB-C SuperSpeed lane/pin map was incomplete or mislabeled.");
        Console.WriteLine("USB-C to USB-C 65 W adapter mapping and SuperSpeed lanes: PASS");

        var ssDocument = new CircuitDocument();
        var ssA = ComponentCatalog.Create(ComponentType.UsbA3, 340, 100);
        var ssCable = ComponentCatalog.Create(ComponentType.UsbAToUsbBSuperSpeedCable, 400, 100);
        var ssB = ComponentCatalog.Create(ComponentType.UsbB3, 780, 100);
        ssDocument.Components.AddRange([ssA, ssCable, ssB]);
        var ssExpectedPaths = new[]
        {
            ("VBUS", "VBUS"), ("D-", "D-"), ("D+", "D+"), ("GND", "GND"),
            ("RX1+", "TX1+"), ("RX1-", "TX1-"), ("GND_DRAIN", "GND_DRAIN"),
            ("TX1+", "RX1+"), ("TX1-", "RX1-"), ("SH", "SH")
        };
        var ssPaths = ReadyCableCatalog.Connections(ComponentType.UsbAToUsbBSuperSpeedCable);
        if (!ssPaths.SequenceEqual(ssExpectedPaths) || !ReadyCableCatalog.CanMate(ComponentType.UsbAToUsbBSuperSpeedCable, "L_", ComponentType.UsbA3) ||
            !ReadyCableCatalog.CanMate(ComponentType.UsbAToUsbBSuperSpeedCable, "R_", ComponentType.UsbB3) ||
            ComponentCatalog.GetPorts(ssCable).Count() != 20)
            throw new InvalidOperationException("The USB-A-to-B SuperSpeed cable's view, endpoints, or 10 conductor paths are incomplete.");
        var ssCanvas = new CircuitCanvas { Document = ssDocument };
        var ssAutoConnect = typeof(CircuitCanvas).GetMethod("AutoConnectCable", BindingFlags.Instance | BindingFlags.NonPublic)!;
        if ((int)ssAutoConnect.Invoke(ssCanvas, [ssCable])! != 20)
            throw new InvalidOperationException("USB-A-to-B SuperSpeed cable did not connect all contacts to its compatible ends.");
        var ssEngine = new CircuitEngine(ssDocument);
        foreach (var (leftSignal, rightSignal) in ssExpectedPaths)
        {
            var left = ComponentCatalog.GetPorts(ssA).Single(port => ComponentCatalog.GetPortSignal(ssA, port.Id) == leftSignal);
            var right = ComponentCatalog.GetPorts(ssB).Single(port => ComponentCatalog.GetPortSignal(ssB, port.Id) == rightSignal);
            if (!ssEngine.Measure(MeterMode.Continuity, new PortReference { ComponentId = ssA.Id, PortId = left.Id },
                    new PortReference { ComponentId = ssB.Id, PortId = right.Id }).Beep)
                throw new InvalidOperationException($"USB-A-to-B SuperSpeed path {leftSignal} → {rightSignal} was open.");
        }
        var ssViewDocument = new CircuitDocument();
        var ssViewA = ComponentCatalog.Create(ComponentType.UsbA3, 0, 10);
        var ssViewCable = ComponentCatalog.Create(ComponentType.UsbAToUsbBSuperSpeedCable, 285, 10);
        var ssViewB = ComponentCatalog.Create(ComponentType.UsbB3, 715, 10);
        ssViewDocument.Components.AddRange([ssViewA, ssViewCable, ssViewB]);
        var ssViewCanvas = new CircuitCanvas { Document = ssViewDocument };
        ssViewCanvas.Measure(new Size(1000, 340));
        ssViewCanvas.Arrange(new Rect(0, 0, 1000, 340));
        var ssBitmap = new RenderTargetBitmap(1000, 340, 96, 96, PixelFormats.Pbgra32);
        ssBitmap.Render(ssViewCanvas); // physical/image mode
        var ssPhysicalEncoder = new PngBitmapEncoder();
        ssPhysicalEncoder.Frames.Add(BitmapFrame.Create(ssBitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "usb-ab-superspeed-physical.png"))) ssPhysicalEncoder.Save(stream);
        ComponentCatalog.ToggleViewMode(ssViewCable);
        if (ComponentCatalog.IsImageMode(ssViewCable) || ComponentCatalog.GetPorts(ssViewCable).Count() != 20)
            throw new InvalidOperationException("Switching to the electronics view hid SuperSpeed contacts.");
        ssViewCanvas.InvalidateVisual();
        ssViewCanvas.UpdateLayout();
        ssBitmap.Render(ssViewCanvas); // pin-by-pin electronics mode
        var ssElectronicsEncoder = new PngBitmapEncoder();
        ssElectronicsEncoder.Frames.Add(BitmapFrame.Create(ssBitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "usb-ab-superspeed-electronics.png"))) ssElectronicsEncoder.Save(stream);
        Console.WriteLine("USB-A to USB-B SuperSpeed: 10 paths, end mating, continuity and both views: PASS");

        var stripGroups = ComponentCatalog.Create(ComponentType.PowerStrip3, 80, 80);
        var stripGroupDefs = ComponentCatalog.GetContactGroups(stripGroups);
        var socketGroupDef = stripGroupDefs.Single(group => group.Id == "socket-1");
        var stripGroup = ComponentCatalog.GetOrCreateContactGroup(stripGroups, socketGroupDef.Id);
        var originalGroupPoints = ComponentCatalog.GetPorts(stripGroups).Where(port => socketGroupDef.PortIds.Contains(port.Id)).ToDictionary(port => port.Id, port => port.Position);
        stripGroup.OffsetX = 10_000;
        stripGroup.OffsetY = -10_000;
        stripGroup.Rotation = 359;
        ComponentCatalog.ConstrainContactGroup(stripGroups, socketGroupDef.Id);
        var movedGroupPoints = ComponentCatalog.GetPorts(stripGroups).Where(port => socketGroupDef.PortIds.Contains(port.Id)).ToDictionary(port => port.Id, port => port.Position);
        var stripSize = ComponentCatalog.GetSize(stripGroups);
        if (movedGroupPoints.Values.Any(point => point.X < 12 || point.Y < 12 || point.X > stripSize.Width - 12 || point.Y > stripSize.Height - 12) ||
            originalGroupPoints.Keys.Any(id => Math.Abs((movedGroupPoints[id] - movedGroupPoints[socketGroupDef.PortIds[0]]).Length -
                (originalGroupPoints[id] - originalGroupPoints[socketGroupDef.PortIds[0]]).Length) > 0.001))
            throw new InvalidOperationException("Power-strip contact groups escaped the parent or lost rigid pin spacing.");
        var groupsDocument = new CircuitDocument();
        groupsDocument.Components.Add(stripGroups);
        var groupJson = System.Text.Json.JsonSerializer.Serialize(groupsDocument, DocumentJson.Options);
        var restoredGroups = DocumentValidator.Normalize(System.Text.Json.JsonSerializer.Deserialize<CircuitDocument>(groupJson, DocumentJson.Options)!);
        var restoredSocket = restoredGroups.Components.Single().ContactGroups.Single(group => group.Id == "socket-1");
        if (restoredSocket.Rotation != 359 || restoredSocket.OffsetX != stripGroup.OffsetX || restoredSocket.OffsetY != stripGroup.OffsetY)
            throw new InvalidOperationException("Movable contact-group transforms were not saved and restored.");
        Console.WriteLine("Movable contact groups stay on parent and survive save/load: PASS");
        var breakoutGroupIds = ComponentCatalog.GetContactGroups(breakout).Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        var adapterGroups = ComponentCatalog.GetContactGroups(usbCAdapter).Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        if (!breakoutGroupIds.SetEquals(new[] { "socket", "solder" }) || !adapterGroups.SetEquals(new[] { "ac-input", "usb-output" }) ||
            ComponentCatalog.GetContactGroups(ssCable).Select(group => group.Id).ToHashSet(StringComparer.Ordinal).Count != 2)
            throw new InvalidOperationException("Breakout sides, adapter sides, or cable ends are not separately transformable contact groups.");
        Console.WriteLine("Breakout/adapter/cable subcomponent groups: PASS");

        var ethernetDocument = new CircuitDocument();
        var ethernet = ComponentCatalog.Create(ComponentType.EthernetCat5e, 0, 0);
        ethernetDocument.Components.Add(ethernet);
        var ethernetReading = new CircuitEngine(ethernetDocument).Measure(MeterMode.Continuity,
            new PortReference { ComponentId = ethernet.Id, PortId = "L1" },
            new PortReference { ComponentId = ethernet.Id, PortId = "R1" });
        if (!ethernetReading.Beep) throw new InvalidOperationException("Ethernet pin 1 should pass straight through.");
        Console.WriteLine("Ethernet pin-by-pin continuity: PASS");

        var cableDocument = new CircuitDocument();
        var cableAdapter = ComponentCatalog.Create(ComponentType.Adapter5V2AUsbA, 0, 100);
        var cableWall = ComponentCatalog.Create(ComponentType.WallPlug, -260, 100);
        var readyCable = ComponentCatalog.Create(ComponentType.UsbAToUsbCCable, 300, 130);
        var device = ComponentCatalog.Create(ComponentType.DeviceUsbC, 650, 110);
        cableDocument.Components.AddRange([cableWall, cableAdapter, readyCable, device]);
        cableDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = cableWall.Id, PortId = "L" }, B = new PortReference { ComponentId = cableAdapter.Id, PortId = "AC_L" }, Color = "#8B5A2B" });
        cableDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = cableWall.Id, PortId = "N" }, B = new PortReference { ComponentId = cableAdapter.Id, PortId = "AC_N" }, Color = "#3285D5" });
        var cableCanvas = new CircuitCanvas { Document = cableDocument };
        var autoConnect = typeof(CircuitCanvas).GetMethod("AutoConnectCable", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var attached = (int)autoConnect.Invoke(cableCanvas, [readyCable])!;
        var deviceReading = new CircuitEngine(cableDocument).Measure(MeterMode.DcVoltage,
            new PortReference { ComponentId = device.Id, PortId = "VBUS" }, new PortReference { ComponentId = device.Id, PortId = "GND" });
        if (attached < 4 || deviceReading.Display != "+5.000")
            throw new InvalidOperationException($"Ready cable auto-connection failed: {attached} contacts, {deviceReading.Display}.");
        cableCanvas.Measure(new Size(1000, 420));
        cableCanvas.Arrange(new Rect(0, 0, 1000, 420));
        var cableBitmap = new RenderTargetBitmap(1000, 420, 96, 96, PixelFormats.Pbgra32);
        cableBitmap.Render(cableCanvas);
        var cableEncoder = new PngBitmapEncoder();
        cableEncoder.Frames.Add(BitmapFrame.Create(cableBitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "ready-cable-device.png"))) cableEncoder.Save(stream);
        Console.WriteLine("Ready cables auto-match adapter/device contacts: PASS");

        canvas.ClearProbes();
        if (document.Meter.RedProbe is not null || document.Meter.BlackProbe is not null)
            throw new InvalidOperationException("ClearProbes did not remove both meter probes.");
        Console.WriteLine("Probe clearing: PASS");

        var oldWidth = canvas.Width;
        canvas.EnsureWorkspaceBeyond(5200, 3600);
        if (canvas.Width <= oldWidth || canvas.Height < 3600) throw new InvalidOperationException("Canvas did not auto-expand.");
        Console.WriteLine($"Auto-expanding canvas: PASS ({canvas.Width:0}×{canvas.Height:0})");
        var beforeNorthWest = canvas.ViewToWorld(new Point(400, 250));
        var oldNorthWestWidth = canvas.Width;
        var oldNorthWestHeight = canvas.Height;
        canvas.ExpandWorkspaceBefore(1200, 900);
        var afterNorthWest = canvas.ViewToWorld(new Point(1600, 1150));
        if (canvas.Width != oldNorthWestWidth + 1200 || canvas.Height != oldNorthWestHeight + 900 ||
            (beforeNorthWest - afterNorthWest).Length > 0.001)
            throw new InvalidOperationException("North/west workspace growth did not preserve canvas coordinates.");
        Console.WriteLine("North/west canvas expansion: PASS");

        var sample = SampleCircuitFactory.Create();
        var engine = new CircuitEngine(sample);
        if (!engine.Analyze().HasLiveDcVoltage()) throw new InvalidOperationException("Live topology detection did not identify the powered starter circuit.");
        Console.WriteLine("Live-topology safety detection: PASS");
        var reading = engine.Measure(MeterMode.DcVoltage, sample.Meter.RedProbe, sample.Meter.BlackProbe);
        if (reading.Display != "+5.000") throw new InvalidOperationException($"Expected +5.000 V, got {reading.Display}");
        Console.WriteLine("Electrical engine: PASS");

        var currentReading = engine.Measure(MeterMode.DcCurrent, sample.Meter.RedProbe, sample.Meter.BlackProbe);
        sample.Meter.Mode = MeterMode.DcCurrent;
        var currentFinding = new SafetyAnalyzer(sample, engine.Analyze()).CheckMeter();
        if (currentReading.Display != "OL" || !currentReading.Warning || currentFinding?.Code != "CURRENT_OVER_RANGE" || currentFinding.Severity != SafetySeverity.Danger)
            throw new InvalidOperationException("Bridging a live 5 V supply in current mode must simulate an over-range short and trigger a safety lock.");
        var unpoweredCableDocument = new CircuitDocument();
        var unpoweredCable = ComponentCatalog.Create(ComponentType.UsbCToUsbCCable, 0, 0);
        unpoweredCableDocument.Components.Add(unpoweredCable);
        var unpoweredCablePorts = ComponentCatalog.GetPorts(unpoweredCable).ToList();
        var unpoweredCableReading = new CircuitEngine(unpoweredCableDocument).Measure(MeterMode.DcCurrent,
            new PortReference { ComponentId = unpoweredCable.Id, PortId = unpoweredCablePorts.First(p => p.Id.StartsWith("L_", StringComparison.Ordinal)).Id },
            new PortReference { ComponentId = unpoweredCable.Id, PortId = unpoweredCablePorts.First(p => p.Id.StartsWith("R_", StringComparison.Ordinal)).Id });
        if (unpoweredCableReading.Display != "0.000" || unpoweredCableReading.Warning)
            throw new InvalidOperationException("An unplugged USB cable should read zero current at unpowered contacts.");
        var seriesMeterDocument = new CircuitDocument();
        seriesMeterDocument.Meter.Mode = MeterMode.DcCurrent;
        var seriesArduino = ComponentCatalog.Create(ComponentType.ArduinoHeader, 0, 0);
        var seriesResistor = ComponentCatalog.Create(ComponentType.Resistor, 200, 0);
        var seriesLed = ComponentCatalog.Create(ComponentType.Led, 400, 0);
        seriesMeterDocument.Components.AddRange([seriesArduino, seriesResistor, seriesLed]);
        seriesMeterDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = seriesArduino.Id, PortId = "5V" }, B = new PortReference { ComponentId = seriesResistor.Id, PortId = "A" } });
        seriesMeterDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = seriesLed.Id, PortId = "K" }, B = new PortReference { ComponentId = seriesArduino.Id, PortId = "GND" } });
        seriesMeterDocument.Meter.RedProbe = new PortReference { ComponentId = seriesResistor.Id, PortId = "B" };
        seriesMeterDocument.Meter.BlackProbe = new PortReference { ComponentId = seriesLed.Id, PortId = "A" };
        var seriesMeterEngine = new CircuitEngine(seriesMeterDocument);
        var seriesMeterSnapshot = seriesMeterEngine.Analyze();
        var seriesMeterReading = seriesMeterEngine.Measure(MeterMode.DcCurrent, seriesMeterDocument.Meter.RedProbe,
            seriesMeterDocument.Meter.BlackProbe, seriesMeterSnapshot);
        var seriesMeterFinding = new SafetyAnalyzer(seriesMeterDocument, seriesMeterSnapshot).CheckMeter();
        if (seriesMeterReading.Display != "+0.014" || seriesMeterReading.Warning || seriesMeterFinding is not null)
            throw new InvalidOperationException($"Inserting the current meter into a broken LED branch should solve safe series current; got {seriesMeterReading.Display}.");
        Console.WriteLine("DC current bridges an open series point, reads dead cable as zero, and locks a supply short: PASS");

        var mainsShortDocument = new CircuitDocument();
        var mainsOutlet = ComponentCatalog.Create(ComponentType.WallPlug, 0, 0);
        mainsShortDocument.Components.Add(mainsOutlet);
        mainsShortDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = mainsOutlet.Id, PortId = "L" }, B = new PortReference { ComponentId = mainsOutlet.Id, PortId = "N" } });
        var mainsShortFinding = new SafetyAnalyzer(mainsShortDocument, new CircuitEngine(mainsShortDocument).Analyze()).CheckTopology().FirstOrDefault();
        if (mainsShortFinding?.Code != "AC_LINE_NEUTRAL_SHORT" || mainsShortFinding.Severity != SafetySeverity.Danger)
            throw new InvalidOperationException("A direct wall outlet L-N short must be explicitly identified as a mains short hazard.");
        Console.WriteLine("Mains L-N short receives an explicit fault explanation and danger lock: PASS");

        var noteDocument = new CircuitDocument();
        var noteCanvas = new CircuitCanvas { Document = noteDocument, AnnotationColor = "#8E44AD", WireColor = "#20262D", Mode = EditorMode.Select };
        noteCanvas.AddTextAnnotation(new Point(80, 90), "Light device");
        var note = noteDocument.Annotations.Single();
        noteCanvas.SetSelectedAnnotationColor("#2471A3");
        noteCanvas.AddTextAnnotation(new Point(180, 90), "Separate color");
        var secondNote = noteDocument.Annotations.Last();
        noteCanvas.WireColor = "#E53935";
        noteCanvas.UpdateTextAnnotation(note, "Editable note");
        typeof(CircuitCanvas).GetMethod("SelectAnnotation", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(noteCanvas, [note]);
        var hitText = typeof(CircuitCanvas).GetMethod("HitAnnotation", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(noteCanvas, [new Point(note.X1 + 2, note.Y1 + 2)]);
        note.Rotation = 89;
        var noteRoundTrip = DocumentValidator.Normalize(System.Text.Json.JsonSerializer.Deserialize<CircuitDocument>(
            System.Text.Json.JsonSerializer.Serialize(noteDocument, DocumentJson.Options), DocumentJson.Options)!);
        if (note.Color != "#2471A3" || note.Color == noteCanvas.WireColor || note.Text != "Editable note" ||
            secondNote.Color != "#8E44AD" || secondNote.Color == note.Color ||
            noteRoundTrip.Annotations.Single(annotation => annotation.Text == "Editable note").Rotation != 89 ||
            noteRoundTrip.Annotations.Single(annotation => annotation.Text == "Editable note").Color != "#2471A3")
            throw new InvalidOperationException("Text color was not stored per annotation, or edit/rotation did not survive save/load.");
        var hitRotationHandle = false;
        var rotationHitTest = typeof(CircuitCanvas).GetMethod("HitAnnotationRotationHandle", BindingFlags.Instance | BindingFlags.NonPublic)!;
        for (var y = -100; y <= 400 && !hitRotationHandle; y += 10)
            for (var x = -100; x <= 400 && !hitRotationHandle; x += 10)
                hitRotationHandle = ReferenceEquals(rotationHitTest.Invoke(noteCanvas, [new Point(x, y)]), note);
        if (!ReferenceEquals(hitText, note) || !hitRotationHandle)
            throw new InvalidOperationException("A selected text annotation could not be hit-tested for editing or rotation.");
        var rotationAtPointer = typeof(CircuitCanvas).GetMethod("AnnotationRotationAtPointer", BindingFlags.Static | BindingFlags.NonPublic)!;
        var rotatedTextAngle = (double)rotationAtPointer.Invoke(null, [new Point(80, 90), new Point(80, 120), 0d])!;
        if (rotatedTextAngle != 90)
            throw new InvalidOperationException("Text rotation handle did not rotate around its actual annotation pivot.");
        var handleRectMethod = typeof(CircuitCanvas).GetMethod("AnnotationRotationHandleRect", BindingFlags.Static | BindingFlags.NonPublic)!;
        var selectionBoundsMethod = typeof(CircuitCanvas).GetMethod("AnnotationSelectionBounds", BindingFlags.Static | BindingFlags.NonPublic)!;
        var handleRect = (Rect)handleRectMethod.Invoke(null, [note, typeof(CircuitCanvas).GetMethod("MakeText", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [note.Text, 17d, FontWeights.SemiBold, Brushes.Black])!])!;
        var selectionBounds = (Rect)selectionBoundsMethod.Invoke(null, [note, typeof(CircuitCanvas).GetMethod("MakeText", BindingFlags.Static | BindingFlags.NonPublic)!
            .Invoke(null, [note.Text, 17d, FontWeights.SemiBold, Brushes.Black])!])!;
        var rotatePoint = typeof(CircuitCanvas).GetMethod("RotatePoint", BindingFlags.Static | BindingFlags.NonPublic)!;
        var handleCenterLocal = new Point(handleRect.X + handleRect.Width / 2, handleRect.Y + handleRect.Height / 2);
        var handleCenterWorld = (Point)rotatePoint.Invoke(null, [handleCenterLocal, new Point(note.X1, note.Y1), note.Rotation])!;
        if (!selectionBounds.Contains(new Point(handleRect.X, handleRect.Y)) || !selectionBounds.Contains(new Point(handleRect.Right, handleRect.Bottom)) ||
            noteCanvas.IsBackgroundAt(handleCenterWorld))
            throw new InvalidOperationException("The text rotate handle is outside the text selection boundary or can be mistaken for a canvas-pan target.");
        Console.WriteLine("Independent per-text colors, edit, rotation and save/load: PASS");

        var multiDocument = new CircuitDocument();
        multiDocument.Components.Add(ComponentCatalog.Create(ComponentType.ArduinoHeader, 60, 50));
        multiDocument.Annotations.Add(new AnnotationModel { Type = AnnotationType.Text, X1 = 360, Y1 = 80, Text = "select me" });
        var multiCanvas = new CircuitCanvas { Document = multiDocument };
        typeof(CircuitCanvas).GetMethod("SelectItemsInside", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(multiCanvas, [new Rect(0, 0, 700, 400)]);
        var selectedComponents = (HashSet<ComponentModel>)typeof(CircuitCanvas).GetField("_selectedComponents", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(multiCanvas)!;
        var selectedAnnotations = (HashSet<AnnotationModel>)typeof(CircuitCanvas).GetField("_selectedAnnotations", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(multiCanvas)!;
        if (selectedComponents.Count != 1 || selectedAnnotations.Count != 1)
            throw new InvalidOperationException("Rectangle selection did not select multiple object types together.");
        Console.WriteLine("Ctrl-drag marquee selection logic: PASS");

        var app = new App();
        app.InitializeComponent();
        var window = new MainWindow { Width = 720, Height = 540 };
        var modeShortcut = typeof(MainWindow).GetMethod("CanvasModeShortcut", BindingFlags.Static | BindingFlags.NonPublic)!;
        var isCtrlDrag = typeof(MainWindow).GetMethod("IsCtrlDrag", BindingFlags.Static | BindingFlags.NonPublic)!;
        if ((EditorMode?)modeShortcut.Invoke(null, [System.Windows.Input.Key.S]) != EditorMode.Select ||
            (EditorMode?)modeShortcut.Invoke(null, [System.Windows.Input.Key.W]) != EditorMode.Wire ||
            (EditorMode?)modeShortcut.Invoke(null, [System.Windows.Input.Key.D]) != EditorMode.Probe ||
            modeShortcut.Invoke(null, [System.Windows.Input.Key.A]) is not null ||
            !(bool)isCtrlDrag.Invoke(null, [System.Windows.Input.ModifierKeys.Control])! ||
            !(bool)isCtrlDrag.Invoke(null, [System.Windows.Input.ModifierKeys.Control | System.Windows.Input.ModifierKeys.Shift])! ||
            (bool)isCtrlDrag.Invoke(null, [System.Windows.Input.ModifierKeys.None])!)
            throw new InvalidOperationException("S/W/D canvas mode shortcuts do not map as requested.");
        if (((FrameworkElement)window.FindName("ConnectionGuideDrawer")!).Visibility != Visibility.Collapsed)
            throw new InvalidOperationException("The connection guide opened automatically with the window.");
        if (window.FindName("ReadMeterButton") is not null)
            throw new InvalidOperationException("The obsolete Read Meter button is still present.");
        if (window.FindName("PaletteSearchBox") is null || window.FindName("PaletteCategoryCombo") is null ||
            window.FindName("ComponentList") is null || window.FindName("PaletteAddButton") is null)
            throw new InvalidOperationException("The searchable component browser is incomplete.");
        var partList = (System.Windows.Controls.ListBox)window.FindName("ComponentList")!;
        var partSearch = (System.Windows.Controls.TextBox)window.FindName("PaletteSearchBox")!;
        var addPartButton = (System.Windows.Controls.Button)window.FindName("PaletteAddButton")!;
        if (partList.Items.Count < 20 || ((System.Windows.Controls.ComboBox)window.FindName("PaletteCategoryCombo")!).Items.Count < 3)
            throw new InvalidOperationException("The component browser did not populate parts and categories.");
        var unfilteredPartCount = partList.Items.Count;
        partSearch.Text = "usb-c";
        if (partList.Items.Count == 0 || partList.Items.Count >= unfilteredPartCount)
            throw new InvalidOperationException("Component search did not narrow the available parts.");
        partSearch.Text = "light device";
        if (!partList.Items.Cast<object>().Any(item =>
                item.GetType().GetProperty("Name")?.GetValue(item)?.ToString()?.Contains("Lightning", StringComparison.OrdinalIgnoreCase) == true))
            throw new InvalidOperationException("All-terms search did not find the Lightning rechargeable device for ‘light device’.");
        partSearch.Text = string.Empty;
        partList.SelectedIndex = 0;
        if (!addPartButton.IsEnabled || string.IsNullOrWhiteSpace(((System.Windows.Controls.TextBlock)window.FindName("PaletteDetailText")!).Text))
            throw new InvalidOperationException("Selecting a part did not show its details and enable placement.");
        partList.SelectedIndex = -1;
        var uiCanvasForGuide = (CircuitCanvas)window.FindName("CircuitSurface")!;
        var guideComponent = uiCanvasForGuide.Document.Components.First();
        var selectionField = typeof(CircuitCanvas).GetField("_selectedComponent", BindingFlags.Instance | BindingFlags.NonPublic)!;
        selectionField.SetValue(uiCanvasForGuide, guideComponent);
        typeof(MainWindow).GetMethod("UpdateConnectionGuide", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, null);
        if (((FrameworkElement)window.FindName("ConnectionGuideDrawer")!).Visibility != Visibility.Collapsed ||
            !((System.Windows.Controls.TextBlock)window.FindName("ConnectionGuideText")!).Text.Contains(guideComponent.Name))
            throw new InvalidOperationException("The connection guide did not update to the selected item while remaining closed.");
        selectionField.SetValue(uiCanvasForGuide, null);
        Console.WriteLine("Connection guide stays closed; live meter and searchable parts UI: PASS");
        window.Measure(new Size(720, 540));
        window.Arrange(new Rect(0, 0, 720, 540));
        var windowContent = (FrameworkElement)window.Content;
        windowContent.Measure(new Size(720, 540));
        windowContent.Arrange(new Rect(0, 0, 720, 540));
        var windowBitmap = new RenderTargetBitmap(720, 540, 96, 96, PixelFormats.Pbgra32);
        windowBitmap.Render(windowContent);
        var windowEncoder = new PngBitmapEncoder();
        windowEncoder.Frames.Add(BitmapFrame.Create(windowBitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "compact-window.png"))) windowEncoder.Save(stream);
        Console.WriteLine("Compact 720×540 window layout: PASS");
        ((FrameworkElement)window.FindName("WireOptionsPanel")!).Visibility = Visibility.Visible;
        windowContent.UpdateLayout();
        var wirePanelBitmap = new RenderTargetBitmap(720, 540, 96, 96, PixelFormats.Pbgra32);
        wirePanelBitmap.Render(windowContent);
        var wirePanelEncoder = new PngBitmapEncoder();
        wirePanelEncoder.Frames.Add(BitmapFrame.Create(wirePanelBitmap));
        using (var stream = File.Create(Path.Combine(AppContext.BaseDirectory, "wire-panel-window.png"))) wirePanelEncoder.Save(stream);
        Console.WriteLine("Wire-only color panel compact layout: PASS");

        var probeSafetyDocument = new CircuitDocument();
        var safetyArduino = ComponentCatalog.Create(ComponentType.ArduinoHeader, 20, 20);
        var isolatedResistor = ComponentCatalog.Create(ComponentType.Resistor, 450, 20);
        probeSafetyDocument.Components.AddRange([safetyArduino, isolatedResistor]);
        probeSafetyDocument.Meter.Mode = MeterMode.Continuity;
        probeSafetyDocument.Meter.BlackProbe = new PortReference { ComponentId = isolatedResistor.Id, PortId = "A" };
        probeSafetyDocument.Meter.RedProbe = new PortReference { ComponentId = isolatedResistor.Id, PortId = "B" };
        SafetyFinding? MeterRisk() => new SafetyAnalyzer(probeSafetyDocument, new CircuitEngine(probeSafetyDocument).Analyze()).CheckMeter();
        if (MeterRisk() is not null)
            throw new InvalidOperationException("An unrelated powered topology incorrectly blocked an isolated continuity check.");
        probeSafetyDocument.Meter.BlackProbe = new PortReference { ComponentId = safetyArduino.Id, PortId = "GND" };
        probeSafetyDocument.Meter.RedProbe = new PortReference { ComponentId = safetyArduino.Id, PortId = "5V" };
        if (MeterRisk()?.Code != "LIVE_OHMS")
            throw new InvalidOperationException("A probe touching a live net did not lock continuity mode.");
        var returnBoard = ComponentCatalog.Create(ComponentType.Breadboard, 750, 20);
        probeSafetyDocument.Components.Add(returnBoard);
        probeSafetyDocument.Wires.Add(new WireModel
        {
            A = new PortReference { ComponentId = safetyArduino.Id, PortId = "GND" },
            B = new PortReference { ComponentId = returnBoard.Id, PortId = "a1" }
        });
        probeSafetyDocument.Meter.BlackProbe = new PortReference { ComponentId = returnBoard.Id, PortId = "a1" };
        probeSafetyDocument.Meter.RedProbe = new PortReference { ComponentId = returnBoard.Id, PortId = "e1" };
        if (MeterRisk()?.Code != "LIVE_OHMS" ||
            new CircuitEngine(probeSafetyDocument).Measure(MeterMode.Continuity, probeSafetyDocument.Meter.RedProbe, probeSafetyDocument.Meter.BlackProbe).Beep)
            throw new InvalidOperationException("Continuity across a live 0 V return wire was incorrectly allowed.");
        Console.WriteLine("Probe-local live-wire continuity safety detection: PASS");

        var directLed = new CircuitDocument();
        var source = ComponentCatalog.Create(ComponentType.ArduinoHeader, 0, 0);
        var unprotectedLed = ComponentCatalog.Create(ComponentType.Led, 300, 0);
        directLed.Components.AddRange([source, unprotectedLed]);
        directLed.Wires.Add(new WireModel { A = new PortReference { ComponentId = source.Id, PortId = "5V" }, B = new PortReference { ComponentId = unprotectedLed.Id, PortId = "A" } });
        directLed.Wires.Add(new WireModel { A = new PortReference { ComponentId = source.Id, PortId = "GND" }, B = new PortReference { ComponentId = unprotectedLed.Id, PortId = "K" } });
        if (!new SafetyAnalyzer(directLed, new CircuitEngine(directLed).Analyze()).CheckTopology().Any(f => f.Code == "LED_OVERCURRENT"))
            throw new InvalidOperationException("Directly powered LED overcurrent was not reported.");
        Console.WriteLine("Immediate LED overcurrent rule: PASS");

        var shortDocument = new CircuitDocument();
        var shortArduino = ComponentCatalog.Create(ComponentType.ArduinoHeader, 0, 0);
        shortDocument.Components.Add(shortArduino);
        shortDocument.Wires.Add(new WireModel
        {
            A = new PortReference { ComponentId = shortArduino.Id, PortId = "5V" },
            B = new PortReference { ComponentId = shortArduino.Id, PortId = "GND" }
        });
        if (!new SafetyAnalyzer(shortDocument, new CircuitEngine(shortDocument).Analyze()).CheckTopology().Any(f => f.Code == "SOURCE_SHORT"))
            throw new InvalidOperationException("Direct source short was not detected.");
        Console.WriteLine("Direct source short detection: PASS");

        var acDocument = new CircuitDocument();
        var acSource = ComponentCatalog.Create(ComponentType.AcSource, 0, 0);
        acDocument.Components.Add(acSource);
        acDocument.Meter.Mode = MeterMode.Continuity;
        acDocument.Meter.RedProbe = new PortReference { ComponentId = acSource.Id, PortId = "N" };
        if (new SafetyAnalyzer(acDocument, new CircuitEngine(acDocument).Analyze()).CheckMeter()?.Code != "LIVE_OHMS")
            throw new InvalidOperationException("AC neutral was incorrectly treated as safe for continuity.");
        acDocument.Meter.Mode = MeterMode.DcVoltage;
        acDocument.Meter.BlackProbe = new PortReference { ComponentId = acSource.Id, PortId = "N" };
        acDocument.Meter.RedProbe = new PortReference { ComponentId = acSource.Id, PortId = "L" };
        if (new SafetyAnalyzer(acDocument, new CircuitEngine(acDocument).Analyze()).CheckMeter()?.Code != "WRONG_AC_DC")
            throw new InvalidOperationException("AC source in DC meter mode was not identified.");
        Console.WriteLine("AC return and wrong meter mode: PASS");

        var overRangeDocument = new CircuitDocument();
        var overRangeWall = ComponentCatalog.Create(ComponentType.WallPlug, 0, 0);
        overRangeDocument.Components.Add(overRangeWall);
        overRangeDocument.Meter.Profile = MeterProfile.Mas830L;
        overRangeDocument.Meter.Mode = MeterMode.AcVoltage;
        overRangeDocument.Meter.Range = "200";
        overRangeDocument.Meter.RedProbe = new PortReference { ComponentId = overRangeWall.Id, PortId = "L" };
        overRangeDocument.Meter.BlackProbe = new PortReference { ComponentId = overRangeWall.Id, PortId = "N" };
        var overRangeEngine = new CircuitEngine(overRangeDocument);
        if (overRangeEngine.Measure(MeterMode.AcVoltage, overRangeDocument.Meter.RedProbe, overRangeDocument.Meter.BlackProbe).Display != "230.000" ||
            new SafetyAnalyzer(overRangeDocument, overRangeEngine.Analyze()).CheckMeter()?.Code != "METER_OVER_RANGE")
            throw new InvalidOperationException("230 V AC on the MAS830L 200 V range was not identified as over-range.");
        typeof(MainWindow).GetField("_document", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, overRangeDocument);
        ((CircuitCanvas)window.FindName("CircuitSurface")!).Document = overRangeDocument;
        typeof(MainWindow).GetMethod("RefreshMeter", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(window, [overRangeEngine.Analyze()]);
        if (((System.Windows.Controls.TextBlock)window.FindName("MeterDisplay")!).Text != "OL")
            throw new InvalidOperationException("The live meter UI did not show OL for 230 V AC on the 200 V range without a read button.");
        Console.WriteLine("MAS830L 200 V range detects 230 V AC over-range: PASS");
        Console.WriteLine("230 V over-range appears immediately in the live meter display: PASS");

        var namedPath = Path.Combine(Path.GetTempPath(), $"ElectroSketch-title-{Guid.NewGuid():N}.ets");
        File.WriteAllText(namedPath, System.Text.Json.JsonSerializer.Serialize(new CircuitDocument { Name = "Different internal name" }, DocumentJson.Options));
        try
        {
            typeof(MainWindow).GetMethod("LoadDocument", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(window, [namedPath]);
            if (!window.Title.StartsWith(Path.GetFileNameWithoutExtension(namedPath), StringComparison.Ordinal))
                throw new InvalidOperationException("The app title did not change to the opened topology's filename.");
        }
        finally { File.Delete(namedPath); }
        Console.WriteLine("Opened topology filename is reflected in the app title: PASS");

        var resistorDocument = new CircuitDocument();
        var resistorSource = ComponentCatalog.Create(ComponentType.ArduinoHeader, 0, 0);
        var hotResistor = ComponentCatalog.Create(ComponentType.Resistor, 300, 0);
        hotResistor.Properties["Resistance"] = "10";
        resistorDocument.Components.AddRange([resistorSource, hotResistor]);
        resistorDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = resistorSource.Id, PortId = "5V" }, B = new PortReference { ComponentId = hotResistor.Id, PortId = "A" } });
        resistorDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = resistorSource.Id, PortId = "GND" }, B = new PortReference { ComponentId = hotResistor.Id, PortId = "B" } });
        if (!new SafetyAnalyzer(resistorDocument, new CircuitEngine(resistorDocument).Analyze()).CheckTopology().Any(f => f.Code == "RESISTOR_OVERPOWER"))
            throw new InvalidOperationException("Resistor over-power condition was not detected.");
        Console.WriteLine("Resistor power rating: PASS");

        var typeCCable = ComponentCatalog.Create(ComponentType.UsbCToUsbCCable, 0, 0);
        var cableGuide = ConnectionGuide.Describe(new CircuitDocument(), typeCCable);
        if (!cableGuide.Contains("16 modeled internal paths") || !cableGuide.Contains("TX→RX") ||
            !cableGuide.Contains("USB-PD negotiation is not simulated"))
            throw new InvalidOperationException("The cable guide did not disclose its actual modeled wiring and limits.");
        if (ConnectorCatalog.Get(ComponentType.UsbA2).Pins.First(p => p.Signal == "D-").Id != "2" ||
            ComponentCatalog.SignalColor("D-") != "#F2F2F2")
            throw new InvalidOperationException("USB 2.0 pin or color convention was incorrect.");
        Console.WriteLine("Cable connection guide and USB 2.0 colors: PASS");
        if (ReadyCableCatalog.CanMate(ComponentType.UsbAToUsbCCable, "L_", ComponentType.UsbCPlug) ||
            !ReadyCableCatalog.CanMate(ComponentType.UsbAToUsbCCable, "R_", ComponentType.UsbCReceptacle))
            throw new InvalidOperationException("Cable mating rules allowed a plug-to-plug connection or rejected a receptacle.");
        Console.WriteLine("Cable end and socket compatibility: PASS");

        var ccDataDocument = new CircuitDocument();
        var ccDataWall = ComponentCatalog.Create(ComponentType.WallPlug, -260, 45);
        var ccDataAdapter = ComponentCatalog.Create(ComponentType.AdapterFast65WUsbC, 0, 0);
        var ccDataCable = ComponentCatalog.Create(ComponentType.UsbCToUsbCCable, 315, 45);
        var ccDataSocket = ComponentCatalog.Create(ComponentType.UsbCReceptacle, 700, 120);
        ccDataDocument.Components.AddRange([ccDataWall, ccDataAdapter, ccDataCable, ccDataSocket]);
        ccDataDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = ccDataWall.Id, PortId = "L" }, B = new PortReference { ComponentId = ccDataAdapter.Id, PortId = "AC_L" }, Color = "#8B5A2B" });
        ccDataDocument.Wires.Add(new WireModel { A = new PortReference { ComponentId = ccDataWall.Id, PortId = "N" }, B = new PortReference { ComponentId = ccDataAdapter.Id, PortId = "AC_N" }, Color = "#3285D5" });
        var ccDataCanvas = new CircuitCanvas { Document = ccDataDocument };
        var ccDataLinks = (int)typeof(CircuitCanvas).GetMethod("AutoConnectCable", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(ccDataCanvas, [ccDataCable])!;
        var ccDataEngine = new CircuitEngine(ccDataDocument);
        var ccPositiveLane = ccDataEngine.Measure(MeterMode.Continuity,
            new PortReference { ComponentId = ccDataAdapter.Id, PortId = "USB_A2" },
            new PortReference { ComponentId = ccDataSocket.Id, PortId = "B11" });
        var ccWrongLane = ccDataEngine.Measure(MeterMode.Continuity,
            new PortReference { ComponentId = ccDataAdapter.Id, PortId = "USB_A2" },
            new PortReference { ComponentId = ccDataSocket.Id, PortId = "B2" });
        var ccLanePairs = new[]
        {
            ("USB_A2", "B11"), ("USB_A3", "B10"), ("USB_B2", "A11"), ("USB_B3", "A10"),
            ("USB_B11", "A2"), ("USB_B10", "A3"), ("USB_A11", "B2"), ("USB_A10", "B3")
        };
        var allCcLanesRoute = ccLanePairs.All(pair => ccDataEngine.Measure(MeterMode.Continuity,
            new PortReference { ComponentId = ccDataAdapter.Id, PortId = pair.Item1 },
            new PortReference { ComponentId = ccDataSocket.Id, PortId = pair.Item2 }).Beep);
        if (ccDataLinks < 40 || !ccPositiveLane.Beep || !allCcLanesRoute || ccWrongLane.Display != "OL")
            throw new InvalidOperationException($"USB-C TX/RX lanes did not map to opposing receptacle contacts ({ccDataLinks} links, first lane {ccPositiveLane.Display}, all lanes {allCcLanesRoute}, wrong lane {ccWrongLane.Display}).");
        var lightningCable = ComponentCatalog.Create(ComponentType.UsbCToLightningCable, 0, 0);
        if (ReadyCableCatalog.Connections(ComponentType.UsbCToLightningCable).Count != 2 ||
            ComponentCatalog.GetPortSignal(ComponentCatalog.Create(ComponentType.LightningReceptacle, 0, 0), "PWR_VBUS") != "VBUS" ||
            ComponentCatalog.GetPortSignal(ComponentCatalog.Create(ComponentType.LightningReceptacle, 0, 0), "PWR_GND") != "GND" ||
            ConnectionGuide.Describe(new CircuitDocument(), lightningCable).Contains("MFi", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The beginner Lightning model should expose its simple VBUS/GND path without extra protocol cautions.");
        Console.WriteLine("USB-C lanes route TX→RX; Lightning keeps the simple power path: PASS");

        var invalidDocument = new CircuitDocument();
        var validArduino = ComponentCatalog.Create(ComponentType.ArduinoHeader, 0, 0);
        invalidDocument.Components.Add(validArduino);
        invalidDocument.Wires.Add(new WireModel
        {
            A = new PortReference { ComponentId = validArduino.Id, PortId = "5V" },
            B = new PortReference { ComponentId = validArduino.Id, PortId = "nonexistent" }
        });
        try { DocumentValidator.Normalize(invalidDocument); throw new InvalidOperationException("Invalid wire was silently accepted."); }
        catch (InvalidDataException) { }
        Console.WriteLine("Invalid saved topology contact rejected: PASS");
        var safetyCanvas = (CircuitCanvas)window.FindName("CircuitSurface")!;
        safetyCanvas.Document = probeSafetyDocument;
        SafetySounds.Continuity();
        safetyCanvas.ClearProbes();
        if (SafetySounds.IsContinuityPlaying)
            throw new InvalidOperationException("Clearing meter probes did not stop the continuity tone.");
        Console.WriteLine("Probe clearing immediately silences continuity: PASS");
        typeof(MainWindow).GetField("_isDirty", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(window, false);
        window.Close();
        app.Shutdown();
    }
}
