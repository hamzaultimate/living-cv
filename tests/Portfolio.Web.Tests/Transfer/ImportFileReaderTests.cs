using System.Text;
using Portfolio.Web.Tests.Support;
using Portfolio.Web.Transfer;
using Xunit;

namespace Portfolio.Web.Tests.Transfer;

/// <summary>The file is untrusted: size, shape, nesting, repeated properties, encoding and version are all checked before any row is read.</summary>
public class ImportFileReaderTests
{
    private const string Minimal = """{ "schemaVersion": 1, "content": { "skills": [] } }""";

    private static ReadResult Parse(string text) => ImportFileReader.Parse(TransferFlow.Utf8(text));

    [Fact]
    public void AValidFile_IsReadAndHoldsItsJson()
    {
        using var read = Parse(Minimal);

        Assert.Null(read.Refusal);
        Assert.Equal(1, read.Document!.RootElement.GetProperty("schemaVersion").GetInt32());
    }

    [Fact]
    public void AUtf8ByteOrderMark_IsAccepted_ButUtf16IsNot()
    {
        var withBom = new byte[] { 0xEF, 0xBB, 0xBF }.Concat(TransferFlow.Utf8(Minimal)).ToArray();
        using var ok = ImportFileReader.Parse(withBom);
        Assert.Null(ok.Refusal);

        using var utf16 = ImportFileReader.Parse(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(Minimal)).ToArray());
        Assert.NotNull(utf16.Refusal);
        Assert.Contains("not valid JSON", utf16.Refusal);
        Assert.Contains("UTF-8", utf16.Refusal);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   \n ")]
    [InlineData("{")]
    [InlineData("""{ "schemaVersion": 1, "content": { "skills": [ { "name": "a" """)]
    [InlineData("""{ "schemaVersion": 1, }""")]
    [InlineData("""{ "schemaVersion": 1 /* note */ }""")]
    [InlineData("""{ "schemaVersion": 1 } trailing""")]
    [InlineData("not json at all")]
    [InlineData("{'schemaVersion': 1}")]
    public void MalformedOrTruncatedText_IsRefused_WithoutQuotingIt(string text)
    {
        using var read = Parse(text);

        Assert.NotNull(read.Refusal);
        Assert.Null(read.Document);
        Assert.DoesNotContain("trailing", read.Refusal);
        Assert.DoesNotContain("note", read.Refusal);
    }

    [Theory]
    [InlineData("[]")]
    [InlineData("\"text\"")]
    [InlineData("42")]
    [InlineData("null")]
    [InlineData("true")]
    public void ATopLevelThatIsNotAnObject_IsRefused(string text)
    {
        using var read = Parse(text);

        Assert.Contains("top level must be an object", read.Refusal);
    }

    [Theory]
    [InlineData("""{ "content": {} }""", "no schemaVersion")]
    [InlineData("""{ "schemaVersion": null }""", "whole number")]
    [InlineData("""{ "schemaVersion": "1" }""", "whole number")]
    [InlineData("""{ "schemaVersion": 1.5 }""", "whole number")]
    [InlineData("""{ "schemaVersion": true }""", "whole number")]
    [InlineData("""{ "schemaVersion": 99999999999 }""", "whole number")]
    [InlineData("""{ "schemaVersion": 2 }""", "newer than this version")]
    [InlineData("""{ "schemaVersion": 0 }""", "cannot read")]
    [InlineData("""{ "schemaVersion": -1 }""", "cannot read")]
    [InlineData("""{ "schemaVersion": 1, "format": "something-else" }""", "another kind of file")]
    [InlineData("""{ "schemaVersion": 1, "format": 3 }""", "another kind of file")]
    public void AMissingWrongOrNewerVersion_IsRefusedWhole_WithAClearMessage(string text, string expected)
    {
        using var read = Parse(text);

        Assert.Contains(expected, read.Refusal);
    }

    [Fact]
    public void TheCurrentVersion_WithOrWithoutAFormatMarker_IsRead()
    {
        using var a = Parse("""{ "schemaVersion": 1 }""");
        using var b = Parse("""{ "schemaVersion": 1, "format": "living-cv-export" }""");

        Assert.Null(a.Refusal);
        Assert.Null(b.Refusal);
    }

