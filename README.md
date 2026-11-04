# JobTracker

Aplicativo desktop Windows em **WPF / .NET 10** para acompanhar candidaturas com SQLite local. A análise de screenshots usa **Ollama local / qwen3-vl:4b-instruct** por padrão, com suporte também ao qwen3-vl:8b. Não envia candidaturas nem acessa automaticamente URLs de vagas.

## Executar

Requisitos: Windows, .NET SDK 10 com WPF para desenvolvimento e Ollama com o modelo local instalado para analisar imagens. A gestão dos registros, edição, agenda e backup funcionam sem inferência.

```powershell
dotnet restore JobTracker/JobTracker.slnx -m:1
dotnet build JobTracker/JobTracker.slnx -m:1
dotnet run --project JobTracker/JobTracker.csproj
```

Em **Configurações → Ollama**, verifique a conexão. Padrões: servidor local http://127.0.0.1:11434, modelo qwen3-vl:4b-instruct, três imagens por requisição e permanência de 600 segundos. Preferências já salvas são preservadas; para trocar o modelo antigo, edite o campo Modelo e salve. O aplicativo não instala nem inicia o servidor/modelo. Não há dependências de Tesseract, Playwright, Chromium ou modelos tessdata.

## Uso

- **Nova vaga → Capturas individuais:** adicione uma ou várias imagens da mesma vaga, ordene, analise com IA, revise os campos e clique em Salvar vaga. Após salvar, o formulário e as imagens são limpos e a tela fica pronta para o próximo cadastro. É possível preencher todos os campos manualmente nessa mesma aba.
- **Nova vaga → Importação em lote:** selecione uma pasta, confira a ordem e inicie. O Qwen agrupa capturas em janelas visuais, salva oportunidades válidas com status Salva e encaminha ambiguidades ao relatório. Há cancelamento, retomada, histórico, correção e exportação JSON.
- **Vagas e detalhes:** busca, filtros, tags, edição da URL opcional, status, histórico e exclusão lógica. Abrir uma URL no navegador é uma ação explícita do usuário.
- **Dashboard, candidaturas e agenda:** métricas reais, acompanhamento por etapas, entrevistas, prazos e lembretes.
- **Configurações:** Ollama, limites de recursos, tema, movimento, transferência JSON/CSV e backup SQLite.

Atalhos: Ctrl+N abre cadastro, Ctrl+S salva o formulário individual, F5 atualiza. Ctrl+V fora de campos de texto cola uma imagem na aba individual. Não existe importação por texto copiado, scraping ou método Manual separado.

Consulte [cadastro individual](docs/job-registration.md), [integração e limites da IA](docs/ollama-vision.md), [importação por pasta](docs/importacao-por-pasta.md) e [relatório da migração](docs/migracao-ia-local.md).

## Dados, migrações e segurança

O banco padrão é:

```text
%LOCALAPPDATA%\JobTracker\jobtracker.db
```

A localização é resolvida pela pasta de aplicação local do Windows. A pasta contém também `Backups`, `Exports` e `Logs`; a gravação normal não depende do diretório do executável. O aplicativo impede duas instâncias para o mesmo caminho de banco.

O EF Core aplica migrações versionadas antes de abrir a janela. Não usa `EnsureCreated` nem `EnsureDeleted`. Antes de atualizar um banco existente, cria backup consistente pela API SQLite. O contexto é criado e descartado por operação, por `IDbContextFactory`; o trabalho de persistência roda fora da thread WPF.

Migrações preservadas:

- `InitialCreate`: estrutura original.
- `LocalSqlitePersistence`: campos opcionais e índices únicos anteriores.
- `DesktopTracking`: campos de acompanhamento, URLs canônicas, revisão para concorrência, tags, eventos, compromissos e preferências. Os registros anteriores são classificados como candidaturas enviadas, mantendo `AppliedAt` como a data conhecida de criação/atualização e adicionando um evento de preservação.
- `ScreenshotFolderImport`: associa grupos, hashes e relatórios persistentes às vagas.
- `LocalVisionCache`: acrescenta apenas a tabela de cache visual; não remove dados nem altera migrations anteriores.
- `ScreenshotCapture`: adiciona somente a coluna opcional `OcrCapture` em `Jobs`, para texto por imagem/consolidado, data UTC, origem e identificador revisável. Não armazena screenshots nem modifica registros anteriores.

