using System;
using System.Runtime.CompilerServices;

namespace ApexPack.Buffers;

/// <summary>
/// High-throughput zero-allocation buffer writer contract.
/// </summary>
public interface IApexBufferWriter
{
    void WriteByte(byte value);
    void WriteSByte(sbyte value);
    void WriteChar(char value);
    void WriteInt16(short value);
    void WriteUInt16(ushort value);
    void WriteInt32(int value);
    void WriteUInt32(uint value);
    void WriteInt64(long value);
    void WriteUInt64(ulong value);
    void WriteSingle(float value);
    void WriteDouble(double value);
    void WriteBoolean(bool value);
    void WriteGuid(Guid value);
    void WriteDateTime(DateTime value);
    void WriteTimeSpan(TimeSpan value);
    void WriteDecimal(decimal value);
    void WriteRaw(ReadOnlySpan<byte> source);
    void WriteString(string? value);
    void WriteUnmanaged<T>(in T value) where T : unmanaged;

    void WriteBitmask8(byte mask);
    void WriteBitmask16(ushort mask);
    void WriteBitmask32(uint mask);
    void WriteBitmask64(ulong mask);
}

/// <summary>
/// High-throughput zero-allocation buffer reader contract.
/// </summary>
public interface IApexBufferReader
{
    byte ReadByte();
    sbyte ReadSByte();
    char ReadChar();
    short ReadInt16();
    ushort ReadUInt16();
    int ReadInt32();
    uint ReadUInt32();
    long ReadInt64();
    ulong ReadUInt64();
    float ReadSingle();
    double ReadDouble();
    bool ReadBoolean();
    Guid ReadGuid();
    DateTime ReadDateTime();
    TimeSpan ReadTimeSpan();
    decimal ReadDecimal();
    ReadOnlySpan<byte> ReadRaw(int length);
    string? ReadString();
    T ReadUnmanaged<T>() where T : unmanaged;

    byte ReadBitmask8();
    ushort ReadBitmask16();
    uint ReadBitmask32();
    ulong ReadBitmask64();
}
