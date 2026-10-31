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

## Fase 2 — orçamento proporcional

Uma inferência local direta, com vaga artificial de 5.126 bytes e o prompt
anterior, processou 2.185 tokens de entrada e 237 de saída em contexto 8.192.
Isso não mede a qualidade da extração nem representa o PDF pessoal do usuário.
Foram 7,56 s de parede, incluindo 3,65 s de carga e 3,19 s de geração.
Nenhuma conclusão comparativa de desempenho é tomada a partir dessa carga fria.

A média dessa entrada foi superior a quatro bytes por token. A heurística usa
três bytes por token para letras/espaços latinos, conta bytes integralmente para
outros caracteres (inclusive símbolos e outros alfabetos), e adiciona margem de
20% ao total. Não é um tokenizer e não garante a contagem de todos os modelos.
Essa escolha é mais conservadora que a amostra medida, sem o extremo anterior
de reservar um token para cada byte de toda a entrada e saída.

O orçamento distingue texto, instruções, prefixo, 96 tokens de enquadramento,
reserva de saída `max(512, tokensTexto + 256)` para conteúdo integral e estrutura
JSON, e margem proporcional. Limites máximos dos campos não entram na conta.
O schema completo aparece somente em `format`; o prompt mantém todos os nomes
e instruções sem duplicar a definição de validação. Sua contribuição adicional
ao conteúdo das mensagens é zero, e continua incluído na chave de cache.

Para a mesma amostra: texto 1.882, instruções 758, prefixo 25, enquadramento 96,
saída 2.138, margem 980: **5.879 tokens estimados**. Com inicial 4.096 e máximo
8.192, seleciona 8.192. Textos menores permanecem em 4.096. A seleção usa degraus
de 4.096 apenas quando necessário, sempre limitada pelo máximo configurado.
Não há cortes, chunking ou retry automático. A mensagem de recusa indica que
se trata de estimativa e preserva o original.

Esta fase também registra os componentes compartilhados de PDF já preparados
na etapa arquitetural anterior, necessários para compilar o orçamento separado.
Não inclui alterações anteriores não relacionadas no bootstrap, README ou serviços de UI.

## Fase 3 — configuração por finalidade

`Model`, limites visuais e preferências já salvas permanecem intactos. Foram
acrescentados `PdfModel`, `PdfContextTokens=4096`, `PdfMaxContextTokens=8192` e
`PdfKeepAliveSeconds=300`. `PdfModel` vazio usa o modelo visual já salvo, para
compatibilidade; quando preenchido, é independente. Não há atualização direta
do banco pessoal nem alteração de configuração global do Ollama.

Nomes são validados sintaticamente e resolvidos nas tags locais a cada operação.
Não há lista fixa de nomes aceitos nem instalação automática. Metadados remotos
são rejeitados tanto em tags quanto em `/api/show`; loopback continua obrigatório.
Screenshots exigem `completion` e `vision`; PDF exige `completion`. Uma tag
textual não pode passar pela validação de visão. Capacidades têm cache limitado
a 32 entradas/5 minutos, separado por servidor, nome e digest. Tags continuam
sendo consultadas para detectar remoção ou substituição do modelo sem usar
identidades obsoletas no cache de resultados.

## Fase 4 — execução e diagnóstico

Uma única instância de `OllamaClient` e o semáforo existente continuam serializando
PDFs, screenshots e agrupamentos. Ao trocar de modelo, o cliente solicita
`keep_alive=0` para o último modelo em que ele iniciou inferência, antes da próxima
chamada. Não enumera nem descarrega indiscriminadamente modelos de outros clientes.
Cache hit não carrega nem descarrega modelos. Falhas no descarregamento impedem
iniciar a nova inferência; não são ocultadas.

PDFs mantêm temperatura zero, sem thinking/streaming, e usam `num_predict` igual
ao contexto selecionado menos a estimativa de entrada. Essa reserva comporta
a saída estimada e a margem; respostas encerradas por limite continuam rejeitadas.
Screenshots mantêm `num_predict=-1`. O contexto e todos os parâmetros efetivos,
incluindo schema e limite de saída, integram o cache PDF. Preferências PDF foram
excluídas da identidade visual para não invalidar caches de imagens ao ajustá-las.

Logs estruturados com identificador por análise registram modelo, contexto,
bytes, todas as parcelas da estimativa, limite de geração, espera do semáforo,
verificação de modelo, HTTP, cache hit, cancelamento/falha/sucesso e tempo total.
Quando presentes, registram tokens reais de entrada/saída e duração de carga,
avaliação de prompt, geração e total do servidor. Nanossegundos são divididos por
1.000.000.000. Uma entrada real maior que a estimativa gera aviso de calibração.
O logger existente passa a aceitar Information somente para `OllamaClient`;
o restante mantém Warning. Nenhum texto, prompt, resposta ou dado pessoal é
registrado nessas métricas. Inferências paralelas e retries não foram adicionados.

