using FeedFabric.Application.Services.Scanning;

namespace FeedFabric.Application.Tests.Scanning;

public class ScanResultTests
{
    [Theory]
    [InlineData("Eicar-Test-Signature", "Eicar-Test-Signature")]
    [InlineData("Foo\r\nBar", "FooBar")]
    [InlineData("Foo\u001b[2JBar", "Foo[2JBar")]
    [InlineData("Foo\tBar\0", "FooBar")]
    public void Infected_drops_control_characters_from_the_signature(string signature, string expected)
    {
        var result = ScanResult.Infected(signature);

        Assert.False(result.IsClean);
        Assert.Equal(expected, result.Signature);
    }
}
