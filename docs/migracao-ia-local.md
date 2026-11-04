# Migração para análise exclusiva por IA local

Implementação de 27/10/2026. As alterações foram feitas no repositório existente, preservando o trabalho que já estava presente. Não foram feitos commits: havia alterações preexistentes e arquivos não rastreados de vários fluxos.

## Resultado e arquitetura

O cadastro tem duas abas: Capturas individuais e Importação em lote. Ambos usam Qwen3-VL 8B no Ollama local, com entrada por imagens e saída JSON validada. Não há seletor de motor, scraping ou importação por texto copiado. O formulário continua permitindo preenchimento manual de todos os campos, inclusive URL opcional.

Presentation → serviços de Application → regras de Domain. Infrastructure fornece preparação/descoberta de imagens, OllamaClient único e persistência EF Core/SQLite. TrackingService, normalização de URLs/texto/tecnologias, duplicidade, agenda, histórico, transferência e backup permanecem compartilhados.

## Remoções efetivas

- Pacotes NuGet Microsoft.Playwright 1.63.0 e Tesseract 5.2.0; cópia de tessdata/ocrsettings e target VerifyOcrResources do csproj.
- TesseractJobImageReader e TesseractJobImageAnalyzer; interfaces IJobImageReader e IJobInformationExtractor; engine selector, opções de OCR, progresso específico e fallback.
- PlaywrightJobPageReader, IJobPageReader, RenderedPage, JobPageBlockedException, inicializador/instalador de Chromium e leitura de páginas por URL.
- JobExtractor, JobRegistrationService, JobHistoryService e seus DTOs exclusivos. IJobRepository, JobRepository e a extensão de DI antiga ficaram sem consumidores e foram removidos; o repositório de acompanhamento continua ativo.
- TextImportView, JobImportMethod, comandos de colar/analisar texto, contador/validação de texto copiado e preferências de captura de páginas.
- JobInformationExtractor foi removido; sua normalização compartilhada foi isolada em JobTextKey. JobTextNormalizer e TechnologyCatalog foram preservados.
- Agrupamento heurístico por OCR e BatchGroupingRefinement foram substituídos pelo agrupamento visual.
- Recursos por.traineddata, eng.traineddata, licença exclusiva desses recursos, ocrsettings.json e documentação de OCR como funcionalidade ativa.

OcrCapture permanece no domínio e na coluna SQLite por compatibilidade com registros históricos. Campos antigos dos payloads de relatórios são preservados como dados legados, sem serem usados para novas inferências. Migrations históricas e tabelas/colunas de dados não foram removidas.

## Arquivos criados ou substituídos

- Application/Interfaces: IAnalysisCache, IJobImageImporter e contratos simplificados de IJobImageAnalyzer/IOllamaConnection.
- Application/Models: ImageModels substitui modelos específicos de OCR; ImageAnalysisModels concentra opções e resultados da IA.
- Application/Services: JobTextKey e simplificação de ImageAnalysisSettings/JobImageAnalysisService.
- Application/BatchImport: ScreenshotGrouping e BatchExtraction exclusivamente visuais; BatchImportCoordinator, BatchModels, BatchReviewService e BatchRegistration adaptados; BatchReportExport adicionado.
- Infrastructure/FileSystem: ImagePreparationService substitui ImagePreprocessingService; ScreenshotDiscovery preservado com verificação de hash antes da persistência.
- Infrastructure/Ollama: OllamaClient, OllamaJobImageAnalyzer, OllamaContracts, OllamaResultMapper, BatchGroupingResolver, OllamaGroupingContract e BatchJobInterpreter adaptados.
- Infrastructure/Persistence: AnalysisCache e configuração EF de VisionAnalysisCache adicionados.
- Migrations: 20261027150912_LocalVisionCache e Designer; snapshot atualizado. Up cria somente a tabela de cache.
- Presentation: ShellViewModel e parciais de importação/imagens, ImageImportView/ViewModel, BatchImportView/ViewModel, OllamaSettingsViewModel, templates EditorView/Views, DesktopServices e DesktopImageServices.
- App.xaml.cs e MainWindow.xaml.cs: DI/ciclo de vida/comandos simplificados. Job.cs e TrackingService: edição explícita da URL opcional com proteção de duplicidade.
- README e documentos de cadastro, Ollama e lote atualizados.

## Cadastro individual

Imagens da mesma vaga podem ser adicionadas, removidas, ampliadas e ordenadas. A análise valida o conjunto antes de preencher campos vazios; identidades conflitantes impedem combinação. As sugestões ficam editáveis. Somente Salvar vaga persiste. Sucesso limpa formulário/imagens e mantém a aba individual pronta para o próximo cadastro. Erro ou recusa de possível duplicidade preserva os dados.

