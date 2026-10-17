namespace Atua.Api.Application.Triagens;

/// <summary>
/// TEMPORÁRIO — trava do teste E2E controlado do Incremento 5 (ver
/// docs/implementation/PLANO-triagem-templates-channel-baileys.md, seção
/// "Trava temporária do E2E"). Remover após o E2E ser validado.
///
/// - Somente as OS listadas em <see cref="AllowedWorkOrderIds"/> ou cujo
///   identificador externo começa com <see cref="SyntheticWorkOrderProviderIdPrefix"/>
///   (OS sintéticas do E2E repetível) são reivindicadas da outbox; as demais
///   permanecem <c>Pending</c>, sem triagem e sem mensagem (fail-closed).
/// - Todo comando de canal usa <see cref="FixedRecipientPhone"/> como
///   destinatário, ignorando o telefone real da OS.
/// </summary>
public static class TriagemTestSafeguard
{
    public const string FixedRecipientPhone = "+5584994241248";

    public const string SyntheticWorkOrderProviderIdPrefix = "E2E-";

    public static readonly Guid[] AllowedWorkOrderIds =
    [
        Guid.Parse("01a0f938-dabc-7ca6-b95c-6f0415aa466e"),
    ];
}
