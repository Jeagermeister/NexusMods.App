using System.Net;
using System.Text.Json;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Apocrypha.Abstractions.NexusWebApi.DTOs.OAuth;
using Apocrypha.Abstractions.NexusWebApi.Types;
using Apocrypha.Networking.NexusWebApi.Auth;
using Apocrypha.Sdk;
using Apocrypha.Sdk.Jobs;
using NSubstitute;

namespace Apocrypha.Networking.NexusWebApi.Tests;

public class OAuthTests
{
    // ReSharper disable once InconsistentNaming
    private readonly Uri ExpectedAuthURL = new("https://users.nexusmods.com/oauth/authorize?response_type=code&scope=openid profile email&code_challenge_method=S256&client_id=nma&redirect_uri=nxm%3A%2F%2Foauth%2Fcallback&code_challenge=QMZ4D7BLeehAXINE9NZ8dho2i5AYVTbfqJ8PhQ4eUrE&state=00000000-0000-0000-0000-000000000000");
    private readonly ILogger<OAuth> _logger;
    private readonly IJobMonitor _jobMonitor;

    // ReSharper disable once ContextualLoggerProblem
    public OAuthTests(ILogger<OAuth> logger, IJobMonitor jobMonitor)
    {
        _logger = logger;
        _jobMonitor = jobMonitor;
    }

