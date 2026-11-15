using System;

namespace ApexPack.Buffers;

/// <summary>
/// 書き込み先バッファの最小契約。値の符号化は <see cref="ApexBufferExtensions"/> に集約し、各実装は領域確保だけを持つ。
/// </summary>
/// <remarks>
/// ワイヤ形式はリトルエンディアン固定。<see cref="IsHostLittleEndian"/> は JIT 時定数として畳み込まれ、
/// リトルエンディアン機ではバイト反転経路がコードごと消える。
/// </remarks>
public interface IApexBufferWriter
{
    /// <summary>ネイティブのメモリ表現がワイヤ形式(LE)と一致するなら true。</summary>
    static abstract bool IsHostLittleEndian { get; }

    /// <summary>これまでに確定したバイト数。</summary>
    int BytesWritten { get; }

    /// <summary><paramref name="size"/> バイトを確保して書き込み位置を進め、その先頭を返す。</summary>
    ref byte GetSpanRef(int size);

    /// <summary>最大 <paramref name="maxSize"/> バイトを確保するが位置は進めない。続けて <see cref="Advance"/> で実際の長さを確定する。</summary>
    ref byte Reserve(int maxSize);

    /// <summary>直前の <see cref="Reserve"/> で確保した領域のうち <paramref name="count"/> バイトを確定する。</summary>
    void Advance(int count);
}

/// <summary>
/// 読み取り元バッファの最小契約。値の復号は <see cref="ApexBufferExtensions"/> に集約する。
/// </summary>
public interface IApexBufferReader
{
    /// <summary>ネイティブのメモリ表現がワイヤ形式(LE)と一致するなら true。</summary>
    static abstract bool IsHostLittleEndian { get; }

    int Consumed { get; }
    int Remaining { get; }

    /// <summary><paramref name="size"/> バイトの存在を検査して読み取り位置を進め、その先頭を返す。</summary>
    ref readonly byte GetSpanRef(int size);

    /// <summary>位置を進めずに次の 1 バイトを返す。</summary>
    byte PeekByte();
}
