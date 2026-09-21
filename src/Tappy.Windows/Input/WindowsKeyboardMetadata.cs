using System.Runtime.InteropServices;
using System.Text;

namespace Tappy.Windows.Input;

internal sealed record KeyboardPnpNode(string InstanceId, Guid? ContainerId, string? FriendlyName, string? BusName);

/// <summary>Read-only, exact-interface PnP lookup. Names never authorize or merge input sources.</summary>
internal static class WindowsKeyboardMetadata
{
    private static readonly PropertyKey InstanceId = new("78C34FC8-104A-4ACA-9EA4-524D52996E57", 256);
    private static readonly PropertyKey ContainerId = new("8C7ED206-3F8A-4827-B3AB-AE9E1FAEFC6C", 2);
    private static readonly PropertyKey FriendlyName = new("A45C254E-DF1C-4EFD-8020-67D146A850E0", 14);
    private static readonly PropertyKey BusName = new("540B947E-8B40-45BC-A8A2-6A0B894CBDA2", 4);

    internal static WirelessKeyboardMetadata Read(string path, string? hidProduct)
    {
        var key = InstanceId;
        var data = new byte[4096];
        uint size = (uint)data.Length;
        if (CM_Get_Device_Interface_PropertyW(path, ref key, out var type, data, ref size, 0) != 0 || type != 18)
        { return WirelessKeyboardMetadata.Empty; }
        var id = Encoding.Unicode.GetString(data, 0, (int)size).TrimEnd('\0');
        if (CM_Locate_DevNodeW(out var node, id, 0) != 0) { return WirelessKeyboardMetadata.Empty; }
        var nodes = new List<KeyboardPnpNode>();
        for (var depth = 0; depth < 8; depth++)
        {
            var text = new StringBuilder(1024);
            if (CM_Get_Device_IDW(node, text, text.Capacity, 0) != 0) { break; }
            var container = ReadProperty(node, ContainerId, 13);
            nodes.Add(new(text.ToString(), container?.Length == 16 ? new Guid(container) : null,
                ReadString(node, FriendlyName), ReadString(node, BusName)));
            if (CM_Get_Parent(out var parent, node, 0) != 0) { break; }
            node = parent;
        }
        return Resolve(nodes, hidProduct, ReadPairedBluetoothDevices(nodes));
    }

    internal static WirelessKeyboardMetadata Resolve(IReadOnlyList<KeyboardPnpNode> nodes, string? hidProduct,
        IReadOnlyList<KeyboardPnpNode>? pairedDevices = null)
    {
        // Stop at the paired Bluetooth device, never borrow the host radio's name.
        var bluetooth = nodes.FirstOrDefault(n => IsBluetoothNode(n.InstanceId));
        if (bluetooth is null) { return WirelessKeyboardMetadata.Empty; }
        var paired = (pairedDevices ?? []).Where(n => IsPairedDeviceNode(n.InstanceId) &&
            IsPrivateContainer(bluetooth.ContainerId) && n.ContainerId == bluetooth.ContainerId).ToArray();
        var name = paired.Length == 1 ? Usable(paired[0].FriendlyName) ?? Usable(paired[0].BusName) : null;
        name ??= Usable(bluetooth.FriendlyName) ?? Usable(bluetooth.BusName) ?? Usable(hidProduct);
        if (name is null && bluetooth.ContainerId is { } container && container != Guid.Empty)
        {
            name = nodes.TakeWhile(n => n != bluetooth).Where(n => n.ContainerId == container)
                .Select(n => Usable(n.BusName) ?? Usable(n.FriendlyName)).FirstOrDefault(n => n is not null);
        }
        return new(true, name, []);
    }

    private static bool IsBluetoothNode(string id) => id.StartsWith("BTHENUM\\", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("BTHLEDEVICE\\", StringComparison.OrdinalIgnoreCase) || id.StartsWith("BTHLE\\", StringComparison.OrdinalIgnoreCase);
    private static bool IsPairedDeviceNode(string id) => id.StartsWith("BTHENUM\\DEV_", StringComparison.OrdinalIgnoreCase) ||
        id.StartsWith("BTHLE\\DEV_", StringComparison.OrdinalIgnoreCase);
    private static bool IsPrivateContainer(Guid? id) => id is { } value && value != Guid.Empty &&
        value != new Guid("00000000-0000-0000-FFFF-FFFFFFFFFFFF");

    private static IReadOnlyList<KeyboardPnpNode> ReadPairedBluetoothDevices(IReadOnlyList<KeyboardPnpNode> nodes)
    {
        var container = nodes.FirstOrDefault(n => IsBluetoothNode(n.InstanceId))?.ContainerId;
        if (!IsPrivateContainer(container)) { return []; }
        var devices = new List<KeyboardPnpNode>();
        foreach (var enumerator in new[] { "BTHENUM", "BTHLE" })
        {
            const uint flags = 0x101; // present devices from this enumerator only
            if (CM_Get_Device_ID_List_SizeW(out var length, enumerator, flags) != 0 || length is 0 or > 65536) { continue; }
            var ids = new char[length];
            if (CM_Get_Device_ID_ListW(enumerator, ids, length, flags) != 0) { continue; }
            foreach (var id in new string(ids).Split('\0', StringSplitOptions.RemoveEmptyEntries).Where(IsPairedDeviceNode))
            {
                if (CM_Locate_DevNodeW(out var node, id, 0) != 0) { continue; }
                var bytes = ReadProperty(node, ContainerId, 13);
                if (bytes?.Length != 16 || new Guid(bytes) != container) { continue; }
                devices.Add(new(id, container, ReadString(node, FriendlyName), ReadString(node, BusName)));
            }
        }
        return devices;
    }

    private static string? Usable(string? name)
    {
        name = KeyboardDeviceNames.CleanReportedName(name);
        return name is null || name.StartsWith("HID", StringComparison.OrdinalIgnoreCase) ||
            name is "Bluetooth LE Device" or "Bluetooth Device" or "USB Input Device" ? null : name;
    }

    private static string? ReadString(uint node, PropertyKey key) =>
        ReadProperty(node, key, 18) is { } bytes ? Encoding.Unicode.GetString(bytes).TrimEnd('\0') : null;

    private static byte[]? ReadProperty(uint node, PropertyKey key, uint expectedType)
    {
        var data = new byte[4096];
        uint size = (uint)data.Length;
        return CM_Get_DevNode_PropertyW(node, ref key, out var type, data, ref size, 0) == 0 &&
            type == expectedType && size <= data.Length ? data[..(int)size] : null;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct PropertyKey(string guid, uint id) { public Guid Format = new(guid); public uint Id = id; }
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_Interface_PropertyW(string path, ref PropertyKey key, out uint type, [Out] byte[] data, ref uint size, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Locate_DevNodeW(out uint node, string id, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_IDW(uint node, StringBuilder id, int length, uint flags);
    [DllImport("cfgmgr32.dll", ExactSpelling = true)]
    private static extern uint CM_Get_Parent(out uint parent, uint node, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_DevNode_PropertyW(uint node, ref PropertyKey key, out uint type, [Out] byte[] data, ref uint size, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_List_SizeW(out uint length, string filter, uint flags);
    [DllImport("cfgmgr32.dll", CharSet = CharSet.Unicode, ExactSpelling = true)]
    private static extern uint CM_Get_Device_ID_ListW(string filter, [Out] char[] ids, uint length, uint flags);
}
