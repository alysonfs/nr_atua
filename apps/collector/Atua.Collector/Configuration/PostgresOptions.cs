namespace Atua.Collector.Configuration;

/// <summary>
/// Opções de conexão com o PostgreSQL (ADR-023).
/// Usada exclusivamente pelo consumer de Change Streams — o loop de coleta
/// não acessa PostgreSQL.
/// </summary>
public sealed class PostgresOptions
{
    public const string SectionName = "Postgres";

    /// <summary>
    /// Connection string do PostgreSQL.
    /// Em produção, fornecida via variável de ambiente ou Secrets Manager.
    /// </summary>
    public string ConnectionString { get; init; } = "Host=localhost;Port=5432;Database=atua;Username=atua";
}
