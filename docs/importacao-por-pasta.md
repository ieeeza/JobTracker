# Importação automática por pasta — Qwen3-VL

Em Nova vaga → Importação em lote, selecione a pasta, confira a ordem e inicie. São aceitos PNG, JPEG, BMP e WEBP com codec WIC. A descoberta valida um arquivo de cada vez, utiliza hash do conteúdo, ordenação natural e timestamps disponíveis. Data de modificação não representa data de captura. A sequência pode ser corrigida antes do início. Originais não são alterados.

## Fluxo visual

1. Pequenas janelas (padrão três imagens, configurável de duas a oito) são enviadas ao Qwen, com sobreposição de uma imagem entre janelas. O limite Ollama de imagens por requisição também é respeitado e deve ser pelo menos dois para agrupar.
2. O modelo identifica partições contíguas observando empresa, cargo, cabeçalhos, identificadores, continuidade e contexto visual. O contrato deve cobrir cada índice uma vez e na ordem.
3. A captura sobreposta conecta a última partição à próxima janela. Divergências de identidade deixam o grupo para revisão. Limites incertos afetam também os vizinhos, evitando salvar uma vaga parcial.
4. Se uma partição analisada cobre o grupo completo, sua extração é reutilizada. Grupos que atravessam várias janelas são extraídos como um conjunto, em requisições limitadas. Não há OCR tradicional nem análise baseada em texto concatenado.
5. A partição final é publicada de forma atômica. As respostas de janelas concluídas já ficam no cache; cancelar antes de terminar o agrupamento não publica grupos parciais.
6. Cada grupo válido passa por TrackingService e pelas regras existentes de duplicidade. O registro é salvo como Salva, sem enviar candidatura e sem confirmação individual.

A confiança visual exige ausência de ambiguidade, empresa e cargo identificados, descrição presente, imagens válidas e extração validada. JSON correto não garante verdade: os dados e as imagens permanecem revisáveis. O modelo pode errar limites ou conteúdo; não há promessa de agrupamento infalível.

## Duplicidade, cancelamento e retomada

Hashes de origem e chave do grupo impedem repetir inserções. Duplicidade confirmada aponta para o registro existente; possível duplicidade fica pendente. Hash já ligado a outra vaga exige revisão. As mesmas regras também detectam duplicações dentro do lote.

Cada vaga usa uma transação curta. Nenhuma transação fica aberta durante inferência. O checkpoint é salvo após cada etapa/registro. Se a aplicação encerrar depois de salvar a vaga mas antes do checkpoint, a chave de origem recupera o vínculo. O botão Cancelar importação no rodapé interrompe HTTP e preserva o progresso confirmado.

Retomar ignora registros importados, duplicados, descartados e pendências humanas; tenta grupos pendentes e falhos. Os arquivos são conferidos novamente pelo hash antes de salvar respostas preservadas. Falha de conexão/modelo/timeout/servidor interrompe o lote com diagnóstico e permite retomar. Erros locais e grupos ambíguos não impedem os demais grupos. Reprocessar solicita inferência nova apenas para o grupo selecionado, ignorando o cache desse pedido. A retomada normal reutiliza cache compatível.

Relatórios antigos mantêm seus dados e associações. Pendências produzidas pelo mecanismo anterior não são salvas automaticamente na migração: exigem revisão visual. Mudança de modelo/digest, prompt ou configuração invalida respostas pendentes para salvamento automático, preservando intervenções humanas e registros concluídos.

## Relatório e revisão

O histórico SQLite informa imagens analisadas, grupos, salvas, duplicadas, pendências, falhas, tempo total, tempo de agrupamento/extração, chamadas efetivas, cache e durações retornadas pelo Ollama. Cache não incrementa chamadas nem inventa tempo de inferência. Grupos são estimativas até revisão.

É possível filtrar resultados, abrir o registro correspondente, visualizar capturas, editar os campos, preservar rascunhos, salvar, separar, mover capturas, unir e reanalisar. A edição de vaga já salva atualiza o mesmo ID. Alterações na composição de um grupo salvo exigem revisão. Não se unem automaticamente dois grupos vinculados a registros distintos. Ao separar um grupo salvo, atualize primeiro a origem para liberar os hashes para a nova vaga.

Exportar relatório JSON reutiliza ITransferFiles e grava o resumo, resultados e origens. É um relatório de consulta, não um arquivo de reimportação de vagas. Para transferir registros use Configurações; para preservar tudo, inclusive histórico, preferências e cache, use backup SQLite.

## Limites e desempenho

Padrão: até 1000 arquivos, uma inferência por vez, janela de três imagens e keep_alive de 600 segundos. Limites são tipados e persistidos entre execuções. Não se carregam todas as imagens da pasta em memória. Respostas HTTP são limitadas a 2 MB; extrações provisórias reaproveitáveis têm limite agregado de 16 milhões de caracteres. Pastas maiores podem ser divididas sem perder a proteção contra duplicidade e o cache por conteúdo.

Não foi criado ou executado teste automatizado nesta migração. A qualidade e a velocidade reais dependem das capturas, da memória e do Ollama instalado e exigem validação com inferência real.
