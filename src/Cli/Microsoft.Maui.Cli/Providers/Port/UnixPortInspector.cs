// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.Versioning;

namespace Microsoft.Maui.Cli.Providers.Port;

[SupportedOSPlatform("macos")]
[SupportedOSPlatform("linux")]
internal sealed class UnixPortInspector : IPortInspector
{
public IReadOnlyList<PortListenerInfo> GetListeners(int port)
{
var listeners = TryLsof(port);
if (listeners is not null) return listeners;

if (OperatingSystem.IsLinux())
{
listeners = TrySs(port);
if (listeners is not null) return listeners;
}

if (OperatingSystem.IsMacOS())
{
listeners = TryNetstat(port);
if (listeners is not null) return listeners;
}

return FallbackIPGlobalProperties(port);
}

private static List<PortListenerInfo>? TryLsof(int port)
{
try
{
var output = RunCommand("lsof", $"-nP -iTCP:{port} -sTCP:LISTEN");
if (output is null) return null;
var result = ParseLsofOutput(output, port);
return result.Count > 0 ? result : null;
}
catch { return null; }
}

private static List<PortListenerInfo>? TrySs(int port)
{
try
{
var output = RunCommand("ss", $"-tlnpH sport = :{port}");
if (output is null) return null;
var result = ParseSsOutput(output, port);
return result.Count > 0 ? result : null;
}
catch { return null; }
}

private static List<PortListenerInfo>? TryNetstat(int port)
{
try
{
var output = RunCommand("netstat", "-anp tcp");
if (output is null) return null;
var result = ParseNetstatOutput(output, port);
return result.Count > 0 ? result : null;
}
catch { return null; }
}

private static List<PortListenerInfo> FallbackIPGlobalProperties(int port)
{
var result = new List<PortListenerInfo>();
var props = IPGlobalProperties.GetIPGlobalProperties();
foreach (var ep in props.GetActiveTcpListeners())
{
if (ep.Port != port) continue;
var family = ep.AddressFamily == System.Net.Sockets.AddressFamily.InterNetworkV6 ? "ipv6" : "ipv4";
result.Add(new PortListenerInfo(port, 0, string.Empty, ep.Address.ToString(), family));
}
return result;
}

internal static List<PortListenerInfo> ParseLsofOutput(string output, int port)
{
var result = new List<PortListenerInfo>();
foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
{
var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
if (parts.Length < 9) continue;

// NAME column may be followed by "(LISTEN)" as a separate token
var namePart = parts[^1] == "(LISTEN)" && parts.Length >= 10 ? parts[^2] : parts[^1];
if (!namePart.Contains(':')) continue;

var colonIdx = namePart.LastIndexOf(':');
var addrRaw = namePart[..colonIdx];
var portRaw = namePart[(colonIdx + 1)..].TrimEnd(')').Trim();
if (!int.TryParse(portRaw, out var parsedPort) || parsedPort != port) continue;

var addr = addrRaw == "*" ? "0.0.0.0" : addrRaw;
var typeCol = parts.Length > 4 ? parts[4] : string.Empty;
var family = typeCol == "IPv6" ? "ipv6" : (addr.Contains(':') ? "ipv6" : "ipv4");

if (!int.TryParse(parts[1], out var pid)) continue;
result.Add(new PortListenerInfo(port, pid, parts[0], addr, family));
}
return result;
}

internal static List<PortListenerInfo> ParseSsOutput(string output, int port)
{
var result = new List<PortListenerInfo>();
foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
{
var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
if (parts.Length < 2) continue;

var localAddr = parts.FirstOrDefault(p => p.EndsWith($":{port}"));
if (localAddr is null) continue;

var colonIdx = localAddr.LastIndexOf(':');
var addrPart = colonIdx >= 0 ? localAddr[..colonIdx] : "0.0.0.0";
var addr = addrPart is "*" or "" ? "0.0.0.0" : addrPart;
var family = addr.Contains('[') || (addr.Contains(':') && !addr.StartsWith("::ffff:")) ? "ipv6" : "ipv4";

int pid = 0;
string processName = string.Empty;
var usersIdx = line.IndexOf("users:((", StringComparison.Ordinal);
if (usersIdx >= 0)
{
var usersSection = line[(usersIdx + 8)..];
var q1 = usersSection.IndexOf('"');
var q2 = q1 >= 0 ? usersSection.IndexOf('"', q1 + 1) : -1;
if (q1 >= 0 && q2 > q1) processName = usersSection[(q1 + 1)..q2];

var pidIdx = usersSection.IndexOf("pid=", StringComparison.Ordinal);
if (pidIdx >= 0)
{
var pidStr = usersSection[(pidIdx + 4)..];
var comma = pidStr.IndexOf(',');
var end = pidStr.IndexOf(')');
var len = comma >= 0 && (end < 0 || comma < end) ? comma : end;
if (len > 0) int.TryParse(pidStr[..len], out pid);
}
}
result.Add(new PortListenerInfo(port, pid, processName, addr, family));
}
return result;
}

internal static List<PortListenerInfo> ParseNetstatOutput(string output, int port)
{
var result = new List<PortListenerInfo>();
foreach (var line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
{
var parts = line.Split(' ', StringSplitOptions.RemoveEmptyEntries);
if (parts.Length < 6) continue;
if (!parts[0].StartsWith("tcp", StringComparison.OrdinalIgnoreCase)) continue;
if (!parts[^1].Equals("LISTEN", StringComparison.OrdinalIgnoreCase)) continue;

var localAddr = parts[3];
var dotIdx = localAddr.LastIndexOf('.');
if (dotIdx < 0) continue;
if (!int.TryParse(localAddr[(dotIdx + 1)..], out var parsedPort) || parsedPort != port) continue;

var addrPart = localAddr[..dotIdx];
var addr = addrPart == "*" ? "0.0.0.0" : addrPart;
var family = parts[0].Contains('6') ? "ipv6" : "ipv4";
result.Add(new PortListenerInfo(port, 0, string.Empty, addr, family));
}
return result;
}

private static string? RunCommand(string executable, string arguments)
{
try
{
using var process = new Process
{
StartInfo = new ProcessStartInfo(executable, arguments)
{
RedirectStandardOutput = true,
RedirectStandardError = true,
UseShellExecute = false,
CreateNoWindow = true
}
};
process.Start();
var output = process.StandardOutput.ReadToEnd();
process.WaitForExit(5000);
return process.ExitCode == 0 || output.Length > 0 ? output : null;
}
catch { return null; }
}
}