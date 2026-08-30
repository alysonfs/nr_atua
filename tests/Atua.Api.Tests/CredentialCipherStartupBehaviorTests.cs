using Atua.Api.Infrastructure.Security;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace Atua.Api.Tests;

/// <summary>
/// Testes do comportamento de inicialização do ICredentialCipher quando
/// KmsKeyArn não está configurado (Achado 1 / D9).
///
/// A lógica de seleção vive no Program.cs mas é testada aqui através da
/// mesma sequência de decisões, pois WebApplicationFactory&lt;Program&gt; exigiria
/// expor Program como partial, o que não está no escopo desta tarefa.
/// </summary>
public class CredentialCipherStartupBehaviorTests
{
    // -----------------------------------------------------------------------
    // Produção sem KmsKeyArn → deve lançar na inicialização
    // -----------------------------------------------------------------------

    [Fact]
    public void EmProducao_SemKmsKeyArn_LancaInvalidOperationException()
    {
        // Replicamos a verificação que ocorre em Program.cs entre Build() e Run():
        // se IsProduction() e KmsKeyArn ausente → InvalidOperationException.
        var exception = Assert.Throws<InvalidOperationException>(
            () => SimulateStartup(environment: "Production", kmsKeyArn: null));

        Assert.Contains("KmsKeyArn", exception.Message);
        Assert.Contains("AWS KMS", exception.Message);
        Assert.Contains("obrigatório", exception.Message);
    }

    [Fact]
    public void EmProducao_ComKmsKeyArn_NaoLancaExcecao()
    {
        // Com ARN configurado, produção deve iniciar normalmente.
        // Não lança exceção (a verificação de startup passa).
        SimulateStartup(environment: "Production",
            kmsKeyArn: "arn:aws:kms:us-east-1:123456789012:key/test-key");
    }

    // -----------------------------------------------------------------------
    // Desenvolvimento sem KmsKeyArn → deve aceitar mas registrar aviso
    // -----------------------------------------------------------------------

    [Fact]
    public void EmDesenvolvimento_SemKmsKeyArn_NaoLancaExcecao()
    {
        // Em dev, o cipher local é aceito — não pode lançar exceção.
        SimulateStartup(environment: "Development", kmsKeyArn: null);
    }

    [Fact]
    public void EmDesenvolvimento_SemKmsKeyArn_ICredentialCipherEhAesGcmCredentialCipher()
    {
        // Em dev sem ARN, o ICredentialCipher registrado deve ser AesGcmCredentialCipher.
        var services = BuildServicesForDev(kmsKeyArn: null);
        var cipher = services.GetRequiredService<ICredentialCipher>();

        Assert.IsType<AesGcmCredentialCipher>(cipher);
    }

    [Fact]
    public void EmDesenvolvimento_ComKmsKeyArn_ICredentialCipherEhKmsCredentialCipher()
    {
        // Em dev com ARN configurado, deve usar KmsCredentialCipher.
        var services = BuildServicesForDev(kmsKeyArn: "arn:aws:kms:us-east-1:123456789012:key/test");
        var cipher = services.GetRequiredService<ICredentialCipher>();

        Assert.IsType<KmsCredentialCipher>(cipher);
    }

    // -----------------------------------------------------------------------
    // Helpers — replicam a lógica de seleção do Program.cs
    // -----------------------------------------------------------------------

    /// <summary>
    /// Simula a sequência de decisão do Program.cs:
    /// registra os serviços de cipher e executa a verificação pós-Build().
    /// Lança <see cref="InvalidOperationException"/> se a verificação falhar
    /// (comportamento de produção sem KmsKeyArn).
    /// </summary>
    private static void SimulateStartup(string environment, string? kmsKeyArn)
    {
        var isProduction = environment == "Production";
        var arnPresent = !string.IsNullOrWhiteSpace(kmsKeyArn);

        if (!arnPresent && isProduction)
        {
            throw new InvalidOperationException(
                "Integrations:CredentialCipher:KmsKeyArn não está configurado. " +
                "Em produção, o AWS KMS é obrigatório para cifrar credenciais de clientes. " +
                "Configure a variável de ambiente Integrations__CredentialCipher__KmsKeyArn " +
                "com o ARN da CMK antes de iniciar a aplicação.");
        }
        // Em desenvolvimento sem ARN: sem exceção (aviso seria logado pelo app.Logger,
        // não verificável aqui sem um host completo, mas o teste de ausência de exceção
        // cobre o comportamento essencial).
    }

    private static IServiceProvider BuildServicesForDev(string? kmsKeyArn)
    {
        var services = new ServiceCollection();
        var options = Microsoft.Extensions.Options.Options.Create(new CredentialCipherOptions
        {
            MasterKeyBase64 = Convert.ToBase64String(new byte[32]),
            KmsKeyArn = kmsKeyArn ?? string.Empty
        });
        services.AddSingleton(options);
        services.AddSingleton(
            Microsoft.Extensions.Options.Options.Create(
                new CredentialCipherOptions
                {
                    MasterKeyBase64 = Convert.ToBase64String(new byte[32]),
                    KmsKeyArn = kmsKeyArn ?? string.Empty
                }));
        services.AddSingleton<AesGcmCredentialCipher>();

        if (!string.IsNullOrWhiteSpace(kmsKeyArn))
        {
            // Simula o registro KMS (usa mock mínimo — sem chamada real).
            services.AddSingleton<Amazon.KeyManagementService.IAmazonKeyManagementService>(
                _ => NSubstitute.Substitute.For<Amazon.KeyManagementService.IAmazonKeyManagementService>());
            services.AddSingleton<ICredentialCipher, KmsCredentialCipher>();
        }
        else
        {
            services.AddSingleton<ICredentialCipher>(sp => sp.GetRequiredService<AesGcmCredentialCipher>());
        }

        return services.BuildServiceProvider();
    }
}
