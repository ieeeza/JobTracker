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
