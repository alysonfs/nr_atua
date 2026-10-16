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

    /// <summary>
    /// Mesma semântica de <see cref="GetOrAddAsync"/>, mas persiste
    /// <paramref name="channelCommand"/> na mesma transação quando
    /// <paramref name="candidate"/> é efetivamente inserida (ADR-042: "A
    /// Master API persistirá o comando de canal na mesma transação que
    /// registra o aceite idempotente da triagem"). Quando a chamada perde a
    /// corrida de criação (outro processo já registrou a triagem e seu
    /// comando), <paramref name="channelCommand"/> nunca é persistido — o
    /// comando já existente pertence exclusivamente ao vencedor da corrida.
    /// </summary>
    Task<TriagemGetOrAddResult> GetOrAddWithChannelCommandAsync(TriagemIdempotencyKey key, Triagem candidate,
        TriagemChannelCommand channelCommand, CancellationToken cancellationToken);

    /// <summary>
    /// Consulta a triagem por identificador interno, sem efeito colateral.
    /// Usado pelo <c>complete</c> do comando de canal (ADR-042) para aplicar
    /// a transição de estado correspondente. Retorna <c>null</c> quando não
    /// encontrada.
    /// </summary>
    Task<Triagem?> FindByIdAsync(Guid id, CancellationToken cancellationToken);

    /// <summary>
    /// Consulta a triagem já registrada para <paramref name="key"/>, sem
    /// efeito colateral (ADR-039: reentrega da mesma chave retorna o
    /// resultado conhecido). Retorna <c>null</c> quando nenhuma triagem
    /// existir ainda para a chave.
    /// </summary>
    /// <param name="key">Chave estável de idempotência (tenant + integração + OS).</param>
    /// <param name="cancellationToken">
    /// Quando cancelado antes da conclusão da consulta, nenhum resultado
    /// deve ser observado como efeito desta chamada.
    /// </param>
    Task<Triagem?> FindByKeyAsync(TriagemIdempotencyKey key, CancellationToken cancellationToken);

    /// <summary>
    /// Persiste uma transição de estado já aplicada em memória a
    /// <paramref name="triagem"/> (ex.: <c>MarcarContatoIniciado</c>,
    /// <c>MarcarFalhaDeContato</c>), usando concorrência otimista via
    /// <see cref="Triagem.ConcurrencyToken"/>.
    ///
    /// Em conflito — outro processo já persistiu uma transição concorrente
    /// para a mesma triagem — lança <see cref="TriagemConcurrencyConflictException"/>.
    /// O chamador nunca deve repetir o efeito externo (chamada ao canal) ao
    /// tratar esse conflito: deve apenas recarregar a instância vencedora
    /// (ex.: via <see cref="FindByKeyAsync"/>) e adotar o resultado já
    /// persistido — ver <c>StartTriagemUseCase</c>.
    /// </summary>
    /// <param name="triagem">
    /// Instância já tracked/conhecida por este repositório (retornada por
    /// <see cref="GetOrAddAsync"/> ou <see cref="FindByKeyAsync"/>), com a
    /// transição já aplicada pelos métodos de domínio de <see cref="Triagem"/>.
    /// </param>
    Task SaveTransitionAsync(Triagem triagem, CancellationToken cancellationToken);
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
