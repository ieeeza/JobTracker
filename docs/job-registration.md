# Cadastro por capturas individuais

A tela Nova vaga contém duas abas: Capturas individuais e Importação em lote.

Na aba individual, todas as imagens pertencem à mesma vaga. Use Adicionar prints, arraste arquivos ou cole uma captura; remova itens, amplie e ajuste a ordem antes de Analisar com IA. São aceitos PNG, JPEG, BMP e WEBP quando houver decodificador WIC instalado.

O único analisador é Qwen3-VL 8B via Ollama local. As imagens são preparadas e enviadas em conjuntos limitados; o JSON é validado antes de preencher campos vazios. Valores já digitados são preservados, com aviso se divergirem da sugestão. Identidades conflitantes ou multipleJobs rejeitam a combinação.

Cargo, empresa, descrição, requisitos, tecnologias, remuneração, localização, modalidade, senioridade, contrato e URL são editáveis. Responsabilidades e benefícios ficam na descrição, conforme o domínio existente. Não há campos fictícios para satisfazer validações.

Salvar vaga consulta duplicidade confirmada e possível. Uma duplicidade confirmada permite abrir o histórico; uma possível exige decisão explícita no modo individual. A IA nunca salva por conta própria neste modo. O status escolhido determina se houve candidatura: análise e gravação não enviam candidaturas.

Após confirmação da gravação, o formulário e as imagens são limpos, a tela permanece em Nova vaga e a aba individual continua selecionada. Falha ou recusa de possível duplicidade preserva o preenchimento. A edição de um registro existente atualiza o mesmo ID, inclusive a URL opcional, com normalização e proteção contra duplicidade.

Dados históricos de OCR continuam disponíveis na revisão de registros antigos. Eles não são usados como mecanismo de análise ou fallback. A importação JSON/CSV em Configurações é transferência de registros existentes, não análise de texto copiado.
