using Atua.Api.Application.Billing;
using Atua.Api.Application.Identity;
using Atua.Api.Endpoints;
using Atua.Api.Infrastructure.Persistence;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Atua.Api.Tests;

public class TrialEndpointSecurityTests
{
    [Fact]
    public async Task PublicaSomenteElegibilidadeInternaProtegida()
    {
        var builder = WebApplication.CreateBuilder();
        builder.Services.AddDbContext<AtuaDbContext>(options =>
            options.UseInMemoryDatabase(Guid.NewGuid().ToString()));
        builder.Services.AddSingleton(TimeProvider.System);
        builder.Services.AddScoped<TrialEligibilityService>();
        builder.Services.AddScoped<TimeZonePreferenceService>();
        builder.Services.AddAuthorization();
        await using var app = builder.Build();
        app.MapTrialEndpoints();
        await app.StartAsync();

        var routes = app.Services.GetRequiredService<IEnumerable<EndpointDataSource>>()
            .SelectMany(source => source.Endpoints).OfType<RouteEndpoint>().ToArray();
        Assert.DoesNotContain(routes, route => route.RoutePattern.RawText!.Contains("/api/internal/trials"));

        var eligibility = Assert.Single(routes, route =>
            route.RoutePattern.RawText == "/api/internal/collector/eligibility");
        Assert.Contains(eligibility.Metadata.GetOrderedMetadata<IAuthorizeData>(),
            metadata => metadata.Policy == "CollectorEligibility");
    }
}
