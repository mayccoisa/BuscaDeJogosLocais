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

        // ---- NormalizeGameName ----
        Console.WriteLine("[NormalizeGameName]");
        Eq("trim e minusculo", "cool game", LocalGameUtils.NormalizeGameName("  Cool Game "));
        Eq("colapsa espacos duplos", "cool game", LocalGameUtils.NormalizeGameName("Cool   Game"));
        Eq("null vira vazio", "", LocalGameUtils.NormalizeGameName(null));
        Check("mesmos nomes com caixa diferente sao iguais",
            LocalGameUtils.NormalizeGameName("The GAME") == LocalGameUtils.NormalizeGameName("the game"));

        // ---- MatchesSavePattern ----
        Console.WriteLine("[MatchesSavePattern]");
        var patterns = LocalGameUtils.DefaultSavePatterns;
        Check("pasta 'Saves' casa (case-insensitive)",
            LocalGameUtils.MatchesSavePattern(new List<string> { "bin", "Saves" }, new List<string>(), patterns));
        Check("arquivo .sav casa por extensao",
            LocalGameUtils.MatchesSavePattern(new List<string>(), new List<string> { "player1.SAV" }, patterns));
        Check("pasta comum nao casa",
            !LocalGameUtils.MatchesSavePattern(new List<string> { "bin", "data" }, new List<string> { "game.exe" }, patterns));
        Check("padroes null nao casa",
            !LocalGameUtils.MatchesSavePattern(new List<string> { "save" }, null, null));
        Check("padrao customizado por nome casa",
            LocalGameUtils.MatchesSavePattern(new List<string> { "MinhaPasta" }, new List<string>(), new List<string> { "minhapasta" }));
        Check("substring parcial NAO casa (match exato)",
            !LocalGameUtils.MatchesSavePattern(new List<string> { "savescreenshots" }, new List<string>(), new List<string> { "save" }));

        // ---- CleanGameName / ExtractVersion ----
        Console.WriteLine("[CleanGameName]");
        string v;
        Eq("tira versao e grupo", "Stardust Wish of Witch",
            LocalGameUtils.CleanGameName("STARDUST.Wish.of.Witch.v20260729-P2P", out v));
        Eq("versao de data extraida", "20260729", v);

        Eq("versao curta", "Starbites", LocalGameUtils.CleanGameName("STARBITES.v1.00.1-P2P", out v));
        Eq("versao curta extraida", "1.00.1", v);

        Eq("preserva hifen do titulo", "Pac-Man World 2 Re-Pac",
            LocalGameUtils.CleanGameName("PAC-MAN.WORLD.2.Re-PAC.v20260522-P2P", out v));

        Eq("grupo sem versao", "Forensics Crime Scene Detective",
            LocalGameUtils.CleanGameName("Forensics.Crime.Scene.Detective-GoldBerg", out v));
        Check("sem versao devolve null", v == null);

        Eq("versao com tres partes", "EverSiege Untold Ages",
            LocalGameUtils.CleanGameName("EverSiege.Untold.Ages.v1.2.4-P2P", out v));
        Eq("versao com tres partes extraida", "1.2.4", v);

        Eq("build vira versao", "Some Game",
            LocalGameUtils.CleanGameName("Some.Game.Build.14209871-TENOKE", out v));
        Eq("build extraido", "Build 14209871", v);

        Eq("update vira versao", "Another Game",
            LocalGameUtils.CleanGameName("Another.Game.v1.0.Update.3-CODEX", out v));
        Eq("versao com update", "1.0 Update 3", v);

        Eq("ruido de repack removido", "Cool Game",
            LocalGameUtils.CleanGameName("Cool.Game.MULTi9.Repack", out v));

        Eq("sigla com pontos preservada", "S.T.A.L.K.E.R. Shadow of Chernobyl",
            LocalGameUtils.CleanGameName("S.T.A.L.K.E.R..Shadow.of.Chernobyl", out v));

        Eq("nome ja limpo nao muda", "Hollow Knight",
            LocalGameUtils.CleanGameName("Hollow Knight", out v));
        Check("nome limpo nao inventa versao", v == null);

        Eq("hifen de titulo sem grupo conhecido fica", "Half-Life",
            LocalGameUtils.CleanGameName("Half-Life", out v));

        Eq("nome vazio devolve vazio", "", LocalGameUtils.CleanGameName("", out v));
        Eq("null devolve vazio", "", LocalGameUtils.CleanGameName(null, out v));

        Eq("underscore vira espaco", "Some Old Game",
            LocalGameUtils.CleanGameName("Some_Old_Game_v2.1-FLT", out v));

        // ---- ToTitleCase ----
        Console.WriteLine("[ToTitleCase]");
        Eq("caixa alta vira titulo", "Stardust Wish of Witch", LocalGameUtils.ToTitleCase("STARDUST WISH OF WITCH"));
        Eq("preposicao no meio fica minuscula", "Lord of the Rings", LocalGameUtils.ToTitleCase("LORD OF THE RINGS"));
        Eq("primeira palavra sempre maiuscula", "The Witcher", LocalGameUtils.ToTitleCase("the witcher"));
        Eq("numeral romano preservado", "Fable II", LocalGameUtils.ToTitleCase("FABLE II"));
        Eq("sigla pontuada preservada", "S.T.A.L.K.E.R. Shadow", LocalGameUtils.ToTitleCase("S.T.A.L.K.E.R. shadow"));
        Eq("hifen capitaliza os dois lados", "Pac-Man", LocalGameUtils.ToTitleCase("PAC-MAN"));
        Eq("token com digito fica intacto", "Need 4 Speed 2K25", LocalGameUtils.ToTitleCase("NEED 4 SPEED 2K25"));
        Eq("camelCase do titulo preservado", "EverSiege Untold Ages", LocalGameUtils.ToTitleCase("EverSiege Untold Ages"));
        Eq("null passa direto", null, LocalGameUtils.ToTitleCase(null));

        Console.WriteLine();
        Console.WriteLine(string.Format("Resultado: {0}/{1} passaram, {2} falha(s).", total - failures, total, failures));
        return failures == 0 ? 0 : 1;
    }
}
