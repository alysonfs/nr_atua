using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens.Contracts;

/// <summary>
/// Contrato de persistência/deduplicação do início da triagem (ADR-039,
/// seção "Decisão proposta", itens 1 e 2; arquitetura, seção 6). Define
/// apenas a operação atômica necessária para que o gatilho de início seja
/// aceito uma única vez por <see cref="TriagemIdempotencyKey"/> — o
/// armazenamento físico (tabela, coleção, índice) fica para incremento
/// futuro e não é definido por este contrato.
/// </summary>
public interface ITriagemRepository
{
    /// <summary>
    /// Obtém a triagem já registrada para <paramref name="key"/> ou registra
    /// <paramref name="candidate"/> atomicamente quando ainda não existir
    /// nenhum registro para a chave. A operação deve ser segura sob
    /// concorrência: chamadas concorrentes com a mesma chave devem resultar
    /// em exatamente um registro criado (<see cref="TriagemGetOrAddResult.Created"/>
    /// <c>true</c>) e todas as demais devem observar a mesma instância
    /// persistida, sem sobrescrever estado já avançado.
    /// </summary>
    /// <param name="key">Chave estável de idempotência (tenant + integração + OS).</param>
    /// <param name="candidate">
    /// Triagem candidata, já construída em <see cref="ETriagemStatus.AguardandoEnvio"/>,
    /// usada somente quando nenhum registro existir ainda para <paramref name="key"/>.
    /// </param>
    /// <param name="cancellationToken">
    /// Quando cancelado antes da conclusão da operação atômica, nenhum
    /// registro deve ser produzido/observado como efeito desta chamada.
    /// </param>
    Task<TriagemGetOrAddResult> GetOrAddAsync(TriagemIdempotencyKey key, Triagem candidate,
        CancellationToken cancellationToken);
}

/// <summary>
/// Resultado da operação atômica get-or-add de <see cref="ITriagemRepository.GetOrAddAsync"/>.
/// </summary>
/// <param name="Triagem">Instância persistida — a candidata quando criada, ou a existente.</param>
/// <param name="Created">
/// <c>true</c> quando esta chamada foi responsável por registrar a
/// candidata; <c>false</c> quando já existia um registro para a chave.
/// </param>
public sealed record TriagemGetOrAddResult(Triagem Triagem, bool Created);
