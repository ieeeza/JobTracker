# Editor e contexto automático de PDFs — 01/11/2026

## Edição e limpeza

`PdfImportViewModel` mantém `ExtractedText` como cópia original do PdfPig e
`EditedText` como cópia editável em memória. Apenas `EditedText` é analisado.
O PDF físico não é escrito. O editor usa seleção, colagem, desfazer/refazer nativos
do WPF. Restaurar recupera o original; selecionar ou extrair novamente reinicia
as cópias e a prévia. Toda edição invalida a análise anterior e seu cadastro.

`PdfTextCleanup`, na Application, remove somente controles isolados conhecidos:
preferências de anúncios/cookies, a sequência completa de cinco itens da barra
de navegação do LinkedIn e links repetidos de navegação com destinos específicos.
Não há remoção por palavras genéricas, domínios inteiros, classificadores ou IA.
Trechos ambíguos ficam intactos. “Desfazer limpeza” recupera o texto imediatamente
anterior, incluindo as edições manuais; uma edição posterior encerra essa opção.
Restaurar o original continua disponível. O usuário sempre pode revisar o resultado.

## Estimativa

O estimador anterior foi mantido: letras/espaços latinos usam três bytes UTF-8
por token; demais caracteres usam o fallback conservador por byte. A reserva de
saída é `max(512, tokensTexto + 256)` e a margem de segurança é de 20% do subtotal.
São contabilizados o texto, as instruções, o prefixo (inclusive o cabeçalho de
continuação quando houver), 96 tokens de enquadramento e a saída prevista.
Os máximos dos campos do schema não são tratados como saída obrigatória.

O schema existente continua em `format`, sem cópia nas mensagens; portanto sua
contribuição adicional ao prompt é zero. Os nomes e regras dos campos permanecem
nas instruções. Não foi introduzido outro schema ou DTO de extração.

