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
            if (indice == null || indice.Count == 0) return null;

            string nomeExeAntigo = string.IsNullOrEmpty(exeAntigoCompleto)
                ? string.Empty
                : Path.GetFileName(exeAntigoCompleto);

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

            foreach (var pasta in indice)
            {
                if (pastasOcupadas != null && pastasOcupadas.Contains(NormalizePath(pasta.Pasta))) continue;

                // Dentro da pasta candidata, o executável que interessa é o de mesmo nome do
                // antigo; não havendo, o primeiro serve para o casamento por nome de pasta.
                var exe = pasta.Exes.FirstOrDefault(e => nomeExeAntigo.Length > 0 &&
                              Path.GetFileName(e.Caminho).Equals(nomeExeAntigo, StringComparison.OrdinalIgnoreCase));
                if (exe == null) exe = pasta.Exes.FirstOrDefault();
                if (exe == null) continue;

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

        public override ISettings GetSettings(bool firstRun) { return settings; }
        public override UserControl GetSettingsView(bool firstRun) { return new BuscaDeJogosLocaisSettingsView(); }
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
                        var achado = ehEmulacao ? null : ProcurarNovaCasa(game, exeAntigo, indice, ocupadas);

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
                            "Selecione os jogos com status \"Mudou de pasta\" para reapontar.",
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
