namespace Atua.Channel.Contracts;

/// <summary>
/// Espelha <c>CompleteChannelCommandRequest</c> da Master API (ADR-042,
/// <c>POST /api/internal/triagens/canal/complete</c>). <see cref="Outcome"/>
/// deve ser <c>"Success"</c>, <c>"TransientFailure"</c> ou
/// <c>"PermanentFailure"</c>. Erros devem ser códigos/descrições seguros —
/// nunca conteúdo de mensagem ou dado sensível.
///
/// <see cref="ClaimedAtUtc"/> é o token de lease devolvido pelo claim
/// (<see cref="ClaimChannelCommandResponse.ClaimedAtUtc"/>) — obrigatório
/// em toda chamada de <c>complete</c>, para que a Master API só aplique o
/// efeito quando esta ainda for a lease vigente (ADR-042, correção de
/// concorrência: uma lease expirada e substituída por outro claim nunca
/// deve ser completada pelo titular antigo).
/// </summary>
public sealed record CompleteChannelCommandRequest(
    Guid CommandId,
    DateTimeOffset ClaimedAtUtc,
    string Outcome,
    string? ErrorCode,
    string? ErrorDescription);
