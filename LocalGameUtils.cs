using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

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
    }
}