A [API pública consultada](https://docs.ollama.com/llms.txt) não lista um endpoint
de tokenização do modelo. Não há tokenizer compatível configurado no projeto.
O cálculo é identificado como estimativa, sem alegar contagem exata. Os coeficientes
estão centralizados em `OllamaPdfContextBudget`; os logs de tokens efetivos permitem
recalibrá-los. A calibração anterior está em `pdf-contexto-desempenho.md`.

Na UI, um debounce de 400 ms cancela estimativas ultrapassadas. O planejamento
executa fora da thread da interface, após carregar as preferências. Não há chamada
HTTP ou inferência a cada edição. A prévia informa caracteres, bytes, tokens
estimados, contexto e número de blocos. O limite do modelo é verificado na análise.

## Contexto e limites

`OllamaPdfPlan` escolhe 4.096 ou 8.192 tokens, ou o teto menor quando configurado.
O limite efetivo é o menor entre 8.192, o máximo PDF salvo e o contexto textual do
modelo informado por `/api/show` (`model_info`, arquitetura e `context_length`).
Metadados ausentes/inválidos impedem a análise com diagnóstico explícito.
`PdfContextTokens` permanece desserializável para compatibilidade com preferências
antigas, mas não obriga uma janela maior; a UI deixa de pedir o contexto inicial.
Valores máximos antigos são preservados, sem autorizar expansões automáticas acima
de 8.192. Nenhuma configuração global do Ollama ou do Windows é modificada.

Limites do processamento PDF:

- 120.000 unidades UTF-16 no editor para análise e até 12 blocos.
- 15 minutos para a operação, incluindo espera, metadados, cache e inferências.
- Cada chamada de geração tem até 300 segundos, ou o timeout salvo se menor.
- Uma inferência por vez, usando o semáforo, o cliente HTTP e a troca de modelos existentes.
- Cancelamento interrompe a sequência; não há retry nem expansão enorme automática.

Esses limites reduzem o uso de memória no computador de referência de 16 GB de
RAM/8 GB de VRAM, sem garantir disponibilidade para todo modelo instalado.
Falhas de memória continuam sendo tratadas pelo cliente compartilhado.

## Blocos e consolidação

O plano completo é validado antes da geração. O divisor encontra o maior prefixo
que cabe no orçamento, preferindo fim de parágrafo, depois fim de linha e, por
último, espaço entre palavras. Um trecho indivisível que excede o teto é recusado.
Não corta caracteres nem descarta sufixos: concatenar os blocos recupera exatamente
o texto editado. Cabeçalhos conhecidos de seções são transportados como contexto
explícito para distinguir, por exemplo, requisitos desejáveis da continuação de
requisitos obrigatórios. Cabeçalhos encontrados no novo trecho têm precedência.

Cada bloco usa `OllamaJobData`, mensagens sem imagens, `SendAsync<T>` e o schema
compartilhado. Fragmentos podem não conter identidade; não recebem identidade
inventada de outros blocos. O cache existente distingue texto, prefixo, modo de
fragmento, digest, prompt, schema e opções efetivas. Um digest diferente durante
a sequência impede combinar resultados de modelos diferentes.

O mapper existente reúne descrições e listas na ordem original, removendo as
repetições segundo as regras já usadas no projeto. Ele preserva requisitos,
desejáveis, tecnologias, responsabilidades e benefícios. Campos escalares
divergentes (empresa, cargo, salário, URL, modalidade etc.) ou `multipleJobs=true`
impedem apresentar a consolidação. Resposta vazia de um bloco, JSON inválido,
limite de geração, janela esgotada ou resultado maior que o formulário também
geram falha explícita. Os blocos concluídos não são apresentados como análise completa.

A prévia avisa quando houve consolidação e quando há blocos sem identidade
completa, que precisam de conferência humana. Uma extração de IA não comprova
cobertura factual integral. O botão Salvar permanece manual e separado da análise.
Após a gravação confirmada, a aba limpa o arquivo selecionado, os textos original
e editado, a prévia e os campos da vaga, ficando pronta para outro PDF. Erros de
gravação preservam os dados. Uma falha de atualização dos painéis após gravar não
restaura a prévia nem permite repetir o cadastro já concluído.

O progresso informa bloco atual, total e contexto. A edição fica bloqueada enquanto
a operação usa seu snapshot imutável; um resultado que não corresponde ao texto
atual é descartado. Métricas registram blocos, estimativas, contexto, cache, duração
e [tokens reais retornados pelo Ollama](https://docs.ollama.com/api/chat), sem texto,
prompts ou respostas de vagas.

## Verificação realizada

- Build Release da solução após cada fase: zero erros e avisos.
- `dotnet format whitespace --folder --include ... --verify-no-changes` nos arquivos
  C# da tarefa e `git diff --check`: aprovados.
- 47 testes existentes aprovados, sem casos novos. Três arquivos existentes tiveram
  adaptações de contrato/expectativas: janela automática, metadados de contexto
  nos handlers e a assinatura do analisador usado no teste de salvamento.
- 28 caminhos de binding da tela PDF conferidos por reflexão no assembly compilado:
  nenhuma propriedade ausente; XAML compilado pelo WPF.
- Diagnóstico manual do plano com a amostra artificial existente repetida em memória:
  19.900 unidades UTF-16, quatro blocos de 8.192 tokens, orçamento estimado de 5.955
  por bloco; concatenação igual ao texto completo, na mesma ordem.
- Consulta somente de metadados ao Qwen local: arquitetura `qwen3vl`, contexto
  informado de 262.144, capacidades `completion`, `vision`, `tools`. A política do
  aplicativo continua limitada a 8.192, apesar do teto maior informado pelo modelo.

Não houve nova inferência real de documento longo nem validação visual interativa
da tela nesta tarefa. Os testes existentes não cobrem todas as novas combinações
de blocos; não foram criados testes unitários, de integração ou E2E, conforme solicitado.
O fluxo de screenshots individuais e em lote não foi alterado.

Arquivos principais: `PdfImportView.xaml`, `PdfImportViewModel.cs`,
`PdfTextCleanup.cs`, `IJobPdfAnalyzer.cs`, `PdfAnalysisEstimate.cs`,
`OllamaPdfContextBudget.cs`, `OllamaPdfPlan.cs`, `OllamaClient.Pdf.cs`,
`OllamaClient.Models.cs`, `OllamaContracts.cs`, `OllamaPdfExtractionContract.cs`,
`OllamaJobPdfAnalyzer.cs`, `OllamaResultMapper.cs`, `ImageAnalysisModels.cs` e
`SettingsView.xaml`.