A coluna antiga `AppliedAt` é conservada por compatibilidade. `HasApplied` distingue oportunidades apenas salvas: nessa situação, o valor técnico da coluna nunca é apresentado como data de candidatura ou contado nas métricas. `CreatedAt`, `UpdatedAt` e eventos usam UTC; a interface converte para horário local. Compromissos e datas de próxima ação usam semântica de calendário local.

Índices únicos protegem URL normalizada, URL canônica e a identidade externa conhecida. A aplicação também cruza URLs originais e canônicas entre registros. Nenhuma vaga é eliminada automaticamente por similaridade de texto. Uma revisão otimista detecta gravações concorrentes, e alterações do agregado e histórico são persistidas na mesma transação do EF.

O antigo `JobTracker/jobtracker.db` não é removido nem sobrescrito. Para utilizá-lo, abra **Configurações → Restaurar backup** e selecione esse arquivo: a aplicação prepara uma cópia, valida e migra essa cópia antes de substituir o banco ativo, mantendo backup dos dados atuais. Também é possível importar registros JSON para juntar históricos sem substituir registros existentes. Duplicidades antigas que inviabilizem índices exigem revisão; não são removidas silenciosamente.

## Importação, exportação e backup

- **JSON, versão 1:** inclui vagas, identidade, dados da candidatura, histórico, tags e compromissos. Preferências pertencem ao backup SQLite, não ao JSON.
- **CSV UTF-8:** separador `;`, cabeçalhos em português, aspas escapadas e campos multilinha. Valores que podem iniciar fórmulas de planilha recebem prefixo de proteção. O importador aceita o formato exportado pelo JobTracker e desfaz apenas essa proteção.
- **Prévia:** informa registros aceitos, ignorados por ID/URL e rejeitados. Empresa/cargo iguais são destacados para confirmação. Nenhum registro existente é sobrescrito. A inserção dos aceitos é atômica e revalida duplicidades antes da gravação.
- Limites de importação: 50 MB, 10.000 registros e limites de tamanho dos campos. CSV não contém o histórico nem a agenda anteriores; use JSON ou SQLite para preservá-los. Exportações de registros legados sem cargo/empresa precisam desses campos preenchidos para reimportação; restauração SQLite preserva esses registros diretamente.
- **Backup:** usa `SqliteConnection.BackupDatabase`, apropriado para banco em uso, escreve em arquivo temporário e publica a cópia completa. Escolha um novo nome; um backup existente não é sobrescrito.
- **Restauração:** exige confirmação, verifica integridade, relacionamentos, versão e esquema, prepara migrações em cópia, cria backup de segurança privado e substitui o destino por transação SQLite. A etapa final não é interrompida pela opção Cancelar.

Evite downgrade depois de usar os recursos novos: a migração inversa remove campos/tabelas adicionais. Faça backup antes de usar ferramentas de migração manualmente. Não copie apenas o `.db` de um banco em uso; prefira o mecanismo de backup do aplicativo.

## Arquitetura

Monólito modular em um executável WPF. Presentation adapta os fluxos de cadastro e relatório; Application coordena análise, agrupamento, duplicidade e registro; Domain protege invariantes de vagas e candidaturas. Infrastructure implementa preparação de imagens, um único cliente Ollama, arquivos e EF Core/SQLite. A grafia histórica Infraestructure permanece nos diretórios.

Os dois modos convergem em OllamaClient, com HttpClient gerenciado pela DI, JSON Schema, validação tipada, cancelamento e uma inferência por vez. Os lotes usam TrackingService e o repositório existente; nenhuma transação SQLite permanece aberta durante a inferência.

## Publicar para Windows

```powershell
dotnet publish JobTracker/JobTracker.csproj -c Release -r win-x64 --self-contained true -p:PublishSingleFile=false -o artifacts/win-x64
```

Distribua a pasta inteira, sem trimming. Para uma distribuição sem runtime, use --self-contained false e instale .NET Desktop Runtime 10. O Ollama é uma instalação independente. Publique em uma pasta vazia para não carregar resíduos de builds anteriores dos mecanismos removidos.

## Verificação

Restore, build, formatação e consistência do modelo EF são os checks desta migração. Nenhum teste novo foi criado nem executado. O único teste WPF existente teve apenas a construção das dependências adaptada, pois referenciava classes removidas. Compilação não comprova qualidade do agrupamento nem inferência real; veja o relatório da migração para limitações verificadas.
