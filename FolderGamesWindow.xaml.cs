using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;

namespace BuscaDeJogosLocais
{
    // Lista os jogos vinculados a uma pasta monitorada e, junto, o que está na pasta
    // mas ficou de fora da biblioteca — é a resposta para "essa pasta está toda puxada?".
    public partial class FolderGamesWindow : UserControl, INotifyPropertyChanged
    {
        public event PropertyChangedEventHandler PropertyChanged;
        private void Notify(string name)
        {
            if (PropertyChanged != null) PropertyChanged(this, new PropertyChangedEventArgs(name));
        }

        private readonly PastaResumo resumo;

        // Só para o botão "Abrir pasta": quem sabe avisar que a pasta não abriu é o plugin, que
        // tem o PlayniteApi.Dialogs. Vem null quando esta tela é construída sem ele, e aí o botão
        // fica escondido — botão visível que não faz nada é pior do que botão ausente.
        private readonly BuscaDeJogosLocais plugin;

        public ICollectionView ItensView { get; private set; }

        public bool PodeAbrirPasta
        {
            get { return plugin != null && !string.IsNullOrWhiteSpace(resumo.Caminho); }
        }

        // Os botões de ação só existem com o plugin (quem toca no banco) e enquanto ainda houver
        // jogo com pasta ausente. Sem alvo, botão visível é convite a clicar à toa.
        public bool PodeAgirSobreAusentes
        {
            get { return plugin != null && itens.Any(i => i.PodeSelecionar); }
        }

        // Quem abriu esta janela lê isto ao fechar, para recontar a tabela de pastas.
        public bool HouveMudanca { get; private set; }

        private readonly ObservableCollection<PastaJogoItem> itens;

        private bool somenteForaDaBiblioteca;
        public bool SomenteForaDaBiblioteca
        {
            get { return somenteForaDaBiblioteca; }
            set { somenteForaDaBiblioteca = value; Notify("SomenteForaDaBiblioteca"); ItensView.Refresh(); }
        }

        public string HeaderText
        {
            get { return resumo.Caminho; }
        }

        public string ResumoText
        {
            get
            {
                return string.Format(
                    "{0} na biblioteca · {1} não importado(s) · {2} com pasta ausente · {3} ignorado(s)",
                    resumo.NaBiblioteca, resumo.NaoImportados, resumo.ComProblema, resumo.Ignorados);
            }
        }

        public FolderGamesWindow(PastaResumo resumo) : this(resumo, null)
        {
        }

        public FolderGamesWindow(PastaResumo resumo, BuscaDeJogosLocais plugin)
        {
            InitializeComponent();
            this.resumo = resumo;
            this.plugin = plugin;
            this.DataContext = this;

            itens = new ObservableCollection<PastaJogoItem>(
                resumo.Itens.OrderBy(i => i.Status).ThenBy(i => i.Nome));

            ItensView = CollectionViewSource.GetDefaultView(itens);
            ItensView.Filter = Filtrar;

            Diagnosticar();
        }

        // Dá a cada linha o motivo e a ação. O que exige ler o disco (pasta não importada) só
        // roda com o plugin; sem ele a tela é só leitura.
        private void Diagnosticar()
        {
            foreach (var item in itens)
            {
                try
                {
                    switch (item.Status)
                    {
                        case "Na biblioteca":
                            item.Motivo = "Pasta existe e o jogo aponta para ela.";
                            break;
                        case "Pasta ausente":
                            item.Motivo = string.Format("A pasta {0} não existe mais. Se foi apagada de propósito, marque como desinstalado ou remova; se mudou de lugar, use a aba \"Jogos que sumiram\".", item.Caminho);
                            break;
                        case "Ignorado":
                            item.Motivo = "Um executável desta pasta está na lista de ignorados (Ajustes). Tire de lá para a busca voltar a considerá-la.";
                            break;
                        case "Não importado":
                            if (plugin == null) { item.Motivo = "Sem diagnóstico (tela aberta sem o plugin)."; break; }
                            var d = plugin.DiagnosticarPastaNaoImportada(item.Caminho);
                            item.Motivo = d.Motivo;
                            item.Exe = d.Exe;
                            item.JogoRelacionadoId = d.JogoRelacionadoId;
                            item.Acao = d.Acao;
                            break;
                    }
                }
                catch (Exception ex)
                {
                    item.Motivo = "Falha ao diagnosticar: " + ex.Message;
                }
            }
        }

