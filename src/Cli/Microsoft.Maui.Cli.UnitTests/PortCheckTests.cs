// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.

using System.Text.Json;
using Microsoft.Maui.Cli.Models;
using Microsoft.Maui.Cli.Output;
using Microsoft.Maui.Cli.Providers.Port;
using Xunit;

namespace Microsoft.Maui.Cli.UnitTests;

public class PortCheckTests
{
[Fact]
public void PortCheckResult_JsonShape_HasExpectedProperties()
{
var result = new PortCheckResult
{
Port = 8080,
InUse = true,
Listeners =
[
new PortListenerResult
{
Pid = 1234,
ProcessName = "dotnet",
Address = "0.0.0.0",
Family = "ipv4",
State = "listen",
}
]
};

var json = JsonSerializer.Serialize(result, MauiCliJsonContext.Default.PortCheckResult);
using var doc = JsonDocument.Parse(json);
var root = doc.RootElement;

Assert.Equal(8080, root.GetProperty("port").GetInt32());
Assert.True(root.GetProperty("in_use").GetBoolean());

var listener = root.GetProperty("listeners")[0];
Assert.Equal(1234, listener.GetProperty("pid").GetInt32());
Assert.Equal("dotnet", listener.GetProperty("process_name").GetString());
Assert.Equal("0.0.0.0", listener.GetProperty("address").GetString());
Assert.Equal("ipv4", listener.GetProperty("family").GetString());
Assert.Equal("listen", listener.GetProperty("state").GetString());
}

[Fact]
public void PortCheckResult_Free_SerializesCorrectly()
{
var result = new PortCheckResult { Port = 80, InUse = false };
var json = JsonSerializer.Serialize(result, MauiCliJsonContext.Default.PortCheckResult);
using var doc = JsonDocument.Parse(json);

Assert.False(doc.RootElement.GetProperty("in_use").GetBoolean());
Assert.Equal(0, doc.RootElement.GetProperty("listeners").GetArrayLength());
}

[Fact]
public void ParseLsofOutput_SingleListener_ReturnsCorrectInfo()
{
var output = "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
             "dotnet  1234 user 10u IPv4 12345  0t0  TCP *:8080 (LISTEN)\n";
var result = UnixPortInspector.ParseLsofOutput(output, 8080);

Assert.Single(result);
Assert.Equal(1234, result[0].Pid);
Assert.Equal("dotnet", result[0].ProcessName);
Assert.Equal("0.0.0.0", result[0].Address);
}

[Fact]
public void ParseLsofOutput_IPv6_ReturnsIpv6Family()
{
var output = "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
             "nginx   999 root 5u  IPv6 54321  0t0  TCP *:443 (LISTEN)\n";
var result = UnixPortInspector.ParseLsofOutput(output, 443);

Assert.Single(result);
Assert.Equal("ipv6", result[0].Family);
}

[Fact]
public void ParseLsofOutput_WrongPort_ReturnsEmpty()
{
var output = "COMMAND PID USER FD TYPE DEVICE SIZE/OFF NODE NAME\n" +
             "dotnet  1234 user 10u IPv4 12345  0t0  TCP *:9090 (LISTEN)\n";
var result = UnixPortInspector.ParseLsofOutput(output, 8080);

Assert.Empty(result);
}

[Fact]
public void ParseSsOutput_WithProcess_ExtractsPidAndName()
{
var output = "LISTEN 0 128 0.0.0.0:8080 0.0.0.0:* users:((\"dotnet\",pid=1234,fd=5))\n";
var result = UnixPortInspector.ParseSsOutput(output, 8080);

Assert.Single(result);
Assert.Equal(1234, result[0].Pid);
Assert.Equal("dotnet", result[0].ProcessName);
Assert.Equal("0.0.0.0", result[0].Address);
}

[Fact]
public void ParseSsOutput_WrongPort_ReturnsEmpty()
{
var output = "LISTEN 0 128 0.0.0.0:9090 0.0.0.0:* users:((\"dotnet\",pid=1234,fd=5))\n";
var result = UnixPortInspector.ParseSsOutput(output, 8080);

Assert.Empty(result);
}

[Fact]
public void ParseNetstatOutput_Tcp4_ExtractsListener()
{
var output = "Proto Recv-Q Send-Q Local Address          Foreign Address        (state)\n" +
             "tcp4  0      0      127.0.0.1.8080         *.*                    LISTEN\n";
var result = UnixPortInspector.ParseNetstatOutput(output, 8080);

Assert.Single(result);
Assert.Equal("127.0.0.1", result[0].Address);
Assert.Equal("ipv4", result[0].Family);
}

[Fact]
public void ParseNetstatOutput_Wildcard_MapsToAllInterfaces()
{
var output = "Proto Recv-Q Send-Q Local Address          Foreign Address        (state)\n" +
             "tcp46 0      0      *.19223                *.*                    LISTEN\n";
var result = UnixPortInspector.ParseNetstatOutput(output, 19223);

Assert.Single(result);
Assert.Equal("0.0.0.0", result[0].Address);
}

[Fact]
public void ParseNetstatOutput_WrongPort_ReturnsEmpty()
{
var output = "Proto Recv-Q Send-Q Local Address          Foreign Address        (state)\n" +
             "tcp4  0      0      127.0.0.1.9090         *.*                    LISTEN\n";
var result = UnixPortInspector.ParseNetstatOutput(output, 8080);

Assert.Empty(result);
}

[Theory]
[InlineData(0x5000u, 80)]
[InlineData(0x1F90u, 8080)]
[InlineData(0x01BBu, 443)]
public void GetPortFromNetworkDword_ReturnsCorrectPort(uint networkDword, int expectedPort)
{
// The formula: ((dword & 0xFF) << 8) | ((dword >> 8) & 0xFF)
var actual = WindowsPortInspector.GetPortFromNetworkDword(networkDword);
Assert.Equal(expectedPort, actual);
}
}