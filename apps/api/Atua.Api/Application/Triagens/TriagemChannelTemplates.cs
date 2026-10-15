namespace Atua.Api.Application.Triagens;

/// <summary>
/// Identificador e versão abstratos do template inicial de contato (comando
/// <c>SendTemplate</c>, arquitetura seção 5.3). O texto aprovado pelo PO
/// ainda não foi definido (arquitetura, seção 3: "o texto do template [...]
/// depende de revisão do PO antes da implementação e validação de envios
/// reais") — este incremento transporta apenas um identificador/versão
/// abstratos ao canal, sem qualquer texto literal de fornecedor ou de
/// conteúdo aprovado.
/// </summary>
internal static class TriagemChannelTemplates
{
    /// <summary>Identificador abstrato do template inicial de contato da triagem.</summary>
    public const string InitialContactTemplateId = "triagem-contato-inicial";

    /// <summary>Versão do template — permite evoluir o texto aprovado pelo PO sem alterar o contrato do canal.</summary>
    public const int InitialContactTemplateVersion = 1;
}
