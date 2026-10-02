# 💼 JobTracker

> Um aplicativo desktop para organizar candidaturas de emprego e evitar perder tempo se candidatando várias vezes para a mesma vaga.

JobTracker é uma aplicação desktop desenvolvida em **C# / .NET com WPF** que permite cadastrar vagas a partir de um simples link.

A aplicação acessa a página da vaga, analisa seu conteúdo e armazena informações importantes como empresa, cargo, descrição e tecnologias utilizadas.

O principal objetivo é resolver um problema comum durante a busca por emprego: **vagas repostadas, vagas fantasmas e candidaturas duplicadas**.

---

## ✨ Funcionalidades

- 🔗 Cadastro de vagas através de URL
- 🌐 Leitura de páginas renderizadas com JavaScript
- 🎭 Web scraping utilizando Playwright
- 🔎 Extração de informações da vaga
- 🏢 Identificação da empresa
- 💻 Identificação das principais tecnologias
- 📝 Armazenamento da descrição da vaga
- 🗃️ Histórico de candidaturas
- 🚫 Detecção de vagas já cadastradas
- 💾 Persistência dos dados em banco de dados
- 🖥️ Interface desktop utilizando WPF

---

## 🎯 Motivação

Durante uma busca por emprego é comum encontrar a mesma vaga diversas vezes.

Empresas podem:

- republicar uma mesma oportunidade;
- manter vagas abertas por longos períodos;
- divulgar a mesma posição em diferentes plataformas;
- manter vagas que já receberam candidatura anteriormente.

Com dezenas ou centenas de candidaturas, lembrar de todas elas se torna praticamente impossível.

O **JobTracker** cria um histórico local das vagas e permite descobrir rapidamente se uma oportunidade já foi analisada ou recebeu uma candidatura anteriormente.

---

## 🧠 Como funciona

O fluxo principal da aplicação é:

```text
Usuário cola o link da vaga
            ↓
Validação da URL
            ↓
Playwright abre a página
            ↓
JavaScript da página é executado
            ↓
Conteúdo renderizado é capturado
            ↓
Informações da vaga são analisadas
            ↓
Sistema verifica se a vaga já existe
            ↓
Dados são armazenados
            ↓
Vaga aparece no histórico
```

O uso do **Playwright** permite trabalhar com sites modernos que carregam grande parte do conteúdo dinamicamente através de JavaScript.

---

## 🛠️ Tecnologias

### Aplicação

- C#
- .NET
- WPF
- XAML

### Web Scraping

- Microsoft Playwright

### Persistência

- Entity Framework Core
- SQL

### Arquitetura

- Modular Monolith
- Repository Pattern
- Separação entre Application, Domain e Infrastructure

---

## 🏗️ Arquitetura

O projeto utiliza uma abordagem de **Monólito Modular**, mantendo uma única aplicação enquanto separa responsabilidades por módulos.

Exemplo da organização:

```text
JobTracker
│
├── Modules
│   │
│   └── JobAnalysis
│       │
│       ├── Domain
│       │   ├── Entities
│       │   └── Repositories
│       │
│       ├── Application
│       │   ├── Models
│       │   ├── Services
│       │   ├── Interfaces
│       │   └── Exceptions
│       │
│       └── Infrastructure
│           ├── Persistence
│           ├── Repositories
│           └── Scraping
│               └── Playwright
│
├── MainWindow.xaml
├── MainWindow.xaml.cs
└── App.xaml
```

Essa estrutura permite que novos módulos sejam adicionados sem transformar a aplicação em um projeto fortemente acoplado.

---

## 🎭 Por que Playwright?

Muitos sites de vagas modernos utilizam frameworks como React, Angular e outros sistemas baseados em JavaScript.

Isso significa que uma requisição HTTP simples pode retornar uma página praticamente sem conteúdo útil.

Por exemplo:

```text
HTTP Request
      ↓
HTML inicial
      ↓
JavaScript executa no navegador
      ↓
API é consultada
      ↓
Descrição da vaga aparece
```

Por isso o JobTracker utiliza **Playwright**, permitindo abrir a página em um navegador real e acessar o conteúdo depois da renderização.

---

## 🚀 Executando o projeto

### Pré-requisitos

Tenha instalado:

- .NET SDK
- Visual Studio
- Banco de dados configurado
- Playwright

Clone o projeto:

```bash
git clone https://github.com/SEU-USUARIO/JobTracker.git
```

Entre na pasta:

```bash
cd JobTracker
```

Restaure as dependências:

```bash
dotnet restore
```

Compile:

```bash
dotnet build
```

Instale os navegadores necessários para o Playwright:

```bash
pwsh bin/Debug/net*/playwright.ps1 install
```

Execute a aplicação:

```bash
dotnet run
```

---

## 🗄️ Banco de dados

O banco de dados mantém o histórico das vagas registradas.

Uma vaga pode armazenar informações como:

```text
Job
├── Id
├── Url
├── Title
├── Company
├── Description
├── Technologies
├── AppliedAt
└── CreatedAt
```

Antes de cadastrar uma nova vaga, a aplicação poderá verificar se aquela oportunidade já foi registrada anteriormente.

---

## 🗺️ Roadmap

O projeto ainda está em desenvolvimento.

Algumas funcionalidades planejadas:

- [x] Interface inicial em WPF
- [x] Campo para inserir URL da vaga
- [x] Validação de URL
- [x] Integração com Playwright
- [x] Leitura de páginas renderizadas por JavaScript
- [ ] Extração automática do cargo
- [ ] Extração automática da empresa
- [ ] Extração automática da descrição
- [ ] Identificação das principais tecnologias
- [ ] Persistência completa das vagas
- [ ] Detecção automática de candidatura duplicada
- [ ] Histórico de candidaturas
- [ ] Busca e filtros
- [ ] Status da candidatura
- [ ] Dashboard com estatísticas
- [ ] Suporte especializado para diferentes plataformas de vagas
- [ ] Análise inteligente da descrição da vaga

---

## 🔮 Ideias futuras

Além de apenas armazenar candidaturas, o JobTracker poderá evoluir para uma ferramenta completa de acompanhamento da busca por emprego.

Possíveis funcionalidades:

```text
Vaga encontrada
      ↓
Análise automática
      ↓
Compatibilidade com o perfil
      ↓
Candidatura registrada
      ↓
Entrevista
      ↓
Teste técnico
      ↓
Oferta
```

Permitindo acompanhar todo o ciclo de uma candidatura em um único lugar.

---

## 🤝 Contribuições

O projeto está sendo desenvolvido principalmente como projeto pessoal e de aprendizado, mas sugestões, issues e pull requests são bem-vindos.

Caso encontre algum problema ou tenha uma ideia de melhoria, abra uma **Issue** no repositório.

---

## 📄 Licença

Este projeto é destinado a fins de estudo e desenvolvimento pessoal.

---

<p align="center">
  Feito com ☕, C# e algumas candidaturas repetidas.
</p>
