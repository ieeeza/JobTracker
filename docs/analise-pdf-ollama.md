# Análise de PDFs com Ollama

## Entrega

O fluxo de PDF permite selecionar um arquivo, extrair o texto com PdfPig,
clicar em **Analisar com IA** e conferir cargo, empresa, localização, modalidade,
tecnologias e descrição. A saída é uma pré-visualização somente leitura.
Nenhuma candidatura é criada, editada ou enviada por esse fluxo.

A implementação usa exclusivamente o modelo local configurado,
`qwen3-vl:8b`, e as preferências Ollama já existentes.

## Estado recebido e decisões

A extração textual, a aba PDF e os registros de PdfPig e CommunityToolkit já
existiam no diretório de trabalho, parcialmente não rastreados. Foram incorporados
apenas os componentes necessários ao fluxo solicitado.

Não existia uma interface funcional `IJobPdfAnalyzer`: havia uma classe vazia
`IJobAnalyzer`, não rastreada, substituída pelo contrato correto.
O DTO inicial `PdfJobAnalysisResult` repetia campos do domínio e não tinha
consumidores. Agora contém `JobContent Content` e avisos de revisão, evitando
um segundo modelo de vaga na Application.

O nome físico preexistente `Infraestructure` foi preservado. Os adaptadores
Ollama continuam no namespace `JobTracker.Modules.JobAnalysis.Infrastructure.Ollama`.

## Fluxo e responsabilidades

1. `PdfImportViewModel` seleciona o arquivo por `IFilePickerService`.
2. `IPdfTextExtractor` extrai o texto fora da thread de interface. O token de
   cancelamento é verificado antes da abertura e entre páginas.
3. `AnalyzePdfCommand` captura o texto atual, limpa a análise anterior e chama
   `IJobPdfAnalyzer.AnalyzeAsync`.
4. `OllamaJobPdfAnalyzer` carrega `OllamaOptions` por `ImageAnalysisSettings`,
   que já lê as preferências do aplicativo.
5. `OllamaClient.ExtractPdfAsync` valida a entrada, adquire o mesmo semáforo
   das análises visuais, verifica o modelo local e consulta o cache.
6. Se necessário, envia mensagens apenas textuais para `/api/chat`. O corpo usa
   `OllamaChatRequest`, `OllamaGenerationOptions` e JSON Schema no campo `format`.
7. O JSON completo é validado e mapeado explicitamente para `JobContent`.
8. A propriedade observável `AnalysisResult` atualiza a pré-visualização WPF.

Não foram criados outros HttpClients, pipelines HTTP, serviços de preferências
ou métodos de verificação de modelo. O método `SendAsync<T>` existente continua
responsável por limites de resposta, timeout, erros HTTP, memória, desconexão
e cancelamento. A única alteração no arquivo original `OllamaClient.cs` foi
tornar a classe `partial`; a extensão textual fica em `OllamaClient.Pdf.cs`.

