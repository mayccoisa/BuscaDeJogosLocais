# 🎮 Busca de Jogos Locais - Extensão para Playnite

Esta extensão automatiza o catálogo de jogos armazenados localmente no seu computador, permitindo que você gerencie e inicie seus jogos sem depender de launchers externos (como Steam ou Epic).

## ✨ Funcionalidades Principais

- **Escaneamento Automático**: Monitora pastas específicas e detecta quando novos executáveis (.exe) de jogos são adicionados.
- **Ferramenta de Importação Manual**: Uma aba dedicada para buscar e selecionar quais jogos você deseja adicionar à sua biblioteca.
- **Identificação por HD (Features)**: Marca automaticamente em qual disco rígido o jogo está instalado (ex: `HD D:`, `HD E:`). Isso permite filtrar sua biblioteca por local físico.
- **Atualização Retroativa**: Botão especial para aplicar as tags de HD em jogos que já foram importados anteriormente.
- **Integração Nativa**: Inicia jogos diretamente pelo Playnite, suportando o rastreio oficial de tempo de jogo.
- **Interface Otimizada**: Tabela com colunas redimensionáveis, ordenáveis e checkboxes de alta visibilidade (Stroke/Borda).

## 🚀 Como Configurar

1.  **Instalação**: Certifique-se de que a extensão está na pasta `%AppData%/Playnite/Extensions/BuscaDeJogosLocais`.
2.  **Configurar Pastas**: Nas configurações da extensão, adicione as pastas onde você guarda seus jogos (ex: `G:\Jogos`).
3.  **Habilitar Tags de HD**: Na aba **Pastas Monitoradas**, ative a opção "Identificar o HD..." se desejar usar os filtros por disco.
4.  **Importar**: Vá na aba **Busca e Importação**, clique em **ESCANEAR AGORA** e selecione os jogos que deseja levar para a sua biblioteca do Playnite.

## 🛠️ Detalhes Técnicos

- **Tecnologia**: C# / .NET Framework 4.6.2 / WPF.
- **Dependências**: Playnite SDK.
- **Lançamento**: Utiliza `AutomaticPlayController` para máxima compatibilidade com o rastreio de tempo do Playnite.

---
*Desenvolvido como uma solução robusta para gerenciamento de bibliotecas de jogos manuais.*
