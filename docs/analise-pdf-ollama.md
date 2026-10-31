# Análise de PDFs com Ollama

Atualização de orçamento, modelos independentes e métricas: [relatório de contexto e desempenho](pdf-contexto-desempenho.md).

## Entrega

O fluxo de PDF permite selecionar um arquivo, extrair o texto com PdfPig,
clicar em **Analisar com IA** e conferir cargo, empresa, localização, modalidade,
senioridade, contratação, salário, URL, tecnologias e requisitos. A descrição
reúne também responsabilidades e benefícios, pelo mesmo mapeamento das imagens.
A saída é uma pré-visualização somente leitura.
Nenhuma candidatura é criada, editada ou enviada por esse fluxo.

A implementação usa exclusivamente um modelo instalado no Ollama local,
configurável para PDFs independentemente do modelo de screenshots. A tag é
resolvida nas tags reais e suas capacidades são verificadas antes da inferência.

## Estado recebido e decisões

A extração textual, a aba PDF e os registros de PdfPig e CommunityToolkit já
existiam no diretório de trabalho, parcialmente não rastreados. Foram incorporados
apenas os componentes necessários ao fluxo solicitado.

Não existia uma interface funcional `IJobPdfAnalyzer`: havia uma classe vazia
`IJobAnalyzer`, não rastreada, substituída pelo contrato correto.
O DTO inicial `PdfJobAnalysisResult` repetia campos do domínio e não tinha
consumidores. Agora contém `JobContent Content`, avisos de revisão e a URL,
evitando um segundo modelo de vaga na Application. Esse retorno representa a
prévia textual sem expor `OllamaJobData` nem carregar metadados de screenshots/OCR.

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
7. O JSON completo usa `OllamaJobData` e é validado por `OllamaResultMapper`;
   o mesmo núcleo de mapeamento das imagens produz `JobContent`.
8. A propriedade observável `AnalysisResult` atualiza a pré-visualização WPF.

Não foram criados outros HttpClients, pipelines HTTP, serviços de preferências
ou métodos de verificação de modelo. O método `SendAsync<T>` existente continua
responsável por limites de resposta, timeout, erros HTTP, memória, desconexão
e cancelamento. `OllamaClient.cs` e sua extensão textual `OllamaClient.Pdf.cs`
compartilham também o parser de respostas, que rejeita propriedades duplicadas.

