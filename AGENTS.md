# AGENTS.md — Padrões obrigatórios de engenharia de software

> Este arquivo define como agentes de IA devem analisar, implementar, refatorar, testar e entregar alterações neste repositório.
> **Objetivo:** software correto, seguro, coeso, fortemente tipado, testável, sustentável e compatível com a arquitetura existente.
> **Prioridade:** requisitos explícitos do usuário e instruções de sistema/de desenvolvedor têm precedência sobre este arquivo. Em caso de conflito entre estas regras e o código atual, preserve o funcionamento e explique a mudança necessária; não faça uma reescrita ampla sem solicitação.

## 1. Princípios inegociáveis

1. **Correção antes de velocidade.** Não implemente soluções provisórias para apenas “fazer passar”.
2. **Sem atalhos silenciosos.** Nunca oculte erros com `catch` vazio, valores fictícios, coerções inseguras, desativação de validações ou supressão indiscriminada de avisos.
3. **Tipagem rigorosa de ponta a ponta.** Trate entradas externas como não confiáveis e valide-as nos limites do sistema.
4. **SOLID e Clean Architecture com pragmatismo.** Preserve as dependências corretas sem criar camadas, interfaces ou abstrações sem propósito real.
5. **Código legível antes de código esperto.** Nomes precisos, funções coesas, fluxos explícitos e baixa complexidade.
6. **Mudanças pequenas e completas.** Resolva a causa raiz; evite refatorações não relacionadas à tarefa.
7. **Comportamento verificável.** Toda alteração relevante exige testes adequados e execução dos checks disponíveis.
8. **Segurança e privacidade por padrão.** Não exponha segredos nem enfraqueça autenticação, autorização, auditoria ou proteção de dados.
9. **Sem afirmações não verificadas.** Não declare que compilou, executou ou testou sem realmente fazê-lo.
10. **Preserve contratos e compatibilidade.** Mudanças incompatíveis devem ser explicitadas e, quando necessárias, acompanhadas de migração/versionamento.

## 2. Antes de alterar qualquer código

1. Leia este arquivo e outros `AGENTS.md` aplicáveis ao diretório alterado.
2. Inspecione a estrutura do projeto, documentação, convenções, dependências e testes existentes.
3. Identifique a linguagem, framework, versões, organização dos módulos e comandos reais de build/test/lint.
4. Rastreie o fluxo completo afetado: entrada → validação → caso de uso → domínio → persistência/integrações → saída.
5. Formule uma hipótese da causa raiz antes de corrigir bugs; não corrija apenas o sintoma.
6. Para alterações não triviais, elabore um plano curto: arquivos afetados, impacto arquitetural, riscos e estratégia de testes.
7. Quando houver ambiguidade, adote a solução mais segura e alinhada ao repositório. Só interrompa para perguntar se a decisão depender de requisito de negócio essencial ausente.
8. Não sobrescreva alterações preexistentes do usuário; examine o diff e preserve trabalho não relacionado.

## 3. SOLID — aplicação obrigatória

### S — Single Responsibility Principle
- Cada classe/módulo deve ter uma responsabilidade coesa e um motivo predominante para mudar.
- Separe regras de negócio, orquestração, persistência, transporte HTTP e apresentação.
- Evite serviços genéricos que concentrem múltiplos fluxos de negócio.

### O — Open/Closed Principle
- Prefira extensão por composição ou estratégias quando existir uma variação real de comportamento.
- Não crie frameworks de abstrações para cenários hipotéticos.
- Não amplie cadeias condicionais complexas quando houver uma alternativa simples e extensível.

### L — Liskov Substitution Principle
- Implementações devem respeitar o contrato, as pré-condições, as pós-condições e a semântica esperada.
- Não introduza subclasses que lançam `NotSupportedException` para operações prometidas pelo contrato.

### I — Interface Segregation Principle
- Contratos pequenos e direcionados ao consumidor, não interfaces enormes e genéricas.
- Evite métodos desnecessários e dependências em funcionalidades não utilizadas.

