# Changelog

Todas as mudanças notáveis deste projeto serão documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/), e o projeto adere ao [Versionamento Semântico](https://semver.org/lang/pt-BR/).

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
