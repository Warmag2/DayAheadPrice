using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Web;
using AwesomeAssertions;
using Faemiyah.DayAheadPrice.Logic;
using Faemiyah.DayAheadPrice.Options;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace Faemiyah.DayAheadPrice.UnitTests;

[TestFixture]
public sealed class EntsoeClientTests
{
    private const string ApiKey = "test-key";
    private const string Domain = "10YFI-1--------U";

    [Test]
    public async Task GetPricesAsync_RejectedWithAReason_ThrowsWithTheStatusAndTheReason()
    {
        // Arrange
        const string reason = "The parameter periodStart is not valid";
        var client = Client(HttpStatusCode.BadRequest, $"<Acknowledgement_MarketDocument><Reason><text>{reason}</text></Reason></Acknowledgement_MarketDocument>");

        // Act
        var act = () => client.GetPricesAsync(Dst.Utc(2025, 10, 27), Dst.Utc(2025, 10, 28), CancellationToken.None);

        // Assert
        var thrown = await act.Should().ThrowAsync<HttpRequestException>();
        thrown.Which.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        thrown.Which.Message.Should().Contain("400").And.Contain(reason);
    }

    [Test]
    public async Task GetPricesAsync_AnyRange_AsksForDayAheadPricesOfTheDomainOverThatRange()
    {
        // Arrange
        var handler = new StubHandler(HttpStatusCode.OK, "<Acknowledgement_MarketDocument><Reason><text>No matching data found</text></Reason></Acknowledgement_MarketDocument>");
        var client = Client(handler);

        // Act
        await client.GetPricesAsync(Dst.Utc(2025, 10, 26, 23), Dst.Utc(2025, 10, 27, 23, 15), CancellationToken.None);

        // Assert
        handler.RequestUri!.AbsolutePath.Should().Be("/api");
        var query = HttpUtility.ParseQueryString(handler.RequestUri.Query);
        query.AllKeys.Should().BeEquivalentTo("periodStart", "periodEnd", "securityToken", "documentType", "contract_MarketAgreement.type", "in_Domain", "out_Domain");
        query["periodStart"].Should().Be("202510262300");
        query["periodEnd"].Should().Be("202510272315");
        query["securityToken"].Should().Be(ApiKey);
        query["documentType"].Should().Be("A44");
        query["contract_MarketAgreement.type"].Should().Be("A01");
        query["in_Domain"].Should().Be(Domain);
        query["out_Domain"].Should().Be(Domain);
    }

    private static EntsoeClient Client(HttpStatusCode status, string body) => Client(new StubHandler(status, body));

    private static EntsoeClient Client(StubHandler handler)
    {
        var httpClient = new HttpClient(handler) { BaseAddress = new Uri("https://example.test/") };
        var options = Microsoft.Extensions.Options.Options.Create(new EndpointOptions { ApiKey = ApiKey, Domain = Domain });

        return new EntsoeClient(NullLogger<EntsoeClient>.Instance, options, new StubFactory(httpClient));
    }

    private sealed class StubFactory(HttpClient client) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => client;
    }

    private sealed class StubHandler(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public Uri? RequestUri { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            RequestUri = request.RequestUri;

            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }
}
