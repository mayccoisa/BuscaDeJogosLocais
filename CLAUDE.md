# CLAUDE.md — Regras do Projeto BuscaDeJogosLocais

Extensão Playnite em C# + WPF (.NET Framework 4.7.2). Plugin do tipo `LibraryPlugin`.

---

## Stack e Restrições de Código

- **Linguagem**: C# com sintaxe máxima **C# 5** (sem `?.`, sem `=>` expression body, sem string interpolation `$""`, sem `nameof`). O compilador usado é `MSBuild 4.0` (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe`).
- **UI**: WPF/XAML com data binding. **Todo estilo e toda cor vivem em `Ui/Theme.xaml`** — o mesmo dicionário da extensão Playnite Hub, herdado do BAML da tela de configurações do Aniki Helper. Cada tela mergeia o dicionário por `pack://application:,,,/BuscaDeJogosLocais;component/Ui/Theme.xaml` e usa as chaves (`Card`, `PrimaryButton`, `ThemedDataGrid`, `StatusPill`, `PageTitle`…). **Não escreva cor literal em tela**: a única exceção é a cor semântica de status, que sai do `StatusToBrushConverter`. Antes da 0.9.0 havia `#11FFFFFF`, `Gold`, `LightGreen` e `OrangeRed` espalhados por 726 linhas de um XAML só, e mudar o visual virou caçada por literal.
- **Uma aba, um arquivo**: a tela de configurações é um shell (`BuscaDeJogosLocaisSettingsView.xaml`) com uma página por aba em `Ui/` (`InicioPage`, `BuscaPage`, `PastasPage`, `ReparoPage`, `DuplicadosPage`, `HistoricoPage`, `AjustesPage`, `AtualizacoesPage`). Os code-behinds são vazios de propósito: regra que vive em página não é testável. **Configuração e tabela não dividem a mesma aba** — foi assim que as opções cresceram até deixar a tabela de pastas com zero pixel.
- **Verificar XAML antes de instalar**: `StaticResource` com chave inexistente compila e só estoura quando a tela abre. Ao mexer em XAML, cheque as chaves contra o `Theme.xaml` e carregue as telas por reflexão em thread STA (`powershell.exe -STA`, ver seção 4b da skill `desenvolvimento-playnite`).
- **Build**: Sempre usar `BuscaDeJogosLocais.Local.csproj` para builds locais (referencia as DLLs de `D:\Playnite\`). O `BuscaDeJogosLocais.csproj` (referenciado pela `.sln`) usa NuGet e é o que a CI compila — **não** apague nem dessincronize os dois.
- **Deploy**: Executar `.\deploy.ps1` para compilar e copiar para a pasta de extensões do Playnite (`D:\Playnite\Extensions\...`).
- **Novos arquivos `.cs`**: Registrar como `<Compile Include>` nos **dois** `.csproj` (`BuscaDeJogosLocais.csproj` e `BuscaDeJogosLocais.Local.csproj`).
- **Novos arquivos XAML**: Sempre registrar o `.xaml` como `<Page>` e o `.cs` como `<Compile DependentUpon>` nos dois arquivos `.csproj` e `.Local.csproj`.
- **Lógica pura e testes**: Lógica de scan/caminhos sem dependência de Playnite/WPF deve ficar em `LocalGameUtils.cs` (testável). Rodar `.\tests\run-tests.ps1` antes de um release e confirmar **todos os testes passando** (compila com o `csc.exe` do Framework, sem NuGet/SDK).

---

## Processo de Release

Ao fazer uma nova versão, **obrigatoriamente** seguir todos os passos abaixo:

### 1. Atualizar o CHANGELOG.md
- Adicionar uma nova seção `## [X.Y.Z] - YYYY-MM-DD` no topo (abaixo do cabeçalho).
- Usar as categorias: **Adicionado**, **Alterado**, **Corrigido**, **Removido**.
- Descrever cada mudança de forma clara para o usuário final entender — não usar jargão interno de código.
- Exemplo de entrada:
  ```markdown
  ## [0.2.0] - 2026-06-01

  ### Adicionado
  - Filtro por gênero na tela de scan.

  ### Corrigido
  - Crash ao escanear pastas sem permissão de leitura.
  ```

### 2. Atualizar a versão
- Editar `extension.yaml` → campo `Version`.
- Seguir Semântico: `MAJOR.MINOR.PATCH` (ex: nova feature = MINOR, correção = PATCH).

### 3. Compilar e testar
- Rodar `.\tests\run-tests.ps1` e confirmar **todos os testes passando**.
- Rodar `.\deploy.ps1` e confirmar **0 erros**.
- Reiniciar o Playnite para validar.

### 4. Commitar, taggear e fazer push
```
git add CHANGELOG.md extension.yaml [arquivos alterados]
git commit -m "chore: bump version to X.Y.Z"
git tag vX.Y.Z
git push origin main
git push origin vX.Y.Z
```

### 5. Publicação automática (CI) + ajuste das notas
- **O push da tag `vX.Y.Z` dispara o workflow `.github/workflows/release.yml`**, que compila a `.sln`, empacota o `.pext` com o Toolbox do Playnite e cria o GitHub Release automaticamente. **Não** é preciso gerar o `.pext` nem rodar `gh release create` manualmente.
- O workflow usa `generate_release_notes: true` (notas automáticas). Depois que o release aparecer, **editar as notas para espelhar o `CHANGELOG.md`**:
  ```
  gh release edit vX.Y.Z --title "vX.Y.Z - <resumo>" --notes-file <arquivo.md>
  ```
- **Não** sobrescrever o asset `.pext` gerado pela CI (ele é o canônico, empacotado pelo Toolbox).

---

## Estrutura de Arquivos

| Arquivo | Responsabilidade |
|---|---|
| `BuscaDeJogosLocais.cs` | Classe principal do plugin, menus, scan, importação, lógica de negócio |
| `LocalGameUtils.cs` | Lógica pura de scan/caminhos (sem Playnite/WPF); o plugin delega a ela — base dos testes |
| `BuscaDeJogosLocaisSettings.cs` | Modelos de dados, ViewModel, commands da UI de configurações |
| `BuscaDeJogosLocaisSettingsView.xaml` | UI das configurações (TabControl com abas) |
| `ScanResultWindow.xaml` | Janela standalone de resultados de scan (usada pelo menu e scan automático) |
| `IntegrityResultView.xaml` | Janela de resultado da verificação de integridade |
| `ConsoleLibraryWindow.xaml` | Prévia de "Completar Biblioteca": console (Fonte) de cada jogo de emulação, lido do emulador que o jogo referencia |
| `tests/RegressionTests.cs` | Testes de regressão da lógica de `LocalGameUtils` (assertions próprias) |
| `tests/run-tests.ps1` | Compila (csc.exe) e executa os testes — sem NuGet/SDK |
| `.github/workflows/release.yml` | CI que empacota o `.pext` e publica o GitHub Release no push de tag `v*` |
| `extension.yaml` | Metadados da extensão (id, nome, versão) |
| `CHANGELOG.md` | Histórico de mudanças por versão — **sempre atualizar antes de um release** |
| `deploy.ps1` | Script de build + cópia para pasta de extensões do Playnite |
