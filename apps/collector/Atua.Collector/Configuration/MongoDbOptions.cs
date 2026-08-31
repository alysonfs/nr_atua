namespace Atua.Collector.Configuration;

/// <summary>
/// Opções de conexão com o MongoDB Atlas (ADR-012).
/// A connection string deve ser fornecida pela variável de ambiente
/// <c>MONGODB_URI</c> ou pela seção <c>MongoDB</c> do appsettings.
/// </summary>
public sealed class MongoDbOptions
{
    public const string SectionName = "MongoDB";

    /// <summary>
    /// Connection string do MongoDB Atlas.
    /// Em produção, fornecida via variável de ambiente <c>MONGODB_URI</c>
    /// (secret <c>atua/mongodb-atlas</c> no Secrets Manager — ADR-012).
    /// </summary>
    public string ConnectionString { get; init; } = "PLACEHOLDER_MONGODB_URI";

    /// <summary>Nome do banco de dados (ADR-021: <c>atua</c>).</summary>
    public string DatabaseName { get; init; } = "atua";
}
