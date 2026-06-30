using System;
using System.Collections.Generic;
using BuscaDeJogosLocais;

// Testes de regressão da lógica central de scan/normalização (LocalGameUtils).
// Compilado com csc.exe do .NET Framework (ver run-tests.ps1) — sem NuGet/SDK.
// Exit code 0 = tudo passou; 1 = houve falha.
class RegressionTests
{
    static int total = 0;
    static int failures = 0;

    static void Check(string name, bool condition)
    {
        total++;
        if (condition)
        {
            Console.WriteLine("  [PASS] " + name);
        }
        else
        {
            failures++;
            Console.WriteLine("  [FAIL] " + name);
        }
    }

    static void Eq(string name, string expected, string actual)
    {
        bool ok = string.Equals(expected, actual);
        Check(name + "  (esperado='" + (expected ?? "<null>") + "' | obtido='" + (actual ?? "<null>") + "')", ok);
    }

    static int Main()
    {
        Console.WriteLine("== Testes de regressao: logica de scan (LocalGameUtils) ==");
        Console.WriteLine();

        // ---- NormalizePath ----
        Console.WriteLine("[NormalizePath]");
        Eq("remove barra final e deixa minusculo", @"c:\games\foo", LocalGameUtils.NormalizePath(@"C:\Games\Foo\"));
        Eq("string vazia para entrada vazia", "", LocalGameUtils.NormalizePath(""));
        Eq("string vazia para null", "", LocalGameUtils.NormalizePath(null));
        Eq("barras duplicadas normalizadas igual ao caminho simples",
            LocalGameUtils.NormalizePath(@"C:\Games\Foo"),
            LocalGameUtils.NormalizePath(@"C:\Games\\Foo\"));

        // ---- IsExplosiveFile ----
        Console.WriteLine("[IsExplosiveFile]");
        Check("exe de jogo normal NAO e explosivo", !LocalGameUtils.IsExplosiveFile(@"C:\Games\Foo\Foo.exe"));
        Check("unins000.exe e explosivo", LocalGameUtils.IsExplosiveFile(@"C:\Games\Foo\unins000.exe"));
        Check("setup.exe e explosivo", LocalGameUtils.IsExplosiveFile(@"C:\Games\Foo\setup.exe"));
        Check("vcredist_x64.exe e explosivo", LocalGameUtils.IsExplosiveFile(@"C:\Games\Foo\vcredist_x64.exe"));
        Check("arquivo nao-exe e ignorado (explosivo)", LocalGameUtils.IsExplosiveFile(@"C:\Games\Foo\leiame.txt"));
        Check("SETUP.EXE em maiusculas tambem e explosivo", LocalGameUtils.IsExplosiveFile(@"C:\Games\Foo\SETUP.EXE"));

        // ---- GetGameRoot ----
        Console.WriteLine("[GetGameRoot]");
        var monitored = new List<string> { LocalGameUtils.NormalizePath(@"C:\Games") };

        string pai1;
        string root1 = LocalGameUtils.GetGameRoot(@"C:\Games\CoolGame\bin\game.exe", monitored, out pai1);
        Eq("raiz do jogo em subpasta profunda",
            LocalGameUtils.NormalizePath(@"C:\Games\CoolGame"), LocalGameUtils.NormalizePath(root1));
        Eq("pasta monitorada pai identificada",
            LocalGameUtils.NormalizePath(@"C:\Games"), LocalGameUtils.NormalizePath(pai1));

        string pai2;
        string root2 = LocalGameUtils.GetGameRoot(@"C:\Games\DirectGame\game.exe", monitored, out pai2);
        Eq("raiz do jogo direto sob a pasta monitorada",
            LocalGameUtils.NormalizePath(@"C:\Games\DirectGame"), LocalGameUtils.NormalizePath(root2));

        string pai3;
        string root3 = LocalGameUtils.GetGameRoot(@"D:\Other\Game\game.exe", monitored, out pai3);
        Check("fora de qualquer pasta monitorada retorna null", root3 == null);
        Eq("pai padrao quando nao encontrado", "Desconhecida", pai3);

        string pai4;
        string root4 = LocalGameUtils.GetGameRoot(@"C:\Games\game.exe", null, out pai4);
        Check("lista de monitoradas null retorna null", root4 == null);

        // ---- IsPathExcluded ----
        Console.WriteLine("[IsPathExcluded]");
        var excl = new List<string> { @"C:\Games\Foo\Foo.exe" };
        Check("casa case-insensitive", LocalGameUtils.IsPathExcluded(@"c:\games\foo\foo.EXE", excl));
        Check("nao casa quando fora da lista", !LocalGameUtils.IsPathExcluded(@"C:\Games\Bar\Bar.exe", excl));
        Check("lista null nao exclui nada", !LocalGameUtils.IsPathExcluded(@"C:\x.exe", null));

        Console.WriteLine();
        Console.WriteLine(string.Format("Resultado: {0}/{1} passaram, {2} falha(s).", total - failures, total, failures));
        return failures == 0 ? 0 : 1;
    }
}
