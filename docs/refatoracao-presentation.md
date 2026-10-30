# Refatoração da apresentação WPF

## Diagnóstico (fase 1)

O projeto é um executável WPF em .NET 10, organizado como monólito modular.
Não há biblioteca MVVM instalada: ObservableModel implementa INotifyPropertyChanged
e UiCommand implementa ICommand. App.xaml.cs é a raiz de composição com DI.

MainWindow recebe ShellViewModel e usa a propriedade Page (texto) para escolher
DataTemplates em Resources/Views.xaml. Todos esses templates recebem o próprio
Shell como DataContext. EditorView.xaml é outro dicionário; ImageImportView herda
o Shell e BatchImportView troca explicitamente o DataContext para Batch.

Shell concentra navegação, filtros, projeções do dashboard/quadro/agenda, formulário,
persistência, duplicidades, preferências, transferência de dados e comandos de imagem.
Suas partials cuidam da análise e da aplicação das sugestões ao formulário.
Action e Batch.Action despacham operações por strings. Isso impede descobrir o
dono de uma ação pelo binding e acopla as telas ao Shell.

EditorViewModel já representa os campos editáveis e também é reutilizado na revisão
de lote. ImageImportViewModel controla imagens, progresso e resultados; BatchImportViewModel
controla descoberta, processamento e revisão; OllamaSettingsViewModel valida a configuração.
Essas responsabilidades serão preservadas. BarTemplate, JobCard e DesignSystem são
recursos compartilhados legítimos. Code-behind trata drag/drop, foco, clipboard e ciclo de vida.

Os casos de uso existentes são TrackingService, JobImageAnalysisService,
BatchImportCoordinator, BatchReviewService, ImageAnalysisSettings e DataTransferService.
SQLite, Ollama e arquivos permanecem nos adaptadores existentes. Não é necessário
alterar o domínio, contratos persistidos, migrations ou dependências NuGet.

## Plano incremental

1. Extrair as cinco telas e o editor para UserControls; manter estilos e recursos.
2. Distribuir estado e comandos entre ViewModels de tela, mantendo o formulário
   reutilizável e extraindo apenas a coordenação comum de operações e atualização de dados.
3. Trocar a navegação por destinos tipados e CurrentViewModel, registrado por DataTemplates.
4. Conferir referências, DI, recursos, bindings, build e testes existentes.

Riscos principais: DataContexts dos templates aninhados, seleção no quadro,
continuidade do cancelamento, recarregamento após salvar e preservação de alterações
não salvas. A compilação XAML não comprova, sozinha, os bindings em execução.

## Estado recebido

Já existiam movimentos para Views, ViewModels e Services, alterações em App/MainWindow,
README, .gitignore, um projeto de testes não rastreado e código/dependência PdfPig.
Esses últimos itens ficam fora da implementação; nenhuma funcionalidade PDF será criada.
O build inicial da solução falhou porque o teste existente não importava o novo
namespace de DesktopImageServices. O executável compilou, mas App.xaml ainda apontava
para o dicionário EditorView na localização antiga.

## Estrutura final