Referência de protocolo: [Structured outputs — Ollama](https://ollama.com/blog/structured-outputs).

## Contrato e validação

`OllamaPdfExtractionContract` define somente instruções textuais e sua versão.
O schema e o DTO completos são `OllamaExtractionContract.Schema` e `OllamaJobData`,
compartilhados com screenshots. As descrições do schema são neutras quanto à
origem do conteúdo; o system prompt de imagens não é usado para PDFs.
`OllamaPdfContextBudget` trata separadamente a validação e o orçamento da entrada.
O conteúdo do PDF é tratado como dados não confiáveis; comandos dentro do
documento não devem substituir o system prompt. A extração preserva os detalhes
em listas estruturadas, sem repetir essas listas em `description`.
Portal de recrutamento não deve ser confundido com empresa contratante.

A validação local não confia apenas no formato solicitado ao modelo:

- Envelope concluído, papel `assistant`, conteúdo presente e ausência de erro.
- Geração encerrada por limite (`done_reason=length`) é rejeitada.
- JSON deve ser um objeto, sem propriedades duplicadas, extras ou ausentes.
- Tipos e nulidade são verificados por System.Text.Json com as opções estritas existentes.
- Campos desconhecidos são `null`; listas obrigatórias usam `[]` quando vazias.
  Nulos dentro das listas e limites excedidos são rejeitados pelo validador comum.
- Modalidade, vínculo e jornada seguem o contrato textual existente das imagens.
- Há limites de comprimento e quantidade e validação de URL HTTP/HTTPS sem credenciais.
- `multipleJobs=true` rejeita múltiplas vagas ou identidades conflitantes antes do cache.
  Instruções do PDF exigem essa sinalização, sem misturar os dados.
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

A análise textual usa `PdfContextTokens` (padrão 4.096) como ponto inicial,
limitado por `PdfMaxContextTokens` (padrão 8.192). Não aplica o modo visual de economia de memória, tamanho de
lotes, dimensões, pixels ou quantidade de imagens à entrada textual.

O orçamento estima separadamente texto, instruções e prefixo: três bytes por
token para prosa latina, fallback por byte para outros caracteres, 96 tokens de
mensagens e saída de no mínimo 512 tokens (texto estimado + 256 para estrutura).
Aplica margem de 20% sobre o total. O schema completo é enviado somente em
`format`, enquanto o prompt explica seus campos. É uma heurística fundamentada
em medição local, não uma contagem exata pelo tokenizer. Os limites máximos dos
campos não representam consumo obrigatório. O relatório detalha limitações.

Quando necessário, o contexto cresce em degraus de 4.096, respeitando
`PdfMaxContextTokens`. O contexto efetivo também integra a chave de cache; as
preferências salvas não são alteradas. Essa expansão pode exigir mais memória.
Documentos que ultrapassam o máximo são recusados antes do envio, com uma
mensagem que informa tamanho, estimativa necessária e máximo configurado.
Não há chunking, truncamento de entrada ou retry com contexto maior.

`WaitForCompletion` e `TimeoutSeconds` continuam obedecidos. PDFs usam
`PdfKeepAliveSeconds` (padrão 300); screenshots preservam `KeepAliveSeconds`.
Cancelamento é propagado ao cliente existente e às operações de cache.
A abertura e a leitura interna de uma página no PdfPig são síncronas: o
cancelamento da extração é cooperativo entre essas operações.

## Cache e persistência

O cache usa o `IAnalysisCache` existente e sua tabela já existente
`VisionAnalysisCache`. Não há migrations, alteração de schema ou gravação de vagas.
Quando uma inferência for bem-sucedida, somente o resultado estruturado validado
será armazenado nesse cache técnico existente.

A chave é um SHA-256 com domínio `pdf-text-v2`, diferente de `vision-v2`, considerando:

- SHA-256 do texto completo.
- Digest e nome do modelo local.
- Endereço local normalizado.
- Versão do prompt e conteúdo completo do prompt/schema compartilhado.
- Contexto, temperatura, política de saída, thinking e keep-alive.

O `CacheKey` visual não é usado. Resultados textuais não podem substituir análises
de imagem. A versão v2 impede reutilizar o cache PDF anterior de seis campos;
não há alteração de tabela nem exclusão dos registros antigos.
O texto integral do PDF e o JSON do modelo não são escritos nos logs.
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
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaPdfContextBudget.cs`
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaClient.Pdf.cs`
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaJobPdfAnalyzer.cs`
- `docs/analise-pdf-ollama.md`

Arquivos preexistentes ajustados:

- `JobTracker/Modules/JobAnalysis/Application/Models/PdfJobAnalysisResult.cs`: reutilização de JobContent.
- `JobTracker/Modules/JobAnalysis/Application/Interfaces/IPdfTextExtractor.cs`:
  token opcional, preservando chamadas existentes.
- `JobTracker/Modules/JobAnalysis/Infraestructure/PdfPigTextExtractor.cs`: cancelamento cooperativo.
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaClient.cs`: classe partial e parser compartilhado.
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaResultMapper.cs`: parser e mapeamento comum de JobContent.
- `JobTracker/Modules/JobAnalysis/Infraestructure/Ollama/OllamaContracts.cs`: descrições do schema independentes da origem textual/visual.
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

## Verificações do ajuste arquitetural (31/10/2026)

- Build e testes Release: 36 testes aprovados, incluindo contratos completos,
  mapeamento de todos os campos, JSON inválido, conflitos, resposta interrompida,
  falha HTTP, cancelamento, validação de cache e preservação do envio de screenshots.
- Os testes HTTP usam respostas controladas; não substituem inferência real.
- DTO, schema e mapper exclusivos do PDF foram removidos. Nenhuma dependência foi adicionada.
- A qualidade da extração com PDFs e screenshots reais não foi revalidada neste ajuste.

## Verificações da entrega inicial

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

## Limitações verificadas na primeira validação

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

## Retomada da validação em 30/10/2026

Na retomada, o servidor local já estava acessível e `GET /api/tags` passou a
listar `qwen3-vl:8b`, digest
`9cf69da2b16d097f7def535917c4b55a95d6c7ee5700eb69ab86b01b3cfca487`.
Nenhum modelo foi instalado ou substituído nesta retomada. O servidor existente
foi preservado; uma tentativa de iniciá-lo retornou porta já ocupada.

O build Debug da solução foi executado novamente, com zero erros e avisos.
Não houve alteração de código nem criação ou modificação de testes nesta etapa.

Foi usado novamente o banco isolado `artifacts/pdf-validation/jobtracker.db`
e o PDF de Analista de Sistemas autorizado pelo usuário. As preferências desse
banco continuavam com contexto de 16.384 tokens e `WaitForCompletion=true`.

Resultados observados:

- Seleção e extração do PDF funcionaram; a análise iniciou com o modelo real.
- O cancelamento pela barra inferior restaurou os comandos e preservou o texto.
- Uma segunda análise permaneceu em andamento por cerca de dez minutos, sem
  resposta final. Foi cancelada manualmente; não foi um timeout do aplicativo.
- `GET /api/ps` confirmou o modelo carregado com contexto de 16.384 tokens e
  runner `llamacpp`. Durante a espera, a RTX 4060 Ti de 8 GB chegou a 100% de uso,
  com aproximadamente 7,5 GB de memória ocupada. Esses dados indicam atividade,
  mas não comprovam progresso útil da geração.
- Duas chamadas diagnósticas diretas a `/api/chat`, sem dados do PDF, também
  expiraram no cliente após 60 segundos: uma com JSON Schema e limite de 32
  tokens, outra sem schema e limite de 16 tokens, ambas com `think=false`.
  Isso demonstra que a falta de resposta também ocorreu fora do JobTracker;
  não determina a causa interna do servidor nem exclui fila residual.
- Consultas somente leitura ao banco isolado confirmaram zero registros em
  `Jobs` e em `VisionAnalysisCache` após os cancelamentos.
- A janela de validação foi fechada normalmente.

O modelo continuou carregado e consumindo GPU mesmo após cancelar as chamadas.
`ollama stop qwen3-vl:8b` retornou sucesso, mas o descarregamento não foi observado
nas consultas seguintes. A tentativa de encerrar somente o runner iniciado durante
esta validação (`llama-server`, PID 21496, início às 19:35:23) foi recusada pelo
Windows com **Access is denied**. O servidor não foi encerrado à força.

**Pendência atual:** reiniciar o Ollama com a permissão da sessão que o iniciou
e repetir a análise. O modelo agora está instalado; a limitação atual é a falta
de resposta final nesta execução local. A prévia preenchida, a qualidade dos seis
campos e o cache hit continuam sem validação real. O cancelamento na interface foi
validado, mas o término do processamento no servidor não foi confirmado.

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