### D — Dependency Inversion Principle
- Regras de negócio não dependem de frameworks, banco de dados, HTTP, UI ou detalhes de infraestrutura.
- Defina abstrações onde existe uma fronteira real; implementações concretas ficam nas bordas.
- Injete dependências explicitamente; evite service locator e instanciar adaptadores dentro do domínio.

## 4. Clean Architecture e fronteiras

Respeite a arquitetura já adotada. Quando houver organização em camadas, use as seguintes direções:

```text
Presentation / API / UI  ──>  Application  ──>  Domain
Infrastructure            ──>  Application / Domain
Composition Root          ──>  registra e conecta implementações
```

- **Domain:** entidades, value objects, invariantes, políticas e eventos de domínio. Independente de framework e detalhes de I/O.
- **Application:** casos de uso, contratos/ports, coordenação de transações e autorização de aplicação. Não conhece detalhes de EF/ORM, HTTP ou UI.
- **Infrastructure:** implementa persistência, mensageria, arquivos, e-mail e serviços externos definidos pelos contratos internos.
- **Presentation:** adapta requisições/respostas, autenticação de borda, parsing e formatação; não contém regras de negócio centrais.
- **Composition Root:** configura injeção de dependências e integrações. Não transfira essa configuração para o domínio.

Regras adicionais:

- Dependências de código sempre apontam para dentro; não crie referências circulares.
- Não passe entidades de persistência diretamente para respostas públicas.
- Use DTOs/contratos próprios nos limites; mapeamento explícito quando melhorar clareza e segurança.
- Valide formato e estrutura na borda; proteja invariantes de negócio também no domínio.
- Posicione interfaces onde seus consumidores precisam delas; não crie `I*` para toda classe sem necessidade.
- Em **monólito modular**, respeite ownership dos módulos. Integração entre módulos por contratos explícitos, nunca acessando tabelas ou classes internas de outro módulo indiscriminadamente.
- Não introduza microsserviços, CQRS, event bus, repositório genérico ou Unit of Work adicional apenas por preferência arquitetural.

## 5. Código limpo e manutenibilidade

- Utilize nomes descritivos que expressem intenção de negócio; evite abreviações obscuras e nomes genéricos (`Data`, `Helper`, `Manager`, `Utils`) sem escopo claro.
- Uma função deve executar uma tarefa coesa; extraia métodos quando isso reduzir complexidade ou eliminar duplicação real.
- Prefira guard clauses, composição e estruturas explícitas a aninhamentos excessivos.
- Elimine código morto, imports não usados, comentários desatualizados e duplicação desnecessária.
- Evite números e strings mágicas; centralize constantes e configurações com significado de domínio.
- Comentários explicam **por que** uma decisão existe, não repetem o que o código faz.
- Evite estados mutáveis compartilhados, efeitos colaterais ocultos e dependências globais.
- Assinaturas, retornos, erros e efeitos devem ser previsíveis.
- Não altere formatação e convenções de arquivos não relacionados.
- Não use padrões sofisticados quando uma implementação simples e clara resolve integralmente o problema.

## 6. Tipagem forte, nulidade e contratos

### Regras gerais
- Não use escape hatches para silenciar o compilador ou o analisador.
- Diferencie ausência de valor, estado inválido, falha operacional e resultado legítimo.
- Modele estados finitos explicitamente (enums, unions, sealed classes, result types quando apropriado).
- Trate desserialização, formulários, variáveis de ambiente, banco e serviços externos como fontes potencialmente inválidas.
- Não confunda validação de tipos em tempo de compilação com validação em tempo de execução.
- Evite retornos ambíguos como `null` para representar erros quando o contrato exige diagnóstico.

### C# / .NET (quando aplicável)
- Habilite e preserve nullable reference types; respeite avisos do compilador e analisadores.
- Prefira tipos explícitos nas APIs públicas, parâmetros bem definidos e `record`/value objects quando fizer sentido.
- Não use `dynamic`, `object`, `!` (null-forgiving) ou casts inseguros para encobrir problemas de modelagem.
- Evite `.Result`/`.Wait()` em fluxos assíncronos; propague `CancellationToken` quando pertinente.
- Não exponha `IQueryable` ou entidades EF Core fora das fronteiras de persistência.
- Configure DI com ciclo de vida adequado e não capture dependências scoped em singletons.