```text
JobTracker/
├── MainWindow.xaml / .xaml.cs        # janela, atalhos e ciclo de vida
├── App.xaml / .xaml.cs               # recursos e composição de dependências
└── Presentation/
    ├── Views/
    │   ├── Shell/ShellView.xaml
    │   ├── Dashboard/DashboardView.xaml
    │   ├── Jobs/JobsView.xaml
    │   ├── Board/BoardView.xaml
    │   ├── Agenda/AgendaView.xaml
    │   ├── Settings/SettingsView.xaml
    │   ├── Editor/EditorView.xaml
    │   └── Imports/
    │       ├── ImageImportView.xaml
    │       └── BatchImportView.xaml
    ├── ViewModels/
    │   ├── ShellViewModel.cs
    │   ├── PageDestination.cs
    │   ├── DashboardViewModel.cs
    │   ├── JobsViewModel.cs
    │   ├── BoardViewModel.cs
    │   ├── AgendaViewModel.cs
    │   ├── SettingsViewModel.cs
    │   ├── OllamaSettingsViewModel.cs
    │   ├── EditorViewModel.cs
    │   ├── JobEditorViewModel.cs
    │   ├── JobEditorViewModel.Analysis.cs
    │   ├── JobEditorViewModel.Images.cs
    │   └── Imports/
    │       ├── ImageImportViewModel.cs
    │       └── BatchImportViewModel.cs
    ├── Commands/
    │   ├── UiCommand.cs
    │   └── AsyncUiCommand.cs
    ├── Components/JobTemplates.xaml
    ├── Converters/NavigationSelectionConverter.cs
    ├── Resources/
    │   ├── DesignSystem.xaml
    │   └── Views.xaml
    ├── Services/
    │   ├── DesktopServices.cs
    │   ├── DesktopImageServices.cs
    │   ├── JobWorkspace.cs
    │   └── UiOperationState.cs
    ├── ObservableModel.cs
    ├── MotionPreferences.cs
    └── DiagnosticLogger.cs
```

Cada View também possui seu `.xaml.cs`. Nas telas extraídas ele apenas chama
`InitializeComponent`. As importações conservam os eventos visuais de drag/drop,
seleção e posicionamento do relatório. Nenhum UserControl cria seu ViewModel.

## Navegação e DataContexts

1. A DI em `App.xaml.cs` cria uma instância de cada ViewModel de tela para a janela.
2. `MainWindow.DataContext` recebe `ShellViewModel`; `ShellView` herda esse contexto.
3. Os botões usam `NavigateCommand` com `PageDestination` via `x:Static`.
   Os rótulos em português são separados dos destinos do enum.
4. `ShellViewModel.CurrentViewModel` aponta para a instância da tela escolhida.
5. O `ContentControl` de `ShellView` apresenta essa instância. Os seis DataTemplates
   implícitos de `Resources/Views.xaml` associam o tipo do ViewModel ao UserControl.
6. Vagas e Lixeira reutilizam `JobsViewModel`, alternando `IsTrash`. Nova vaga e
   Detalhes reutilizam `JobEditorViewModel`. Filtros, ordenação, cards e estado dos
   relatórios permanecem nas instâncias, sem recriação de ViewModels ao navegar.
7. Abertura de vagas usa eventos com `JobInfo`, recebidos pelo Shell. A confirmação
   de descarte continua antes de sair de um formulário alterado.

`ImageImportView` é o painel composto das duas abas do cadastro e recebe
`JobEditorViewModel`: os bindings `Images.*` acessam o estado/comandos da importação
individual; `Ollama.*` acessa a configuração compartilhada. `BatchImportView`
define explicitamente `DataContext="{Binding Batch}"`. A seleção dentro dos
templates do quadro busca `BoardView.DataContext`, sem depender do Window/Shell.

## Responsabilidades e serviços

| ViewModel | Responsabilidade | Dependências principais |
|---|---|---|
| Shell | Navegação, atalhos globais, coordenação entre telas e fechamento | ViewModels das telas, JobWorkspace, UiOperationState, DesktopServices |
| Dashboard | Indicadores e atividade recente | DashboardSummary, JobWorkspace |
| Jobs | Pesquisa, filtros, ordenação, cards, seleção, recuperação e arquivamento | TrackingService, JobWorkspace, DesktopServices |
| Board | Colunas por status e mudança de etapa | JobsViewModel (filtros/seleção compartilhados), TrackingService, JobWorkspace |
| Agenda | Período, compromissos, conclusão e abertura da vaga | TrackingService, JobWorkspace |
| Settings | Tema, movimento, importação/exportação JSON/CSV e backup/restauração | IPreferences, IDatabaseMaintenance, DataTransferService, DesktopServices |
| OllamaSettings | Validação, persistência e verificação de configurações locais | ImageAnalysisSettings, IOllamaConnection |
| JobEditor | Cadastro/detalhes, salvamento, duplicidades, histórico, agendamento e aplicação de sugestões | TrackingService, JobIdentifier, JobWorkspace, serviços desktop e ViewModels de importação |
| Editor | Campos editáveis reutilizados no cadastro e na revisão do lote | DTO JobContent e catálogo de status |
| ImageImport | Imagens, ordem, prévia, clipboard, análise e progresso | IJobImageImporter, JobImageAnalysisService, ImageLimits, DesktopImageServices |
| BatchImport | Pasta, processamento, histórico, relatório, revisão e agrupamento | Casos de uso/contratos BatchImport, ImageAnalysisSettings e serviços desktop |

