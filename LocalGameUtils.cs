using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace BuscaDeJogosLocais
{
    // Lógica pura de scan/normalização de caminhos, SEM dependência de Playnite ou WPF.
    // Mantida isolada para permitir testes de regressão automatizados (ver tests/).
    // O plugin (BuscaDeJogosLocais.cs) delega para estes métodos.
    public static class LocalGameUtils
    {
        // Executáveis que não são jogos (instaladores, utilitários, redistribuíveis, etc.).
        public static readonly string[] ExeBlacklist =
        {
            "unins", "setup", "crash", "helper", "update", "redist", "bugreport",
            "sendreport", "steamerrorreporter", "dxwebsetup", "dotnet", "vcredist",
            "tool", "media", "storybook"
        };

        public static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return string.Empty;
            return Path.GetFullPath(path).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).ToLowerInvariant();
        }

        // Retorna true se o arquivo NÃO deve ser considerado um jogo
        // (não é .exe, ou cai na blacklist de utilitários/instaladores).
        public static bool IsExplosiveFile(string path)
        {
            string p = path.ToLowerInvariant();
            if (!p.EndsWith(".exe")) return true;
            return ExeBlacklist.Any(b => p.Contains(b));
        }

        // Sobe a árvore de diretórios a partir do executável até achar uma pasta cujo
        // pai seja uma das pastas monitoradas. Retorna a pasta raiz do jogo (ou null).
        // 'normalizedMonitoredPaths' deve conter caminhos já normalizados via NormalizePath.
        public static string GetGameRoot(string filePath, List<string> normalizedMonitoredPaths, out string monitoredPai)
        {
            monitoredPai = "Desconhecida";
            if (string.IsNullOrEmpty(filePath) || normalizedMonitoredPaths == null) return null;

            DirectoryInfo current = new DirectoryInfo(Path.GetDirectoryName(filePath));
            while (current != null && current.Parent != null)
            {
                string currentParentPath = NormalizePath(current.Parent.FullName);
                if (normalizedMonitoredPaths.Contains(currentParentPath))
                {
                    monitoredPai = current.Parent.FullName;
                    return current.FullName;
                }
                current = current.Parent;
            }
            return null;
        }

        // True se o caminho do executável estiver na lista de excluídos (case-insensitive).
        public static bool IsPathExcluded(string exePath, IEnumerable<string> excludedExePaths)
        {
            if (excludedExePaths == null || string.IsNullOrEmpty(exePath)) return false;
            return excludedExePaths.Any(e => e != null && e.Equals(exePath, StringComparison.OrdinalIgnoreCase));
        }

        // True se 'path' está dentro de 'folder' (ou é a própria pasta). Compara caminhos
        // normalizados e exige separador na fronteira, para "C:\Games2" não contar como
        // filho de "C:\Games".
        public static bool IsUnderFolder(string path, string folder)
        {
            if (string.IsNullOrEmpty(path) || string.IsNullOrEmpty(folder)) return false;

            string p = NormalizePath(path);
            string f = NormalizePath(folder);
            if (p.Length == 0 || f.Length == 0) return false;
            if (p.Equals(f, StringComparison.OrdinalIgnoreCase)) return true;

            return p.StartsWith(f + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
        }

        // Padrões usados para reconhecer que uma pasta de jogo contém um "save" local.
        // Entradas começando com "." são tratadas como extensões de arquivo (ex: ".sav");
        // as demais são comparadas com nomes de pastas ou nomes de arquivo (sem extensão).
        public static readonly string[] DefaultSavePatterns =
        {
            "save", "saves", "saved", "savegame", "savegames", "savedata",
            "profile", "profiles", "playerdata", "userdata", "slot",
            ".sav", ".save"
        };

        // Normaliza o nome de um jogo para agrupar duplicatas (trim + minúsculo + espaços colapsados).
        public static string NormalizeGameName(string name)
        {
            if (string.IsNullOrEmpty(name)) return string.Empty;
            string n = name.Trim().ToLowerInvariant();
            while (n.Contains("  ")) n = n.Replace("  ", " ");
            return n;
        }

        // ---------------------------------------------------------------------
        // Limpeza de nome de pasta no padrão "scene release"
        // (ex.: "STARDUST.Wish.of.Witch.v20260729-P2P" -> "STARDUST Wish of Witch" + versão "20260729")
        // ---------------------------------------------------------------------

        // Grupos de release conhecidos. Só são removidos quando aparecem no final do nome,
        // depois de um hífen. A lista existe para não confundir com hífen legítimo do título
        // (ex.: "PAC-MAN.WORLD.2.Re-PAC" não pode virar "PAC-MAN WORLD 2 Re").
        public static readonly string[] ReleaseGroups =
        {
            "p2p", "gog", "codex", "plaza", "skidrow", "reloaded", "hoodlum", "tenoke",
            "rune", "empress", "flt", "doge", "cpy", "elamigos", "fitgirl", "dodi",
            "goldberg", "tinyiso", "darksiders", "prophet", "simplex", "ali213", "3dm",
            "razor1911", "razordox", "unleashed", "dinobytes", "zola", "chronos", "kaos",
            "anomaly", "masquerade", "i_know", "rvtfix", "onlinefix", "gamesena",
            "steampunks", "sisters", "0xdeadc0de", "fckdrm", "nosteam", "rg", "kaoskrew"
        };

        // Sufixos que descrevem o empacotamento, não o jogo. Removidos apenas do fim do nome.
        public static readonly string[] NoiseTokens =
        {
            "repack", "rip", "proper", "readnfo", "nfofix", "crackfix", "cracked", "portable",
            "x64", "x86", "win64", "win32", "pcdvd", "iso", "full", "final", "retail"
        };

        private static readonly Regex VersionRegex =
            new Regex(@"(?:^|[._\s-])v(\d+(?:\.\d+)*[a-z]?)(?![a-z0-9])", RegexOptions.IgnoreCase);
        private static readonly Regex BuildRegex =
            new Regex(@"(?:^|[._\s-])build[._\s-]?(\d{2,})", RegexOptions.IgnoreCase);
        private static readonly Regex UpdateRegex =
            new Regex(@"(?:^|[._\s-])update[._\s-]?(\d+(?:\.\d+)*)", RegexOptions.IgnoreCase);
        private static readonly Regex MultiRegex =
            new Regex(@"^multi\d*$", RegexOptions.IgnoreCase);
        private static readonly Regex AcronymRegex =
            new Regex(@"(?:\b[A-Za-z]\.){2,}");

        private const char AcronymDotMarker = '\u0001';

        // Extrai a versão embutida no nome da pasta. Retorna null quando não há versão reconhecível.
        // Reconhece "v1.00.1", "v20260729", "Build.12345" e "Update.3" (e combinações).
        public static string ExtractVersion(string folderName)
        {
            if (string.IsNullOrEmpty(folderName)) return null;

            var partes = new List<string>();

            var mv = VersionRegex.Match(folderName);
            if (mv.Success) partes.Add(mv.Groups[1].Value);

            var mb = BuildRegex.Match(folderName);
            if (mb.Success) partes.Add(string.Format("Build {0}", mb.Groups[1].Value));

            var mu = UpdateRegex.Match(folderName);
            if (mu.Success) partes.Add(string.Format("Update {0}", mu.Groups[1].Value));

            if (partes.Count == 0) return null;
            return string.Join(" ", partes.ToArray());
        }

        // Atalho para quem só quer o nome limpo, sem a versão.
        public static string CleanGameNameOnly(string folderName)
        {
            string ignorado;
            return CleanGameName(folderName, out ignorado);
        }

        // Devolve o nome legível de uma pasta de jogo e, via 'version', a versão encontrada (ou null).
        // Nunca devolve string vazia: se a limpeza consumir tudo, o nome original é preservado.
        public static string CleanGameName(string folderName, out string version)
        {
            version = ExtractVersion(folderName);
            if (string.IsNullOrEmpty(folderName)) return string.Empty;

            string nome = folderName.Trim();

            // 1. Corta a partir do primeiro marcador de versão/build/update — dali pra frente
            //    é sempre metadado de release (versão, grupo, repack, idiomas...).
            int corte = -1;
            foreach (var m in new[] { VersionRegex.Match(nome), BuildRegex.Match(nome), UpdateRegex.Match(nome) })
            {
                if (!m.Success) continue;
                if (m.Index <= 0) continue;
                if (corte < 0 || m.Index < corte) corte = m.Index;
            }
            if (corte > 0) nome = nome.Substring(0, corte);

            // 2. Grupo de release colado no fim ("-GoldBerg"), quando não havia versão pra cortar.
            nome = StripReleaseGroup(nome);

            // 3. Separadores: ponto e underscore viram espaço, preservando siglas ("S.T.A.L.K.E.R.").
            nome = AcronymRegex.Replace(nome, delegate(Match m) { return m.Value.Replace('.', AcronymDotMarker); });
            nome = nome.Replace('.', ' ').Replace('_', ' ').Replace(AcronymDotMarker, '.');
            while (nome.Contains("  ")) nome = nome.Replace("  ", " ");
            nome = nome.Trim(' ', '-');

            // 4. Ruído de empacotamento sobrando no fim ("Repack", "MULTi9", "x64", "Incl DLC").
            nome = StripTrailingNoise(nome);

            // 5. Capitalização normalizada ("STARDUST Wish of Witch" -> "Stardust Wish of Witch").
            nome = ToTitleCase(nome);

            if (string.IsNullOrEmpty(nome)) return folderName.Trim();
            return nome;
        }

        // Palavras que ficam em minúsculo quando não são a primeira do título.
        private static readonly string[] LowercaseWords =
        {
            "a", "an", "and", "as", "at", "but", "by", "for", "from", "in", "into",
            "nor", "of", "on", "or", "the", "to", "vs", "with",
            "e", "da", "de", "do", "das", "dos", "em", "no", "na", "para", "por", "um", "uma"
        };

        private static readonly Regex RomanRegex =
            new Regex(@"^(?:M{0,3})(?:CM|CD|D?C{0,3})(?:XC|XL|L?X{0,3})(?:IX|IV|V?I{0,3})$", RegexOptions.IgnoreCase);
        private static readonly Regex AcronymTokenRegex =
            new Regex(@"^(?:[A-Za-z]\.){2,}$");
        private static readonly Regex TemDigitoRegex = new Regex(@"\d");

        // Normaliza a capitalização de um título: cada palavra com a inicial maiúscula,
        // preposições curtas em minúsculo, siglas/numerais romanos preservados.
        // Ex.: "STARDUST Wish of Witch" -> "Stardust Wish of Witch"; "FABLE II" -> "Fable II".
        public static string ToTitleCase(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return nome;

            var tokens = nome.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            var saida = new List<string>();

            for (int i = 0; i < tokens.Length; i++)
            {
                string token = tokens[i];

                // Siglas pontuadas (S.T.A.L.K.E.R.) e numerais romanos (II, XIV) ficam como estão, em caixa alta.
                if (AcronymTokenRegex.IsMatch(token) ||
                    (token.Length > 0 && RomanRegex.IsMatch(token) && token.Length > 1))
                {
                    saida.Add(token.ToUpperInvariant());
                    continue;
                }

                // Token com dígito misturado (2D, 4K, HD2) fica intacto — mexer só atrapalha.
                if (TemDigitoRegex.IsMatch(token) && token.Any(char.IsLetter))
                {
                    saida.Add(token);
                    continue;
                }

                // CamelCase intencional do título (EverSiege, PlayerUnknown) é preservado:
                // só conta quando há maiúscula logo depois de minúscula.
                if (TemCamelCase(token))
                {
                    saida.Add(token);
                    continue;
                }

                string lower = token.ToLowerInvariant();
                if (i > 0 && LowercaseWords.Contains(lower))
                {
                    saida.Add(lower);
                    continue;
                }

                saida.Add(CapitalizarComHifen(lower));
            }

            return string.Join(" ", saida.ToArray());
        }

        // True quando o token tem maiúscula logo após minúscula ("EverSiege"), sinal de
        // camelCase escolhido pelo autor do jogo. "PAC-MAN" e "STARDUST" não entram aqui.
        private static bool TemCamelCase(string token)
        {
            for (int i = 1; i < token.Length; i++)
            {
                if (char.IsUpper(token[i]) && char.IsLower(token[i - 1])) return true;
            }
            return false;
        }

        // Capitaliza a inicial e também a letra depois de cada hífen ("pac-man" -> "Pac-Man").
        private static string CapitalizarComHifen(string palavra)
        {
            var chars = palavra.ToCharArray();
            bool inicio = true;
            for (int i = 0; i < chars.Length; i++)
            {
                if (inicio && char.IsLetter(chars[i]))
                {
                    chars[i] = char.ToUpperInvariant(chars[i]);
                    inicio = false;
                }
                else if (chars[i] == '-' || chars[i] == ':' || chars[i] == '(')
                {
                    inicio = true;
                }
            }
            return new string(chars);
        }

        /// <summary>
        /// Decide qual nome de console vira a "biblioteca" (Fonte) de um jogo de emulação.
        /// A ordem de confiança é: o que o perfil do emulador declara, depois a plataforma que o
        /// próprio jogo já tem. Quando nenhuma das duas existe devolve vazio — nunca inventa
        /// console a partir do nome do emulador, que é texto livre do usuário ("PS2", "New Emulator").
        /// </summary>
        public static string EscolherNomeDeConsole(List<string> plataformasDoPerfil, string plataformaDoJogo)
        {
            string doJogo = string.IsNullOrEmpty(plataformaDoJogo) ? string.Empty : plataformaDoJogo.Trim();

            var doPerfil = new List<string>();
            if (plataformasDoPerfil != null)
            {
                foreach (var p in plataformasDoPerfil)
                {
                    if (string.IsNullOrEmpty(p)) continue;
                    string limpo = p.Trim();
                    if (limpo.Length > 0) doPerfil.Add(limpo);
                }
            }

            if (doPerfil.Count == 0) return doJogo;

            // Um mesmo perfil pode cobrir mais de um console (um emulador de Game Boy que também
            // roda Game Boy Color). Nesse caso quem desempata é a plataforma do próprio jogo.
            if (doJogo.Length > 0)
            {
                foreach (var p in doPerfil)
                {
                    if (string.Equals(p, doJogo, StringComparison.OrdinalIgnoreCase)) return p;
                }
            }

            if (doPerfil.Count == 1) return doPerfil[0];
            return doJogo.Length > 0 ? doJogo : doPerfil[0];
        }

        private static string StripReleaseGroup(string nome)
        {
            string atual = nome.TrimEnd('.', '-', '_', ' ');
            int hifen = atual.LastIndexOf('-');
            if (hifen <= 0 || hifen == atual.Length - 1) return atual;

            string sufixo = atual.Substring(hifen + 1).Trim();
            if (sufixo.IndexOf('.') >= 0 || sufixo.IndexOf(' ') >= 0) return atual;
            if (!ReleaseGroups.Contains(sufixo.ToLowerInvariant())) return atual;

            return atual.Substring(0, hifen).TrimEnd('.', '-', '_', ' ');
        }

        private static string StripTrailingNoise(string nome)
        {
            var tokens = nome.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries).ToList();

            bool removeu = true;
            while (removeu && tokens.Count > 1)
            {
                removeu = false;
                string ultimo = tokens[tokens.Count - 1];
                string lower = ultimo.ToLowerInvariant();

                if (NoiseTokens.Contains(lower) || MultiRegex.IsMatch(lower) ||
                    lower == "dlc" || lower == "dlcs" || lower == "incl" || lower == "all")
                {
                    tokens.RemoveAt(tokens.Count - 1);
                    removeu = true;
                }
            }

            return string.Join(" ", tokens.ToArray()).Trim();
        }

        // Decide se os nomes de pastas/arquivos informados casam com algum padrão de save.
        // 'dirNames' e 'fileNames' devem conter apenas os nomes (não caminhos completos).
        // Lógica pura para permitir testes: quem enumera o disco é o plugin.
        public static bool MatchesSavePattern(IEnumerable<string> dirNames, IEnumerable<string> fileNames, IEnumerable<string> savePatterns)
        {
            if (savePatterns == null) return false;

            var extPatterns = new List<string>();
            var namePatterns = new List<string>();
            foreach (var p in savePatterns)
            {
                if (string.IsNullOrEmpty(p)) continue;
                string lp = p.Trim().ToLowerInvariant();
                if (lp.Length == 0) continue;
                if (lp[0] == '.') extPatterns.Add(lp);
                else namePatterns.Add(lp);
            }

            if (dirNames != null && namePatterns.Count > 0)
            {
                foreach (var d in dirNames)
                {
                    if (d == null) continue;
                    if (namePatterns.Contains(d.ToLowerInvariant())) return true;
                }
            }

            if (fileNames != null)
            {
                foreach (var f in fileNames)
                {
                    if (f == null) continue;
                    string lf = f.ToLowerInvariant();
                    string ext = Path.GetExtension(lf);
                    if (!string.IsNullOrEmpty(ext) && extPatterns.Contains(ext)) return true;

                    string baseName = Path.GetFileNameWithoutExtension(lf);
                    if (namePatterns.Contains(baseName)) return true;
                }
            }

            return false;
        }

        // ---------------------------------------------------------------------
        // Relocalização: reconhecer que uma pasta que "sumiu" apenas mudou de lugar
        // ---------------------------------------------------------------------
        //
        // Por que isto existe: até a versão 0.8.0 a extensão casava biblioteca × disco
        // SÓ por caminho exato. Pasta movida ou renomeada virava duas coisas erradas ao
        // mesmo tempo — um "jogo novo" para importar e um registro "ausente" oferecido
        // pré-marcado para desinstalar. E a busca do reparo, quando achava o executável
        // numa pasta cujo nome não batia com o nome do jogo nem com o da pasta antiga,
        // DESCARTAVA o acerto em silêncio. O resultado observado em biblioteca real: o
        // jogo estava no disco, foi encontrado, e mesmo assim voltou como "Não Encontrado".
        //
        // A regra nova é somar evidência em vez de exigir uma coincidência única, e
        // devolver junto o porquê do casamento — para a tela poder mostrar em que a
        // extensão se baseou, em vez de pedir fé.

        // Nome de executável que não identifica jogo nenhum: casar só por ele apontaria
        // qualquer pasta do disco. Vale como sinal fraco, nunca como prova.
        public static readonly string[] GenericExeNames =
        {
            "game", "start", "launcher", "launch", "play", "run", "main", "app",
            "client", "startup", "bin", "win64", "win32", "shipping"
        };

        public const int RelocationScoreAlta = 55;
        public const int RelocationScoreMinima = 35;

        public class RelocationMatch
        {
            public string PastaCandidata { get; set; }
            public string ExeCandidato { get; set; }
            public int Pontos { get; set; }
            public string Motivo { get; set; }
            public string Confianca { get; set; }   // "Alta" | "Média"
            // True só quando a evidência é forte o bastante para a linha já nascer marcada.
            public bool Confiavel { get { return Pontos >= RelocationScoreAlta; } }
        }

        public static bool IsGenericExeName(string exePath)
        {
            if (string.IsNullOrEmpty(exePath)) return true;
            string nome = Path.GetFileNameWithoutExtension(exePath);
            if (string.IsNullOrEmpty(nome)) return true;
            nome = nome.ToLowerInvariant();
            foreach (string g in GenericExeNames)
            {
                if (nome == g) return true;
            }
            return false;
        }

        // Reduz um nome de pasta/jogo à sua forma comparável: tira versão e grupo de
        // release (CleanGameName), acentos, pontuação e espaço repetido.
        // "Elden.Ring.v1.12-FitGirl Repack" e "Elden Ring" caem no mesmo texto.
        public static string SlugForMatch(string nome)
        {
            if (string.IsNullOrEmpty(nome)) return string.Empty;

            string limpo = CleanGameNameOnly(nome);
            if (string.IsNullOrEmpty(limpo)) limpo = nome;

            string semAcento = limpo.Normalize(NormalizationForm.FormD);
            var sb = new StringBuilder();
            foreach (char c in semAcento)
            {
                if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark) continue;
                if (char.IsLetterOrDigit(c)) sb.Append(char.ToLowerInvariant(c));
                else if (char.IsWhiteSpace(c)) sb.Append(' ');
                // pontuação some: "S.T.A.L.K.E.R." e "STALKER" viram o mesmo texto.
            }

            string saida = sb.ToString();
            while (saida.Contains("  ")) saida = saida.Replace("  ", " ");
            return saida.Trim();
        }

        private static string NomeDaPasta(string caminho)
        {
            if (string.IsNullOrEmpty(caminho)) return string.Empty;
            try { return new DirectoryInfo(caminho).Name; }
            catch (Exception) { return string.Empty; }
        }

        // Caminho do executável relativo à pasta do jogo ("bin\x64\jogo.exe"), em minúsculo.
        // Estrutura interna igual é sinal de que é a MESMA instalação, não outra cópia.
        private static string CaminhoRelativoDoExe(string pastaRaiz, string exePath)
        {
            if (string.IsNullOrEmpty(pastaRaiz) || string.IsNullOrEmpty(exePath)) return string.Empty;
            string raiz = NormalizePath(pastaRaiz);
            string exe = NormalizePath(exePath);
            string prefixo = raiz + Path.DirectorySeparatorChar.ToString();
            if (!exe.StartsWith(prefixo, StringComparison.OrdinalIgnoreCase)) return string.Empty;
            return exe.Substring(prefixo.Length);
        }

        /// <summary>
        /// Pontua a hipótese "esta pasta candidata é o jogo que sumiu daquela pasta antiga".
        /// Devolve null quando a evidência não chega ao mínimo — melhor não sugerir nada do
        /// que apontar o jogo errado. Nunca decide sozinha: quem chama mostra o Motivo.
        /// Tamanho de arquivo desconhecido entra como 0 e simplesmente não pontua.
        /// </summary>
        public static RelocationMatch AvaliarRelocalizacao(
            string nomeDoJogo,
            string pastaAntiga, string exeAntigo, long tamanhoExeAntigo,
            string pastaCandidata, string exeCandidato, long tamanhoExeCandidato)
        {
            if (string.IsNullOrEmpty(pastaCandidata)) return null;

            int pontos = 0;
            var motivos = new List<string>();

            // --- 1. Executável ---
            string nomeExeAntigo = string.IsNullOrEmpty(exeAntigo) ? string.Empty : Path.GetFileName(exeAntigo);
            string nomeExeCandidato = string.IsNullOrEmpty(exeCandidato) ? string.Empty : Path.GetFileName(exeCandidato);
            bool exeIgual = nomeExeAntigo.Length > 0 &&
                            nomeExeAntigo.Equals(nomeExeCandidato, StringComparison.OrdinalIgnoreCase);
            bool exeGenerico = IsGenericExeName(nomeExeAntigo);

            if (exeIgual)
            {
                if (exeGenerico)
                {
                    pontos += 15;
                    motivos.Add("mesmo executável, mas de nome genérico");
                }
                else
                {
                    pontos += 35;
                    motivos.Add("mesmo executável (" + nomeExeCandidato + ")");
                }
            }

            // --- 2. Tamanho do executável: é o sinal que distingue a MESMA cópia ---
            if (exeIgual && tamanhoExeAntigo > 0 && tamanhoExeAntigo == tamanhoExeCandidato)
            {
                pontos += 30;
                motivos.Add("arquivo do mesmo tamanho");
            }

            // --- 3. Estrutura interna ("bin\x64\jogo.exe" nos dois lados) ---
            string relAntigo = CaminhoRelativoDoExe(pastaAntiga, exeAntigo);
            string relCandidato = CaminhoRelativoDoExe(pastaCandidata, exeCandidato);
            if (relAntigo.Length > 0 && relAntigo.Equals(relCandidato, StringComparison.OrdinalIgnoreCase) &&
                relAntigo.IndexOf(Path.DirectorySeparatorChar) >= 0)
            {
                pontos += 10;
                motivos.Add("mesma estrutura interna de pastas");
            }

            // --- 4. Nome: vale o sinal MAIS FORTE, não a soma dos três ---
            string nomePastaAntiga = NomeDaPasta(pastaAntiga);
            string nomePastaCandidata = NomeDaPasta(pastaCandidata);
            string slugAntigo = SlugForMatch(nomePastaAntiga);
            string slugCandidato = SlugForMatch(nomePastaCandidata);
            string slugJogo = SlugForMatch(nomeDoJogo);

            int pontosNome = 0;
            string motivoNome = null;

            if (nomePastaCandidata.Length > 0 &&
                nomePastaCandidata.Equals(nomePastaAntiga, StringComparison.OrdinalIgnoreCase))
            {
                pontosNome = 25; motivoNome = "pasta com o mesmo nome de antes";
            }
            else if (slugCandidato.Length > 0 && slugCandidato == slugAntigo)
            {
                pontosNome = 20; motivoNome = "mesmo nome de pasta ignorando versão e grupo";
            }
            else if (slugCandidato.Length > 0 && slugCandidato == slugJogo)
            {
                pontosNome = 18; motivoNome = "pasta com o nome do jogo";
            }
            else if (slugJogo.Length >= 4 && slugCandidato.Length >= 4 &&
                     (slugCandidato.Contains(slugJogo) || slugJogo.Contains(slugCandidato)))
            {
                pontosNome = 10; motivoNome = "nome da pasta parecido com o do jogo";
            }

            if (pontosNome > 0)
            {
                pontos += pontosNome;
                motivos.Add(motivoNome);
            }

            if (pontos < RelocationScoreMinima) return null;

            var m = new RelocationMatch();
            m.PastaCandidata = pastaCandidata;
            m.ExeCandidato = exeCandidato;
            m.Pontos = pontos;
            m.Motivo = string.Join(" · ", motivos.ToArray());
            m.Confianca = pontos >= RelocationScoreAlta ? "Alta" : "Média";
            return m;
        }

        /// <summary>
        /// Reforça o candidato quando o executável do jogo perdido só existe em UMA pasta de
        /// todo o disco monitorado. Este é o sinal que salva o caso mais comum de pasta movida:
        /// o tamanho do arquivo antigo não pode ser comparado (ele sumiu junto com a pasta), e
        /// sem isto um jogo movido E renomeado ficaria eternamente em "Média", pedindo
        /// confirmação para algo que só tem uma resposta possível.
        /// Não vale para executável de nome genérico: "game.exe" único é coincidência, não prova.
        /// </summary>
        public static RelocationMatch ReforcarPorExclusividade(RelocationMatch match, string exeAntigo, int quantasPastasTemEsseExe)
        {
            if (match == null) return null;
            if (quantasPastasTemEsseExe != 1) return match;
            if (IsGenericExeName(exeAntigo)) return match;

            match.Pontos += 20;
            match.Motivo = match.Motivo + " · esse executável só existe nessa pasta em todo o disco monitorado";
            match.Confianca = match.Pontos >= RelocationScoreAlta ? "Alta" : "Média";
            return match;
        }

        /// <summary>
        /// Escolhe o melhor candidato de uma lista. Empate técnico (diferença menor que 10
        /// pontos entre os dois primeiros) REBAIXA a confiança para "Média": duas pastas
        /// igualmente plausíveis é exatamente o caso em que reapontar sozinho erraria.
        /// </summary>
        public static RelocationMatch MelhorCandidato(List<RelocationMatch> candidatos)
        {
            if (candidatos == null) return null;

            var ordenados = candidatos.Where(c => c != null).OrderByDescending(c => c.Pontos).ToList();
            if (ordenados.Count == 0) return null;

            RelocationMatch melhor = ordenados[0];
            if (ordenados.Count > 1 && (melhor.Pontos - ordenados[1].Pontos) < 10)
            {
                melhor.Confianca = "Média";
                melhor.Motivo = melhor.Motivo + " · outra pasta parecida também serve, confira antes";
                if (melhor.Pontos >= RelocationScoreAlta) melhor.Pontos = RelocationScoreAlta - 1;
            }
            return melhor;
        }
    }
}
