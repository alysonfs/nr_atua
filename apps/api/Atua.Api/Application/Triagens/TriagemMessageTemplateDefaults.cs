using Atua.Api.Domain.Triagens;

namespace Atua.Api.Application.Triagens;

internal static class TriagemMessageTemplateDefaults
{
    public const string InitialContact = """
        📩 Triagem da OS {{numero_os}}

        Olá! Somos da {{nome_posto}}, assistência técnica autorizada
        {{fabricante}}. Recebemos a ordem de serviço do produto {{produto}} e
        precisamos realizar uma triagem antes do atendimento, para aumentar a chance
        de resolver o problema na primeira visita.

        Por favor, envie:
        ✅ descrição detalhada do defeito, incluindo eventual código de erro;
        📄 foto legível da nota fiscal;
        🏷️ foto legível da etiqueta com modelo e número de série;
        📷 pelo menos uma foto ou vídeo mostrando o defeito;
        📍 endereço completo onde o produto está instalado.

        Se possível, envie também um ponto de referência e a localização do endereço.

        Você pode enviar as informações em mensagens separadas. Quanto mais detalhes
        recebermos, mais rápido poderemos analisar o atendimento.

        Se as informações obrigatórias não forem enviadas em até 48 horas, a análise
        poderá atrasar e nossa equipe poderá entrar em contato para continuar o
        atendimento. A ordem de serviço não será cancelada automaticamente por falta
        dessas informações.
        """;

    public const string Reminder24Hours = """
        Olá! Para continuarmos a triagem da OS {{numero_os}}, ainda precisamos receber:
        {{itens_pendentes}}

        Você pode enviar os itens em mensagens separadas. Assim que recebermos as
        informações obrigatórias, seguiremos com a análise.
        """;

    public const string Reminder36Hours = """
        Olá! A triagem da OS {{numero_os}} continua incompleta. Ainda faltam:
        {{itens_pendentes}}

        O prazo de 48 horas termina em aproximadamente 12 horas. A ordem de serviço
        não será cancelada automaticamente por falta dessas informações, mas a
        ausência dos dados poderá atrasar o atendimento e nossa equipe poderá entrar
        em contato.
        """;

    public static IReadOnlyList<TriagemMessageTemplate> CreatePublishedTemplates(
        Guid tenantId, Guid integrationId, DateTimeOffset now) =>
    [
        TriagemMessageTemplate.CreatePublished(Guid.CreateVersion7(), tenantId, integrationId,
            ETriagemMessageTemplateType.InitialContact, 1, InitialContact, isActive: true, now),
        TriagemMessageTemplate.CreatePublished(Guid.CreateVersion7(), tenantId, integrationId,
            ETriagemMessageTemplateType.Reminder24Hours, 1, Reminder24Hours, isActive: true, now),
        TriagemMessageTemplate.CreatePublished(Guid.CreateVersion7(), tenantId, integrationId,
            ETriagemMessageTemplateType.Reminder36Hours, 1, Reminder36Hours, isActive: true, now)
    ];
}