`UiOperationState` centraliza apenas exclusão de operações concorrentes na UI,
cancelamento, tarefa aguardada no fechamento, mensagens e tratamento de erros.
Os comandos assíncronos expõem `ExecuteAsync`; o nome passado à operação serve
somente ao diagnóstico, sem selecionar comportamento por string. `AsyncUiCommand`
não depende dos serviços nem de ViewModels. `UiCommand<T>` valida o tipo de seu parâmetro.

`JobWorkspace` carrega os DTOs de `TrackingService` e notifica as telas após
gravações. Não implementa regras de negócio nem persistência. Não foram criadas
interfaces de ViewModel, mediadores, frameworks de navegação ou dependências NuGet.
Os ViewModels não referenciam EF/SQLite ou implementações dos módulos de infraestrutura.

As partials foram preservadas onde têm propósito: análise/aplicação de sugestões e
imagens/duplicidades pertencem agora a `JobEditorViewModel`, não ao Shell.
A confirmação de gravação continua precedendo a limpeza do cadastro; falha ao
recarregar os painéis após o commit não é apresentada como falha de inserção.

## Inventário de alterações

Arquivos novos nesta refatoração, relativos a `JobTracker/Presentation`:

- `Views/{Shell,Dashboard,Jobs,Board,Agenda,Settings,Editor}/<nome>View.xaml`
  e os respectivos `.xaml.cs`.
- `ViewModels/{Dashboard,Jobs,Board,Agenda,Settings,JobEditor}ViewModel.cs`.
- `ViewModels/PageDestination.cs`.
- `Commands/AsyncUiCommand.cs` e `Commands/UiCommand.cs` (extraído de ObservableModel).
- `Services/JobWorkspace.cs` e `Services/UiOperationState.cs`.
- `Components/JobTemplates.xaml`.
- Fora da Presentation: `docs/refatoracao-presentation.md`.

Arquivos existentes modificados ou movidos:

- `App.xaml`, `App.xaml.cs`, `MainWindow.xaml`, `MainWindow.xaml.cs`.
- `Presentation/ObservableModel.cs`, `Resources/DesignSystem.xaml`, `Resources/Views.xaml`.
- `ShellViewModel.cs` foi refeito em `ViewModels`; suas partials `Import` e `Images`
  foram transferidas para `JobEditorViewModel.Analysis.cs` e `JobEditorViewModel.Images.cs`.
- `EditorViewModel` e `OllamaSettingsViewModel` permanecem em `ViewModels`.
- `ImageImportViewModel` e `BatchImportViewModel` foram organizados em `ViewModels/Imports`.
- `ImageImportView` e `BatchImportView`, incluindo seus code-behinds, foram organizados
  em `Views/Imports` e seus bindings passaram a apontar para comandos específicos.
- `NavigationSelectionConverter` foi movido para `Converters` e passou a comparar destinos tipados.
- Os movimentos anteriores de `DesktopServices` e `DesktopImageServices` para `Services`
  foram incorporados com seus namespaces, preservando suas implementações.
