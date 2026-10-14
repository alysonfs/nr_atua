using System.ComponentModel.DataAnnotations;
using Atua.Api.Application.Triagens;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;

namespace Atua.Api.Tests;

/// <summary>
/// Testes de validação de <see cref="TriagemOutboxOptions"/> (ADR-041), pelo
/// mesmo pipeline registrado em <c>Program.cs</c>
/// (<c>ValidateDataAnnotations</c> + <c>ValidateOnStart</c> +
/// <see cref="TriagemOutboxOptionsValidator"/>) — sem defaults silenciosos
/// inválidos.
/// </summary>
public class TriagemOutboxOptionsTests
{
    [Fact]
    public void ValoresPadraoSaoValidos()
    {
        var options = new TriagemOutboxOptions();

        Assert.Equal(5, options.PollingIntervalSeconds);
        Assert.Equal(20, options.BatchSize);
        Assert.Equal(60, options.LeaseSeconds);
        Assert.Equal(10, options.MaxAttempts);
        Assert.Equal(5, options.BaseRetrySeconds);
        Assert.Equal(300, options.MaxRetrySeconds);

        BuildValidatedOptions(ConfigDictionary());
    }

    [Theory]
    [InlineData(nameof(TriagemOutboxOptions.PollingIntervalSeconds), "0")]
    [InlineData(nameof(TriagemOutboxOptions.BatchSize), "0")]
    [InlineData(nameof(TriagemOutboxOptions.LeaseSeconds), "0")]
    [InlineData(nameof(TriagemOutboxOptions.MaxAttempts), "0")]
    [InlineData(nameof(TriagemOutboxOptions.BaseRetrySeconds), "0")]
    [InlineData(nameof(TriagemOutboxOptions.MaxRetrySeconds), "0")]
    public void RejeitaValoresForaDoIntervaloPermitido(string propertyName, string invalidValue)
    {
        var config = ConfigDictionary();
        config[propertyName] = invalidValue;

        var exception = Assert.Throws<OptionsValidationException>(() => BuildValidatedOptions(config));
        Assert.Contains(propertyName, exception.Message);
    }

    [Fact]
    public void RejeitaMaxRetrySecondsMenorQueBaseRetrySeconds()
    {
        var config = ConfigDictionary();
        config[nameof(TriagemOutboxOptions.BaseRetrySeconds)] = "100";
        config[nameof(TriagemOutboxOptions.MaxRetrySeconds)] = "50";

        var exception = Assert.Throws<OptionsValidationException>(() => BuildValidatedOptions(config));
        Assert.Contains(nameof(TriagemOutboxOptions.MaxRetrySeconds), exception.Message);
    }

    private static Dictionary<string, string?> ConfigDictionary() => new()
    {
        [nameof(TriagemOutboxOptions.PollingIntervalSeconds)] = "5",
        [nameof(TriagemOutboxOptions.BatchSize)] = "20",
        [nameof(TriagemOutboxOptions.LeaseSeconds)] = "60",
        [nameof(TriagemOutboxOptions.MaxAttempts)] = "10",
        [nameof(TriagemOutboxOptions.BaseRetrySeconds)] = "5",
        [nameof(TriagemOutboxOptions.MaxRetrySeconds)] = "300",
    };

    private static TriagemOutboxOptions BuildValidatedOptions(Dictionary<string, string?> values)
    {
        var normalized = values.ToDictionary(
            pair => $"{TriagemOutboxOptions.SectionName}:{pair.Key}",
            pair => pair.Value);

        var configuration = new ConfigurationBuilder().AddInMemoryCollection(normalized).Build();

        var services = new ServiceCollection();
        services.AddOptions<TriagemOutboxOptions>()
            .Bind(configuration.GetSection(TriagemOutboxOptions.SectionName))
            .ValidateDataAnnotations()
            .ValidateOnStart();
        services.AddSingleton<IValidateOptions<TriagemOutboxOptions>, TriagemOutboxOptionsValidator>();

        using var provider = services.BuildServiceProvider();

        // ValidateOnStart só dispara via IStartupValidator/hosted services reais;
        // em teste de unidade, força a validação lendo .Value, que aciona o
        // pipeline de IValidateOptions (DataAnnotations + validador customizado)
        // da mesma forma.
        return provider.GetRequiredService<IOptions<TriagemOutboxOptions>>().Value;
    }
}
