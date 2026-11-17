#:property TargetFramework=net10.0
#:project ../../src/ApexPack.Core/ApexPack.Core.csproj

using System;
using System.Linq;
using ApexPack;

int?[] readings = [10, null, 30, 40];

byte[] payload = ApexPackSerializer.Serialize(readings);
int?[] restored = ApexPackSerializer.Deserialize<int?[]>(payload);

if (!readings.SequenceEqual(restored))
    throw new InvalidOperationException("The restored values differ from the input.");

Console.WriteLine($"Payload: {payload.Length} bytes");
Console.WriteLine($"Restored: {string.Join(", ", restored.Select(value => value?.ToString() ?? "null"))}");
