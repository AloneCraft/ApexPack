using System;
using Xunit;
using ApexPack;

namespace ApexPack.Tests;

public class StringUtf16EdgeCasesTest
{
    [Fact]
    public void Utf16Strings_JapaneseAndEmojiAndLongStrings_PreserveExactCharacters()
    {
        string japaneseText = "日本語シリアライザテスト：漢字 ひらがな カタカナ 濁点（がぎぐげご）半濁点（ぱぴぷぺぽ）！";
        string emojiText = "🚀✨🎉🔥🍣🌸👘🗾👨‍💻🧙‍♂️🦄";
        string longText = new string('A', 50_000);

        var model = new StringEdgeCasesModel
        {
            NullString = null,
            EmptyString = "",
            JapaneseString = japaneseText,
            EmojiString = emojiText,
            LongString = longText
        };

        byte[] bytes = ApexPackSerializer.Serialize(model);
        var restored = ApexPackSerializer.Deserialize<StringEdgeCasesModel>(bytes);

        Assert.Null(restored.NullString);
        Assert.Equal("", restored.EmptyString);
        Assert.Equal(japaneseText, restored.JapaneseString);
        Assert.Equal(emojiText, restored.EmojiString);
        Assert.Equal(longText, restored.LongString);
    }
}
