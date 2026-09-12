using Playnite.SDK;
using Playnite.SDK.Models;
using Playnite.SDK.Plugins;
using Playnite.SDK.Events;
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Diagnostics;

namespace BuscaDeJogosLocais
{
    public class BuscaDeJogosLocais : LibraryPlugin
    {
        private static readonly ILogger logger = LogManager.GetLogger();
        private List<FileSystemWatcher> _vigias = new List<FileSystemWatcher>();
        public override Guid Id { get { return Guid.Parse("186d9374-4173-420d-b17a-e2ace45bb317"); } }
        public override string Name { get { return "Local"; } }
        public override LibraryClient Client { get { return null; } }

        /// <summary>
        /// O ícone do "Local" no menu de bibliotecas, à esquerda. É CAMINHO EM DISCO, e não um
        /// recurso embutido na DLL: o Playnite lê o arquivo direto, então ele precisa sair no
        /// pacote (ver o library-icon.png nos dois .csproj).
        ///
        /// Sem o arquivo devolvemos null, e não um caminho que não existe: null o Playnite trata
        /// como "use o padrão", e caminho quebrado vira linha sem ícone nenhum, sem erro no log.
        /// </summary>
        public override string LibraryIcon
        {
            get
            {
                try
                {
                    var pasta = Path.GetDirectoryName(
                        System.Reflection.Assembly.GetExecutingAssembly().Location);
                    var arquivo = Path.Combine(pasta, "library-icon.png");
                    return File.Exists(arquivo) ? arquivo : null;
                }
                catch (Exception)
                {
                    return null;
                }
            }
        }

        private BuscaDeJogosLocaisSettingsViewModel settings;

        public BuscaDeJogosLocais(IPlayniteAPI api) : base(api)
        {
            Properties = new LibraryPluginProperties { HasSettings = true };
            settings = new BuscaDeJogosLocaisSettingsViewModel(this);
        }

        public override void OnApplicationStarted(OnApplicationStartedEventArgs args)
        {
            UpdateWatchers();
            VerificarScanAutomatico();
        }

        public void UpdateWatchers()
        {
            foreach (var watcher in _vigias) watcher.Dispose();
            _vigias.Clear();

            if (settings.Settings.Pastas != null)
            {
                foreach (var pasta in settings.Settings.Pastas)
                {
                    if (Directory.Exists(pasta))
                    {
                        var watcher = new FileSystemWatcher(pasta) { IncludeSubdirectories = true, EnableRaisingEvents = true };
                        watcher.Created += (s, e) => {
                            logger.Info(string.Format("Novo arquivo detectado: {0}", e.FullPath));
                        };
                        _vigias.Add(watcher);
                    }
                }
            }
        }

