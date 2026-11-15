using System;
using ApexPack;
using ApexPack.Buffers;
using ApexPack.Tests.Comparison;
using Xunit;

namespace ApexPack.Tests;

/// <summary>
/// 壊れた・悪意のある入力は、割り当て前に <see cref="ApexSecurityException"/> で拒否される。
/// </summary>
public class SecurityDefenseTests
{
    /// <summary>CompactUInt の 4 バイト形式(0xFE + u32 LE)。</summary>
    private static byte[] Compact32(uint value, int padding)
    {
        var bytes = new byte[5 + padding];
        bytes[0] = 0xFE;
        BitConverter.TryWriteBytes(bytes.AsSpan(1), value);
        return bytes;
    }

    [Fact]
    public void AllocationBomb_LargeArrayCount_ThrowsSecurityException()
    {
        // 件数 20 億(count + 1 で符号化)、本体は 3 バイトしかない
        byte[] payload = Compact32(2_000_000_001, padding: 3);
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.DeserializeArray<Person>(payload));
    }

    [Fact]
    public void AllocationBomb_CountBeyondInt32_ThrowsSecurityException()
    {
        byte[] payload = Compact32(0xFFFF_FFFE, padding: 3);
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.DeserializeArray<Person>(payload));
    }

    [Fact]
    public void InvalidLengthHeader_ThrowsSecurityException()
    {
        byte[] payload = { 0xFF, 1, 2, 3 };
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.DeserializeArray<Person>(payload));
    }

    [Fact]
    public void AllocationBomb_UnmanagedArray_ThrowsSecurityException()
    {
        // int 要素 1 億個を名乗るが本体は 8 バイト
        byte[] payload = Compact32(100_000_001, padding: 8);
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.DeserializeUnmanagedArray<int>(payload));
    }

    [Fact]
    public void AllocationBomb_StringLength_ThrowsSecurityException()
    {
        // 1 バイト/文字で 10 億文字を名乗る文字列ヘッダー
        byte[] payload = Compact32((1_000_000_000u << 1) + 1, padding: 4);

        void ReadMaliciousString()
        {
            var reader = new ApexSpanReader(payload);
            reader.ReadString();
        }

        Assert.Throws<ApexSecurityException>(ReadMaliciousString);
    }

    [Fact]
    public void AllocationBomb_WideStringLength_ThrowsSecurityException()
    {
        byte[] payload = Compact32((600_000_000u << 1) + 2, padding: 4);

        void ReadMaliciousString()
        {
            var reader = new ApexSpanReader(payload);
            reader.ReadString();
        }

        Assert.Throws<ApexSecurityException>(ReadMaliciousString);
    }

    [Fact]
    public void TruncatedPayload_Throws()
    {
        var person = new Person { Age = 30, Name = "Truncated payload" };
        byte[] bytes = ApexPackSerializer.Serialize(person);
        for (int length = 1; length < bytes.Length; length++)
        {
            byte[] cut = bytes.AsSpan(0, length).ToArray();
            Assert.ThrowsAny<Exception>(() => ApexPackSerializer.Deserialize<Person>(cut));
        }
    }

    [Fact]
    public void DeserializeSafe_RecursionDepthBomb_ThrowsSecurityException()
    {
        var root = new Node { Value = 0 };
        var current = root;
        for (int i = 1; i <= 10; i++)
        {
            current.Next = new Node { Value = i };
            current = current.Next;
        }

        byte[] bytes = ApexPackSerializer.Serialize(root);

        var strictOptions = new ApexSecurityOptions
        {
            MaxDepth = 5,
            MaxTotalElements = 1000,
            MaxCollectionLength = 1000
        };

        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.DeserializeSafe<Node>(bytes, in strictOptions));

        // 上限内なら同じデータを読める
        var restored = ApexPackSerializer.DeserializeSafe<Node>(bytes, ApexSecurityOptions.Default);
        Assert.Equal(10, Depth(restored));
    }

    [Fact]
    public void DeserializeSafe_CollectionLengthLimit_ThrowsSecurityException()
    {
        byte[] bytes = ApexPackSerializer.Serialize(new int[100]);
        var options = new ApexSecurityOptions { MaxDepth = 8, MaxCollectionLength = 10, MaxTotalElements = 1000 };
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.DeserializeSafe<int[]>(bytes, in options));
    }

    [Fact]
    public void DeserializeSafe_TypeNameWithoutResolver_IsRejected()
    {
        byte[] bytes = ApexPackSerializer.Serialize<Type>(typeof(string));
        Assert.Throws<ApexSecurityException>(() => ApexPackSerializer.DeserializeSafe<Type>(bytes, ApexSecurityOptions.Default));

        var allowed = ApexPackSerializer.Deserialize<Type>(bytes, new ApexSerializerOptions
        {
            Security = ApexSecurityOptions.Default,
            // 型名は Version / Culture / PublicKeyToken を除いた形で書かれる
            TypeResolver = name => name == "System.String, System.Private.CoreLib" ? typeof(string) : null
        });
        Assert.Equal(typeof(string), allowed);
    }

    private static int Depth(Node node)
    {
        int depth = 0;
        for (var n = node.Next; n is not null; n = n.Next) depth++;
        return depth;
    }
}