Referência de protocolo: [Structured outputs — Ollama](https://ollama.com/blog/structured-outputs).

## Contrato e validação

`OllamaPdfExtractionContract` define o prompt, a versão do prompt, a versão do
schema, os campos obrigatórios e os limites. O conteúdo do PDF é tratado como
dados não confiáveis; comandos dentro do documento não devem substituir o
system prompt. A descrição deve preservar os detalhes relevantes, sem resumir.
Portal de recrutamento não deve ser confundido com empresa contratante.

A validação local não confia apenas no formato solicitado ao modelo:

- Envelope concluído, papel `assistant`, conteúdo presente e ausência de erro.
- Geração encerrada por limite (`done_reason=length`) é rejeitada.
- JSON deve ser um objeto, sem propriedades duplicadas, extras ou ausentes.
- Tipos e nulidade são verificados por System.Text.Json com as opções estritas existentes.
- Campos desconhecidos são `null`; tecnologias são um array obrigatório, vazio
  quando desconhecidas. Nulos dentro da lista, strings vazias e limites excedidos
  são rejeitados explicitamente.
- Modalidade aceita somente `Remoto`, `Híbrido`, `Presencial` ou `null`.
- Há limites de comprimento e quantidade, e rejeição de tecnologias duplicadas.
- O resultado precisa conter um cargo textual ou uma descrição com conteúdo mínimo;
  metadados isolados, como empresa ou localização, não bastam.
- O cache é submetido à mesma validação da resposta recém-gerada.

No JSON de transporte, desconhecido permanece `null`. No mapeamento para o
`JobContent` existente, que usa strings não nulas, ausência vira string vazia.
A interface informa que campos vazios não foram identificados; nenhum dado é
inventado para preencher campos obrigatórios.

JSON estruturalmente válido não garante veracidade ou cobertura factual completa.
A conferência humana do documento continua necessária.

## Contexto e recursos

A análise textual usa `ContextTokens` diretamente, respeitando sua relação com
`MaxContextTokens`. Não aplica o modo visual de economia de memória, tamanho de
lotes, dimensões, pixels ou quantidade de imagens à entrada textual.

Esta versão recusa documentos acima de um orçamento conservador de bytes UTF-8:
desconta prompt/schema e 1.024 unidades de margem do contexto configurado e
reserva espaço equivalente ao texto para a saída. Isso é uma estimativa
conservadora, não uma contagem exata pelo tokenizer do modelo.

Não há chunking, truncamento de entrada, retry com contexto maior ou expansão
automática. A mensagem informa o limite e orienta a usar um documento menor ou
ajustar manualmente as preferências conforme a memória disponível. Um contexto
muito pequeno pode não comportar o próprio contrato e a reserva de saída.

`WaitForCompletion`, `TimeoutSeconds` e `KeepAliveSeconds` continuam obedecidos.
Cancelamento é propagado ao cliente existente e às operações de cache.
A abertura e a leitura interna de uma página no PdfPig são síncronas: o
cancelamento da extração é cooperativo entre essas operações.

## Cache e persistência

O cache usa o `IAnalysisCache` existente e sua tabela já existente
`VisionAnalysisCache`. Não há migrations, alteração de schema ou gravação de vagas.
Quando uma inferência for bem-sucedida, somente o resultado estruturado validado
será armazenado nesse cache técnico existente.

A chave é um SHA-256 com domínio `pdf-text-v1`, diferente de `vision-v2`, considerando:

- SHA-256 do texto completo.
- Digest e nome do modelo local.
- Endereço local normalizado.
- Versões e conteúdo do prompt/schema.
- Contexto, temperatura, política de saída, thinking e keep-alive.

O `CacheKey` visual não é usado. Resultados textuais não podem substituir análises
de imagem. O texto integral do PDF e o JSON do modelo não são escritos nos logs.
Os logs registram operação, tipo da exceção, stack técnica e metadados HTTP.

## Estado e interface

O ViewModel usa `ObservableObject`, `ObservableProperty`, `RelayCommand` e
`NotifyCanExecuteChangedFor`. Mantém `SelectPdfCommand`, `ExtractPdfCommand`
e `IsExtracting`; acrescenta análise, cancelamento e resultado observável.

A extração e a análise participam do `UiOperationState` existente. Assim,
o fechamento da janela cancela e aguarda a operação, e a navegação/configuração
fica bloqueada durante o processamento. Comandos locais também impedem troca do
documento e análises simultâneas.

Selecionar um PDF, inclusive o mesmo caminho novamente, ou extrair de novo
invalida resultados anteriores. Falhas e cancelamento restauram os estados no
`finally`, preservando o texto quando a falha é apenas na análise.

A View usa `Card`, `Heading`, `Muted`, `TextBrush`, `BackgroundBrush` e os
estilos existentes de controles. As cores são dinâmicas para acompanhar o tema.
O formulário manual e o comando de salvar não ficam ativos na aba PDF.
As abas individuais e de lote continuam com seus próprios fluxos.

## Inventário de arquivos

Novos nesta implementação:

- `JobTracker/Modules/JobAnalysis/Application/Interfaces/IJobPdfAnalyzer.cs`
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaPdfExtractionContract.cs`
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaPdfResultMapper.cs`
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaClient.Pdf.cs`
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaJobPdfAnalyzer.cs`
- `docs/analise-pdf-ollama.md`

Arquivos preexistentes ajustados:

- `JobTracker/Modules/JobAnalysis/Application/Models/PdfJobAnalysisResult.cs`: reutilização de JobContent.
- `JobTracker/Modules/JobAnalysis/Application/Interfaces/IPdfTextExtractor.cs`:
  token opcional, preservando chamadas existentes.
- `JobTracker/Modules/JobAnalysis/Infraestructure/PdfPigTextExtractor.cs`: cancelamento cooperativo.
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaClient.cs`: classe partial.
- `JobTracker/Presentation/ViewModels/Imports/PdfImportViewModel.cs`: análise e estado.
- `JobTracker/Presentation/Views/Imports/PdfImportView.xaml`: comandos, progresso e prévia.
- `JobTracker/Presentation/ViewModels/JobEditorViewModel.cs`: salvar somente no formulário individual.
- `JobTracker/Presentation/ViewModels/JobEditorViewModel.Analysis.cs`: separar a aba PDF do formulário.
- `JobTracker/App.xaml.cs`: registro de IJobPdfAnalyzer.

Também incorporados ao histórico por serem necessários à funcionalidade, mantendo
a implementação que já existia: `IFilePickerService.cs`, `PdfFilePickerService.cs`,
`PdfImportView.xaml.cs`, ligação da aba em `ImageImportView.xaml`, injeção de Pdf
no editor e referências já instaladas de PdfPig/CommunityToolkit em
`JobTracker.csproj`. Nenhuma biblioteca ou versão nova foi introduzida no workspace.

A classe vazia e não utilizada `IJobAnalyzer.cs`, que ainda não estava rastreada,
foi removida ao criar a interface específica.

## Verificações

- Build inicial antes da implementação: aprovado, zero erros e avisos.
- `dotnet build JobTracker/JobTracker.slnx --no-restore -v minimal`:
  aprovado após cada fase e ao final, zero erros e avisos.
- `dotnet build JobTracker/JobTracker.slnx -c Release --no-restore -v minimal`:
  aprovado, zero erros e avisos.
- `dotnet test JobTracker.Tests/JobTracker.Tests.csproj --no-build --no-restore -v minimal`:
  1 aprovado, 0 falhas, 0 ignorados.
- `dotnet format whitespace JobTracker.csproj --no-restore --include <arquivos C# do PDF> --verify-no-changes`:
  aprovado, executado no diretório JobTracker.
- `git diff --check`: aprovado.
- Nenhum teste ou arquivo de teste foi criado; os testes existentes não foram alterados.

Validação manual em execução com banco exclusivo em
`artifacts/pdf-validation/jobtracker.db` e PDF autorizado pelo usuário:

- Inicialização e resolução das dependências.
- Aba PDF, comandos inicialmente desabilitados e seleção de arquivo.
- Extração real pelo PdfPig e habilitação do botão de análise.
- Preservação do texto após falhas.
- Recusa por contexto insuficiente com 8.192 tokens.
- Leitura de preferências após salvar contexto de 16.384 tokens:
  o documento passou pela validação de tamanho.
- Mensagem de modelo ausente, sem encerrar a aplicação.
- Mensagem de Ollama desligado, com os comandos novamente disponíveis.
- Tema claro e tema escuro, com a correção de contraste conferida após recompilar e reabrir o aplicativo.
- Atalho Ctrl+S inativo na aba PDF e ausência do formulário de gravação nessa aba.

## Limitações verificadas

O Ollama estava instalado, mas desligado. Foi iniciado temporariamente para a
validação; `GET /api/tags` retornou `{"models":[]}`. Assim, o modelo
`qwen3-vl:8b` não estava disponível nesse servidor local. O processo iniciado
para a validação foi encerrado depois dos checks.

Não foram validados com inferência real: qualidade dos seis campos,
prévia preenchida por resposta do Qwen, cache hit de PDF, timeout, falha de memória
e cancelamento durante geração. Esses caminhos estão implementados e compilados,
mas não foram substituídos por mocks ou resultados simulados.

A análise visual e o lote não foram executados com um modelo real nesta tarefa.
O cliente visual permanece intacto, exceto por `partial`, e seu teste WPF existente
passou. Isso não substitui validação de inferência visual.

## Commits e trabalho local preservado

1. `3b018e6` — contrato, schema e validação da extração textual.
2. `d6cf74d` — extensão do cliente Ollama e cache textual.
3. `3f23d25` — adaptador de aplicação e DI.
4. `5e12468` — integração WPF, estado, cancelamento e pré-visualização.
5. O commit deste relatório registra a formatação final e a correção do tema escuro.

A organização anterior de `App.xaml.cs` ficou no diretório de trabalho; somente
os registros necessários ao PDF foram incluídos nos commits desta tarefa.
README, .gitignore, AGENTS.md, a mudança de ordem dos usings de UiOperationState,
documentos anteriores e o projeto de testes não rastreado foram preservados fora
desses commits. O banco pessoal e o PDF fornecido não foram modificados.
