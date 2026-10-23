using System.ComponentModel.DataAnnotations;

namespace Atua.Api.Infrastructure.Llm;

/// <summary>
/// Configurações do adaptador Bedrock de <c>ITriagemLlmClient</c> (ADR-048),
/// seção <c>TriagemLlm</c>. Validadas no startup (<c>ValidateDataAnnotations</c>
/// + <c>ValidateOnStart</c> em <c>Program.cs</c>) — nenhum valor inválido é
/// silenciosamente aceito.
///
/// <see cref="ModelId"/> não tem valor padrão de produção: a escolha do
/// modelo nativo em `sa-east-1` é pendência explícita do ADR-048 e do PO
/// (`triagem-llm-conducao.md` §7). O valor usado em
/// <c>appsettings.Development.json</c> é apenas um exemplo para
/// desenvolvimento, não uma decisão de produto.
/// </summary>
public sealed class TriagemLlmOptions
{
    public const string SectionName = "TriagemLlm";

    /// <summary>
    /// Liga o adaptador Bedrock real. Quando <c>false</c> (padrão), o DI
    /// registra <c>FakeTriagemLlmClient</c> — nenhuma chamada à AWS é feita.
    /// </summary>
    public bool Enabled { get; init; }

    /// <summary>Região do Bedrock — ADR-048 exige `sa-east-1` (modelo nativo na região, sem inferência entre regiões).</summary>
    [Required]
    public string Region { get; init; } = "sa-east-1";

    /// <summary>
    /// Identificador do modelo Bedrock. Sem valor padrão — decisão do PO
    /// (ADR-048, pendência). Obrigatório apenas quando <see cref="Enabled"/>
    /// é <c>true</c> (ver <see cref="TriagemLlmOptionsValidator"/>).
    /// </summary>
    public string ModelId { get; init; } = "";

    /// <summary>Teto de tokens de saída por chamada — contém custo e tamanho da resposta.</summary>
    [Range(1, 8_192)]
    public int MaxOutputTokens { get; init; } = 512;

    /// <summary>Tempo limite por chamada ao Bedrock, em segundos.</summary>
    [Range(1, 120)]
    public int TimeoutSeconds { get; init; } = 20;
}