        private void OnAcaoClick(object sender, RoutedEventArgs e)
        {
            var botao = sender as FrameworkElement;
            var item = botao != null ? botao.DataContext as PastaJogoItem : null;
            if (item == null || plugin == null) return;

            try
            {
                switch (item.Acao)
                {
                    case "importar": Importar(item); break;
                    case "adotar": Adotar(item); break;
                    case "reapontar": Reapontar(item); break;
                }
            }
            catch (Exception ex)
            {
                plugin.PlayniteApi.Dialogs.ShowErrorMessage(
                    string.Format("Não deu para executar \"{0}\" em {1}:\n\n{2}\n\nO detalhe está no log do Playnite (playnite.log).", item.AcaoTexto, item.NomePasta, ex.Message),
                    "Erro");
            }
        }

        private void Importar(PastaJogoItem item)
        {
            if (string.IsNullOrEmpty(item.Exe))
            {
                plugin.PlayniteApi.Dialogs.ShowMessage("Nenhum executável escolhido para esta pasta.", "Aviso");
                return;
            }

            if (plugin.PlayniteApi.Dialogs.ShowMessage(
                    string.Format("Importar «{0}»?\n\nPasta: {1}\nExecutável: {2}", item.Nome, item.Caminho, item.Exe),
                    "Confirmar", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            var scanned = new ScannedGame
            {
                Nome = item.Nome,
                NomeOriginal = item.NomePasta,
                Versao = item.Versao ?? string.Empty,
                CaminhoExe = item.Exe,
                PastaRaiz = item.Caminho,
                PastaMonitoradaPai = resumo.Caminho,
                UltimaVerificacao = DateTime.Now.ToString("dd/MM/yyyy HH:mm")
            };

            var ids = plugin.ImportarLote(new List<ScannedGame> { scanned });
            if (ids.Count == 0)
            {
                plugin.PlayniteApi.Dialogs.ShowMessage(
                    "A importação não criou o jogo. O motivo mais comum é já existir um jogo apontando para esta pasta — reabra a janela para ver o diagnóstico atualizado.",
                    "Nada importado");
                return;
            }

            item.GameId = ids[0];
            item.Status = "Na biblioteca";
            item.Motivo = "Importado agora por esta janela.";
            item.Acao = string.Empty;
            resumo.NaoImportados = Math.Max(0, resumo.NaoImportados - 1);
            AtualizarResumo();
            plugin.BaixarMetadadosDosImportados(ids);
        }

        private void Adotar(PastaJogoItem item)
        {
            if (plugin.PlayniteApi.Dialogs.ShowMessage(
                    "Trazer este jogo para a extensão?\n\nEle continua o mesmo jogo (tempo, capa, tags); só passa a contar como importado local, e a busca e as tabelas passam a enxergá-lo.",
                    "Confirmar", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            if (!plugin.AdotarJogo(item.JogoRelacionadoId, item.Caminho))
            {
                plugin.PlayniteApi.Dialogs.ShowMessage("Não foi possível: o jogo pertence a outra biblioteca ou não existe mais.", "Aviso");
                return;
            }

            item.GameId = item.JogoRelacionadoId;
            item.Status = "Na biblioteca";
            item.Motivo = "Trazido para esta extensão agora.";
            item.Acao = string.Empty;
            resumo.NaoImportados = Math.Max(0, resumo.NaoImportados - 1);
            AtualizarResumo();
        }

        private void Reapontar(PastaJogoItem item)
        {
            if (plugin.PlayniteApi.Dialogs.ShowMessage(
                    string.Format("Reapontar o jogo da biblioteca para esta pasta?\n\nNova pasta: {0}\nExecutável: {1}", item.Caminho, item.Exe),
                    "Confirmar", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            if (!plugin.RelocarJogo(item.JogoRelacionadoId, item.Caminho, item.Exe))
            {
                plugin.PlayniteApi.Dialogs.ShowMessage("Não foi possível reapontar: jogo não encontrado ou pasta inexistente.", "Aviso");
                return;
            }

            item.GameId = item.JogoRelacionadoId;
            item.Status = "Na biblioteca";
            item.Motivo = "Reapontado agora para esta pasta.";
            item.Acao = string.Empty;
            resumo.NaoImportados = Math.Max(0, resumo.NaoImportados - 1);
            // O registro antigo com pasta ausente pode estar nesta mesma lista: some daqui.
            var antigo = itens.FirstOrDefault(i => i != item && i.GameId == item.GameId);
            if (antigo != null) itens.Remove(antigo);
            AtualizarResumo();
        }

        private void OnCopiarDiagnosticoClick(object sender, RoutedEventArgs e)
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("BuscaDeJogosLocais — diagnóstico da pasta");
            sb.AppendLine("Pasta monitorada: " + resumo.Caminho);
            sb.AppendLine("Existe em disco: " + resumo.ExisteEmDisco);
            sb.AppendLine("Gerado em: " + DateTime.Now.ToString("dd/MM/yyyy HH:mm:ss"));
            sb.AppendLine(ResumoText);
            sb.AppendLine();
            foreach (var i in itens)
            {
                sb.AppendLine(string.Format("[{0}] {1}", i.Status, i.Nome));
                sb.AppendLine("  pasta: " + i.Caminho);
                if (!string.IsNullOrEmpty(i.Versao)) sb.AppendLine("  versão: " + i.Versao);
                if (i.GameId != Guid.Empty) sb.AppendLine("  gameId: " + i.GameId);
                if (i.JogoRelacionadoId != Guid.Empty) sb.AppendLine("  jogo relacionado: " + i.JogoRelacionadoId);
                if (!string.IsNullOrEmpty(i.Exe)) sb.AppendLine("  exe: " + i.Exe);
                if (!string.IsNullOrEmpty(i.Motivo)) sb.AppendLine("  motivo: " + i.Motivo);
                if (i.TemAcao) sb.AppendLine("  ação sugerida: " + i.AcaoTexto);
            }

            try
            {
                Clipboard.SetText(sb.ToString());
                if (plugin != null) plugin.PlayniteApi.Dialogs.ShowMessage("Diagnóstico copiado. Cole no chat para a gente olhar o caso real.", "Copiado");
            }
            catch (Exception ex)
            {
                if (plugin != null) plugin.PlayniteApi.Dialogs.ShowErrorMessage("Não consegui copiar: " + ex.Message, "Erro");
            }
        }

        private bool Filtrar(object item)
        {
            if (!SomenteForaDaBiblioteca) return true;
            var jogo = (PastaJogoItem)item;
            return jogo.Status != "Na biblioteca";
        }

        private void OnAbrirPastaClick(object sender, RoutedEventArgs e)
        {
            if (plugin != null) plugin.AbrirPastaNoExplorador(resumo.Caminho);
        }

        private List<PastaJogoItem> AlvosSelecionados()
        {
            var alvos = itens.Where(i => i.PodeSelecionar && i.Selecionado && i.GameId != Guid.Empty).ToList();
            if (alvos.Count == 0)
            {
                plugin.PlayniteApi.Dialogs.ShowMessage(
                    "Marque um ou mais jogos com \"Pasta ausente\" para agir sobre eles.", "Aviso");
            }
            return alvos;
        }

        private void AtualizarResumo()
        {
            resumo.ComProblema = itens.Count(i => i.Status == "Pasta ausente");
            resumo.NaBiblioteca = itens.Count(i => i.Status == "Na biblioteca");
            resumo.NaoImportados = itens.Count(i => i.Status == "Não importado");
            HouveMudanca = true;
            Notify("ResumoText");
            Notify("PodeAgirSobreAusentes");
            ItensView.Refresh();
        }

        private void OnMarcarDesinstaladoClick(object sender, RoutedEventArgs e)
        {
            var alvos = AlvosSelecionados();
            if (alvos.Count == 0) return;

            if (plugin.PlayniteApi.Dialogs.ShowMessage(
                    string.Format("Marcar {0} jogo(s) como desinstalado(s)?\n\nEles continuam na biblioteca e entram em Histórico > Desinstalações.", alvos.Count),
                    "Confirmar", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            int marcados = 0;
            foreach (var item in alvos)
            {
                if (plugin.MarcarComoDesinstalado(item.GameId)) marcados++;
                item.Status = "Desinstalado";
                item.Selecionado = false;
            }
            AtualizarResumo();
            plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogo(s) marcado(s) como desinstalado(s).", marcados), "Sucesso");
        }

        private void OnRemoverClick(object sender, RoutedEventArgs e)
        {
            var alvos = AlvosSelecionados();
            if (alvos.Count == 0) return;

            if (plugin.PlayniteApi.Dialogs.ShowMessage(
                    string.Format("Remover {0} jogo(s) da biblioteca do Playnite?\n\nIsso apaga a entrada (tempo de jogo, capa, tags). Nada é apagado do disco.", alvos.Count),
                    "Confirmar", MessageBoxButton.YesNo) != MessageBoxResult.Yes)
                return;

            int removidos = 0;
            foreach (var item in alvos)
            {
                if (plugin.RemoverDaBiblioteca(item.GameId)) removidos++;
                itens.Remove(item);
            }
            AtualizarResumo();
            plugin.PlayniteApi.Dialogs.ShowMessage(string.Format("{0} jogo(s) removido(s) da biblioteca.", removidos), "Sucesso");
        }

        private void OnFecharClick(object sender, RoutedEventArgs e)
        {
            var window = Window.GetWindow(this);
            if (window != null) window.Close();
        }
    }
}
