namespace Atua.Collector.Contracts;

/// <summary>
/// Credenciais em claro retornadas pelo claim (ADR-021/D9-B).
/// NUNCA logar Password nem BaseUrl completa.
/// </summary>
public sealed record CredentialPayload(
    string Username,
    string Password,
    string? BaseUrl);
