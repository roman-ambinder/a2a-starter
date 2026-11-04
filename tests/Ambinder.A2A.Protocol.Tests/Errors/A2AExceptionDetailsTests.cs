using System.Text.Json;
using Ambinder.A2A.Protocol;

namespace Ambinder.A2A.Protocol.Tests.Errors;

public class A2AExceptionDetailsTests
{
    [Fact]
    public void Details_is_empty_without_data()
        => Assert.Empty(new A2AException(A2AErrorCode.TaskNotFound, "m").Details);

    [Fact]
    public void Details_is_empty_when_data_is_not_an_object()
        => Assert.Empty(new A2AException(A2AErrorCode.TaskNotFound, "m", JsonDocument.Parse("[1,2]").RootElement).Details);

    [Fact]
    public void Details_flattens_strings_arrays_and_other_values()
    {
        var data = JsonDocument.Parse("""{"a":"x","list":["1.0","2.0"],"n":5,"flag":true}""").RootElement;
        var details = new A2AException(A2AErrorCode.VersionNotSupported, "m", data).Details;

        Assert.Equal("x", details["a"]);
        Assert.Equal("1.0,2.0", details["list"]);
        Assert.Equal("5", details["n"]);
        Assert.Equal("true", details["flag"]);
    }

    [Fact]
    public void Details_is_a_read_only_string_map_computed_once()
    {
        var error = new A2AException(A2AErrorCode.TaskNotFound, "m", JsonDocument.Parse("""{"k":"v"}""").RootElement);

        Assert.IsAssignableFrom<IReadOnlyDictionary<string, string>>(error.Details);
        Assert.False(error.Details is IDictionary<string, string> { IsReadOnly: false });
        Assert.Same(error.Details, error.Details);
    }
}
