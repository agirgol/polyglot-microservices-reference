using Shouldly;
using Xunit;

namespace Orders.IntegrationTests;

/// <summary>
/// That the application starts as a web application, before anything asks it a
/// question.
/// </summary>
/// <remarks>
/// <para>
/// This looks redundant — every other test creates a client, so a broken host
/// fails all of them. It exists because of how one such breakage read: adding
/// the JasperFx command line left a host whose services resolved perfectly and
/// whose web server was never started, so every test failed at
/// <c>CreateClient()</c> with "the server has not been started", pointing at the
/// test rather than at the cause.
/// </para>
/// <para>
/// Splitting the two makes the next one legible: if only the second of these
/// fails, the host is fine and the web server is not.
/// </para>
/// </remarks>
public sealed class TheHostStartsTests(OrdersUnderTest service) : IClassFixture<OrdersUnderTest>
{
    [Fact]
    public void Its_services_resolve()
    {
        service.Services.ShouldNotBeNull();
    }

    [Fact]
    public void And_it_serves_http()
    {
        service.CreateClient().BaseAddress.ShouldNotBeNull();
    }
}
