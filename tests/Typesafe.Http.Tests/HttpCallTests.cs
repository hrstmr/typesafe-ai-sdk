namespace Typesafe.Http.Tests;

public class HttpCallTests
{
    [Fact]
    public void StoresMethodAndRoute()
    {
        var call = new HttpCall<object, object> { Method = "GET", Route = "/ping" };

        Assert.Equal("GET", call.Method);
        Assert.Equal("/ping", call.Route);
    }
}
