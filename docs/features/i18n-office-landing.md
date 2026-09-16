# Internacionalizacao do Office e da Landing

## Escopo

O Office e a Landing suportam os locales BCP 47:

- `pt-BR`;
- `en-US`;
- `es-AR`.

O primeiro acesso usa o idioma do navegador quando ele pertence a uma das
familias suportadas. Idiomas nao suportados usam `pt-BR`.

## Selecao e persistencia

A Landing e o Office possuem seletores acessiveis com bandeira, nome do idioma,
estado selecionado e suporte a teclado.

A Landing persiste a escolha no navegador. Quando a escolha foi explicita, os
links de login e cadastro incluem o parametro `locale`.

O Office:

1. consome e remove o `locale` da URL;
2. aplica a escolha localmente;
3. marca a preferencia para sincronizacao;
4. depois do login ou da restauracao da sessao, salva a escolha no perfil;
5. quando nao existe escolha pendente, recupera o idioma salvo no perfil.

Uma falha de sincronizacao nao impede o uso da aplicacao no idioma local. O
Office preserva a alteracao e oferece nova tentativa.

## Contrato da API

Os endpoints exigem autenticacao `BrowserSession`.

```http
GET /api/users/me/locale
```

Resposta:

```json
{
  "locale": "pt-BR"
}
```

```http
PUT /api/users/me/locale
Content-Type: application/json

{
  "locale": "es-AR"
}
```

O `PUT` retorna `204 No Content`. Valores fora da lista suportada retornam:

```json
{
  "error": "invalid_locale"
}
```

## Status do iService

Os valores recebidos do provedor permanecem inalterados na API e na
persistencia. A traducao acontece apenas no Office.

O resumo mensal possui traducoes para:

- `Request Cancel`;
- `assigned`;
- `Exchange Proposal`;
- `closed`;
- `cancelled`;
- `Payment Rejected`;
- `Request to Explain`;
- `pending`;
- `Payment Approved`.

A comparacao e case-insensitive. Status desconhecidos sao exibidos no formato
original para evitar perda de informacao.

## Validacao

- Office: 117 testes aprovados, lint sem erros e build aprovado;
- Landing: 14 testes aprovados, lint e build aprovados;
- API: 17 testes direcionados de locale aprovados e build sem erros;
- revisao de QA aprovada sem bugs bloqueantes.

A verificacao visual responsiva deve ser repetida quando o browser integrado
estiver disponivel.