        public void ExecuteManualScan(ObservableCollection<ScannedGame> listToPopulate, string singleFolder = null)
        {
            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                if (settings.Settings.Pastas == null || settings.Settings.Pastas.Count == 0) return;

                string agora = DateTime.Now.ToString("dd/MM/yyyy HH:mm");
                var gamesFoundMap = new Dictionary<string, ScannedGame>(StringComparer.OrdinalIgnoreCase);
                var monitoredPaths = settings.Settings.Pastas.Select(p => NormalizePath(p)).ToList();

                var pastasParaEscanear = string.IsNullOrEmpty(singleFolder)
                    ? settings.Settings.Pastas.ToList()
                    : new List<string> { singleFolder };

                foreach (var pasta in pastasParaEscanear)
                {
                    if (Directory.Exists(pasta))
                    {
                        // Guarda o denominador ("17/20") também quando o scan roda pelo menu,
                        // sem a tela de configurações ter sido aberta.
                        try { PastaTotais.Registrar(pasta, Directory.GetDirectories(pasta).Length); }
                        catch (Exception) { }

                        try
                        {
                            var files = SafeEnumerateFiles(pasta, "*.exe", progressArgs.CancelToken);
                            foreach (var file in files)
                            {
                                if (progressArgs.CancelToken.IsCancellationRequested) break;
                                
                                if (IsExplosiveFile(file)) continue;
                                if (IsExcluded(file)) continue;

                                string monitoredPai;
                                string gameRoot = GetGameRootInternal(file, monitoredPaths, out monitoredPai);
                                if (string.IsNullOrEmpty(gameRoot) || !Directory.Exists(gameRoot)) continue;

                                if (!gamesFoundMap.ContainsKey(gameRoot))
                                {
                                    bool jaExiste = PlayniteApi.Database.Games.Any(g => 
                                        g.InstallDirectory != null && 
                                        NormalizePath(g.InstallDirectory).Equals(NormalizePath(gameRoot), StringComparison.OrdinalIgnoreCase));

                                    string nomePasta = new DirectoryInfo(gameRoot).Name;
                                    string versaoDetectada;
                                    string nomeLimpo = LocalGameUtils.CleanGameName(nomePasta, out versaoDetectada);
                                    if (!settings.Settings.LimparNomeDaPasta) nomeLimpo = nomePasta;

                                    gamesFoundMap[gameRoot] = new ScannedGame
                                    {
                                        Nome = nomeLimpo,
                                        NomeOriginal = nomePasta,
                                        Versao = versaoDetectada == null ? string.Empty : versaoDetectada,
                                        CaminhoExe = file,
                                        PastaRaiz = gameRoot,
                                        PastaMonitoradaPai = monitoredPai,
                                        JaExiste = jaExiste,
                                        Selecionado = !jaExiste,
                                        UltimaVerificacao = agora
                                    };

                                    RegistrarVerificacao(gameRoot, agora);
                                }
                            }
                        }
                        catch (Exception ex) { logger.Error(ex, string.Format("Erro crítico ao escanear {0}", pasta)); }
                    }
                }
                
                // Reconciliação: pasta nova no disco + jogo da biblioteca cuja pasta sumiu podem
                // ser a MESMA instalação que mudou de lugar. Sem esta passada o scan devolvia os
                // dois lados como problemas separados — um "jogo novo" para importar e um registro
                // "ausente" para desinstalar —, e importar criava o jogo duplicado.
                try { ReconciliarPastasMovidas(gamesFoundMap, progressArgs.CancelToken); }
                catch (Exception ex) { logger.Error(ex, "Erro ao reconciliar pastas movidas"); }

                var finalResults = gamesFoundMap.Values.OrderBy(g => g.Nome).ToList();
                PlayniteApi.MainView.UIDispatcher.Invoke(() => {
                    foreach (var g in finalResults) listToPopulate.Add(g);
                });

                try { SavePluginSettings(settings.Settings); } catch (Exception) { }
            }, new GlobalProgressOptions(
                string.IsNullOrEmpty(singleFolder)
                    ? "Buscando jogos locais em todas as pastas..."
                    : string.Format("Buscando jogos locais em {0}...", singleFolder),
                true));
        }

        private bool IsExplosiveFile(string path)
        {
            return LocalGameUtils.IsExplosiveFile(path);
        }

        // Carimba a pasta como "vista agora". É o que permite responder, meses depois,
        // "quando foi a última vez que este jogo estava mesmo no disco?".
        private void RegistrarVerificacao(string pastaRaiz, string quando)
        {
            if (settings.Settings.Verificacoes == null)
                settings.Settings.Verificacoes = new ObservableCollection<VerificacaoEntry>();

            var existente = settings.Settings.Verificacoes
                .FirstOrDefault(v => v.PastaRaiz != null && v.PastaRaiz.Equals(pastaRaiz, StringComparison.OrdinalIgnoreCase));

            if (existente != null) existente.Data = quando;
            else settings.Settings.Verificacoes.Add(new VerificacaoEntry { PastaRaiz = pastaRaiz, Data = quando });
        }

        /// <summary>Data do último scan que confirmou esta pasta, ou "—" se nunca foi verificada.</summary>
        public string ObterUltimaVerificacao(string pastaRaiz)
        {
            if (string.IsNullOrEmpty(pastaRaiz) || settings.Settings.Verificacoes == null) return "—";

            var entrada = settings.Settings.Verificacoes
                .FirstOrDefault(v => v.PastaRaiz != null && v.PastaRaiz.Equals(pastaRaiz, StringComparison.OrdinalIgnoreCase));

            return entrada != null ? entrada.Data : "—";
        }

        private bool IsExcluded(string caminhoExe)
        {
            if (settings == null || settings.Settings == null) return false;
            var ignorados = settings.Settings.CaminhosIgnorados;
            if (ignorados == null) return false;
            return LocalGameUtils.IsPathExcluded(caminhoExe, ignorados.Select(e => e.CaminhoExe));
        }

        public void AdicionarExcluido(ScannedGame jogo)
        {
            if (settings.Settings.CaminhosIgnorados.Any(e => e.CaminhoExe.Equals(jogo.CaminhoExe, StringComparison.OrdinalIgnoreCase)))
                return;

            settings.Settings.CaminhosIgnorados.Add(new ExcludedEntry
            {
                CaminhoExe = jogo.CaminhoExe,
                NomeJogo = jogo.Nome,
                PastaMonitoradaPai = jogo.PastaMonitoradaPai,
                DataExclusao = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
            });
            SavePluginSettings(settings.Settings);
        }

        private void AbrirScanRapido()
        {
            if (settings.Settings.Pastas == null || settings.Settings.Pastas.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Nenhuma pasta monitorada configurada. Acesse as configurações da extensão para adicionar pastas.", "Busca de Jogos");
                return;
            }

            var resultados = new ObservableCollection<ScannedGame>();
            ExecuteManualScan(resultados);

            if (resultados.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Nenhum jogo encontrado nas pastas configuradas.", "Busca de Jogos");
                return;
            }

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = true,
                ShowMinimizeButton = false
            });
            window.Title = string.Format("Jogos Encontrados ({0})", resultados.Count);
            window.Width = 900;
            window.Height = 600;
            window.Content = new ScanResultWindow(
                resultados,
                importar: (selecionados) =>
                {
                    BaixarMetadadosDosImportados(ImportarLote(selecionados));
                },
                ignorar: (jogo) => AdicionarExcluido(jogo),
                reapontar: (movidos) => ReapontarLote(movidos)
            );
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowDialog();
        }

        private void VerificarScanAutomatico()
        {
            if (!settings.Settings.EscanearAutomaticamente) return;

            var ultimo = settings.Settings.DataUltimoScanAutomatico;
            int intervalo = settings.Settings.IntervaloScanDias;
            if (ultimo != null && (DateTime.Now - ultimo.Value).TotalDays < intervalo) return;

            if (settings.Settings.Pastas == null || settings.Settings.Pastas.Count == 0) return;

            var resultados = new ObservableCollection<ScannedGame>();
            ExecuteManualScan(resultados);

            var novos = resultados.Where(g => !g.JaExiste).ToList();

            settings.Settings.DataUltimoScanAutomatico = DateTime.Now;
            SavePluginSettings(settings.Settings);

            if (novos.Count == 0) return;

            PlayniteApi.MainView.UIDispatcher.Invoke(() =>
            {
                var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
                {
                    ShowMaximizeButton = true,
                    ShowMinimizeButton = false
                });
                window.Title = string.Format("Scan Automático: {0} novo(s) jogo(s) encontrado(s)", novos.Count);
                window.Width = 900;
                window.Height = 600;
                window.Content = new ScanResultWindow(
                    new ObservableCollection<ScannedGame>(novos),
                    importar: (selecionados) =>
                    {
                        BaixarMetadadosDosImportados(ImportarLote(selecionados));
                    },
                    ignorar: (jogo) => AdicionarExcluido(jogo),
                    reapontar: (movidos) => ReapontarLote(movidos)
                );
                window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
                window.ShowDialog();
            });
        }

        private IEnumerable<string> SafeEnumerateFiles(string path, string searchPattern, System.Threading.CancellationToken cancelToken)
        {
            var stack = new Stack<string>();
            stack.Push(path);

            while (stack.Count > 0)
            {
                if (cancelToken.IsCancellationRequested) yield break;

                string currentDir = stack.Pop();
                
                // Tenta enumerar arquivos no diretório atual
                IEnumerable<string> files = null;
                try
                {
                    files = Directory.EnumerateFiles(currentDir, searchPattern);
                }
                catch (UnauthorizedAccessException)
                {
                    continue; 
                }
                catch (Exception ex)
                {
                    logger.Debug(string.Format("Erro ao enumerar arquivos em {0}: {1}", currentDir, ex.Message));
                    continue;
                }

                if (files != null)
                {
                    foreach (var file in files) yield return file;
                }

                // Tenta enumerar subdiretórios
                IEnumerable<string> subDirs = null;
                try
                {
                    subDirs = Directory.EnumerateDirectories(currentDir);
                }
                catch (UnauthorizedAccessException)
                {
                    continue;
                }
                catch (Exception ex)
                {
                    logger.Debug(string.Format("Erro ao enumerar diretórios em {0}: {1}", currentDir, ex.Message));
                    continue;
                }

                if (subDirs != null)
                {
                    foreach (var dir in subDirs) stack.Push(dir);
                }
            }
        }

        public string NormalizePath(string path)
        {
            return LocalGameUtils.NormalizePath(path);
        }

        private string GetGameRootInternal(string filePath, List<string> monitoredPaths, out string monitoredPai)
        {
            return LocalGameUtils.GetGameRoot(filePath, monitoredPaths, out monitoredPai);
        }

        /// <summary>
        /// Casa as pastas recém-encontradas no disco com os jogos da biblioteca cuja pasta sumiu.
        /// O que casa deixa de ser "jogo novo para importar" e passa a ser "mudou de pasta",
        /// desmarcado — reapontar é decisão do usuário, e importar sem reconciliar duplicaria o jogo.
        /// </summary>
        private void ReconciliarPastasMovidas(Dictionary<string, ScannedGame> encontrados, System.Threading.CancellationToken cancelToken)
        {
            var novos = encontrados.Values.Where(g => !g.JaExiste).ToList();
            if (novos.Count == 0) return;

            var perdidos = PlayniteApi.Database.Games
                .Where(g => (g.PluginId == Guid.Empty || g.PluginId == Id) &&
                            !string.IsNullOrEmpty(g.InstallDirectory) &&
                            !Directory.Exists(g.InstallDirectory))
                .Where(g => g.GameActions == null || !g.GameActions.Any(a => a.Type == GameActionType.Emulator))
                .ToList();
            if (perdidos.Count == 0) return;

            // As pastas recém-vistas viram um índice do mesmo formato que o do reparo, para os
            // dois caminhos usarem exatamente a mesma régua de evidência.
            var indice = novos.Select(n => new PastaEmDisco
            {
                Pasta = n.PastaRaiz,
                PastaMonitoradaPai = n.PastaMonitoradaPai,
                Exes = new List<ExeEmDisco> { new ExeEmDisco { Caminho = n.CaminhoExe, Tamanho = TamanhoDe(n.CaminhoExe) } }
            }).ToList();

            var ocupadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var g in PlayniteApi.Database.Games)
            {
                if (string.IsNullOrEmpty(g.InstallDirectory)) continue;
                if (!Directory.Exists(g.InstallDirectory)) continue;
                ocupadas.Add(NormalizePath(g.InstallDirectory));
            }

            // Uma pasta nova só pode ser a casa de UM jogo perdido. Quem chega primeiro com mais
            // pontos fica com ela; o segundo continua perdido, em vez de os dois apontarem para lá.
            var tomadas = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

            foreach (var game in perdidos)
            {
                if (cancelToken.IsCancellationRequested) return;

                string exeAntigo = null;
                if (game.GameActions != null)
                {
                    var fa = game.GameActions.FirstOrDefault(a => a.Type == GameActionType.File);
                    if (fa != null && !string.IsNullOrEmpty(fa.Path)) exeAntigo = PlayniteApi.ExpandGameVariables(game, fa.Path);
                }

                var achado = ProcurarNovaCasa(game, exeAntigo, indice, ocupadas);
                if (achado == null) continue;

                string chave = NormalizePath(achado.PastaCandidata);
                int pontosDoDono;
                if (tomadas.TryGetValue(chave, out pontosDoDono) && pontosDoDono >= achado.Pontos) continue;

                var alvo = encontrados.Values.FirstOrDefault(n => NormalizePath(n.PastaRaiz).Equals(chave, StringComparison.OrdinalIgnoreCase));
                if (alvo == null) continue;

                tomadas[chave] = achado.Pontos;
                alvo.MovidoDeGameId = game.Id;
                alvo.MovidoDePasta = game.InstallDirectory;
                alvo.MovidoDeNome = game.Name;
                alvo.MotivoMudanca = achado.Motivo;
                alvo.ConfiancaMudanca = achado.Confianca;
                // Nunca pré-marcado: marcado aqui significaria IMPORTAR, que é justamente
                // criar o duplicado que esta reconciliação existe para impedir.
                alvo.Selecionado = false;
            }
        }

        /// <summary>
        /// Reaponta em lote as pastas que a reconciliação reconheceu como mudança de lugar, e
        /// devolve quantas foram. Usada pela janela do scan (menu) e pela aba de busca, para as
        /// duas telas fazerem exatamente a mesma coisa.
        /// </summary>
        public int ReapontarLote(List<ScannedGame> movidos)
        {
            if (movidos == null || movidos.Count == 0) return 0;

            int ok = 0;
            using (PlayniteApi.Database.BufferedUpdate())
            {
                foreach (var m in movidos)
                {
                    if (!m.EhMudancaDePasta) continue;
                    if (!RelocarJogo(m.MovidoDeGameId, m.PastaRaiz, m.CaminhoExe)) continue;

                    m.MovidoDeGameId = Guid.Empty;
                    m.JaExiste = true;
                    m.Selecionado = false;
                    ok++;
                }
            }

            PlayniteApi.Dialogs.ShowMessage(
                string.Format("{0} jogo(s) reapontados para a pasta onde estão hoje.", ok),
                "Reapontar");
            return ok;
        }

        private static long TamanhoDe(string caminho)
        {
            if (string.IsNullOrEmpty(caminho)) return 0;
            try { return new FileInfo(caminho).Length; }
            catch (Exception) { return 0; }
        }

        /// <summary>
        /// Reaponta um jogo da biblioteca para a pasta onde ele está hoje: caminho de instalação,
        /// ação de arquivo e o estado "instalado". Devolve false quando não havia o que mudar.
        /// Não mexe em nome, capa, tags nem tempo de jogo — o registro é o mesmo, só mudou de casa.
        /// </summary>
        public bool RelocarJogo(Guid gameId, string novaPasta, string novoExe)
        {
            var game = PlayniteApi.Database.Games.Get(gameId);
            if (game == null) return false;
            if (string.IsNullOrEmpty(novaPasta) || !Directory.Exists(novaPasta)) return false;

            game.InstallDirectory = novaPasta;
            game.IsInstalled = true;

            if (!string.IsNullOrEmpty(novoExe))
            {
                if (game.GameActions == null) game.GameActions = new ObservableCollection<GameAction>();
                var fileAction = game.GameActions.FirstOrDefault(a => a.Type == GameActionType.File);
                if (fileAction == null)
                {
                    game.GameActions.Add(new GameAction { Type = GameActionType.File, Path = novoExe, Name = "Jogar", WorkingDir = "{InstallDir}" });
                }
                else
                {
                    fileAction.Path = novoExe;
                    fileAction.WorkingDir = "{InstallDir}";
                }
            }

            PlayniteApi.Database.Games.Update(game);

            // O jogo voltou: se ele estava no histórico de desinstalações, a linha de lá deixou de
            // ser verdade. Manter faria a aba "Desinstalados" contradizer a biblioteca.
            try
            {
                if (settings.Settings.HistoricoDesinstalacoes != null)
                {
                    var entrada = settings.Settings.HistoricoDesinstalacoes.FirstOrDefault(h => h.GameId == gameId);
                    if (entrada != null) settings.Settings.HistoricoDesinstalacoes.Remove(entrada);
                    SavePluginSettings(settings.Settings);
                }
            }
            catch (Exception) { }

            return true;
        }

        /// <summary>
        /// Varre as pastas monitoradas UMA vez e devolve o índice de pastas de jogo em disco,
        /// com os executáveis de cada uma e o tamanho deles.
        /// A busca antiga do reparo varria a árvore inteira POR JOGO perdido; com dez jogos
        /// ausentes e uma biblioteca grande isso é a mesma leitura de disco repetida dez vezes.
        /// Aqui a varredura acontece uma vez e todo mundo consulta o mesmo índice.
        /// </summary>
        public List<PastaEmDisco> IndexarPastasEmDisco(System.Threading.CancellationToken cancelToken)
        {
            var porPasta = new Dictionary<string, PastaEmDisco>(StringComparer.OrdinalIgnoreCase);
            if (settings.Settings.Pastas == null) return new List<PastaEmDisco>();

            var monitoredPaths = settings.Settings.Pastas.Select(p => NormalizePath(p)).ToList();

            foreach (var pasta in settings.Settings.Pastas)
            {
                if (cancelToken.IsCancellationRequested) break;
                if (!Directory.Exists(pasta)) continue;

                try
                {
                    foreach (var file in SafeEnumerateFiles(pasta, "*.exe", cancelToken))
                    {
                        if (cancelToken.IsCancellationRequested) break;
                        if (IsExplosiveFile(file)) continue;
                        if (IsExcluded(file)) continue;

                        string monitoradoPai;
                        string gameRoot = GetGameRootInternal(file, monitoredPaths, out monitoradoPai);
                        if (string.IsNullOrEmpty(gameRoot)) continue;

                        string chave = NormalizePath(gameRoot);
                        PastaEmDisco alvo;
                        if (!porPasta.TryGetValue(chave, out alvo))
                        {
                            alvo = new PastaEmDisco
                            {
                                Pasta = gameRoot,
                                PastaMonitoradaPai = monitoradoPai,
                                Exes = new List<ExeEmDisco>()
                            };
                            porPasta[chave] = alvo;
                        }

                        long tamanho = 0;
                        try { tamanho = new FileInfo(file).Length; }
                        catch (Exception) { }

                        alvo.Exes.Add(new ExeEmDisco { Caminho = file, Tamanho = tamanho });
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, string.Format("Erro ao indexar {0}", pasta));
                }
            }

            return porPasta.Values.ToList();
        }

        /// <summary>
        /// Lê os executáveis de UMA pasta específica, no mesmo formato do índice. Serve para a
        /// pasta do próprio jogo, que pode estar fora das pastas monitoradas.
        /// </summary>
        private PastaEmDisco LerExesDaPasta(string pasta)
        {
            if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta)) return null;

            var item = new PastaEmDisco
            {
                Pasta = pasta,
                PastaMonitoradaPai = null,
                Exes = new List<ExeEmDisco>()
            };

            try
            {
                foreach (var file in SafeEnumerateFiles(pasta, "*.exe", System.Threading.CancellationToken.None))
                {
                    if (IsExplosiveFile(file)) continue;
                    if (IsExcluded(file)) continue;

                    long tamanho = 0;
                    try { tamanho = new FileInfo(file).Length; }
                    catch (Exception) { }

                    item.Exes.Add(new ExeEmDisco { Caminho = file, Tamanho = tamanho });
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, string.Format("Erro ao ler os executáveis de {0}", pasta));
                return null;
            }

            return item.Exes.Count == 0 ? null : item;
        }

        /// <summary>
        /// Procura, no índice de disco, a pasta que provavelmente é a nova casa de um jogo cuja
        /// pasta sumiu. Devolve null quando a evidência não chega ao mínimo — não sugerir nada
        /// é melhor que apontar o jogo errado, porque reapontar errado é o usuário perdendo o
        /// vínculo do jogo certo sem perceber.
        ///
        /// 'pastasOcupadas' são as pastas que outro jogo da biblioteca já reivindica: relocar
        /// para cima delas criaria dois registros apontando para a mesma instalação.
        /// </summary>
        public LocalGameUtils.RelocationMatch ProcurarNovaCasa(
            Game game, string exeAntigoCompleto,
            List<PastaEmDisco> indice, HashSet<string> pastasOcupadas)
        {
            if (indice == null) return null;

            string nomeExeAntigo = string.IsNullOrEmpty(exeAntigoCompleto)
                ? string.Empty
                : Path.GetFileName(exeAntigoCompleto);

            // A pasta do próprio jogo entra na busca. Ela pode estar fora das pastas
            // monitoradas (biblioteca antiga, jogo importado à mão), e é justamente onde mora a
            // resposta quando só o executável sumiu.
            var busca = indice;
            if (!string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory))
            {
                string minha = NormalizePath(game.InstallDirectory);
                if (!indice.Any(p => NormalizePath(p.Pasta).Equals(minha, StringComparison.OrdinalIgnoreCase)))
                {
                    var propria = LerExesDaPasta(game.InstallDirectory);
                    if (propria != null)
                    {
                        busca = new List<PastaEmDisco>(indice);
                        busca.Add(propria);
                    }
                }
            }
            indice = busca;
            if (indice.Count == 0) return null;

            // Em quantas pastas do disco esse executável aparece? Um só lugar é o que transforma
            // "mesmo nome de arquivo" em resposta única (ver ReforcarPorExclusividade).
            int pastasComEsseExe = 0;
            if (nomeExeAntigo.Length > 0)
            {
                foreach (var p in indice)
                {
                    if (p.Exes.Any(e => Path.GetFileName(e.Caminho).Equals(nomeExeAntigo, StringComparison.OrdinalIgnoreCase)))
                        pastasComEsseExe++;
                }
            }

            var candidatos = new List<LocalGameUtils.RelocationMatch>();

            string minhaPasta = string.IsNullOrEmpty(game.InstallDirectory)
                ? string.Empty
                : NormalizePath(game.InstallDirectory);

            foreach (var pasta in indice)
            {
                string chavePasta = NormalizePath(pasta.Pasta);
                // "Ocupada" quer dizer ocupada por OUTRO jogo. A pasta que o próprio jogo já
                // registra nunca é conflito: excluí-la era o que impedia a extensão de ver que
                // o jogo continuava ali, só com outro executável.
                bool ehMinha = minhaPasta.Length > 0 &&
                               chavePasta.Equals(minhaPasta, StringComparison.OrdinalIgnoreCase);
                if (!ehMinha && pastasOcupadas != null && pastasOcupadas.Contains(chavePasta)) continue;

                // Qual dos executáveis da pasta é o jogo. Pegar o primeiro da varredura era
                // sorteio: numa pasta de repack o primeiro pode ser o desinstalador.
                string caminhoEscolhido = LocalGameUtils.MelhorExeDaPasta(
                    game.Name, game.InstallDirectory, exeAntigoCompleto,
                    pasta.Pasta, pasta.Exes.Select(e => e.Caminho).ToList());
                if (string.IsNullOrEmpty(caminhoEscolhido)) continue;

                var exe = pasta.Exes.FirstOrDefault(e =>
                    e.Caminho.Equals(caminhoEscolhido, StringComparison.OrdinalIgnoreCase));
                if (exe == null) continue;
                // O executável proposto tem que existir de verdade. O índice pode estar velho,
                // e propor um caminho morto é reproduzir exatamente a falha que estamos
                // corrigindo, só que com a extensão dizendo que resolveu.
                if (!File.Exists(exe.Caminho)) continue;

                var m = LocalGameUtils.AvaliarRelocalizacao(
                    game.Name,
                    game.InstallDirectory, exeAntigoCompleto, 0,
                    pasta.Pasta, exe.Caminho, exe.Tamanho);

                if (m == null) continue;

                bool exeIgual = nomeExeAntigo.Length > 0 &&
                                Path.GetFileName(exe.Caminho).Equals(nomeExeAntigo, StringComparison.OrdinalIgnoreCase);
                if (exeIgual)
                {
                    m = LocalGameUtils.ReforcarPorExclusividade(m, exeAntigoCompleto, pastasComEsseExe);
                }

                // Nome de executável igual, e SÓ isso, não é candidato — é homônimo. Dois jogos
                // que compartilham o lançador ("start_protected_game.exe" do Denuvo,
                // "gamelaunchhelper.exe" da Store) casavam aqui, e a tela oferecia reapontar um
                // jogo para a pasta de outro. Quando o arquivo é único no disco monitorado a
                // exclusividade já limpou esta marca, e o candidato continua valendo.
                if (m.SomenteNomeDoExe) continue;

                candidatos.Add(m);
            }

            return LocalGameUtils.MelhorCandidato(candidatos);
        }

        public bool ImportarJogoManual(ScannedGame scanned)
        {
            Guid ignorado;
            return ImportarJogoManual(scanned, out ignorado);
        }

        /// <summary>
        /// Importa vários jogos de uma vez em modo bufferizado (um único refresh de biblioteca no
        /// fim, em vez de um por jogo) e devolve os ids criados. Marca os itens importados.
        /// </summary>
        public List<Guid> ImportarLote(IEnumerable<ScannedGame> selecionados)
        {
            var importados = new List<Guid>();
            if (selecionados == null) return importados;

            using (PlayniteApi.Database.BufferedUpdate())
            {
                foreach (var jogo in selecionados)
                {
                    Guid novoId;
                    if (!ImportarJogoManual(jogo, out novoId)) continue;

                    jogo.JaExiste = true;
                    jogo.Selecionado = false;
                    importados.Add(novoId);
                }
            }

            return importados;
        }

        public bool ImportarJogoManual(ScannedGame scanned, out Guid gameId)
        {
            gameId = Guid.Empty;

            // Pasta que a reconciliação já reconheceu como a nova casa de um jogo existente não é
            // importação: importar aqui criaria o segundo registro do MESMO jogo, com o tempo de
            // jogo e as capas do original ficando para trás no registro antigo.
            if (scanned.EhMudancaDePasta) return false;

            string normRoot = NormalizePath(scanned.PastaRaiz);
            if (PlayniteApi.Database.Games.Any(g => g.InstallDirectory != null && NormalizePath(g.InstallDirectory).Equals(normRoot, StringComparison.OrdinalIgnoreCase)))
                return false;

            var game = new Game(scanned.Nome)
            {
                PluginId = Id,
                InstallDirectory = scanned.PastaRaiz,
                IsInstalled = true,
                GameActions = new ObservableCollection<GameAction> { 
                    new GameAction { Type = GameActionType.File, Path = scanned.CaminhoExe, Name = "Jogar", WorkingDir = "{InstallDir}" } 
                }
            };

            if (settings.Settings.GuardarVersaoDetectada && !string.IsNullOrEmpty(scanned.Versao))
            {
                game.Version = scanned.Versao;
            }

            var tag = PlayniteApi.Database.Tags.Add("Importado Local");
            game.TagIds = new List<Guid> { tag.Id };

            if (settings.Settings.TagDriveAsFeature)
            {
                ApplyDriveTag(game);
            }

            ApplyLocalSource(game);

            PlayniteApi.Database.Games.Add(game);
            gameId = game.Id;

            try {
                settings.Settings.HistoricoImportacoes.Add(new ImportLogEntry { 
                    DataImportacao = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"), 
                    NomeJogo = scanned.Nome, 
                    Origem = scanned.PastaRaiz,
                    PastaMonitoradaPai = scanned.PastaMonitoradaPai
                });
                SavePluginSettings(settings.Settings);
            } catch (Exception) { }
            return true;
        }

        /// <summary>
        /// Roda o download de metadados (capa, ícone, fundo, descrição, gêneros...) nos jogos recém-importados,
        /// usando as fontes de metadados instaladas no Playnite. Respeita a opção da tela de configurações
        /// e nunca sobrescreve campo que o jogo já tenha preenchido.
        /// </summary>
        public void BaixarMetadadosDosImportados(List<Guid> gameIds, bool forcar = false)
        {
            if (!forcar && !settings.Settings.BaixarMetadadosAposImportar) return;
            if (gameIds == null || gameIds.Count == 0) return;

            var downloader = new MetadataDownloader(PlayniteApi);
            if (!downloader.HasProviders)
            {
                logger.Warn("Nenhuma fonte de metadados instalada no Playnite; download pós-importação ignorado.");
                PlayniteApi.Dialogs.ShowMessage(
                    "Os jogos foram importados, mas nenhuma fonte de metadados está instalada no Playnite (ex: IGDB).\n\n" +
                    "Instale uma fonte em Add-ons para que a capa e os metadados sejam baixados automaticamente.",
                    "Metadados");
                return;
            }

            int atualizados = RodarDownloadMetadados(downloader, gameIds, false);
            logger.Info(string.Format("Metadados preenchidos em {0} de {1} jogo(s).", atualizados, gameIds.Count));

            // Passada 1 é auto-match silencioso: o provedor devolve vazio quando não tem certeza do
            // título. Quem sobrou sem capa só resolve no modo manual, o mesmo da janela de edição —
            // e esse pode abrir uma janela por jogo, então é escolha do usuário, nunca automático.
            var semCapa = gameIds
                .Select(id => PlayniteApi.Database.Games.Get(id))
                .Where(g => g != null && string.IsNullOrEmpty(g.CoverImage))
                .Select(g => g.Id)
                .ToList();

            if (semCapa.Count == 0) return;

            var pergunta = string.Format(
                "{0} jogo(s) continuaram sem capa: as fontes não encontraram correspondência automática para o título.\n\n" +
                "Quer tentar de novo no modo manual? É o mesmo modo do \"Download metadata\" do Playnite, em que a fonte pode abrir uma janela para você escolher o jogo certo.",
                semCapa.Count);

            if (PlayniteApi.Dialogs.ShowMessage(pergunta, "Metadados", System.Windows.MessageBoxButton.YesNo)
                != System.Windows.MessageBoxResult.Yes) return;

            int manuais = RodarDownloadMetadados(downloader, semCapa, true);
            logger.Info(string.Format("Metadados manuais preenchidos em {0} de {1} jogo(s).", manuais, semCapa.Count));
        }

        private int RodarDownloadMetadados(MetadataDownloader downloader, List<Guid> gameIds, bool interativo)
        {
            int atualizados = 0;

            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                progressArgs.ProgressMaxValue = gameIds.Count;

                // Um refresh de biblioteca só no fim; o usuário acompanha pela barra de progresso.
                using (PlayniteApi.Database.BufferedUpdate())
                {
                    foreach (var id in gameIds)
                    {
                        if (progressArgs.CancelToken.IsCancellationRequested) break;

                        var jogo = PlayniteApi.Database.Games.Get(id);
                        progressArgs.Text = jogo != null
                            ? string.Format("Baixando metadados: {0}", jogo.Name)
                            : "Baixando metadados...";

                        try
                        {
                            if (downloader.Download(id, progressArgs.CancelToken, interativo)) atualizados++;
                        }
                        catch (Exception ex)
                        {
                            logger.Error(ex, string.Format("Falha ao baixar metadados do jogo {0}.", id));
                        }

                        progressArgs.CurrentProgressValue++;
                    }
                }
            }, new GlobalProgressOptions(
                interativo ? "Buscando metadados (modo manual)..." : "Baixando metadados dos jogos...", true)
            { IsIndeterminate = false });

            return atualizados;
        }

        /// <summary>
        /// Abre uma pasta no Explorador, ou explica por que não deu.
        ///
        /// O Process.Start fica aqui e a decisão fica em LocalGameUtils, que é testável. Pasta
        /// inacessível vira aviso com o motivo, e não uma janela de erro do Windows: o motivo
        /// (HD desligado, letra trocada) é a informação que a pessoa precisa, e o Explorador não
        /// tem como dá-la.
        /// </summary>
        public void AbrirPastaNoExplorador(string caminho)
        {
            string erro;
            var alvo = LocalGameUtils.CaminhoParaAbrirNoExplorador(caminho, out erro);
            if (alvo == null)
            {
                PlayniteApi.Dialogs.ShowMessage(erro, "Busca de Jogos Locais");
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(alvo) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                logger.Warn(ex, "Não consegui abrir a pasta no Explorador.");
                PlayniteApi.Dialogs.ShowMessage(
                    string.Format("Não consegui abrir \"{0}\": {1}", alvo, ex.Message),
                    "Busca de Jogos Locais");
            }
        }

        public override ISettings GetSettings(bool firstRun) { return settings; }
        public override UserControl GetSettingsView(bool firstRun) { return new BuscaDeJogosLocaisSettingsView(); }

        /// <summary>
        /// A entrada na barra lateral do modo Desktop, que abre a MESMA tela de sempre.
        ///
        /// Até aqui tudo o que a extensão faz morava em Complementos › Configuração, que é onde
        /// ninguém procura uma ferramenta que se usa toda semana. A tela é a mesma e o view model
        /// é o mesmo objeto — abrir uma segunda instância do view model faria as duas telas
        /// discordarem sobre a lista de pastas, e a última a gravar apagaria a outra.
        /// </summary>
        public override IEnumerable<SidebarItem> GetSidebarItems()
        {
            yield return new SidebarItem
            {
                Title = "Jogos locais",
                Type = SiderbarItemType.View,
                // O mesmo desenho do ícone da biblioteca, aqui em vetor para acompanhar a cor e o
                // tamanho do tema. Ver Ui/IconArt.
                Icon = Ui.IconArt.Build(CorDoTema("TextBrush"), CorDoTema("GlyphBrush"), 20),
                Opened = () => new BuscaDeJogosLocaisSettingsView(settings)
            };
        }

        /// <summary>
        /// Um pincel do tema em uso, com reserva.
        ///
        /// Sem a reserva o Fill fica nulo e o ícone SOME sem erro nenhum: a barra lateral mostra um
        /// espaço em branco clicável, e não há nada no log para investigar. Branco é feio num tema
        /// claro, mas visível — e visível ganha de invisível.
        /// </summary>
        private System.Windows.Media.Brush CorDoTema(string chave)
        {
            try
            {
                var pincel = PlayniteApi.Resources.GetResource(chave) as System.Windows.Media.Brush;
                if (pincel != null) return pincel;
            }
            catch (Exception) { }

            return System.Windows.Media.Brushes.White;
        }
        public override IEnumerable<GameMetadata> GetGames(LibraryGetGamesArgs args) { return new List<GameMetadata>(); }

        public override IEnumerable<PlayController> GetPlayActions(GetPlayActionsArgs args)
        {
            if (args.Game.PluginId != Id) yield break;

            var game = args.Game;
            if (game.GameActions != null && game.GameActions.Count > 0)
            {
                var action = game.GameActions[0];
                if (action.Type == GameActionType.File)
                {
                    yield return new AutomaticPlayController(game)
                    {
                        Path = action.Path,
                        Arguments = action.Arguments,
                        WorkingDir = action.WorkingDir,
                        Name = action.Name,
                        TrackingMode = TrackingMode.Default
                    };
                }
            }
        }

        public const string NomeFonteLocal = "Local";

        // Marca o jogo com a Fonte (Source) "Local" para exibição e filtros nativos do Playnite.
        public bool ApplyLocalSource(Game game)
        {
            try
            {
                var fonte = PlayniteApi.Database.Sources.Add(NomeFonteLocal);
                if (fonte != null && game.SourceId != fonte.Id)
                {
                    game.SourceId = fonte.Id;
                    return true;
                }
            }
            catch (Exception) { }
            return false;
        }

        // Marca um jogo como desinstalado (mantém na biblioteca, igual ao comportamento da Steam).
        public bool MarcarComoDesinstalado(Guid gameId)
        {
            var game = PlayniteApi.Database.Games.Get(gameId);
            if (game == null) return false;
            if (!game.IsInstalled) return false;
            game.IsInstalled = false;
            PlayniteApi.Database.Games.Update(game);

            try
            {
                if (settings.Settings.HistoricoDesinstalacoes == null)
                    settings.Settings.HistoricoDesinstalacoes = new ObservableCollection<UninstallLogEntry>();

                settings.Settings.HistoricoDesinstalacoes.Add(new UninstallLogEntry
                {
                    GameId = game.Id,
                    DataDesinstalacao = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                    NomeJogo = game.Name,
                    Origem = game.InstallDirectory
                });
                SavePluginSettings(settings.Settings);
            }
            catch (Exception) { }

            return true;
        }

        // Tira o jogo da biblioteca do Playnite. Não toca no disco: é para o jogo que a pessoa
        // já apagou por conta própria e cuja entrada ficou órfã.
        public bool RemoverDaBiblioteca(Guid gameId)
        {
            try
            {
                var game = PlayniteApi.Database.Games.Get(gameId);
                if (game == null) return false;
                PlayniteApi.Database.Games.Remove(gameId);
                return true;
            }
            catch (Exception ex)
            {
                logger.Error(ex, string.Format("Falha ao remover jogo da biblioteca: {0}", gameId));
                return false;
            }
        }

        /// <summary>
        /// Explica por que uma subpasta de pasta monitorada está fora da biblioteca, e diz o
        /// que dá para fazer. A tabela de pastas dizia só "Não importado", e isso escondia
        /// quatro situações diferentes — a mais traiçoeira é o jogo JÁ estar na biblioteca,
        /// só que adicionado à mão ou por outra fonte: a busca o reconhecia como "já existe"
        /// e não importava, e a tabela, que só conta jogo desta extensão, dizia que faltava.
        /// </summary>
        public DiagnosticoPasta DiagnosticarPastaNaoImportada(string sub)
        {
            var d = new DiagnosticoPasta { Acao = string.Empty };
            if (string.IsNullOrEmpty(sub)) { d.Motivo = "Caminho vazio."; return d; }

            // 1) Já existe jogo apontando para esta pasta (ou para dentro dela), de qualquer fonte?
            Game existente = null;
            try
            {
                existente = PlayniteApi.Database.Games.FirstOrDefault(g =>
                    !string.IsNullOrEmpty(g.InstallDirectory) &&
                    (LocalGameUtils.EhMesmaPasta(g.InstallDirectory, sub) || LocalGameUtils.IsUnderFolder(g.InstallDirectory, sub)));
            }
            catch (Exception ex) { logger.Error(ex, "Falha ao procurar jogo existente para " + sub); }

            if (existente != null)
            {
                bool subpasta = !LocalGameUtils.EhMesmaPasta(existente.InstallDirectory, sub);
                string onde = subpasta ? string.Format(" (aponta para a subpasta {0})", existente.InstallDirectory) : string.Empty;
                d.JogoRelacionadoId = existente.Id;

                if (existente.PluginId == Id)
                {
                    d.Motivo = string.Format("Já é «{0}» nesta extensão{1}. A tabela não casou porque a pasta do jogo não é exatamente esta.", existente.Name, onde);
                }
                else if (existente.PluginId == Guid.Empty)
                {
                    d.Motivo = string.Format("Já está na biblioteca como «{0}», adicionado à mão no Playnite (fora desta extensão){1}. A busca vê a pasta como \"já existe\" e não importa; a tabela só conta jogo desta extensão.", existente.Name, onde);
                    d.Acao = "adotar";
                }
                else
                {
                    d.Motivo = string.Format("Já está na biblioteca como «{0}» pela fonte «{1}»{2}. Esta extensão não mexe em jogo de outra biblioteca.", existente.Name, NomeDoPlugin(existente.PluginId, existente.SourceId), onde);
                }
                logger.Info(string.Format("[Diagnóstico] {0}: {1}", sub, d.Motivo));
                return d;
            }

            // 2) Tem executável?
            var exes = new List<string>();
            int arquivosVistos = 0;
            try
            {
                foreach (var f in EnumerarLimitado(sub, 4, 4000))
                {
                    arquivosVistos++;
                    if (!f.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) continue;
                    if (IsExplosiveFile(f)) continue;
                    if (IsExcluded(f)) continue;
                    exes.Add(f);
                }
            }
            catch (Exception ex) { logger.Error(ex, "Falha ao ler " + sub); }

            if (exes.Count == 0)
            {
                d.Motivo = arquivosVistos == 0
                    ? "Pasta vazia (ou sem permissão de leitura). Nada para importar."
                    : string.Format("Nenhum .exe encontrado ({0} arquivo(s) lidos, até 4 níveis). A busca não tem como importar; se o jogo abre por outro tipo de arquivo, adicione à mão no Playnite.", arquivosVistos);
                logger.Info(string.Format("[Diagnóstico] {0}: {1}", sub, d.Motivo));
                return d;
            }

            string nomeLimpo = LocalGameUtils.CleanGameNameOnly(new DirectoryInfo(sub).Name);
            string melhor = LocalGameUtils.MelhorExeDaPasta(nomeLimpo, null, null, sub, exes) ?? exes[0];
            d.Exe = melhor;
            string exeRel = melhor.Length > sub.Length ? melhor.Substring(sub.Length).TrimStart('\\') : Path.GetFileName(melhor);

            // 3) Existe jogo com este nome cuja pasta sumiu? Então provavelmente é ele, que mudou de lugar.
            try
            {
                string slug = LocalGameUtils.SlugForMatch(nomeLimpo);
                var perdido = PlayniteApi.Database.Games.FirstOrDefault(g =>
                    (g.PluginId == Id || g.PluginId == Guid.Empty) &&
                    !string.IsNullOrEmpty(g.InstallDirectory) &&
                    !Directory.Exists(g.InstallDirectory) &&
                    slug.Length >= 3 && LocalGameUtils.SlugForMatch(g.Name) == slug);
                if (perdido != null)
                {
                    d.JogoRelacionadoId = perdido.Id;
                    d.Acao = "reapontar";
                    d.Motivo = string.Format("Parece ser «{0}», que na biblioteca aponta para {1} — pasta que não existe mais. Provavelmente mudou de lugar; reapontar mantém tempo de jogo e capa. Executável: {2}.", perdido.Name, perdido.InstallDirectory, exeRel);
                    logger.Info(string.Format("[Diagnóstico] {0}: {1}", sub, d.Motivo));
                    return d;
                }
            }
            catch (Exception ex) { logger.Error(ex, "Falha ao procurar jogo perdido para " + sub); }

            d.Acao = "importar";
            d.Motivo = exes.Count == 1
                ? string.Format("Executável encontrado ({0}), mas a pasta nunca foi importada. Importe daqui ou pela aba Busca.", exeRel)
                : string.Format("{0} executáveis encontrados; o mais provável é {1}. A pasta nunca foi importada — importe daqui ou pela aba Busca.", exes.Count, exeRel);
            logger.Info(string.Format("[Diagnóstico] {0}: {1}", sub, d.Motivo));
            return d;
        }

        // Nome legível de quem é dono do jogo: o plugin instalado, ou a fonte, ou o id cru.
        private string NomeDoPlugin(Guid pluginId, Guid sourceId)
        {
            try
            {
                var p = PlayniteApi.Addons.Plugins.FirstOrDefault(x => x.Id == pluginId);
                var lib = p as LibraryPlugin;
                if (lib != null && !string.IsNullOrEmpty(lib.Name)) return lib.Name;
            }
            catch (Exception) { }
            string fonte = NomeDaFonte(sourceId);
            return string.IsNullOrEmpty(fonte) ? pluginId.ToString() : fonte;
        }

        // Leitura com teto: o diagnóstico roda ao abrir a janela da pasta, e uma subpasta com
        // dezenas de milhares de arquivos não pode travar a tela.
        private IEnumerable<string> EnumerarLimitado(string raiz, int profundidadeMax, int maxArquivos)
        {
            var fila = new Queue<KeyValuePair<string, int>>();
            fila.Enqueue(new KeyValuePair<string, int>(raiz, 0));
            int contados = 0;
            while (fila.Count > 0)
            {
                var atual = fila.Dequeue();
                string[] arquivos;
                try { arquivos = Directory.GetFiles(atual.Key); }
                catch (Exception) { continue; }
                foreach (var a in arquivos)
                {
                    if (contados++ >= maxArquivos) yield break;
                    yield return a;
                }
                if (atual.Value >= profundidadeMax) continue;
                string[] dirs;
                try { dirs = Directory.GetDirectories(atual.Key); }
                catch (Exception) { continue; }
                foreach (var dir in dirs) fila.Enqueue(new KeyValuePair<string, int>(dir, atual.Value + 1));
            }
        }

        /// <summary>
        /// Traz para esta extensão um jogo que foi adicionado à mão no Playnite (PluginId vazio)
        /// e aponta para uma pasta monitorada. Sem isso ele ficava num limbo: a busca dizia que
        /// já existia e a tabela de pastas dizia que faltava.
        /// </summary>
        public bool AdotarJogo(Guid gameId, string pasta)
        {
            var game = PlayniteApi.Database.Games.Get(gameId);
            if (game == null) return false;
            if (game.PluginId != Guid.Empty && game.PluginId != Id)
            {
                logger.Warn(string.Format("[Adotar] Recusado: «{0}» pertence ao plugin {1}.", game.Name, game.PluginId));
                return false;
            }
            game.PluginId = Id;
            if (!string.IsNullOrEmpty(pasta) && string.IsNullOrEmpty(game.InstallDirectory)) game.InstallDirectory = pasta;
            game.IsInstalled = !string.IsNullOrEmpty(game.InstallDirectory) && Directory.Exists(game.InstallDirectory);
            ApplyLocalSource(game);
            PlayniteApi.Database.Games.Update(game);
            logger.Info(string.Format("[Adotar] «{0}» agora é desta extensão ({1}).", game.Name, game.InstallDirectory));
            return true;
        }

        /// <summary>
        /// O caminho está dentro de uma pasta monitorada marcada como disco removível cuja
        /// raiz não está presente agora? Nesse caso o jogo não sumiu: o HD é que está fora.
        /// Barato: só consulta as raízes removíveis, nunca o caminho do jogo.
        /// </summary>
        public bool DiscoRemovivelDesconectado(string caminho)
        {
            if (string.IsNullOrEmpty(caminho)) return false;
            var removiveis = settings.Settings.PastasRemoviveis;
            if (removiveis == null || removiveis.Count == 0) return false;
            foreach (var raiz in removiveis)
            {
                if (string.IsNullOrEmpty(raiz)) continue;
                if (!LocalGameUtils.IsUnderFolder(caminho, raiz) && !LocalGameUtils.EhMesmaPasta(caminho, raiz)) continue;
                bool existe;
                try { existe = Directory.Exists(raiz); } catch (Exception) { existe = false; }
                if (!existe) return true;
            }
            return false;
        }

        /// <summary>O ícone de um emulador já lido (para restaurar a aba do cache).</summary>
        public object IconeDoEmulador(string executavel, bool existe)
        {
            if (!existe || string.IsNullOrEmpty(executavel) || executavel == "—") return null;
            return IconeDoExecutavel(executavel);
        }

        public List<string> GetSavePatterns()
        {
            if (settings != null && settings.Settings != null && settings.Settings.PadroesSave != null && settings.Settings.PadroesSave.Count > 0)
                return settings.Settings.PadroesSave.ToList();
            return LocalGameUtils.DefaultSavePatterns.ToList();
        }

        // Percorre a pasta do jogo (recursivamente) procurando por um "save" segundo os padrões configurados.
        // Se houver save em qualquer nível dentro da pasta, NÃO é seguro apagar a pasta.
        public bool HasSaveInRoot(string root)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root)) return false;

            var patterns = GetSavePatterns();
            var dirNames = new List<string>();
            var fileNames = new List<string>();

            var stack = new Stack<string>();
            stack.Push(root);
            while (stack.Count > 0)
            {
                string cur = stack.Pop();

                try
                {
                    foreach (var d in Directory.EnumerateDirectories(cur))
                    {
                        dirNames.Add(new DirectoryInfo(d).Name);
                        stack.Push(d);
                    }
                }
                catch (Exception) { }

                try
                {
                    foreach (var f in Directory.EnumerateFiles(cur))
                        fileNames.Add(Path.GetFileName(f));
                }
                catch (Exception) { }

                if (LocalGameUtils.MatchesSavePattern(dirNames, fileNames, patterns))
                    return true;
            }

            return false;
        }

        // Envia a pasta do jogo duplicado para a Lixeira, remove o jogo da biblioteca e registra no histórico.
        public bool RemoverDuplicata(DuplicateGameItem item)
        {
            if (item == null) return false;

            string destino = "Lixeira";
            bool pastaOk = true;

            try
            {
                if (!string.IsNullOrEmpty(item.InstallDir) && Directory.Exists(item.InstallDir))
                {
                    Microsoft.VisualBasic.FileIO.FileSystem.DeleteDirectory(
                        item.InstallDir,
                        Microsoft.VisualBasic.FileIO.UIOption.OnlyErrorDialogs,
                        Microsoft.VisualBasic.FileIO.RecycleOption.SendToRecycleBin);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, string.Format("Falha ao enviar pasta para a Lixeira: {0}", item.InstallDir));
                destino = "Falha";
                pastaOk = false;
            }

            if (!pastaOk) return false;

            bool removidoDb = false;
            try
            {
                var game = PlayniteApi.Database.Games.Get(item.GameId);
                if (game != null)
                {
                    PlayniteApi.Database.Games.Remove(item.GameId);
                    removidoDb = true;
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, string.Format("Falha ao remover jogo duplicado da biblioteca: {0}", item.Nome));
            }

            try
            {
                if (settings.Settings.HistoricoDuplicatasRemovidas == null)
                    settings.Settings.HistoricoDuplicatasRemovidas = new ObservableCollection<DuplicateRemovalLogEntry>();

                settings.Settings.HistoricoDuplicatasRemovidas.Add(new DuplicateRemovalLogEntry
                {
                    DataRemocao = DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"),
                    NomeJogo = item.Nome,
                    PastaRemovida = item.InstallDir,
                    TinhaSave = item.TemSaveNaRaiz ? "Sim" : "Não",
                    Destino = destino,
                    RemovidoDaBiblioteca = removidoDb
                });
            }
            catch (Exception) { }

            return true;
        }

        public bool ApplyDriveTag(Game game)
        {
            if (string.IsNullOrEmpty(game.InstallDirectory)) return false;
            try
            {
                string drive = Path.GetPathRoot(game.InstallDirectory);
                if (!string.IsNullOrEmpty(drive))
                {
                    string featureName = string.Format("HD {0}", drive.TrimEnd('\\'));
                    var feature = PlayniteApi.Database.Features.Add(featureName);
                    if (game.FeatureIds == null) game.FeatureIds = new List<Guid>();
                    if (!game.FeatureIds.Contains(feature.Id))
                    {
                        game.FeatureIds.Add(feature.Id);
                        return true;
                    }
                }
            } catch (Exception) { }
            return false;
        }

        public override IEnumerable<MainMenuItem> GetMainMenuItems(GetMainMenuItemsArgs args)
        {
            yield return new MainMenuItem
            {
                Description = "Buscar Novos Jogos",
                MenuSection = "@Local",
                Action = (mainMenuItemArgs) => AbrirScanRapido()
            };

            yield return new MainMenuItem
            {
                Description = "Limpar Nomes dos Jogos Locais",
                MenuSection = "@Local",
                Action = (mainMenuItemArgs) => LimparNomesDosJogosLocais()
            };

            yield return new MainMenuItem
            {
                Description = "Completar Biblioteca (consoles de emulação)",
                MenuSection = "@Local",
                Action = (mainMenuItemArgs) => CompletarBibliotecaDeEmulacao()
            };

            yield return new MainMenuItem
            {
                Description = "Verificar Integridade da Biblioteca",
                MenuSection = "@Local",
                Action = (mainMenuItemArgs) => CheckLibraryIntegrity()
            };
        }

        /// <summary>
        /// Reaplica a limpeza de nome (e a versão detectada) nos jogos que já foram importados
        /// por esta extensão. Só mexe em jogo cujo nome realmente muda; mostra prévia antes de gravar.
        /// </summary>
        public void LimparNomesDosJogosLocais()
        {
            var jogos = PlayniteApi.Database.Games.Where(g => g.PluginId == Id).ToList();
            var itens = new ObservableCollection<RenameItem>();

            foreach (var jogo in jogos)
            {
                // A versão sai do nome da pasta quando existe; o nome do jogo é o fallback.
                string baseNome = jogo.Name;
                string origem = !string.IsNullOrEmpty(jogo.InstallDirectory) && Directory.Exists(jogo.InstallDirectory)
                    ? new DirectoryInfo(jogo.InstallDirectory).Name
                    : jogo.Name;

                string versaoPasta;
                LocalGameUtils.CleanGameName(origem, out versaoPasta);

                string versaoNome;
                string limpo = LocalGameUtils.CleanGameName(baseNome, out versaoNome);
                string versao = versaoNome != null ? versaoNome : versaoPasta;

                bool mudaNome = !string.IsNullOrEmpty(limpo) && limpo != jogo.Name;
                bool ganhaVersao = !string.IsNullOrEmpty(versao) && string.IsNullOrEmpty(jogo.Version);

                if (!mudaNome && !ganhaVersao) continue;

                itens.Add(new RenameItem
                {
                    GameId = jogo.Id,
                    NomeAtual = jogo.Name,
                    NomeNovo = string.IsNullOrEmpty(limpo) ? jogo.Name : limpo,
                    Versao = string.IsNullOrEmpty(versao) ? (jogo.Version == null ? string.Empty : jogo.Version) : versao,
                    Selecionado = true
                });
            }

            if (itens.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Nenhum jogo local precisa de ajuste de nome.", "Limpar Nomes");
                return;
            }

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = true,
                ShowMinimizeButton = false
            });
            window.Title = string.Format("Limpar Nomes ({0})", itens.Count);
            window.Width = 950;
            window.Height = 600;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            // A renomeação grava na hora; a busca de metadados só roda DEPOIS que a modal fecha,
            // senão a barra de progresso e os diálogos da fonte nascem atrás da janela.
            var renomeados = new List<Guid>();
            window.Content = new RenamePreviewWindow(itens, (selecionados) => renomeados.AddRange(AplicarRenomeacoes(selecionados)));
            window.ShowDialog();

            if (renomeados.Count == 0) return;

            var resposta = PlayniteApi.Dialogs.ShowMessage(
                string.Format("{0} jogo(s) atualizados.\n\nQuer buscar capa e metadados agora para esses jogos, com o nome já corrigido?", renomeados.Count),
                "Limpar Nomes", System.Windows.MessageBoxButton.YesNo);

            if (resposta == System.Windows.MessageBoxResult.Yes)
            {
                BaixarMetadadosDosImportados(renomeados, true);
            }
        }

        // Grava as renomeações confirmadas na prévia. Só toca no que o usuário deixou marcado.
        // Devolve os ids efetivamente alterados, para a busca de metadados rodar depois.
        private List<Guid> AplicarRenomeacoes(List<RenameItem> selecionados)
        {
            var alterados = new List<Guid>();

            // Alteração em lote entra em modo bufferizado: sem isso, cada Update dispara os eventos
            // de notificação do banco e a interface se redesenha jogo a jogo — imperceptível em
            // biblioteca pequena, travamento em biblioteca grande.
            using (PlayniteApi.Database.BufferedUpdate())
            {
                foreach (var item in selecionados)
                {
                    var jogo = PlayniteApi.Database.Games.Get(item.GameId);
                    if (jogo == null) continue;

                    try
                    {
                        bool mudou = false;

                        if (!string.IsNullOrEmpty(item.NomeNovo) && item.NomeNovo != jogo.Name)
                        {
                            jogo.Name = item.NomeNovo.Trim();
                            mudou = true;
                        }

                        if (!string.IsNullOrEmpty(item.Versao) && item.Versao != jogo.Version)
                        {
                            jogo.Version = item.Versao.Trim();
                            mudou = true;
                        }

                        if (mudou)
                        {
                            PlayniteApi.Database.Games.Update(jogo);
                            alterados.Add(jogo.Id);
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, string.Format("Falha ao renomear o jogo {0}", item.NomeAtual));
                    }
                }
            }

            return alterados;
        }

        // ===================== BIBLIOTECA POR CONSOLE (jogos de emulação) =====================
        //
        // O tema (Aniki ReMake, e o Playnite nativo) agrupa a "biblioteca" pela Fonte do jogo:
        // Steam, Epic, Local. Jogo de emulação entra sem Fonte nenhuma e cai todo no mesmo balde.
        // Aqui a Fonte passa a ser o CONSOLE (PlayStation 2, Nintendo Switch...), lido do emulador
        // que o próprio jogo referencia — não de palpite sobre o nome da pasta.

        /// <summary>
        /// Monta a prévia de "Completar Biblioteca": uma linha por jogo que tem ação de emulador,
        /// com o console que o emulador declara. Não grava nada.
        /// </summary>
        public List<ConsoleLibraryItem> MapearBibliotecaDeEmulacao()
        {
            var itens = new List<ConsoleLibraryItem>();
            var emuladores = PlayniteApi.Database.Emulators.ToList();
            var nomesDeConsole = NomesDeConsoleConhecidos();

            foreach (var game in PlayniteApi.Database.Games.ToList())
            {
                if (game.GameActions == null) continue;

                GameAction acao = null;
                foreach (var a in game.GameActions)
                {
                    if (a.Type == GameActionType.Emulator) { acao = a; break; }
                }
                if (acao == null) continue;

                Emulator emulador = null;
                foreach (var e in emuladores)
                {
                    if (e.Id == acao.EmulatorId) { emulador = e; break; }
                }

                string nomePerfil;
                var plataformasPerfil = ResolverPlataformasDoPerfil(emulador, acao.EmulatorProfileId, out nomePerfil);
                string plataformaJogo = PrimeiraPlataformaDoJogo(game);
                string sugestao = LocalGameUtils.EscolherNomeDeConsole(plataformasPerfil, plataformaJogo);

                string origem = "—";
                if (plataformasPerfil.Count > 0 && !string.IsNullOrEmpty(sugestao)) origem = "Emulador";
                else if (!string.IsNullOrEmpty(sugestao)) origem = "Plataforma do jogo";

                string atual = NomeDaFonte(game.SourceId);

                bool jaCerta = !string.IsNullOrEmpty(sugestao) && string.Equals(atual, sugestao, StringComparison.OrdinalIgnoreCase);
                // Fonte de loja (Steam, Epic) não é sobrescrita sem o usuário mandar: o jogo pode ser
                // uma compra de loja rodando em emulador, e a fonte ali é informação de verdade.
                bool fonteAlheia = !string.IsNullOrEmpty(atual) && !nomesDeConsole.Contains(atual);

                string situacao;
                if (string.IsNullOrEmpty(sugestao)) situacao = "Sem console identificado";
                else if (jaCerta) situacao = "Já está correta";
                else if (string.IsNullOrEmpty(atual)) situacao = "Sem biblioteca";
                else if (fonteAlheia) situacao = string.Format("Já tem a fonte \"{0}\"", atual);
                else situacao = string.Format("Muda de \"{0}\"", atual);

                itens.Add(new ConsoleLibraryItem
                {
                    GameId = game.Id,
                    NomeJogo = game.Name,
                    Emulador = emulador != null ? emulador.Name : "(emulador não encontrado)",
                    Perfil = string.IsNullOrEmpty(nomePerfil) ? "—" : nomePerfil,
                    PlataformaDoJogo = string.IsNullOrEmpty(plataformaJogo) ? "—" : plataformaJogo,
                    BibliotecaAtual = string.IsNullOrEmpty(atual) ? "—" : atual,
                    BibliotecaNova = sugestao,
                    Origem = origem,
                    Situacao = situacao,
                    Selecionado = !string.IsNullOrEmpty(sugestao) && !jaCerta && !fonteAlheia
                });
            }

            return itens.OrderBy(i => i.BibliotecaNova).ThenBy(i => i.NomeJogo).ToList();
        }

        // ------------------------------------------------------------------ emuladores

        /// <summary>
        /// Uma linha por emulador configurado, com versão, ícone e o que há nas pastas que ele
        /// varre — quanto já virou biblioteca e quanto ficou de fora.
        ///
        /// As pastas saem de <c>Database.GameScanners</c>, e não do InstallDir do emulador: o que
        /// o Playnite varre é o que está configurado em Bibliotecas › Emulação › pastas de
        /// varredura, e é contra ISSO que "está na biblioteca?" faz sentido. A pasta onde o
        /// emulador está instalado quase nunca é a pasta das ROMs, e usá-la produziria uma tela
        /// que diz "0 jogos" para quem tem mil.
        ///
        /// Emulador sem pasta de varredura entra na lista mesmo assim, com a situação dizendo
        /// isso. Sumir da tela faria parecer que o Playnite não conhece o emulador, que é outro
        /// problema e leva a pessoa a reconfigurar o que já estava certo.
        /// </summary>
        public List<EmuladorResumo> MapearEmuladores()
        {
            var resumos = new List<EmuladorResumo>();

            List<Emulator> emuladores;
            List<GameScannerConfig> varreduras;
            try
            {
                emuladores = PlayniteApi.Database.Emulators.ToList();
                varreduras = PlayniteApi.Database.GameScanners.ToList();
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Não consegui ler os emuladores configurados.");
                return resumos;
            }

            var mapeadas = RomsJaNaBiblioteca();

            foreach (var emulador in emuladores)
            {
                var resumo = new EmuladorResumo();
                resumo.EmuladorId = emulador.Id;
                resumo.Nome = string.IsNullOrWhiteSpace(emulador.Name) ? "(sem nome)" : emulador.Name;
                resumo.InstallDir = emulador.InstallDir;
                resumo.Itens = new List<EmuladorJogoItem>();

                var executavel = ExecutavelDoEmulador(emulador);
                resumo.Executavel = string.IsNullOrEmpty(executavel) ? "—" : executavel;
                resumo.ExecutavelExiste = !string.IsNullOrEmpty(executavel) && File.Exists(executavel);
                resumo.Versao = resumo.ExecutavelExiste ? VersaoDoExecutavel(executavel) : "—";
                resumo.Icone = resumo.ExecutavelExiste ? IconeDoExecutavel(executavel) : null;
                resumo.Plataformas = PlataformasDoEmulador(emulador);

                var pastas = new List<string>();
                foreach (var varredura in varreduras)
                {
                    if (varredura.EmulatorId != emulador.Id) continue;
                    if (string.IsNullOrWhiteSpace(varredura.Directory)) continue;

                    pastas.Add(varredura.Directory);
                    LerPastaDeVarredura(emulador, varredura, mapeadas, resumo);
                }

                resumo.TotalPastas = pastas.Count;
                resumo.Pastas = pastas.Count == 0 ? "—" : string.Join(" · ", pastas.ToArray());
                resumo.PrimeiraPasta = pastas.Count == 0 ? null : pastas[0];
                resumo.TotalArquivos = resumo.Itens.Count;
                resumo.NaBiblioteca = resumo.Itens.Count(i => i.GameId != Guid.Empty);
                resumo.ForaDaBiblioteca = resumo.TotalArquivos - resumo.NaBiblioteca;
                resumo.Itens = resumo.Itens.OrderBy(i => i.Status).ThenBy(i => i.Nome).ToList();

                resumos.Add(resumo);
            }

            return resumos.OrderBy(r => r.Nome).ToList();
        }

        /// <summary>
        /// Todo caminho de ROM que a biblioteca já conhece, normalizado, apontando para o jogo.
        ///
        /// O caminho guardado pode ser RELATIVO quando a varredura foi importada com
        /// "caminhos relativos" ligado — ele chega como "{InstallDir}\jogo.iso". Comparar essa
        /// string com o caminho lido do disco nunca casa, e o efeito seria a tela dizer que a
        /// biblioteca inteira está fora dela. Por isso tudo passa por ExpandGameVariables antes.
        /// </summary>
        private Dictionary<string, Game> RomsJaNaBiblioteca()
        {
            var mapa = new Dictionary<string, Game>(StringComparer.OrdinalIgnoreCase);

            foreach (var game in PlayniteApi.Database.Games.ToList())
            {
                if (game.Roms == null || game.Roms.Count == 0) continue;

                foreach (var rom in game.Roms)
                {
                    if (rom == null || string.IsNullOrWhiteSpace(rom.Path)) continue;

                    string caminho;
                    try
                    {
                        caminho = PlayniteApi.ExpandGameVariables(game, rom.Path);
                    }
                    catch (Exception)
                    {
                        caminho = rom.Path;
                    }

                    var chave = LocalGameUtils.NormalizePath(caminho);
                    if (string.IsNullOrEmpty(chave)) continue;
                    if (!mapa.ContainsKey(chave)) mapa[chave] = game;
                }
            }

            return mapa;
        }

        /// <summary>Lê uma pasta de varredura e joga o que achou dentro do resumo do emulador.</summary>
        private void LerPastaDeVarredura(Emulator emulador, GameScannerConfig varredura,
                                         Dictionary<string, Game> mapeadas, EmuladorResumo resumo)
        {
            if (!Directory.Exists(varredura.Directory)) return;

            var extensoes = ExtensoesDoPerfil(emulador, varredura.EmulatorProfileId);
            var plataforma = PlataformaDaVarredura(varredura);

            List<string> nomesDeBoot = null;
            if (extensoes.Count == 0 && PerfilImportaPorScript(emulador, varredura.EmulatorProfileId))
            {
                nomesDeBoot = NomesDeRomDaBiblioteca(emulador, mapeadas);
                if (nomesDeBoot.Count == 0) nomesDeBoot.AddRange(LocalGameUtils.NomesDeBootConhecidos);
                resumo.Observacao = string.Format(
                    "Este emulador importa por script (o jogo é uma pasta, não um arquivo). Contei como jogo só os arquivos chamados {0}; o resto da pasta é conteúdo do jogo, não jogo.",
                    string.Join(", ", nomesDeBoot.ToArray()));
                logger.Info(string.Format("[Emulador] {0}: perfil por script; nomes de boot usados: {1}.", emulador.Name, string.Join(", ", nomesDeBoot.ToArray())));
            }

            IEnumerable<string> arquivos;
            try
            {
                arquivos = Directory.EnumerateFiles(
                    varredura.Directory, "*",
                    varredura.ScanSubfolders ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly);
            }
            catch (Exception ex)
            {
                // Pasta sem permissão, ou HD que desligou no meio: o emulador continua na lista
                // com o que já foi lido, em vez de a tela inteira morrer por causa de uma pasta.
                logger.Warn(ex, string.Format("Não consegui ler a pasta de varredura {0}.", varredura.Directory));
                return;
            }

            // Faixas de áudio referenciadas por .cue (e discos listados num .m3u) não são jogo:
            // o Playnite importa o .cue/.m3u, não o que está dentro dele.
            var lista = arquivos.ToList();
            var subordinados = LocalGameUtils.ArquivosSubordinados(lista, LerLinhasSeguro);
            if (subordinados.Count > 0)
                logger.Info(string.Format("[Emulador] {0}: {1} arquivo(s) em {2} são faixas/discos de .cue ou .m3u e não contam.", emulador.Name, subordinados.Count, varredura.Directory));

            foreach (var arquivo in lista)
            {
                if (subordinados.Contains(LocalGameUtils.NormalizePath(arquivo))) continue;

                if (nomesDeBoot != null)
                {
                    if (!LocalGameUtils.EhArquivoDeBoot(arquivo, nomesDeBoot)) continue;
                }
                else if (!LocalGameUtils.EhArquivoDeRom(arquivo, extensoes)) continue;

                var chave = LocalGameUtils.NormalizePath(arquivo);
                Game jogo = null;
                mapeadas.TryGetValue(chave, out jogo);

                var item = new EmuladorJogoItem();
                item.GameId = jogo != null ? jogo.Id : Guid.Empty;
                item.Nome = jogo != null ? jogo.Name : LocalGameUtils.NomeDeRomParaExibicao(arquivo);
                item.Arquivo = Path.GetFileName(arquivo);
                item.Caminho = arquivo;
                item.Pasta = varredura.Directory;
                item.Plataforma = plataforma;
                item.Status = jogo != null ? "Na biblioteca" : "Fora da biblioteca";

                resumo.Itens.Add(item);
            }
        }

        private static string[] LerLinhasSeguro(string caminho)
        {
            try { return File.ReadAllLines(caminho); }
            catch (Exception) { return null; }
        }

        /// <summary>
        /// O executável do emulador. Perfil personalizado guarda o caminho; perfil embutido só
        /// guarda o nome, e o executável mora na definição que o Playnite distribui.
        ///
        /// O caminho pode ser relativo ao InstallDir, e frequentemente é.
        /// </summary>
        private string ExecutavelDoEmulador(Emulator emulador)
        {
            if (emulador == null) return null;

            // Todos os candidatos, na ordem: perfis personalizados (caminho literal, pode trazer
            // {EmulatorDir}) e depois os perfis da definição embutida (expressão regular). A
            // versão anterior parava no PRIMEIRO perfil com executável; um perfil personalizado
            // apontando para um binário que não existe mais escondia o embutido que existia.
            var candidatos = new List<KeyValuePair<string, bool>>(); // (padrão, éRegex)

            if (emulador.CustomProfiles != null)
            {
                foreach (var perfil in emulador.CustomProfiles)
                {
                    if (perfil == null || string.IsNullOrWhiteSpace(perfil.Executable)) continue;
                    candidatos.Add(new KeyValuePair<string, bool>(perfil.Executable, false));
                }
            }

            if (emulador.BuiltinProfiles != null && !string.IsNullOrEmpty(emulador.BuiltInConfigId))
            {
                EmulatorDefinition definicao = null;
                foreach (var d in PlayniteApi.Emulation.Emulators)
                {
                    if (string.Equals(d.Id, emulador.BuiltInConfigId, StringComparison.OrdinalIgnoreCase)) { definicao = d; break; }
                }

                if (definicao == null)
                {
                    logger.Warn(string.Format("[Emulador] {0}: BuiltInConfigId \"{1}\" não existe nas definições do Playnite.", emulador.Name, emulador.BuiltInConfigId));
                }
                else if (definicao.Profiles != null)
                {
                    foreach (var perfil in definicao.Profiles)
                    {
                        if (perfil == null || string.IsNullOrWhiteSpace(perfil.StartupExecutable)) continue;
                        candidatos.Add(new KeyValuePair<string, bool>(perfil.StartupExecutable, true));
                    }
                }
            }

            if (candidatos.Count == 0)
            {
                logger.Warn(string.Format("[Emulador] {0}: nenhum perfil (personalizado ou embutido) declara executável. InstallDir={1}", emulador.Name, emulador.InstallDir));
                return null;
            }

            foreach (var candidato in candidatos)
            {
                var achado = ResolverExecutavel(emulador, candidato.Key, candidato.Value);
                if (!string.IsNullOrEmpty(achado)) return achado;
            }

            logger.Warn(string.Format("[Emulador] {0}: nenhum dos {1} padrão(ões) resolveu em InstallDir={2}: {3}",
                emulador.Name, candidatos.Count, emulador.InstallDir, string.Join(" | ", candidatos.Select(c => c.Key).ToArray())));
            return null;
        }

        private string ResolverExecutavel(Emulator emulador, string relativo, bool ehRegex)
        {
            if (string.IsNullOrWhiteSpace(relativo)) return null;

            // O StartupExecutable da definição embutida é uma EXPRESSÃO REGULAR, não um nome de
            // arquivo: todas as ~600 definições do Playnite vêm como "^retroarch\.exe$",
            // "^shadPS4.*\.exe$". A versão anterior tratava isso como curinga do sistema de
            // arquivos, o GetFiles estourava com o "^", e TODO emulador embutido saía como
            // "não encontrado", sem versão e sem ícone. Verificado em 11/09/2026 lendo
            // Emulation/Emulators/*/emulator.yaml.
            try
            {
                string installDir = emulador.InstallDir;

                // Perfil personalizado vem com as variáveis do Playnite ("{EmulatorDir}\\x.exe").
                // Sem expandir, o caminho nunca existe e o emulador sai como "não encontrado".
                if (!ehRegex)
                {
                    relativo = relativo
                        .Replace("{EmulatorDir}", installDir ?? string.Empty)
                        .Replace("{PlayniteDir}", PlayniteApi.Paths.ApplicationPath ?? string.Empty)
                        .Trim().Trim('"');
                    if (Path.IsPathRooted(relativo))
                    {
                        if (File.Exists(relativo)) return relativo;
                        logger.Warn(string.Format("[Emulador] {0}: perfil aponta para {1}, que não existe.", emulador.Name, relativo));
                        return null;
                    }
                }

                if (string.IsNullOrWhiteSpace(installDir) || !Directory.Exists(installDir))
                {
                    logger.Warn(string.Format("[Emulador] {0}: pasta de instalação vazia ou inexistente ({1}); padrão do executável: {2}.", emulador.Name, installDir, relativo));
                    return null;
                }

                if (ehRegex)
                {
                    var re = new System.Text.RegularExpressions.Regex(relativo, System.Text.RegularExpressions.RegexOptions.IgnoreCase);
                    foreach (var arquivo in EnumerarLimitado(installDir, 3, 20000))
                    {
                        if (re.IsMatch(Path.GetFileName(arquivo))) return arquivo;
                    }
                    logger.Warn(string.Format("[Emulador] {0}: nenhum arquivo em {1} (3 níveis) casa com /{2}/.", emulador.Name, installDir, relativo));
                    return null;
                }

                if (relativo.IndexOf('*') >= 0 || relativo.IndexOf('?') >= 0)
                {
                    var achados = Directory.GetFiles(installDir, relativo, SearchOption.TopDirectoryOnly);
                    if (achados.Length == 0) logger.Warn(string.Format("[Emulador] {0}: nenhum arquivo em {1} casa com {2}.", emulador.Name, installDir, relativo));
                    return achados.Length > 0 ? achados[0] : null;
                }

                var direto = Path.Combine(installDir, relativo);
                if (File.Exists(direto)) return direto;
                logger.Warn(string.Format("[Emulador] {0}: executável esperado não existe: {1}.", emulador.Name, direto));
                return null;
            }
            catch (Exception ex)
            {
                logger.Error(ex, string.Format("[Emulador] {0}: falha ao resolver o executável a partir de \"{1}\" em {2}.", emulador.Name, relativo, emulador.InstallDir));
                return null;
            }
        }

        /// <summary>
        /// Abre no Explorador a pasta de logs com o extensions.log selecionado. O que uma
        /// extensão escreve pelo LogManager do SDK vai para o EXTENSIONS.LOG, não para o
        /// playnite.log — a 0.11.2 apontava para o arquivo errado, e o usuário mandou um log
        /// sem nenhuma linha [Emulador].
        /// </summary>
        public void AbrirPastaDosLogs()
        {
            string pasta = null;
            try { pasta = PlayniteApi.Paths.ConfigurationPath; } catch (Exception) { }
            if (string.IsNullOrEmpty(pasta) || !Directory.Exists(pasta))
            {
                PlayniteApi.Dialogs.ShowMessage("Não achei a pasta de configuração do Playnite.", "Logs");
                return;
            }
            string log = Path.Combine(pasta, "extensions.log");
            try
            {
                if (File.Exists(log))
                    System.Diagnostics.Process.Start("explorer.exe", "/select,\"" + log + "\"");
                else
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(pasta) { UseShellExecute = true });
            }
            catch (Exception ex)
            {
                PlayniteApi.Dialogs.ShowMessage("A pasta dos logs é:\n" + pasta + "\n\n(" + ex.Message + ")", "Logs");
            }
        }

        /// <summary>O perfil usado pela varredura importa por script (sem ImageExtensions)?</summary>
        private bool PerfilImportaPorScript(Emulator emulador, string profileId)
        {
            if (emulador == null || string.IsNullOrEmpty(profileId)) return false;
            if (emulador.BuiltinProfiles == null || string.IsNullOrEmpty(emulador.BuiltInConfigId)) return false;

            foreach (var perfil in emulador.BuiltinProfiles)
            {
                if (perfil.Id != profileId) continue;
                foreach (var d in PlayniteApi.Emulation.Emulators)
                {
                    if (!string.Equals(d.Id, emulador.BuiltInConfigId, StringComparison.OrdinalIgnoreCase)) continue;
                    if (d.Profiles == null) return false;
                    foreach (var dp in d.Profiles)
                    {
                        if (string.Equals(dp.Name, perfil.BuiltInProfileName, StringComparison.OrdinalIgnoreCase))
                            return dp.ScriptGameImport;
                    }
                }
            }
            return false;
        }

        /// <summary>
        /// Os nomes de arquivo que a biblioteca já usa como ROM para este emulador. É como a
        /// extensão aprende o que é "jogo" num perfil que importa por script.
        /// </summary>
        private List<string> NomesDeRomDaBiblioteca(Emulator emulador, Dictionary<string, Game> mapeadas)
        {
            var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var par in mapeadas)
            {
                var g = par.Value;
                if (g.GameActions == null) continue;
                if (!g.GameActions.Any(a => a.Type == GameActionType.Emulator && a.EmulatorId == emulador.Id)) continue;
                try { nomes.Add(Path.GetFileName(par.Key)); } catch (Exception) { }
            }
            return nomes.ToList();
        }

        /// <summary>
        /// A versão do emulador, lida do próprio executável.
        ///
        /// Prefere o ProductVersion: emulador costuma escrever ali a versão que ele mostra na
        /// própria tela ("2.4.0-dev"), enquanto o FileVersion fica num "1.0.0.0" de build. Quando
        /// nenhum dos dois diz nada, devolve travessão — inventar versão faria a coluna perder a
        /// credibilidade justo onde ela precisa ser confiável.
        /// </summary>
        private string VersaoDoExecutavel(string caminho)
        {
            try
            {
                var info = FileVersionInfo.GetVersionInfo(caminho);
                if (info != null && !string.IsNullOrWhiteSpace(info.ProductVersion)) return info.ProductVersion.Trim();
                if (info != null && !string.IsNullOrWhiteSpace(info.FileVersion)) return info.FileVersion.Trim();
            }
            catch (Exception ex)
            {
                logger.Warn(ex, string.Format("Não consegui ler a versão de {0}.", caminho));
            }

            return "—";
        }

        /// <summary>
        /// O ícone do emulador, extraído do executável dele. O Playnite não distribui ícone de
        /// emulador nenhum (as definições em Emulation/Emulators só têm o emulator.yaml), então
        /// o binário é a única fonte.
        ///
        /// A imagem é CONGELADA porque quem a desenha é a thread de UI e quem a cria pode não ser.
        /// </summary>
        private System.Windows.Media.Imaging.BitmapSource IconeDoExecutavel(string caminho)
        {
            System.Drawing.Icon icone = null;
            try
            {
                icone = System.Drawing.Icon.ExtractAssociatedIcon(caminho);
                if (icone == null) return null;

                var imagem = System.Windows.Interop.Imaging.CreateBitmapSourceFromHIcon(
                    icone.Handle,
                    System.Windows.Int32Rect.Empty,
                    System.Windows.Media.Imaging.BitmapSizeOptions.FromEmptyOptions());

                imagem.Freeze();
                return imagem;
            }
            catch (Exception)
            {
                // Executável sem ícone, ou num formato que o Windows não lê: a linha fica sem
                // ícone e o resto da tabela continua de pé.
                return null;
            }
            finally
            {
                if (icone != null) icone.Dispose();
            }
        }

        /// <summary>Os consoles que o emulador atende, juntando o que todos os perfis dele declaram.</summary>
        private string PlataformasDoEmulador(Emulator emulador)
        {
            var nomes = new List<string>();

            if (emulador.CustomProfiles != null)
            {
                foreach (var perfil in emulador.CustomProfiles)
                {
                    string ignorado;
                    foreach (var nome in ResolverPlataformasDoPerfil(emulador, perfil.Id, out ignorado))
                    {
                        if (!nomes.Contains(nome)) nomes.Add(nome);
                    }
                }
            }

            if (emulador.BuiltinProfiles != null)
            {
                foreach (var perfil in emulador.BuiltinProfiles)
                {
                    string ignorado;
                    foreach (var nome in ResolverPlataformasDoPerfil(emulador, perfil.Id, out ignorado))
                    {
                        if (!nomes.Contains(nome)) nomes.Add(nome);
                    }
                }
            }

            return nomes.Count == 0 ? "—" : string.Join(", ", nomes.ToArray());
        }

        /// <summary>As extensões de ROM que o perfil usado por esta varredura declara.</summary>
        private List<string> ExtensoesDoPerfil(Emulator emulador, string profileId)
        {
            var extensoes = new List<string>();
            if (emulador == null || string.IsNullOrEmpty(profileId)) return extensoes;

            if (emulador.CustomProfiles != null)
            {
                foreach (var perfil in emulador.CustomProfiles)
                {
                    if (perfil.Id != profileId) continue;
                    if (perfil.ImageExtensions != null) extensoes.AddRange(perfil.ImageExtensions);
                    return extensoes;
                }
            }

            if (emulador.BuiltinProfiles == null || string.IsNullOrEmpty(emulador.BuiltInConfigId)) return extensoes;

            foreach (var perfil in emulador.BuiltinProfiles)
            {
                if (perfil.Id != profileId) continue;

                EmulatorDefinition definicao = null;
                foreach (var d in PlayniteApi.Emulation.Emulators)
                {
                    if (string.Equals(d.Id, emulador.BuiltInConfigId, StringComparison.OrdinalIgnoreCase)) { definicao = d; break; }
                }
                if (definicao == null || definicao.Profiles == null) return extensoes;

                foreach (var dp in definicao.Profiles)
                {
                    if (!string.Equals(dp.Name, perfil.BuiltInProfileName, StringComparison.OrdinalIgnoreCase)) continue;
                    if (dp.ImageExtensions != null) extensoes.AddRange(dp.ImageExtensions);
                    return extensoes;
                }

                return extensoes;
            }

            return extensoes;
        }

        /// <summary>O console que a varredura força, quando ela força algum.</summary>
        private string PlataformaDaVarredura(GameScannerConfig varredura)
        {
            if (varredura.OverridePlatformId == Guid.Empty) return "—";
            var plataforma = PlayniteApi.Database.Platforms.Get(varredura.OverridePlatformId);
            return plataforma != null && !string.IsNullOrEmpty(plataforma.Name) ? plataforma.Name : "—";
        }

        /// <summary>
        /// Plataformas que o perfil usado pelo jogo declara. Perfil personalizado aponta para
        /// plataformas do banco; perfil embutido só carrega o nome, e o vínculo com o console mora
        /// na definição que o Playnite distribui (Emulation/Emulators/*/emulator.yaml).
        /// </summary>
        private List<string> ResolverPlataformasDoPerfil(Emulator emulador, string profileId, out string nomePerfil)
        {
            nomePerfil = string.Empty;
            var nomes = new List<string>();
            if (emulador == null || string.IsNullOrEmpty(profileId)) return nomes;

            if (emulador.CustomProfiles != null)
            {
                foreach (var perfil in emulador.CustomProfiles)
                {
                    if (perfil.Id != profileId) continue;
                    nomePerfil = perfil.Name;
                    if (perfil.Platforms == null) return nomes;

                    foreach (var platformId in perfil.Platforms)
                    {
                        var plataforma = PlayniteApi.Database.Platforms.Get(platformId);
                        if (plataforma != null && !string.IsNullOrEmpty(plataforma.Name)) nomes.Add(plataforma.Name);
                    }
                    return nomes;
                }
            }

            if (emulador.BuiltinProfiles == null || string.IsNullOrEmpty(emulador.BuiltInConfigId)) return nomes;

            foreach (var perfil in emulador.BuiltinProfiles)
            {
                if (perfil.Id != profileId) continue;
                nomePerfil = perfil.Name;

                EmulatorDefinition definicao = null;
                foreach (var d in PlayniteApi.Emulation.Emulators)
                {
                    if (string.Equals(d.Id, emulador.BuiltInConfigId, StringComparison.OrdinalIgnoreCase)) { definicao = d; break; }
                }
                if (definicao == null || definicao.Profiles == null) return nomes;

                EmulatorDefinitionProfile perfilDefinicao = null;
                foreach (var dp in definicao.Profiles)
                {
                    if (string.Equals(dp.Name, perfil.BuiltInProfileName, StringComparison.OrdinalIgnoreCase)) { perfilDefinicao = dp; break; }
                }
                if (perfilDefinicao == null || perfilDefinicao.Platforms == null) return nomes;

                foreach (var specId in perfilDefinicao.Platforms)
                {
                    foreach (var plataforma in PlayniteApi.Emulation.Platforms)
                    {
                        if (!string.Equals(plataforma.Id, specId, StringComparison.OrdinalIgnoreCase)) continue;
                        if (!string.IsNullOrEmpty(plataforma.Name)) nomes.Add(plataforma.Name);
                        break;
                    }
                }
                return nomes;
            }

            return nomes;
        }

        private string PrimeiraPlataformaDoJogo(Game game)
        {
            if (game.PlatformIds == null) return string.Empty;
            foreach (var id in game.PlatformIds)
            {
                var plataforma = PlayniteApi.Database.Platforms.Get(id);
                if (plataforma != null && !string.IsNullOrEmpty(plataforma.Name)) return plataforma.Name;
            }
            return string.Empty;
        }

        private string NomeDaFonte(Guid sourceId)
        {
            if (sourceId == Guid.Empty) return string.Empty;
            var fonte = PlayniteApi.Database.Sources.Get(sourceId);
            return fonte != null && fonte.Name != null ? fonte.Name : string.Empty;
        }

        // Todo nome de console que o Playnite conhece (definições de emulação + plataformas do
        // banco). Serve para separar "fonte que esta função escreveu" de "fonte de loja".
        private HashSet<string> NomesDeConsoleConhecidos()
        {
            var nomes = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (var p in PlayniteApi.Emulation.Platforms)
                {
                    if (!string.IsNullOrEmpty(p.Name)) nomes.Add(p.Name);
                }
            }
            catch (Exception) { }

            try
            {
                foreach (var p in PlayniteApi.Database.Platforms)
                {
                    if (!string.IsNullOrEmpty(p.Name)) nomes.Add(p.Name);
                }
            }
            catch (Exception) { }

            return nomes;
        }

        /// <summary>Grava a Fonte confirmada na prévia. Devolve os ids realmente alterados.</summary>
        public List<Guid> AplicarBibliotecaDeConsole(List<ConsoleLibraryItem> selecionados)
        {
            var alterados = new List<Guid>();
            if (selecionados == null) return alterados;

            using (PlayniteApi.Database.BufferedUpdate())
            {
                foreach (var item in selecionados)
                {
                    if (item == null || string.IsNullOrEmpty(item.BibliotecaNova)) continue;

                    var game = PlayniteApi.Database.Games.Get(item.GameId);
                    if (game == null) continue;

                    try
                    {
                        var fonte = PlayniteApi.Database.Sources.Add(item.BibliotecaNova.Trim());
                        if (fonte == null || game.SourceId == fonte.Id) continue;

                        game.SourceId = fonte.Id;
                        PlayniteApi.Database.Games.Update(game);
                        alterados.Add(game.Id);
                    }
                    catch (Exception ex)
                    {
                        logger.Error(ex, string.Format("Falha ao gravar a biblioteca do jogo {0}", item.NomeJogo));
                    }
                }
            }

            return alterados;
        }

        /// <summary>Abre a prévia de "Completar Biblioteca" e aplica o que o usuário confirmar.</summary>
        public void CompletarBibliotecaDeEmulacao()
        {
            List<ConsoleLibraryItem> itens = null;
            PlayniteApi.Dialogs.ActivateGlobalProgress(
                (progressArgs) => { itens = MapearBibliotecaDeEmulacao(); },
                new GlobalProgressOptions("Lendo os emuladores dos jogos...", false));

            if (itens == null || itens.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage(
                    "Nenhum jogo de emulação encontrado na biblioteca.\n\n" +
                    "Esta função lê o emulador que cada jogo referencia; jogos de PC (Steam, Epic, locais) não entram.",
                    "Completar Biblioteca");
                return;
            }

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = true,
                ShowMinimizeButton = false
            });
            window.Title = string.Format("Completar Biblioteca ({0} jogo(s) de emulação)", itens.Count);
            window.Width = 1050;
            window.Height = 620;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;

            var alterados = new List<Guid>();
            window.Content = new ConsoleLibraryWindow(
                new ObservableCollection<ConsoleLibraryItem>(itens),
                (selecionados) => alterados.AddRange(AplicarBibliotecaDeConsole(selecionados)));
            window.ShowDialog();

            // Mensagem só depois que a modal fecha: diálogo aberto de dentro do callback nasce atrás dela.
            if (alterados.Count == 0) return;
            PlayniteApi.Dialogs.ShowMessage(
                string.Format("{0} jogo(s) agora aparecem na biblioteca pelo console.", alterados.Count),
                "Completar Biblioteca");
        }

        public void CheckLibraryIntegrity()
        {
            var results = new ObservableCollection<IntegrityResult>();
            PlayniteApi.Dialogs.ActivateGlobalProgress((progressArgs) =>
            {
                var games = PlayniteApi.Database.Games.ToList();

                // A verificação só sabia dizer o que sumiu, e as duas saídas que ela oferecia
                // (remover ou desinstalar) eram destrutivas. Indexar o disco antes deixa a tela
                // dizer também PARA ONDE o jogo foi — que é a resposta certa na maioria dos casos.
                progressArgs.Text = "Lendo as pastas monitoradas...";
                var indice = IndexarPastasEmDisco(progressArgs.CancelToken);
                var ocupadas = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var g in games)
                {
                    if (string.IsNullOrEmpty(g.InstallDirectory)) continue;
                    if (!Directory.Exists(g.InstallDirectory)) continue;
                    ocupadas.Add(NormalizePath(g.InstallDirectory));
                }
                progressArgs.Text = "Verificando integridade da biblioteca...";

                foreach (var game in games)
                {
                    if (progressArgs.CancelToken.IsCancellationRequested) break;
                    if (DiscoRemovivelDesconectado(game.InstallDirectory)) continue;

                    bool folderMissing = false;
                    bool exeMissing = false;

                    if (!string.IsNullOrEmpty(game.InstallDirectory))
                    {
                        if (!Directory.Exists(game.InstallDirectory))
                        {
                            folderMissing = true;
                        }
                    }

                    if (game.GameActions != null && game.GameActions.Any(a => a.Type == GameActionType.File))
                    {
                        var primaryAction = game.GameActions.FirstOrDefault(a => a.Type == GameActionType.File);
                        if (primaryAction != null)
                        {
                            string fullPath = PlayniteApi.ExpandGameVariables(game, primaryAction.Path);
                            if (!File.Exists(fullPath))
                            {
                                exeMissing = true;
                            }
                        }
                    }

                    if (folderMissing || exeMissing)
                    {
                        string exeAntigo = null;
                        if (game.GameActions != null)
                        {
                            var fa = game.GameActions.FirstOrDefault(a => a.Type == GameActionType.File);
                            if (fa != null && !string.IsNullOrEmpty(fa.Path)) exeAntigo = PlayniteApi.ExpandGameVariables(game, fa.Path);
                        }

                        bool ehEmulacao = game.GameActions != null &&
                                          game.GameActions.Any(a => a.Type == GameActionType.Emulator);
                        // Jogo de outra biblioteca (Steam, GOG, Epic) continua APARECENDO aqui,
                        // porque saber que ele está quebrado é útil, mas não recebe proposta de
                        // reapontamento: quem manda no caminho dele é o launcher dono, e gravar
                        // uma ação de arquivo por cima é sequestrar o jogo.
                        bool ehDeOutraBiblioteca = game.PluginId != Guid.Empty && game.PluginId != Id;
                        var achado = (ehEmulacao || ehDeOutraBiblioteca)
                            ? null
                            : ProcurarNovaCasa(game, exeAntigo, indice, ocupadas);

                        PlayniteApi.MainView.UIDispatcher.Invoke(() =>
                        {
                            results.Add(new IntegrityResult
                            {
                                GameId = game.Id,
                                Name = game.Name,
                                InstallDir = game.InstallDirectory,
                                FolderMissing = folderMissing,
                                ExeMissing = exeMissing,
                                NovaPasta = achado == null ? null : achado.PastaCandidata,
                                NovoExe = achado == null ? null : achado.ExeCandidato,
                                Motivo = achado == null ? null : achado.Motivo,
                                Confianca = achado == null ? null : achado.Confianca,
                                SoOExe = achado != null && achado.MesmaPasta,
                                // Jogo que foi ENCONTRADO não nasce marcado: as duas ações desta
                                // tela são destrutivas, e ele não precisa de nenhuma das duas.
                                Selected = achado == null
                            });
                        });
                    }
                }
            }, new GlobalProgressOptions("Verificando integridade da biblioteca...", true));

            if (results.Count == 0)
            {
                PlayniteApi.Dialogs.ShowMessage("Nenhum problema de integridade encontrado.", "Scanner de Integridade");
                return;
            }

            var window = PlayniteApi.Dialogs.CreateWindow(new WindowCreationOptions
            {
                ShowMaximizeButton = false,
                ShowMinimizeButton = false
            });

            window.Title = "Integridade da Biblioteca - Itens Ausentes";
            window.Content = new IntegrityResultView(
                results,
                onRemove: (selectedItems) =>
                {
                    if (selectedItems.Count == 0)
                    {
                        PlayniteApi.Dialogs.ShowMessage("Nenhum item selecionado.", "Aviso");
                        return;
                    }
                    if (PlayniteApi.Dialogs.ShowMessage(string.Format("Tem certeza que deseja remover {0} jogo(s) da biblioteca?", selectedItems.Count), "Confirmar Remoção", MessageBoxButton.YesNo) == MessageBoxResult.Yes)
                    {
                        foreach (var item in selectedItems)
                        {
                            PlayniteApi.Database.Games.Remove(item.GameId);
                        }
                        PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogo(s) removido(s).", selectedItems.Count), "Sucesso");
                        window.Close();
                    }
                },
                onMarkUninstalled: (selectedItems) =>
                {
                    // Item que a busca encontrou no disco fica de fora mesmo se marcado à mão:
                    // marcar como desinstalado o jogo que está ali, funcionando, é o erro que
                    // esta rodada corrige.
                    var alvos = selectedItems.Where(i => !i.TemNovaCasa).ToList();
                    if (alvos.Count == 0)
                    {
                        PlayniteApi.Dialogs.ShowMessage(
                            selectedItems.Count == 0
                                ? "Nenhum item selecionado."
                                : "Os itens selecionados foram encontrados no disco — use \"Reapontar\" neles, em vez de marcar como desinstalados.",
                            "Aviso");
                        return;
                    }
                    int marcados = 0;
                    foreach (var item in alvos)
                    {
                        if (MarcarComoDesinstalado(item.GameId)) marcados++;
                    }
                    PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogo(s) marcado(s) como desinstalado(s).", marcados), "Sucesso");
                    window.Close();
                },
                onRelocate: (selectedItems) =>
                {
                    var alvos = selectedItems.Where(i => i.TemNovaCasa).ToList();
                    if (alvos.Count == 0)
                    {
                        PlayniteApi.Dialogs.ShowMessage(
                            "Selecione os jogos encontrados no disco (\"Mudou de pasta\" ou \"Executável mudou\") para reapontar.",
                            "Aviso");
                        return;
                    }

                    int ok = 0;
                    using (PlayniteApi.Database.BufferedUpdate())
                    {
                        foreach (var item in alvos)
                        {
                            if (RelocarJogo(item.GameId, item.NovaPasta, item.NovoExe)) ok++;
                        }
                    }
                    PlayniteApi.Dialogs.ShowMessage(
                        string.Format("{0} jogo(s) reapontados para a pasta onde estão hoje.", ok), "Sucesso");
                    window.Close();
                },
                onClose: () => window.Close());

            window.SizeToContent = SizeToContent.WidthAndHeight;
            window.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            window.ShowDialog();
        }
    }
}
