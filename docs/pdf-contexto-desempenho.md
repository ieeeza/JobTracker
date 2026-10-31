# Contexto e desempenho da análise textual — 31/10/2026

## Fase 1 — diagnóstico reproduzido

No código recebido, `OllamaPdfContextBudget` usa:

`UTF8(SystemPrompt + UserPrefix) + 1024 + 2 * UTF8(texto)`.

Leitura do contrato compilado em Release reproduziu exatamente o relato:

| Parcela | Unidades contabilizadas como tokens |
|---|---:|
| Texto de entrada (bytes UTF-8) | 5.126 |
| Reserva de saída, igual à entrada em bytes | 5.126 |
| Instruções textuais + separador do schema | 1.984 |
| JSON Schema dentro do system prompt | 1.704 |
| Prefixo da mensagem do usuário | 70 |
| Margem fixa de mensagens/JSON | 1.024 |
| Total | **15.034** |

Não havia tokenização nem conversão proporcional: um byte era tratado como um
token. O schema também era enviado em `format`, mas esse segundo envio não era
somado pelo cálculo. `maxLength` e `maxItems` não eram tratados como consumo
obrigatório. A reserva de saída duplicava os bytes do texto, não o contexto
inteiro. A expansão usava o total calculado até `MaxContextTokens`; PDFs não
recebiam limites de pixels/imagens, mas compartilhavam modelo e contexto visual.

O prompt compartilhava o schema para orientar os campos e restringir a geração.
O cliente usava uma inferência por vez, uma consulta de tags por análise e cache
por digest/modelo/contexto. Não havia retry no PDF. O cálculo de contexto e a
duplicação integral do schema no prompt eram candidatos à correção; não foi
atribuída lentidão ao semáforo sem medição.

Consulta local confirmou `qwen3-vl:4b-instruct`, `qwen3-vl:8b-instruct`,
`qwen3-vl:8b` e `gpt-oss:20b` instalados. `/api/show` do 4B informou capacidades
`completion`, `vision` e `tools`, sem metadados remotos. Nenhum modelo foi instalado.

Build inicial: `dotnet build JobTracker/JobTracker.slnx -c Release --no-restore -v minimal`,
aprovado, zero erros e avisos. Alterações locais anteriores foram preservadas.

Referências de protocolo: [chat e métricas em nanossegundos](https://docs.ollama.com/api/chat),
[structured outputs](https://docs.ollama.com/capabilities/structured-outputs).
O schema em `format` restringe a resposta; a documentação recomenda também
orientar o modelo pelo prompt. A revisão textual deve preservar a explicação
dos campos mesmo que remova a cópia integral do schema.