## Lote e otimizações

Janelas pequenas com sobreposição visual de uma imagem identificam transições. O contrato exige partições contíguas cobrindo todos os índices, sem repetição. A partição final é publicada inteira. Grupos ambíguos e limites vizinhos vão para revisão; não são salvos automaticamente. A extração retornada junto com uma partição é reutilizada quando cobre a vaga inteira. Grupos maiores são extraídos por conjuntos limitados.

OllamaClient serializa inferências, usa keep_alive e valida o modelo/digest local. O cache usa hashes das imagens preparadas em ordem, tipo de contrato, prompt/schema, modelo/digest e opções. Não guarda imagens, Base64 ou comandos. Janelas diferentes não reutilizam incorretamente respostas de imagens isoladas. Retomada reutiliza resultados compatíveis; Reprocessar grupo força nova inferência somente naquele grupo.

A preparação mantém detalhes e resolução e normaliza BMP/WEBP para PNG, sem ampliar capturas pequenas nem aplicar redução/compressão agressiva para caber em limites. A análise individual usa economia de memória por padrão: uma captura por requisição e contexto de até 4096 tokens, sem expansão automática; a opção pode ser desativada nas configurações. Descoberta e inferência não carregam a pasta inteira em memória. Há limites de arquivos, bytes, pixels e tamanho das respostas. Os limites de lote e do Ollama são persistidos.

## Persistência, histórico e relatório

Vagas válidas usam TrackingService, com status Salva, verificação de duplicidade dentro e fora do lote e transação individual. Uma vaga ambígua ou potencial duplicata permanece no relatório. Hashes/chaves de origem permitem recuperar um commit ocorrido antes do checkpoint, evitando reinserção. Nenhuma transação permanece aberta durante inferência.

Cancelamento e falhas preservam resultados confirmados. Retomar ignora resultados concluídos e intervenções humanas, retomando pendentes/falhos. Mudanças de contrato/modelo/opções impedem salvar automaticamente resultados incompatíveis. O relatório permite abrir registros, editar dados, separar/unir/mover capturas, descartar pendências, reanalisar e exportar JSON por ITransferFiles. Editar grupo vinculado atualiza o mesmo ID.

São medidos tempos de descoberta, agrupamento e extração, chamadas HTTP efetivas, acertos de cache e durações de carga/avaliação retornadas pelo Ollama. Cache não recebe tempo fictício de inferência. O banco real do usuário não foi aberto ou migrado nesta tarefa; a migração será aplicada pelo inicializador existente, com backup, ao iniciar a versão nova.

## Verificações realizadas

- Restore da solução com -m:1: concluído. A primeira tentativa em rede restrita teve NU1900; a repetição com acesso ao NuGet concluiu sem avisos.
- Build Release da solução, --no-restore -warnaserror -m:1 -p:UseSharedCompilation=false: sucesso, zero avisos e zero erros. Inclui os analisadores .NET habilitados no csproj.
- dotnet format whitespace em modo pasta, nos arquivos do escopo, seguido de --verify-no-changes: sucesso.
- EF database update aplicado somente ao banco descartável artifacts/batch-migration-check.db: migração LocalVisionCache concluída.
- EF migrations has-pending-model-changes: nenhuma diferença entre modelo e snapshot.
- git diff --check: sem erros de whitespace; Git informa conversões de fim de linha em arquivos com convenção CRLF.
- Busca de referências e conferência de project.assets.json/JobTracker.deps.json: sem dependências ativas de Tesseract ou Microsoft.Playwright.
- /api/tags em 127.0.0.1:11434: conexão indisponível (HttpRequestException).

Nenhum teste novo foi criado e nenhum teste foi executado. O único teste de apresentação existente teve sua montagem de dependências adaptada para compilar após as remoções, sem alterar suas asserções e sem adicionar mocks.

## Limitações verificadas

Não houve inferência real, avaliação de qualidade/velocidade do Qwen nem execução interativa do WPF nesta migração. Esses comportamentos não são comprovados pelo build. Agrupamento visual é uma estimativa: sequências com várias vagas numa captura, identificação ausente ou transições incertas exigem revisão. Grupos que atravessam janelas podem exigir extração adicional. WEBP depende do codec WIC instalado.

A redução automática de resolução foi evitada para preservar texto pequeno; imagens acima dos limites precisam de ajuste dos limites ou recorte. Não é permitido unir automaticamente dois grupos vinculados a registros salvos diferentes. Exportação JSON do relatório é para consulta; backup SQLite preserva todo o histórico/cache.
