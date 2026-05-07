# CLAUDE.md — Regras do Projeto BuscaDeJogosLocais

Extensão Playnite em C# + WPF (.NET Framework 4.7.2). Plugin do tipo `LibraryPlugin`.

---

## Stack e Restrições de Código

- **Linguagem**: C# com sintaxe máxima **C# 5** (sem `?.`, sem `=>` expression body, sem string interpolation `$""`, sem `nameof`). O compilador usado é `MSBuild 4.0` (`C:\Windows\Microsoft.NET\Framework64\v4.0.30319\MSBuild.exe`).
- **UI**: WPF/XAML com data binding. Seguir o padrão de estilos existente (fundo `#11FFFFFF`, texto `Gold` para títulos, agrupamentos com `GroupHeaderStyle`).
- **Build**: Sempre usar `BuscaDeJogosLocais.Local.csproj` para builds locais (referencia as DLLs de `C:\Users\mayco\Downloads\Playnite\`).
- **Deploy**: Executar `.\deploy.ps1` para compilar e copiar para a pasta de extensões do Playnite.
- **Novos arquivos XAML**: Sempre registrar o `.xaml` como `<Page>` e o `.cs` como `<Compile DependentUpon>` nos dois arquivos `.csproj` e `.Local.csproj`.

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

### 3. Compilar e testar o deploy
- Rodar `.\deploy.ps1` e confirmar **0 erros**.
- Reiniciar o Playnite para validar.

### 4. Commitar, taggear e publicar
```
git add CHANGELOG.md extension.yaml [arquivos alterados]
git commit -m "chore: bump version to X.Y.Z"
git tag vX.Y.Z
git push origin main
git push origin vX.Y.Z
```

### 5. Criar pacote .pext e GitHub Release
- Zipar: `BuscaDeJogosLocais.dll`, `extension.yaml`, `icon.png`, `Localization/`
- Renomear para `BuscaDeJogosLocais_vX.Y.Z.pext`
- Criar release no GitHub com `gh release create vX.Y.Z BuscaDeJogosLocais_vX.Y.Z.pext`
- As notas do release devem espelhar o conteúdo adicionado no `CHANGELOG.md`.

---

## Estrutura de Arquivos

| Arquivo | Responsabilidade |
|---|---|
| `BuscaDeJogosLocais.cs` | Classe principal do plugin, menus, scan, importação, lógica de negócio |
| `BuscaDeJogosLocaisSettings.cs` | Modelos de dados, ViewModel, commands da UI de configurações |
| `BuscaDeJogosLocaisSettingsView.xaml` | UI das configurações (TabControl com abas) |
| `ScanResultWindow.xaml` | Janela standalone de resultados de scan (usada pelo menu e scan automático) |
| `IntegrityResultView.xaml` | Janela de resultado da verificação de integridade |
| `extension.yaml` | Metadados da extensão (id, nome, versão) |
| `CHANGELOG.md` | Histórico de mudanças por versão — **sempre atualizar antes de um release** |
| `deploy.ps1` | Script de build + cópia para pasta de extensões do Playnite |