    [Fact]
    public void ARepeatedProperty_IsRefused_AtAnyDepth_WithoutEchoingIt()
    {
        foreach (var text in new[]
                 {
                     """{ "schemaVersion": 1, "schemaVersion": 2 }""",
                     """{ "schemaVersion": 1, "content": { "skills": [ { "name": "<img onerror=x>", "name": "b" } ] } }""",
                     """{ "schemaVersion": 1, "profile": { "hero": { "headline": "a", "headline": "b" } } }""",
                 })
        {
            using var read = Parse(text);

            Assert.Contains("same property twice", read.Refusal);
            Assert.DoesNotContain("<img", read.Refusal);
        }
    }

    [Fact]
    public void DeepNesting_IsRefusedAtTheCap_AndJustUnderItIsRead()
    {
        string Nested(int depth) => """{ "schemaVersion": 1, "x": """ + new string('[', depth) + new string(']', depth) + " }";

        using var tooDeep = Parse(Nested(TransferLimits.MaxJsonDepth + 1));
        Assert.Contains("nested more than", tooDeep.Refusal);

        using var fine = Parse(Nested(TransferLimits.MaxJsonDepth - 2));
        Assert.Null(fine.Refusal);

        // The depth is checked by the parser as it reads, so a megabyte of brackets is stopped at the cap, not after it has been built.
        using var huge = ImportFileReader.Parse(TransferFlow.Utf8("""{ "schemaVersion": 1, "x": """ + new string('[', 900_000)));
        Assert.NotNull(huge.Refusal);
    }

    [Fact]
    public async Task AFileOverTheCap_IsRefusedFromItsDeclaredLength_AndNothingIsRead()
    {
        var stream = new ThrowingStream();

        using var read = await ImportFileReader.ReadAsync(stream, TransferLimits.MaxFileBytes + 1, CancellationToken.None);

        Assert.Contains("larger than 2 MB", read.Refusal);
        Assert.False(stream.WasRead);
    }

    [Fact]
    public async Task ABodyThatLiesAboutItsLength_IsCutOffWhileItIsRead()
    {
        var oversized = new MemoryStream(new byte[TransferLimits.MaxFileBytes + 5000]);

        using var read = await ImportFileReader.ReadAsync(oversized, declaredLength: 100, CancellationToken.None);

        Assert.Contains("larger than 2 MB", read.Refusal);
        Assert.True(oversized.Position <= TransferLimits.MaxFileBytes + 16 * 1024, "the reader stopped soon after the cap");
    }

    [Fact]
    public async Task AFileExactlyAtTheCap_IsRead_AndAnEmptyOrMissingOneIsRefused()
    {
        var padding = new string(' ', TransferLimits.MaxFileBytes - Minimal.Length);
        using var atCap = await ImportFileReader.ReadAsync(new MemoryStream(TransferFlow.Utf8(Minimal + padding)), TransferLimits.MaxFileBytes, CancellationToken.None);
        Assert.Null(atCap.Refusal);

        using var empty = await ImportFileReader.ReadAsync(new MemoryStream(), 0, CancellationToken.None);
        Assert.Contains("Choose a file", empty.Refusal);
        using var none = await ImportFileReader.ReadAsync(null, 0, CancellationToken.None);
        Assert.Contains("Choose a file", none.Refusal);
    }

    [Fact]
    public void AHugeString_InsideTheCap_IsReadHere_AndRefusedLaterByTheFieldRules()
    {
        // 1.5 MB in one value is within the file cap; the limit that stops it is the field's own (see the validation tests).
        using var read = Parse("{ \"schemaVersion\": 1, \"content\": { \"skills\": [ { \"name\": \"" + new string('x', 1_500_000) + "\" } ] } }");

        Assert.Null(read.Refusal);
    }

    private sealed class ThrowingStream : MemoryStream
    {
        public bool WasRead { get; private set; }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            WasRead = true;
            return base.ReadAsync(buffer, cancellationToken);
        }
    }
}