- O arquivo **já existente e não rastreado** `JobTracker.Tests/ImageImportViewTests.cs`
  teve somente imports, construção das dependências e montagem da UI ajustados.
  O host agora é uma janela WPF e o contexto informa `ScreenshotModeIndex`.
  Todas as asserções e o número de testes foram preservados. Nenhum teste foi criado.

Remoções lógicas: o antigo dicionário `Resources/EditorView.xaml` foi substituído
por `Views/Editor/EditorView.xaml`; o dispatcher `Action`, `ActionRequested` do lote
e o switch central de operações foram removidos. Os demais caminhos removidos no
diff são movimentos ou extrações dos componentes, sem exclusão de funcionalidades.

## Verificação e limites

- `dotnet build JobTracker/JobTracker.slnx --no-restore -v minimal`: aprovado,
  **0 erros e 0 warnings**, após cada fase de código.
- `dotnet build JobTracker/JobTracker.slnx -c Release --no-restore -v minimal`:
  aprovado, **0 erros e 0 warnings**.
- `dotnet test JobTracker.Tests/JobTracker.Tests.csproj --no-build --no-restore -v minimal`:
  **1 aprovado, 0 falhas, 0 ignorados**. O teste exercita reabertura e bindings de
  progresso e conteúdo histórico somente leitura, com e sem captura histórica.
- `dotnet format whitespace JobTracker.csproj --no-restore --include Presentation App.xaml.cs MainWindow.xaml.cs --verify-no-changes`
  (executado no diretório `JobTracker`): aprovado, nenhuma alteração necessária.
- `git diff --check`: aprovado.
- Comparação estática entre os XAMLs originais e finais de Dashboard, Jobs, Board,
  Agenda, Settings, Editor e ambas as importações: estrutura de elementos e propriedades
  estáticas preservadas. Bindings, comandos e namespaces foram excluídos dessa
  comparação porque são o objeto da refatoração.

Validação em execução com banco exclusivo em
`artifacts/presentation-validation/jobtracker.db`: inicialização/DI, dashboard,
abertura do cadastro, salvamento manual por Ctrl+S, limpeza após confirmação,
listagem do registro, projeção no quadro e abertura de detalhes pela seleção no
template do quadro; carregamento da agenda e das configurações. Após a extração
final do Shell, o aplicativo foi reaberto e mostrou os dados persistidos e os
indicadores, Ctrl+N abriu o cadastro e a aba de lote carregou seu relatório e
controles com o DataContext próprio. Os dados reais do usuário não foram usados.

O comando de verificação do Ollama apresentou a falha de conexão esperada e liberou
a interface. **Inferência real e cancelamento de inferência não foram validados**,
pois o serviço local não respondeu. Também não foram exercitados de ponta a ponta:
processamento/retomada de lote, drag/drop/clipboard, alteração de status, conclusão
de compromissos, duplicidade com dados reais, backup/restauração, transferência
JSON/CSV, tema escuro e todas as combinações de filtros. Seus fluxos foram revisados
no código e seus bindings compilados; isso não equivale a validação funcional completa.

## Commits e preservação do trabalho local

1. `05e722d` — diagnóstico arquitetural.
2. `c9c20d9` — Views independentes e recursos compartilhados.
3. `c33ec51` — responsabilidades e comandos por ViewModel, com integração mínima
   da navegação para manter a fase compilável.
4. `1dc32a3` — ShellView, navegação tipada, namespaces e dependências finais.
5. O commit de entrega deste relatório registra a conclusão da validação e a
   limpeza final: atalho de salvamento também aguardável e remoção de delegação redundante.

Os commits incluem somente a refatoração da Presentation, sua integração e este
relatório, incorporando os movimentos anteriores necessários da Presentation.
O projeto de testes que já estava não rastreado permanece assim; seus ajustes
locais não foram transformados em um novo projeto de testes no histórico.
README, .gitignore, AGENTS.md, documentos anteriores e o código/dependência PDF
preexistentes permanecem fora dos commits. Domain, Application, Infrastructure,
migrations e dados/configurações pessoais não foram alterados nesta tarefa.
