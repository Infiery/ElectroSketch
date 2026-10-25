namespace ElectroSketch.Core;

public static class ReadyCableCatalog
{
    public static (string Left, string Right) Ends(ComponentType type) => type switch
    {
        ComponentType.UsbAToLightningCable => ("USB-A", "Lightning"),
        ComponentType.UsbCToUsbCCable => ("USB-C", "USB-C"),
        ComponentType.UsbAToUsbACable => ("USB-A", "USB-A"),
        ComponentType.UsbBToUsbBCable => ("USB-B", "USB-B"),
        ComponentType.UsbAToUsbBCable => ("USB-A", "USB-B"),
        ComponentType.UsbAToUsbBSuperSpeedCable => ("USB-A", "USB-B"),
        ComponentType.UsbAToUsbCCable => ("USB-A", "USB-C"),
        ComponentType.UsbCToLightningCable => ("USB-C", "Lightning"),
        _ => throw new ArgumentOutOfRangeException(nameof(type), "Not a ready cable")
    };

    public static bool CanMate(ComponentType cableType, string prefix, ComponentType target)
    {
        if (!ComponentCatalog.IsReadyCable(cableType)) return false;
        var ends = Ends(cableType);
        var end = prefix == "L_" ? ends.Left : prefix == "R_" ? ends.Right : "";
        return end switch
        {
            "USB-A" => target is ComponentType.UsbA2 or ComponentType.UsbA3 or ComponentType.DeviceUsbA or ComponentType.Adapter5V2AUsbA or ComponentType.AdapterFast18WUsbA,
            "USB-B" => target is ComponentType.UsbB2 or ComponentType.UsbB3 or ComponentType.DeviceUsbB,
            "USB-C" => target is ComponentType.UsbCReceptacle or ComponentType.DeviceUsbC or
                ComponentType.Adapter5V2AUsbC or ComponentType.AdapterFast20WUsbC or ComponentType.AdapterFast65WUsbC,
            "Lightning" => target is ComponentType.LightningReceptacle or ComponentType.DeviceLightning,
            _ => false
        };
    }

    public static IReadOnlyList<(string Left, string Right)> Connections(ComponentType cableType)
    {
        var signals = ComponentCatalog.CableSignals(cableType);
        if (cableType == ComponentType.UsbCToUsbCCable)
        {
            return [
                ("VBUS", "VBUS"), ("GND", "GND"), ("CC", "CC"), ("D+", "D+"), ("D-", "D-"),
                ("TX1+", "RX1+"), ("TX1-", "RX1-"), ("TX2+", "RX2+"), ("TX2-", "RX2-"),
                ("RX1+", "TX1+"), ("RX1-", "TX1-"), ("RX2+", "TX2+"), ("RX2-", "TX2-"),
                ("SBU1", "SBU1"), ("SBU2", "SBU2"), ("SH", "SH")
            ];
        }
        if (cableType == ComponentType.UsbAToUsbBSuperSpeedCable)
        {
            return [
                ("VBUS", "VBUS"), ("D-", "D-"), ("D+", "D+"), ("GND", "GND"),
                ("RX1+", "TX1+"), ("RX1-", "TX1-"), ("GND_DRAIN", "GND_DRAIN"),
                ("TX1+", "RX1+"), ("TX1-", "RX1-"), ("SH", "SH")
            ];
        }
        if (cableType == ComponentType.UsbAToUsbCCable)
            return [("VBUS", "VBUS"), ("D-", "D-"), ("D+", "D+"), ("GND", "GND"), ("SH", "SH")];
        return signals.Select(signal => (signal, signal)).ToArray();
    }
}
