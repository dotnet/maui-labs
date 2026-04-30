// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Runtime.Versioning;

namespace Microsoft.Maui.Cli.Providers.Port;

[SupportedOSPlatform("windows")]
internal sealed class WindowsPortInspector : IPortInspector
{
private const int AF_INET = 2;
private const int AF_INET6 = 23;

private enum TcpTableClass
{
OwnerPidListener = 6,
}

[StructLayout(LayoutKind.Sequential)]
private struct MibTcpRowOwnerPid
{
public uint dwState;
public uint dwLocalAddr;
public uint dwLocalPort;
public uint dwRemoteAddr;
public uint dwRemotePort;
public uint dwOwningPid;
}

[StructLayout(LayoutKind.Sequential)]
private struct MibTcp6RowOwnerPid
{
[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
public byte[] ucLocalAddr;
public uint dwLocalScopeId;
public uint dwLocalPort;
[MarshalAs(UnmanagedType.ByValArray, SizeConst = 16)]
public byte[] ucRemoteAddr;
public uint dwRemoteScopeId;
public uint dwRemotePort;
public uint dwState;
public uint dwOwningPid;
}

[DllImport("iphlpapi.dll", SetLastError = true)]
private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref uint dwOutBufLen, bool sort, int ipVersion, TcpTableClass tblClass, uint reserved);

public IReadOnlyList<PortListenerInfo> GetListeners(int port)
{
var result = new List<PortListenerInfo>();
result.AddRange(GetListenersForFamily(port, AF_INET));
result.AddRange(GetListenersForFamily(port, AF_INET6));
return result;
}

private List<PortListenerInfo> GetListenersForFamily(int port, int afFamily)
{
var result = new List<PortListenerInfo>();
uint bufLen = 0;
GetExtendedTcpTable(IntPtr.Zero, ref bufLen, false, afFamily, TcpTableClass.OwnerPidListener, 0);
if (bufLen == 0) return result;

var buffer = Marshal.AllocHGlobal((int)bufLen);
try
{
if (GetExtendedTcpTable(buffer, ref bufLen, false, afFamily, TcpTableClass.OwnerPidListener, 0) != 0)
return result;

var count = Marshal.ReadInt32(buffer);
const int rowOffset = 4;

if (afFamily == AF_INET)
{
var rowSize = Marshal.SizeOf<MibTcpRowOwnerPid>();
for (int i = 0; i < count; i++)
{
var row = Marshal.PtrToStructure<MibTcpRowOwnerPid>(buffer + rowOffset + i * rowSize);
if (GetPortFromNetworkDword(row.dwLocalPort) != port) continue;

var addr = new System.Net.IPAddress(row.dwLocalAddr).ToString();
var processName = GetProcessName((int)row.dwOwningPid);
result.Add(new PortListenerInfo(port, (int)row.dwOwningPid, processName, addr, "ipv4"));
}
}
else
{
var rowSize = Marshal.SizeOf<MibTcp6RowOwnerPid>();
for (int i = 0; i < count; i++)
{
var row = Marshal.PtrToStructure<MibTcp6RowOwnerPid>(buffer + rowOffset + i * rowSize);
if (GetPortFromNetworkDword(row.dwLocalPort) != port) continue;

var addr = new System.Net.IPAddress(row.ucLocalAddr).ToString();
var processName = GetProcessName((int)row.dwOwningPid);
result.Add(new PortListenerInfo(port, (int)row.dwOwningPid, processName, addr, "ipv6"));
}
}
}
finally
{
Marshal.FreeHGlobal(buffer);
}
return result;
}

internal static int GetPortFromNetworkDword(uint networkDword)
=> PortInspectorHelpers.GetPortFromNetworkDword(networkDword);

private static string GetProcessName(int pid)
{
try { return Process.GetProcessById(pid).ProcessName; }
catch { return string.Empty; }
}
}