### TypeScript / JavaScript (quando aplicável)
- Mantenha `strict: true` e verificações estritas compatíveis no `tsconfig`.
- **Proibido `any`**, inclusive `as any`, salvo exceção localizada, justificada e documentada por necessidade comprovada.
- Prefira `unknown` para dados externos, com narrowing ou schema validation antes do uso.
- Evite `as Tipo`, `!`, `@ts-ignore`, `@ts-nocheck` e regras ESLint desabilitadas para esconder erro. Exceções devem ser específicas e justificadas.
- Modele respostas de API, estados de UI e erros com unions discriminadas quando útil.
- Mantenha tipos compartilhados consistentes entre cliente e servidor; não assuma que tipo TS valida payload em runtime.

### Kotlin (quando aplicável)
- Respeite null safety; não use `!!` como substituto de validação.
- Use `sealed`/`data class` e tipos explícitos nos limites para representar estados e contratos.
- Prefira coroutines com concorrência estruturada e tratamento correto de cancelamento.
- Evite `GlobalScope`, bloqueios indevidos e exceções engolidas.

## 7. Erros, logs e observabilidade

- Falhe de modo explícito, com erro relevante e contexto suficiente para diagnóstico.
- Não use `catch` vazio, `catch (Exception)` que apenas retorna sucesso/fallback ou logs genéricos sem ação.
- Capture exceções no nível adequado; preserve stack trace e causas.
- Diferencie erros esperados de negócio, problemas de validação e falhas inesperadas de infraestrutura.
- Não faça retry indiscriminado: apenas operações seguras/idempotentes, com limites, backoff e timeout apropriados.
- Produza logs estruturados e correlacionáveis, sem senhas, tokens, dados pessoais desnecessários ou payloads sensíveis.
- Não altere invariantes ou estado de negócio somente para contornar falhas.

## 8. Segurança e integridade dos dados

- Nunca hardcode segredos, chaves privadas, credenciais ou tokens. Use mecanismos existentes de configuração e secret management.
- Valide autenticação **e** autorização em operações protegidas; UI escondida não é controle de acesso.
- Siga o princípio do menor privilégio, inclusive para banco e integrações.
- Proteja entradas contra SQL injection, XSS, SSRF, path traversal e outros riscos pertinentes.
- Utilize consultas parametrizadas e APIs seguras; não concatene SQL com input.
- Aplique transações quando múltiplas operações precisarem preservar a mesma invariante.
- Planeje migrações sem perda acidental de dados; não exclua tabelas/colunas sem avaliar impacto.
- Considere concorrência, condições de corrida, idempotência e consistência onde o fluxo exigir.
- Não enfraqueça controles de segurança para corrigir testes, ambiente ou integração.

## 9. Testes e critérios de aceitação

- Adicione ou ajuste testes proporcionais ao risco e ao comportamento alterado.
- **Unitários:** regras de negócio, invariantes, variações e casos de erro.
- **Integração:** banco, persistência, contratos HTTP, autenticação e integrações pertinentes.
- **Regressão:** para bugs, crie teste que falhe antes da correção quando viável.
- Cubra casos de sucesso, entrada inválida, ausência de recurso, falhas externas e limites relevantes.
- Evite testes frágeis que apenas espelham implementação interna.
- Não substitua testes por mocks de tudo; teste integrações reais nos pontos importantes.
- Rode os comandos existentes de format/lint/typecheck/build/test aplicáveis ao escopo.
- Não reduza cobertura, exclua testes, altere asserts ou ignore falhas para obter pipeline verde.
- Se uma verificação não puder ser executada, informe precisamente qual foi omitida e por quê.

## 10. Banco de dados e APIs

- Preserve constraints, chaves, índices e integridade referencial.
- Use migrações versionadas e reversíveis quando a tecnologia permitir.
- Não altere schema com ações destrutivas sem autorização explícita e plano de migração.
- Cuide de paginação, filtros, ordenação e performance de consultas; evite N+1 quando relevante.
- Em APIs, mantenha contratos previsíveis, validações claras, códigos HTTP corretos e formatos de erro consistentes.
- Não altere endpoints públicos ou contratos persistidos sem avaliar clientes existentes.
- Não exponha erros internos, detalhes de SQL ou stack traces em respostas públicas.

