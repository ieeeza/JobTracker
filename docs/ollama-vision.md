# Análise exclusiva por IA local

Ollama local, modelo padrão qwen3-vl:4b-instruct (também aceita qwen3-vl:8b), POST /api/chat com imagens e JSON Schema. A instalação e execução do Ollama são externas ao aplicativo. Verificar conexão consulta /api/tags, exige o modelo instalado e rejeita modelos remotos. Apenas localhost e endereços IP de loopback são aceitos; redirects, proxy e cookies estão desativados.

OllamaClient é compartilhado por extração individual, agrupamento e extração em lote. HttpClient pertence à DI. Um semáforo mantém apenas uma inferência pesada por vez. CancellationToken é propagado até HTTP e leitura de arquivos. Há espera até conclusão ou timeout configurável, erro tipado para conexão, modelo ausente, memória, resposta inválida e contexto insuficiente. Não existe fallback.

## Preparação e limites

ImagePreparationService valida conteúdo e dimensões com WIC. Padrões: 20 MB por arquivo, 16.000 pixels por lado, 24 milhões de pixels, proporção até 80:1 e 256 MB de imagens comprimidas/miniaturas no cadastro individual. Inferência: três imagens, 24 MB e 24 milhões de pixels por requisição; grupos maiores são fracionados. Agrupamento: janelas de duas a oito imagens, limitadas também pelo tamanho configurado no Ollama.

PNG/JPEG conservam a resolução; BMP/WEBP são normalizados sem perda para PNG. Não se ampliam capturas: interpolação não acrescenta informação e pode quadruplicar os pixels enviados. Não se reduz texto pequeno automaticamente: quando uma captura não cabe, a operação exige ajuste dos limites ou recorte legível. Miniaturas são só para apresentação, nunca entrada da IA. Arquivos originais não são alterados. WEBP requer um codec WIC disponível no Windows. MaxPreparedDimension permanece no contrato de preferências por compatibilidade; não provoca redimensionamento.

Na análise individual, PreferLowMemoryIndividualAnalysis está ativado por padrão, inclusive ao carregar preferências antigas que não contenham essa propriedade. Esse modo usa uma imagem por requisição, contexto limitado ao menor valor entre o configurado e 4096, sem expansão automática. Os resultados completos são combinados com as validações existentes. Uma resposta truncada produz erro explícito, preserva o formulário e orienta recortar a captura ou desativar a economia de memória. As configurações salvas de contexto não são sobrescritas. Desativando a opção, voltam os limites configurados (padrão 8192, máximo de expansão 32768) e a repetição limitada após resposta truncada. A importação por pasta continua usando seus limites próprios. keep_alive padrão 600 segundos evita descarregamento entre requisições.

A extração solicita cada seção no campo apropriado, sem duplicar requisitos, responsabilidades e benefícios em description; o mapeamento compõe a descrição final. A tela individual exibe o tempo decorrido real e o contexto da requisição, sem estimar um prazo de conclusão.

Diagnóstico local em 27/10/2026: RTX 4060 Ti com 8188 MiB, 7712 MiB ocupados; Ollama com contexto 16384, cache KV de 2304 MiB e somente 25/37 camadas na GPU. Uma chamada registrada terminou em HTTP 500 após 3min20s. O modo econômico reduz a pressão do contexto, mas o ganho depende da memória disponível e do conteúdo das imagens; esses dados não constituem um benchmark da versão corrigida.

## Cache e validação

VisionAnalysisCache armazena apenas respostas estruturadas, sem imagens ou Base64. A chave considera SHA-256 das imagens efetivamente preparadas, sua ordem, modelo/digest instalado, prompt/schema, versão do protocolo e opções. Uma imagem isolada, uma janela de agrupamento e um grupo completo têm contextos diferentes: não se substitui um pelo outro. Janelas sobrepostas exigem nova comparação quando a vizinhança muda. Repetições de conjuntos idênticos reutilizam respostas compatíveis e revalidam seus contratos.

Os schemas de agrupamento e extração são separados. O agrupamento também pode retornar dados da partição para economizar uma segunda chamada. O parser exige cobertura contígua exata dos índices, campos e tamanhos válidos; múltiplas vagas ou identidades divergentes impedem cadastro automático. Confiança é uma estimativa do modelo, não uma probabilidade calibrada.

Screenshots são conteúdo não confiável. Prompts mandam ignorar instruções presentes nas imagens. A IA não recebe ferramentas, acesso ao banco, comandos de sistema ou execução de código. Logs omitem imagens, texto completo e raciocínio do modelo. Campos ausentes permanecem vazios e são revisáveis.