## Fase 5 — interface

Configurações passam a exibir modelo de imagens, modelo de PDFs (vazio mantém o
modelo visual), contexto inicial/máximo e permanência próprios dos PDFs. Os
botões Verificar imagens e Verificar PDF usam tags e capacidades reais sem
carregar os modelos. Salvar preserva todas as preferências anteriores, incluindo
limites visuais, timeout e modo de economia. Os novos campos seguem o DesignSystem.
O contexto visual continua no bloco de limites de imagens.

## Fase 6 — validação e limites da evidência

A mesma amostra artificial de 5.126 bytes foi enviada ao modelo instalado
`qwen3-vl:4b-instruct`, com prompt/schema/opções do código compilado. Seleção:
8.192 tokens, estimativa 5.879, `num_predict=5431`. Resposta `done=true`,
`done_reason=stop`, `multipleJobs=false`, aceita por `OllamaResultMapper.ParsePdf`.
A amostra repete responsabilidades e requisitos equivalentes; não substitui um
documento real diversificado nem permite avaliar cobertura factual geral.

| Medição local | Contrato anterior | Contrato atual |
|---|---:|---:|
| Tokens reais no prompt | 2.185 | 1.718 |
| Tokens gerados | 237 | 238 |
| Carga do modelo (s) | 3,651 | 5,728 |
| Avaliação do prompt (s) | 0,470 | 0,368 |
| Geração (s) | 3,192 | 3,530 |
| Tempo de parede (s) | 7,560 | 9,794 |

A redução de tokens de prompt foi observada. Não há comprovação de ganho de
tempo total: as cargas diferiram e apenas uma medição por contrato foi feita.
`/api/ps` confirmou contexto 8.192 e cerca de 4,32 GB de VRAM ocupada pelo 4B
nessa execução; não é uma garantia para outros documentos ou modelos.

Uma preferência antiga com imagem `qwen3-vl:8b`, contexto 12.000, máximo 32.768
e permanência 600 foi desserializada com opções estritas: todos esses valores
foram preservados. PDF recebeu 4.096/8.192/300 e fallback para o modelo antigo.
Nenhum banco pessoal foi modificado.

Verificações:

- `dotnet build JobTracker/JobTracker.slnx -c Release --no-restore -v minimal`:
  aprovado em cada fase, zero erros e avisos no resultado final.
- `dotnet test JobTracker.Tests/JobTracker.Tests.csproj -c Release --no-restore -v minimal`:
  36 testes existentes aprovados. Nenhum caso de teste foi criado. Ajustes mínimos
  foram necessários em três arquivos existentes: entradas de fronteira deixaram
  de pressupor byte=token; configurações passaram a usar propriedades PDF; os
  handlers passaram a responder `/api/show`; nome inválido agora testa sintaxe.
- 20 bindings `Ollama.*` da tela de configurações conferidos por reflexão contra
  o assembly compilado: nenhuma propriedade ausente. Não substitui QA visual.
- Formatação dos arquivos alterados e `git diff --check` verificados.
- O índice preparado para commit foi exportado para um diretório isolado:
  build Release com zero erros/avisos e os mesmos 36 testes aprovados, sem
  depender das mudanças pessoais mantidas fora dos commits.
- O filtro da composition root libera Information apenas para a categoria do
  cliente; a alteração foi separada da refatoração local preexistente em App.xaml.cs.

O projeto de testes e seu acesso interno já existiam no workspace sem rastreamento;
são versionados na validação para tornar reproduzível a solução que já os referencia.
Não foram gerados testes novos nesta tarefa.

Limitações: não houve análise do PDF pessoal, nova inferência de screenshots ou
validação visual interativa da tela. A alternância entre dois modelos não foi
medida com duas inferências reais. A estimativa depende do tokenizer; o fallback
por byte para alfabetos não latinos pode recusar documentos que um tokenizer
específico comportaria. Nenhum modelo foi instalado, variável global alterada
ou quantidade de VRAM presumida como garantia.

Principais arquivos: `OllamaPdfContextBudget.cs`, `OllamaPdfExtractionContract.cs`,
`ImageAnalysisModels.cs`, `IJobImageAnalyzer.cs`, `OllamaClient.cs`,
`OllamaClient.Pdf.cs`, `OllamaClient.Models.cs`, `OllamaContracts.cs`,
`OllamaSettingsViewModel.cs`, `SettingsView.xaml`, `DiagnosticLogger.cs` e o
filtro pontual de `App.xaml.cs`. A etapa de contexto também registra os contratos,
mapper e prévia completa preparados na solicitação arquitetural anterior.