## 11. Proibições explícitas — sem atalhos

Não faça nenhuma das ações abaixo para “resolver rápido”:

- Silenciar erros de compilação/tipagem com `any`, `dynamic`, `!`, `!!`, casts forçados, `@ts-ignore`, `#pragma` ou equivalente sem causa demonstrada.
- Desabilitar lint, testes, análise estática, validação, autenticação ou autorização para contornar uma falha.
- Adicionar `TODO`, `FIXME`, `throw new NotImplementedException()`, mocks ou dados falsos no caminho crítico e apresentar a tarefa como concluída.
- Fazer `catch` genérico e retornar objeto vazio, `null`, `200 OK`, `true` ou valor default que mascare falha real.
- Duplicar regras de negócio entre controller, frontend, application e domínio.
- Usar SQL montado por concatenação, armazenar senhas em texto puro ou registrar informações sensíveis.
- Criar dependência circular, acesso direto indevido entre módulos ou acoplamento do domínio à infraestrutura.
- Alterar dependências, versões, lockfiles, workflows, configurações ou arquitetura sem relação justificada com a tarefa.
- Reescrever módulos inteiros quando uma correção localizada e consistente é suficiente.
- Assumir que compila ou funciona sem verificar quando o ambiente permite execução.

**Exceções técnicas:** se uma exceção a essas regras for inevitável por limitação comprovada, mantenha-a mínima, explique-a no resultado e apresente o risco e a alternativa. Nunca esconda a exceção.

## 12. Fluxo obrigatório para cada tarefa

### A. Entendimento
- Resuma o comportamento esperado e os critérios de aceitação.
- Identifique quais camadas e contratos serão afetados.
- Confirme as convenções existentes no código, sem presumir estrutura de pastas.

### B. Implementação
- Faça a menor mudança **completa** que resolve a causa raiz.
- Preserve a separação de responsabilidades e a direção das dependências.
- Valide entradas e proteja invariantes no nível correto.
- Atualize contratos, mapeamentos, migrations, testes e documentação quando necessários.

### C. Verificação
- Revise o diff para efeitos colaterais e mudanças acidentais.
- Execute os checks reais disponíveis: format, lint, typecheck, build e testes pertinentes.
- Verifique erros de compilador/analisador e não os suprima artificialmente.
- Confirme que o fluxo não introduziu regressões, problemas de concorrência ou vazamento de dados.

### D. Entrega
Ao concluir, responda objetivamente com:
1. **O que mudou:** comportamento e arquivos/áreas principais.
2. **Por que:** causa raiz e decisão arquitetural relevante.
3. **Verificações:** comandos executados e respectivos resultados.
4. **Pendências/riscos:** apenas os reais; indique explicitamente o que não foi validado.

## 13. Definição de pronto (Definition of Done)

Uma tarefa só está concluída quando:

- [ ] O requisito solicitado está implementado integralmente, sem caminhos críticos simulados.
- [ ] A solução respeita SOLID e as fronteiras arquiteturais existentes.
- [ ] Os contratos estão tipados, as entradas externas validadas e a nulidade tratada corretamente.
- [ ] Os erros são tratados sem mascaramento e sem vazamento de informações sensíveis.
- [ ] Testes pertinentes foram adicionados/atualizados e executados quando possível.
- [ ] Build, lint, typecheck e demais checks aplicáveis foram executados quando disponíveis.
- [ ] Não há alterações não relacionadas, dependências supérfluas ou warnings introduzidos sem justificativa.
- [ ] Não foram introduzidas vulnerabilidades, regressões ou quebras de contrato não documentadas.
- [ ] O resultado descreve fielmente o que foi feito, validado e o que ficou pendente.

**Regra final:** jamais priorize aparentar progresso em vez de entregar uma solução correta. Se houver bloqueio real, exponha-o com precisão; não invente resultados, não mascare erros e não comprometa a arquitetura para fazer a tarefa parecer terminada.
