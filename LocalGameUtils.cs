using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    }
}