    [Fact]
    public async Task AuthorizeRequestTest()
    {
        #region Setup
        var stateId = "00000000-0000-0000-0000-000000000000";

        var messageHandler = Substitute.ForPartsOf<MockHttpMessageHandler>();
        messageHandler
            .SendMock(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(ReplyToken)),
            }));

        var httpClient = new HttpClient(messageHandler);

        var idGen = Substitute.For<IIDGenerator>();
        idGen.UUIDv4().Returns(stateId);

        var os = Substitute.For<IOSInterop>();
        #endregion

        #region Execution
        var oauth = new OAuth(_jobMonitor, _logger, httpClient, idGen, os);
        var tokenTask = oauth.AuthorizeRequest(CancellationToken.None);
        oauth.AddUrl(NXMUrl.Parse($"nxm://oauth/callback?state={stateId}&code=code").OAuth);
        var result = await tokenTask;
        #endregion

        #region Verification

        _ = idGen.Received(2).UUIDv4();
        os.Received(1).OpenUri(ExpectedAuthURL);
        result.Should().BeEquivalentTo(ReplyToken);
        #endregion
    }

    [Fact]
    public async Task RefreshTokenTest()
    {
        #region Setup
        var stateId = "00000000-0000-0000-0000-000000000000";

        var messageHandler = Substitute.ForPartsOf<MockHttpMessageHandler>();
        messageHandler
            .SendMock(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent(JsonSerializer.Serialize(ReplyToken)),
            }));

        var httpClient = new HttpClient(messageHandler);

        var idGen = Substitute.For<IIDGenerator>();
        idGen.UUIDv4().Returns(stateId);

        var os = Substitute.For<IOSInterop>();
        #endregion

        #region Execution
        var oauth = new OAuth(_jobMonitor, _logger, httpClient, idGen, os);
        var token = await oauth.RefreshToken("refresh_token", CancellationToken.None);
        #endregion

        #region Verification

        _ = idGen.DidNotReceive().UUIDv4();
        os.DidNotReceive().OpenUri(Arg.Any<Uri>());
        token.Should().BeEquivalentTo(ReplyToken);

        #endregion
    }

    [Fact]
    public async Task ThrowsOnInvalidResponse()
    {
        #region Setup
        var stateId = "00000000-0000-0000-0000-000000000000";

        var messageHandler = Substitute.ForPartsOf<MockHttpMessageHandler>();
        messageHandler
            .SendMock(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.OK,
                Content = new StringContent("foo"),
            }));

        var httpClient = new HttpClient(messageHandler);

        var idGen = Substitute.For<IIDGenerator>();
        idGen.UUIDv4().Returns(stateId);

        var os = Substitute.For<IOSInterop>();
        #endregion

        #region Execution
        var oauth = new OAuth(_jobMonitor, _logger, httpClient, idGen, os);
        Func<Task> call = () => oauth.AuthorizeRequest(CancellationToken.None);
        var tokenTask = call.Should().ThrowAsync<JsonException>();
        oauth.AddUrl(NXMUrl.Parse($"nxm://oauth/callback?state={stateId}&code=code").OAuth);
        await tokenTask;
        #endregion
    }

    [Fact]
    public async Task RefreshToken_RejectedRefresh_ThrowsSessionExpired()
    {
        // Nexus rejects a dead session with 400 + an OAuth error body; blindly deserializing
        // it used to produce a null-field token and the cryptic "Invalid new token" log.
        var messageHandler = Substitute.ForPartsOf<MockHttpMessageHandler>();
        messageHandler
            .SendMock(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.BadRequest,
                Content = new StringContent("""{"error":"invalid_grant","error_description":"The provided authorization grant is invalid"}"""),
            }));

        var oauth = new OAuth(_jobMonitor, _logger, new HttpClient(messageHandler), Substitute.For<IIDGenerator>(), Substitute.For<IOSInterop>());

        Func<Task> call = () => oauth.RefreshToken("refresh_token", CancellationToken.None);
        await call.Should().ThrowAsync<OAuthSessionExpiredException>();
    }

    [Fact]
    public async Task RefreshToken_TransientServerError_DoesNotCountAsExpired()
    {
        var messageHandler = Substitute.ForPartsOf<MockHttpMessageHandler>();
        messageHandler
            .SendMock(Arg.Any<HttpRequestMessage>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new HttpResponseMessage
            {
                StatusCode = HttpStatusCode.InternalServerError,
                Content = new StringContent("<html>upstream error</html>"),
            }));

        var oauth = new OAuth(_jobMonitor, _logger, new HttpClient(messageHandler), Substitute.For<IIDGenerator>(), Substitute.For<IOSInterop>());

        // A Nexus outage must NOT log the user out — it surfaces as a plain HTTP failure.
        Func<Task> call = () => oauth.RefreshToken("refresh_token", CancellationToken.None);
        await call.Should().ThrowAsync<HttpRequestException>();
    }

    // TODO: requires jobs to be cancellable
    // [Fact]
    // public async Task AuthorizationCanBeCanceled()
    // {
    //     #region Setup
    //     var stateId = "00000000-0000-0000-0000-000000000000";
    //
    //     var messageHandler = Substitute.ForPartsOf<MockHttpMessageHandler>();
    //     var httpClient = new HttpClient(messageHandler);
    //
    //     var idGen = Substitute.For<IIDGenerator>();
    //     idGen.UUIDv4().Returns(stateId);
    //
    //     var os = Substitute.For<IOSInterop>();
    //     var cts = new CancellationTokenSource();
    //     #endregion
    //
    //     #region Execution
    //     var oauth = new OAuth(_jobMonitor, _logger, httpClient, idGen, os);
    //     Func<Task> call = () => oauth.AuthorizeRequest(cts.Token);
    //     var task = call.Should().ThrowAsync<OperationCanceledException>();
    //     cts.Cancel();
    //     await task;
    //     #endregion
    // }

    private static readonly JwtTokenReply ReplyToken =
        new()
        {
            AccessToken = "access_token",
            RefreshToken = "refresh_token",
            Scope = "public",
            Type = "Bearer",
            CreatedAt = 1677143380,
            ExpiresIn = 21600,
        };

    [Fact]
    public void Test_GenerateAuthorizeUrl()
    {
        var res = OAuth.GenerateAuthorizeUrl("QMZ4D7BLeehAXINE9NZ8dho2i5AYVTbfqJ8PhQ4eUrE", "00000000-0000-0000-0000-000000000000");
        res.Should().Be(ExpectedAuthURL);
    }
}
