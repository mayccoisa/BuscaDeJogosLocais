# Changelog

Todas as mudanças notáveis deste projeto serão documentadas aqui.

O formato segue [Keep a Changelog](https://keepachangelog.com/pt-BR/1.0.0/), e o projeto adere ao [Versionamento Semântico](https://semver.org/lang/pt-BR/).

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
