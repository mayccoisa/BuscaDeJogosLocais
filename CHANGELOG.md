# Changelog

Todas as mudanças notáveis deste projeto serão documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/), e o projeto adere ao [Versionamento Semântico](https://semver.org/lang/pt-BR/).

---

## [0.9.0] - 2026-08-24

### Corrigido
- **Jogo que muda de pasta não é mais tratado como jogo que sumiu.** Até agora a extensão comparava a biblioteca com o disco só pelo caminho exato. Bastava mover ou renomear a pasta de um jogo para ele virar dois problemas ao mesmo tempo: a pasta nova aparecia como "jogo novo para importar" e o registro antigo aparecia como ausente, já marcado para ser desinstalado. Importar criava o jogo duplicado; desinstalar tirava da biblioteca um jogo que estava no disco, funcionando. Agora a busca reconhece que é a mesma instalação e oferece reapontar, mantendo o registro com o tempo de jogo, as capas e as tags.
- **A busca por jogos perdidos parou de descartar o acerto em silêncio.** Ela encontrava o executável e, se o nome da pasta nova não fosse igual ao nome do jogo nem ao da pasta antiga, jogava o resultado fora e continuava procurando — justamente o caso de quem move e renomeia a pasta. O jogo voltava como "Não Encontrado" mesmo estando no disco.
- **Nada que a extensão encontrou nasce marcado para desinstalar.** Antes, o item "Não Encontrado" já vinha selecionado, então um erro da busca virava desinstalação com um clique. Agora só a evidência forte vem marcada, e jogo com pasta encontrada não entra na ação de desinstalar nem se você marcar à mão.
- **Jogo sem ação de arquivo passou a ser procurado.** A busca exigia que o jogo tivesse um executável configurado; quem só tinha a pasta de instalação nunca era procurado, e ficava para sempre na lista de ausentes.
- A verificação de integridade também procura onde o jogo está hoje. Ela só sabia oferecer duas saídas, as duas destrutivas (remover da biblioteca ou marcar como desinstalado), e agora tem "Reapontar" para o que apenas mudou de lugar.

### Adicionado
- **Coluna "Por quê"**: cada jogo encontrado mostra em que a extensão se baseou para dizer que é ele — mesmo executável, arquivo do mesmo tamanho, mesma estrutura interna de pastas, nome equivalente ignorando versão e grupo de release, ou o executável existir numa única pasta de todo o disco monitorado. A situação é "Mudou de pasta" quando a evidência é forte, e "Provável (confira)" quando pede sua conferência. Duas pastas igualmente plausíveis derrubam a confiança de propósito: reapontar para a pasta errada é perder o vínculo do jogo certo sem perceber.
- **Aba "Início"**, com o retrato da sua biblioteca local: quantos jogos estão nela, quantas pastas do disco ficaram de fora, quantos jogos estão com a pasta ausente e quantas pastas você monitora — mais uma frase dizendo o que fazer a seguir. Antes era preciso abrir cinco abas e apertar o botão de busca de cada uma para saber se havia algo errado.
- Aviso quando uma pasta monitorada está inacessível (HD externo desligado, unidade de rede fora, letra de disco trocada). É a causa mais comum de "sumiu tudo", e agora ela aparece antes de qualquer botão que marque jogo como desinstalado.

### Alterado
- **A tela de configurações foi repaginada inteira**, no mesmo padrão visual da extensão Playnite Hub: superfícies escuras, cartões, tipografia própria e o dourado marcando a aba selecionada. Antes eram nove abas com as cores fixas espalhadas por 726 linhas de um arquivo só.
- O que abre por cima da tela também seguiu o tema: a lista do seletor de pasta (que aparecia branca, com o item destacado em amarelo do Windows), as dicas de texto, as barras de rolagem e a setinha dos grupos das tabelas.
- As abas foram reorganizadas: as opções de nome, versão, metadados e busca automática saíram de dentro de "Pastas Monitoradas" e ganharam a aba **Ajustes** — é ali que elas cresciam até empurrar a tabela de pastas para fora da tela. As quatro abas que eram só registro (importações, ignorados, desinstalados e duplicados removidos) viraram uma aba **Histórico** com abas laterais.
- "Reparar Desinstalados" virou **"Jogos que sumiram"**, e a ação em destaque passou a ser reapontar, não desinstalar.
- A busca de jogos perdidos ficou mais rápida: ela varria a árvore de pastas inteira uma vez para cada jogo ausente, e agora lê o disco uma vez só para todos.

---

## [0.8.0] - 2026-08-05

### Adicionado
- **Completar Biblioteca dos jogos de emulação.** Jogo de emulação entra na biblioteca sem Fonte nenhuma, então a tela de perfil do tema joga todos eles num balde só, ao lado de Steam e Xbox. O botão novo (aba "Pastas Monitoradas", e também no menu **Local › Completar Biblioteca**) lê o emulador e o perfil que cada jogo referencia e usa o **console** como Fonte: PlayStation 2, Nintendo Switch, PlayStation, Game Boy Advance, PSP. Aí o perfil passa a mostrar onde os seus jogos estão realmente concentrados.
- O console sai da definição de emulador do próprio Playnite — o mesmo dado que ele usa para importar a ROM —, não de palpite sobre o nome da pasta ou do emulador. Quando o jogo não tem perfil de emulador definido, vale a plataforma que ele já tem; se não houver nenhuma das duas, a linha fica em branco para você preencher. Console nunca é inventado.
- Antes de gravar aparece uma prévia com uma linha por jogo: emulador, biblioteca atual, console novo, de onde o console foi lido e o que muda. Dá para editar o nome na tabela e desmarcar linha a linha, e no topo fica o resumo de quantos jogos ficam em cada console. Nada é gravado até clicar em Aplicar.
- Jogo que já tem Fonte de loja (Steam, Epic) aparece na lista mas **não** vem marcado: a fonte dele é informação de verdade e não é sobrescrita sem você mandar.

---

## [0.7.3] - 2026-08-02

### Alterado
- **Operações em lote ficaram mais leves.** Importar vários jogos, renomear em massa, aplicar tags de HD ou a fonte "Local", relinkar e baixar metadados atualizavam a biblioteca jogo a jogo, e o Playnite redesenhava a tela a cada um. Agora cada operação faz uma atualização só, no fim. Em biblioteca pequena a diferença é discreta; em biblioteca grande é a diferença entre travar e não travar.

---

## [0.7.2] - 2026-08-02

### Alterado
- O cabeçalho de cada pasta na lista de busca passa a mostrar **"17/20 pastas"** em vez de "17 itens": quantos jogos daquela pasta estão na lista contra quantas pastas existem no disco. O que falta para fechar a conta é exatamente o que ficou de fora — mesma leitura da aba "Pastas Monitoradas". Vale na tela de configurações e na janela do scan.

### Corrigido
- **A tabela de resumo das pastas voltou a aparecer.** As caixas de configuração no topo da aba consumiam toda a altura e a tabela ficava com zero pixel, sumindo da tela sem aviso. Agora a aba rola e a tabela tem altura própria.
- O resumo em texto que ficava embaixo virou uma linha de total, já que o detalhe por pasta agora está na tabela.

---

## [0.7.1] - 2026-08-02

### Corrigido
- **A atualização agora troca de versão de verdade.** Mesmo abrindo o Playnite com o arquivo, ele apenas *registrava* a instalação para uma inicialização seguinte — o resultado é que você reiniciava, o botão dizia que tinha atualizado e a extensão continuava na versão antiga. Agora, com sua confirmação, o Playnite é fechado, os arquivos da extensão são trocados direto na pasta dela e o Playnite volta já na versão nova, sem depender de mais nenhum reinício.
- A aba "Atualizações" ganhou o botão **"Ver log da última atualização"** e um texto explicando que a "Versão instalada" no topo é a prova de que a troca deu certo. Se alguma vez ela não subir, o log diz em que passo parou.

---

## [0.7.0] - 2026-08-02

### Adicionado
- **A aba "Busca e Importação" já abre com os jogos que estão na biblioteca**, em vez de uma lista vazia esperando um scan. Dá para ver de cara o que a extensão está gerenciando; o botão de escanear continua servindo para procurar novidades no disco.
- Nova coluna **"Última verificação"**: a data do último scan que confirmou aquela pasta no disco. É a resposta para "esse jogo ainda estava lá quando?" — jogo nunca verificado aparece com "—".

### Corrigido
- **Legibilidade das tabelas no tema escuro.** O cabeçalho e as células das grades usavam as cores padrão do Windows (fundo claro com texto escuro), que ficavam ilegíveis sobre o tema escuro do Playnite. Agora todas as tabelas da extensão seguem a cor de texto do tema em uso, inclusive na linha selecionada.

---

## [0.6.0] - 2026-08-02

### Adicionado
- **A aba "Pastas Monitoradas" agora mostra o que cada pasta trouxe para a biblioteca.** Cada pasta vira uma linha com a situação (Tudo importado / Parcialmente importado / Nada importado / Pasta inacessível) e as contagens: quantos jogos estão na biblioteca, quantos ficaram de fora, quantos estão com a pasta ausente e quantos você mandou ignorar. Fica na cara qual pasta não está puxando nada.
- Botão **"Ver jogos"** em cada pasta: abre a lista completa daquela pasta, com nome, versão, status e caminho, e um filtro **"Mostrar só o que ficou de fora"** para ir direto ao que deu problema.
- Jogos locais que estão fora de todas as pastas monitoradas aparecem numa linha própria, em vez de sumirem da conta.

### Corrigido
- **A atualização da extensão agora instala de verdade.** O arquivo era entregue ao Playnite que já estava aberto, e nesse caso ele apenas traz a janela para a frente e descarta o arquivo — por isso a mensagem "confirme na janela que o Playnite vai abrir" aparecia e nada acontecia. Agora a extensão avisa que o Playnite precisa fechar, encerra o programa e o reabre já na tela de instalação. Se você preferir fazer depois, ela mostra onde o arquivo foi salvo.

---

## [0.5.1] - 2026-08-02

### Corrigido
- **A busca de capa agora traz resultado**. A extensão pedia os metadados no modo "download em massa", em que a fonte (IGDB e afins) tenta adivinhar o jogo sozinha e devolve vazio quando não tem certeza do título — era por isso que a capa só vinha quando você rodava o "Download metadata" do Playnite na mão.
- A busca de metadados depois de renomear já não nasce atrás da janela de prévia: ela só começa quando você fecha a prévia.

### Alterado
- Quando algum jogo continua sem capa depois da busca automática, a extensão avisa quantos foram e oferece **uma segunda tentativa no modo manual** — o mesmo do "Download metadata" do Playnite, em que a fonte pode abrir uma janela para você escolher o jogo certo. Fica sempre como escolha sua, nunca automático.

---

## [0.5.0] - 2026-08-02

### Adicionado
- **Nome limpo na importação**: o nome da pasta de release deixa de virar o nome do jogo. "STARDUST.Wish.of.Witch.v20260729-P2P" entra na biblioteca como **"Stardust Wish of Witch"**, com versão, grupo de release, "Repack", "MULTi9" e afins removidos. Hífen legítimo do título é preservado ("Pac-Man World 2 Re-Pac"), assim como siglas ("S.T.A.L.K.E.R."), numerais romanos ("Fable II") e camelCase ("EverSiege").
- **A versão não se perde**: o que estava no nome da pasta (v1.00.1, v20260729, Build 12345, Update 3) vai para o campo nativo **Versão** do jogo no Playnite, onde dá para consultar e filtrar.
- **Prévia antes de importar**: na tela de resultados do scan, o nome e a versão agora são **editáveis** direto na tabela, e o nome original da pasta aparece ao passar o mouse. Nada é gravado antes de você conferir.
- **Limpeza retroativa**: novo item de menu **Local › Limpar Nomes dos Jogos Locais** (e botão equivalente nas configurações) analisa os jogos já importados e abre uma janela de prévia com "nome atual → nome novo", item a item, editável e desmarcável. Depois de aplicar, a extensão oferece buscar capa e metadados de novo — é justamente o nome sujo que fazia as fontes não acharem nada na importação.
- Duas opções novas nas configurações, em **Nome e versão**: *Limpar o nome da pasta ao importar* e *Guardar a versão detectada*, ambas ligadas por padrão.

---

## [0.4.0] - 2026-07-28

### Adicionado
- Nova aba **"Atualizações"** nas configurações: um botão verifica se saiu uma versão mais nova da extensão, mostra o que mudou e, se você confirmar, baixa e abre a instalação no próprio Playnite. Acabou a rotina de entrar no GitHub e baixar o arquivo à mão. Basta reiniciar o Playnite depois de instalar.
- **Download automático de metadados após importar**: ao terminar a importação, a extensão busca capa, ícone, imagem de fundo, descrição, gêneros, desenvolvedores, publishers, data de lançamento, notas e links nas fontes de metadados que você já usa no Playnite (IGDB, Xbox Metadata e afins). Os jogos entram na biblioteca já com a capa, sem precisar rodar o download um por um.
  - Vale para os três caminhos de importação: aba "Busca e Importação", janela do scan manual e janela do scan automático.
  - Campo que o jogo já tenha preenchido **nunca** é sobrescrito, e dá para acompanhar e cancelar pela barra de progresso.
  - A opção fica na aba "Pastas Monitoradas", em **Metadados**, e já vem ligada.

---

## [0.3.0] - 2026-07-13

### Adicionado
- Nova aba **"Duplicados"**: encontra jogos locais duplicados (mesmo nome) e, para cada cópia, verifica se há um **save dentro da pasta**. As cópias **sem save** já vêm marcadas como seguras para remoção. Ao confirmar, a pasta é enviada para a **Lixeira do Windows** (recuperável) e o jogo é removido da biblioteca. O plugin sempre mantém pelo menos uma cópia de cada jogo.
- Configuração de **padrões de save** (dentro da aba Duplicados): defina nomes de pastas/arquivos (ex: `saves`) ou extensões (ex: `.sav`) que indicam progresso salvo. Já vem com uma lista padrão.
- Nova aba **"Duplicados Removidos"**: histórico das pastas enviadas para a Lixeira, incluindo se tinham save e o destino, para acompanhamento.

### Alterado
- Na aba **"Reparar Desinstalados"**, os jogos **não encontrados** agora já vêm **marcados** automaticamente (assim como os prontos para relink), evitando ter que selecionar um por um antes de marcar como desinstalados.

---

## [0.2.1] - 2026-06-30

### Alterado
- A **biblioteca** dos jogos importados agora aparece como **"Local"** na barra lateral do Playnite, em vez de "Busca de Jogos Locais" — mais fácil de encontrar e com nome mais claro. Vale automaticamente para os jogos já importados (basta reiniciar o Playnite). A Fonte dos jogos já era "Local".
- O grupo da extensão no menu **Extensões** passou a se chamar **"Local"**.

---

## [0.2.0] - 2026-06-30

### Adicionado
- **Marcar jogos como desinstalados**: igual ao comportamento das bibliotecas integradas (ex: Steam), agora é possível marcar um jogo como desinstalado sem removê-lo da biblioteca. Disponível em dois lugares:
  - Na janela de **Verificar Integridade da Biblioteca**, pelo botão "🚫 Marcar como Desinstalado".
  - Na aba **Relink** das configurações, pelo botão "🚫 MARCAR COMO DESINSTALADOS", que age apenas nos jogos com status "Não Encontrado" — ou seja, depois que o plugin confirmou que o jogo não está em nenhuma das pastas monitoradas.
- **Botão para marcar jogos já importados como Fonte "Local"** (configurações), aplicando a nova marcação retroativamente à biblioteca existente.
- **Histórico de desinstalações**: nova aba **"Desinstalados"** nas configurações, que registra todos os jogos marcados como desinstalados pela extensão (data, nome e pasta de origem). Cada item tem um botão **"Reinstalar"** para marcar o jogo como instalado novamente, e há a opção de limpar o histórico.

### Alterado
- Jogos importados agora recebem a **Fonte "Local"** (campo Source nativo do Playnite), em vez de serem identificados apenas pela biblioteca da extensão. Isso melhora a exibição e permite filtrar os jogos locais pelos filtros nativos do Playnite.
- A busca de jogos perdidos (aba Relink) agora também considera jogos **instalados cujo executável sumiu**, não apenas os já marcados como desinstalados.

---

## [0.1.2] - 2026-05-07

### Adicionado
- **Menu de acesso direto**: novo item "Buscar Novos Jogos" em `Extensões → Busca de Jogos Locais`. Abre uma janela de scan e importação sem precisar entrar nas Configurações.
- **Scan automático semanal**: opção nas configurações (aba "Pastas Monitoradas") para escanear automaticamente ao iniciar o Playnite. O intervalo em dias é configurável (padrão: 7). A modal de importação aparece **somente se houver jogos novos**.
- **Lista de excluídos reversível**: botão 🚫 em qualquer resultado de scan ignora permanentemente aquele jogo nas buscas futuras. Nova aba **"Excluídos"** nas configurações exibe todos os itens ignorados e permite removê-los individualmente para reverter a exclusão.
- **Nova janela `ScanResultWindow`**: janela independente de scan/importação com DataGrid agrupado por pasta, filtro "ocultar já importados", seleção por checkbox e botão de ignorar por linha.

### Alterado
- `GetMainMenuItems` agora expõe dois itens: "Buscar Novos Jogos" e "Verificar Integridade da Biblioteca".
- `OnApplicationStarted` agora dispara a verificação de scan automático.
- `ExecuteManualScan` agora filtra automaticamente os caminhos presentes na lista de excluídos.

---

## [0.0.2] - 2026-05-06

### Adicionado
- Versão inicial extraída como repositório standalone.
- Escaneamento manual de pastas monitoradas com detecção de `.exe`.
- Importação de jogos com tag automática "Importado Local".
- Identificação de HD como Feature (ex: `HD D:`).
- Histórico de importações com filtro por pasta.
- Ferramenta "Reparar Desinstalados" para relinkagem de jogos movidos.
- Verificação de integridade da biblioteca via menu de Extensões.
- FileSystemWatcher para monitoramento em tempo real das pastas.
