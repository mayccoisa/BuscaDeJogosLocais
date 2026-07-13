# Changelog

Todas as mudanças notáveis deste projeto serão documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/), e o projeto adere ao [Versionamento Semântico](https://semver.org/lang/pt-BR/).

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
