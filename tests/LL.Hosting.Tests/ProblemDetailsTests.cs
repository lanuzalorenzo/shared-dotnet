using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace LL.Hosting.Tests;

public sealed class ProblemDetailsTests
{
    [Fact]
    public async Task Turns_an_unhandled_exception_into_problem_details()
    {
        await using var app = await TestHost.StartAsync(
            configureServices: builder => builder.Services.AddApiProblemDetails(),
            configurePipeline: app =>
            {
                app.UseApiProblemDetails();
                app.MapGet("/boom", () => { throw new InvalidOperationException("fallo de prueba"); });
            });

        using var client = app.GetTestClient();
        using var response = await client.GetAsync("/boom");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);
    }
}
