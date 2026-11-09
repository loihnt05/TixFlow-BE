using System.Text.Json;
using Microsoft.AspNetCore.Http;
using TixFlow.Api.Contracts;

namespace TixFlow.Api.Tests;

public class ApiResponseTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    [Fact]
    public void SuccessKeepsExistingDataContract()
    {
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(new ApiResponse<object>(new { id = 7 }), Json));
        Assert.Equal(7, body.RootElement.GetProperty("data").GetProperty("id").GetInt32());
        Assert.True(body.RootElement.GetProperty("success").GetBoolean());
        Assert.False(string.IsNullOrWhiteSpace(body.RootElement.GetProperty("message").GetString()));
    }

    [Theory]
    [InlineData(400)] [InlineData(401)] [InlineData(403)] [InlineData(404)]
    [InlineData(409)] [InlineData(422)] [InlineData(429)] [InlineData(500)] [InlineData(503)]
    public void ErrorKeepsProblemDetailsAndCorrelation(int status)
    {
        var context = new DefaultHttpContext { TraceIdentifier = "test-trace" };
        context.Request.Path = "/api/v1/holds";
        var error = ApiErrorResponse.Create(context, status);
        using var body = JsonDocument.Parse(JsonSerializer.Serialize(error, Json));
        Assert.Equal(status, body.RootElement.GetProperty("status").GetInt32());
        Assert.Equal(error.Detail, body.RootElement.GetProperty("message").GetString());
        Assert.Equal("test-trace", body.RootElement.GetProperty("traceId").GetString());
        Assert.False(body.RootElement.GetProperty("success").GetBoolean());
        Assert.DoesNotContain("email", error.Message);
        Assert.Equal(status, ((IStatusCodeHttpResult)error.ToResult()).StatusCode);
    }

    [Fact]
    public void ValidationRetainsFieldErrorsAndCustomMessage()
    {
        var errors = new Dictionary<string,string[]> { ["quantity"] = ["Must be positive."] };
        var result = ApiErrorResponse.Create(new DefaultHttpContext(), 422, "VALIDATION_ERROR", "Check your input.", errors);
        Assert.Equal("Check your input.", result.Message);
        Assert.Equal(errors, result.Errors);
        Assert.Throws<ArgumentOutOfRangeException>(() => ApiErrorResponse.Create(new DefaultHttpContext(), 200));
    }
}
