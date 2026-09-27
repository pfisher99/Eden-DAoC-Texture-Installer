namespace SqeaksDaocTextures.Tests;

public sealed class PathSafetyTests
{
    [Theory]
    [InlineData("zones/zone001/texture.dds", "zones")]
    [InlineData("Tutorial\\zones\\file.dds", "Tutorial")]
    public void Valid_entries_return_top_level(string input, string expected)
    {
        Assert.Equal(expected, PathSafety.ValidateArchiveEntry(input).TopLevel);
    }

    [Theory]
    [InlineData("../outside.txt")]
    [InlineData("zones/../../outside.txt")]
    [InlineData("C:/outside.txt")]
    [InlineData("/outside.txt")]
    public void Unsafe_entries_are_rejected(string input)
    {
        Assert.Throws<InvalidDataException>(() => PathSafety.ValidateArchiveEntry(input));
    }
}
