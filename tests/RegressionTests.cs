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

        // ---- IsUnderFolder ----
        Console.WriteLine("[IsUnderFolder]");
        Check("subpasta direta casa", LocalGameUtils.IsUnderFolder(@"C:\Games\CoolGame", @"C:\Games"));
        Check("subpasta profunda casa", LocalGameUtils.IsUnderFolder(@"C:\Games\CoolGame\bin", @"C:\Games"));
        Check("a propria pasta casa", LocalGameUtils.IsUnderFolder(@"C:\Games", @"C:\Games\"));
        Check("prefixo parecido NAO casa", !LocalGameUtils.IsUnderFolder(@"C:\Games2\X", @"C:\Games"));
        Check("outra raiz nao casa", !LocalGameUtils.IsUnderFolder(@"D:\Games\X", @"C:\Games"));
        Check("null nao casa", !LocalGameUtils.IsUnderFolder(null, @"C:\Games"));

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

        // ---- EscolherNomeDeConsole ----
        Console.WriteLine("[EscolherNomeDeConsole]");
        Eq("perfil com um console manda", "Sony PlayStation 2",
            LocalGameUtils.EscolherNomeDeConsole(new List<string> { "Sony PlayStation 2" }, "Sony PlayStation 2"));

        Eq("sem perfil cai na plataforma do jogo", "Nintendo Switch",
            LocalGameUtils.EscolherNomeDeConsole(null, "Nintendo Switch"));

        Eq("perfil vazio cai na plataforma do jogo", "Nintendo Switch",
            LocalGameUtils.EscolherNomeDeConsole(new List<string>(), "Nintendo Switch"));

        Eq("perfil com varios consoles: plataforma do jogo desempata", "Nintendo Game Boy Color",
            LocalGameUtils.EscolherNomeDeConsole(
                new List<string> { "Nintendo Game Boy", "Nintendo Game Boy Color" }, "Nintendo Game Boy Color"));

        Eq("perfil com varios consoles e jogo sem plataforma: fica o primeiro", "Nintendo Game Boy",
            LocalGameUtils.EscolherNomeDeConsole(
                new List<string> { "Nintendo Game Boy", "Nintendo Game Boy Color" }, ""));

        Eq("perfil manda quando o jogo esta sem plataforma", "Sony PlayStation",
            LocalGameUtils.EscolherNomeDeConsole(new List<string> { "Sony PlayStation" }, null));

        Eq("nada de nada devolve vazio (nunca inventa console)", "",
            LocalGameUtils.EscolherNomeDeConsole(null, null));

        Eq("entrada so com espaco em branco nao vira console", "",
            LocalGameUtils.EscolherNomeDeConsole(new List<string> { "   " }, "  "));

        Eq("espaco em volta e aparado", "Sony PSP",
            LocalGameUtils.EscolherNomeDeConsole(new List<string> { "  Sony PSP  " }, null));


        // ---- SlugForMatch ----
        Console.WriteLine("[SlugForMatch]");
        Eq("versao e grupo somem", "elden ring", LocalGameUtils.SlugForMatch("Elden.Ring.v1.12-FitGirl"));
        Eq("pontuacao de sigla some", "stalker", LocalGameUtils.SlugForMatch("S.T.A.L.K.E.R."));
        Eq("acento some", "pokemon x", LocalGameUtils.SlugForMatch("Pokémon X"));
        Eq("vazio para null", "", LocalGameUtils.SlugForMatch(null));

        // ---- IsGenericExeName ----
        Console.WriteLine("[IsGenericExeName]");
        Check("game.exe e generico", LocalGameUtils.IsGenericExeName(@"D:\Jogos\Foo\game.exe"));
        Check("start.exe e generico", LocalGameUtils.IsGenericExeName("start.exe"));
        Check("EldenRing.exe NAO e generico", !LocalGameUtils.IsGenericExeName(@"D:\Jogos\Foo\EldenRing.exe"));

        // ---- AvaliarRelocalizacao ----
        Console.WriteLine("[AvaliarRelocalizacao]");

        // O caso que motivou tudo: a pasta mudou de lugar E de nome, mas os arquivos sao os mesmos.
        // Antes disso a extensao descartava esse acerto e devolvia "Nao Encontrado" pre-marcado
        // para desinstalar.
        var movidoERenomeado = LocalGameUtils.AvaliarRelocalizacao(
            "Elden Ring",
            @"D:\Jogos\Elden.Ring.v1.02-CODEX", @"D:\Jogos\Elden.Ring.v1.02-CODEX\eldenring.exe", 51200,
            @"E:\Games\ER Definitivo", @"E:\Games\ER Definitivo\eldenring.exe", 51200);
        Check("pasta movida e renomeada e reconhecida", movidoERenomeado != null);
        Check("pasta movida e renomeada tem confianca Alta",
            movidoERenomeado != null && movidoERenomeado.Confiavel);
        Check("o motivo cita o executavel",
            movidoERenomeado != null && movidoERenomeado.Motivo.Contains("eldenring.exe"));
        Check("o motivo cita o tamanho",
            movidoERenomeado != null && movidoERenomeado.Motivo.Contains("mesmo tamanho"));

        // Mesmo nome de pasta noutro disco: nao precisa de tamanho para ser confiavel.
        var soMovido = LocalGameUtils.AvaliarRelocalizacao(
            "Hades",
            @"D:\Jogos\Hades", @"D:\Jogos\Hades\Hades.exe", 0,
            @"E:\Jogos\Hades", @"E:\Jogos\Hades\Hades.exe", 0);
        Check("pasta so movida e confiavel", soMovido != null && soMovido.Confiavel);

        // Versao/grupo diferentes no nome da pasta continuam sendo o mesmo jogo.
        var repackTrocado = LocalGameUtils.AvaliarRelocalizacao(
            "Stardust Wish of Witch",
            @"D:\Jogos\STARDUST.Wish.of.Witch.v20260729-P2P", @"D:\Jogos\STARDUST.Wish.of.Witch.v20260729-P2P\stardust.exe", 0,
            @"D:\Jogos\STARDUST.Wish.of.Witch.v20260801-TENOKE", @"D:\Jogos\STARDUST.Wish.of.Witch.v20260801-TENOKE\stardust.exe", 0);
        Check("mesma pasta com versao nova continua sendo o jogo",
            repackTrocado != null && repackTrocado.Confiavel);

        // Executavel de nome generico, sozinho, NAO aponta jogo nenhum.
        var soGenerico = LocalGameUtils.AvaliarRelocalizacao(
            "Jogo Um",
            @"D:\Jogos\Jogo Um", @"D:\Jogos\Jogo Um\game.exe", 0,
            @"D:\Jogos\Outra Coisa Totalmente Diferente", @"D:\Jogos\Outra Coisa Totalmente Diferente\game.exe", 0);
        Check("game.exe sozinho nao vira candidato", soGenerico == null);

        // ...mas com o nome batendo, vale como Media (mostra, nao decide sozinho).
        var genericoComNome = LocalGameUtils.AvaliarRelocalizacao(
            "Jogo Um",
            @"D:\Jogos\Jogo Um", @"D:\Jogos\Jogo Um\game.exe", 0,
            @"E:\Jogos\Jogo Um", @"E:\Jogos\Jogo Um\game.exe", 0);
        Check("game.exe com pasta de mesmo nome vira candidato", genericoComNome != null);
        Check("...mas so com confianca Media",
            genericoComNome != null && !genericoComNome.Confiavel);

        // Nada em comum: nao inventa candidato.
        var nadaAVer = LocalGameUtils.AvaliarRelocalizacao(
            "Hollow Knight",
            @"D:\Jogos\Hollow Knight", @"D:\Jogos\Hollow Knight\hollow_knight.exe", 1024,
            @"E:\Jogos\Celeste", @"E:\Jogos\Celeste\Celeste.exe", 4096);
        Check("pasta sem relacao nao vira candidato", nadaAVer == null);

        // Tamanho diferente derruba a confianca, mas o candidato continua visivel.
        var mesmoExeOutroTamanho = LocalGameUtils.AvaliarRelocalizacao(
            "Foo Bar",
            @"D:\Jogos\Foo Bar", @"D:\Jogos\Foo Bar\foobar.exe", 1000,
            @"E:\Jogos\Baz Qux", @"E:\Jogos\Baz Qux\foobar.exe", 9999);
        Check("mesmo exe com tamanho diferente ainda aparece", mesmoExeOutroTamanho != null);
        Check("...com confianca Media, nao Alta",
            mesmoExeOutroTamanho != null && !mesmoExeOutroTamanho.Confiavel);

        // ---- ReforcarPorExclusividade ----
        // O caso REAL de pasta movida: o exe antigo sumiu junto com a pasta, entao o tamanho
        // nao pode ser comparado. Sobra o nome do executavel -- que, sendo unico no disco
        // monitorado, e resposta unica e nao palpite.
        Console.WriteLine("[ReforcarPorExclusividade]");
        var semTamanho = LocalGameUtils.AvaliarRelocalizacao(
            "Elden Ring",
            @"D:\Jogos\Elden.Ring.v1.02-CODEX", @"D:\Jogos\Elden.Ring.v1.02-CODEX\eldenring.exe", 0,
            @"E:\Games\ER Definitivo", @"E:\Games\ER Definitivo\eldenring.exe", 0);
        Check("movido+renomeado sem tamanho aparece como Media", semTamanho != null && !semTamanho.Confiavel);
        var reforcado = LocalGameUtils.ReforcarPorExclusividade(
            semTamanho, @"D:\Jogos\Elden.Ring.v1.02-CODEX\eldenring.exe", 1);
        Check("exe unico no disco eleva para Alta", reforcado != null && reforcado.Confiavel);
        Check("o motivo explica a exclusividade",
            reforcado != null && reforcado.Motivo.Contains("s\u00f3 existe nessa pasta"));

        var duasPastas = LocalGameUtils.ReforcarPorExclusividade(
            LocalGameUtils.AvaliarRelocalizacao("Elden Ring",
                @"D:\A\Elden Ring", @"D:\A\Elden Ring\eldenring.exe", 0,
                @"E:\B\Outro Nome", @"E:\B\Outro Nome\eldenring.exe", 0),
            @"D:\A\Elden Ring\eldenring.exe", 2);
        Check("exe em duas pastas nao ganha reforco", duasPastas != null && !duasPastas.Confiavel);

        var genericoUnico = LocalGameUtils.ReforcarPorExclusividade(
            LocalGameUtils.AvaliarRelocalizacao("Jogo Um",
                @"D:\Jogos\Jogo Um", @"D:\Jogos\Jogo Um\game.exe", 0,
                @"E:\Jogos\Jogo Um", @"E:\Jogos\Jogo Um\game.exe", 0),
            @"D:\Jogos\Jogo Um\game.exe", 1);
        Check("game.exe unico nao vira prova", genericoUnico != null && !genericoUnico.Confiavel);
        Check("reforco em null continua null",
            LocalGameUtils.ReforcarPorExclusividade(null, "x.exe", 1) == null);

        // ---- MelhorCandidato ----
        Console.WriteLine("[MelhorCandidato]");
        Check("lista vazia devolve null", LocalGameUtils.MelhorCandidato(new List<LocalGameUtils.RelocationMatch>()) == null);
        Check("lista null devolve null", LocalGameUtils.MelhorCandidato(null) == null);

        var a = new LocalGameUtils.RelocationMatch { PastaCandidata = @"E:\A", Pontos = 90, Motivo = "x", Confianca = "Alta" };
        var b = new LocalGameUtils.RelocationMatch { PastaCandidata = @"E:\B", Pontos = 40, Motivo = "y", Confianca = "Média" };
        var vencedorFolgado = LocalGameUtils.MelhorCandidato(new List<LocalGameUtils.RelocationMatch> { b, a });
        Check("o de mais pontos vence", vencedorFolgado != null && vencedorFolgado.PastaCandidata == @"E:\A");
        Check("vencedor folgado mantem a confianca", vencedorFolgado != null && vencedorFolgado.Confiavel);

        // Empate tecnico: duas pastas igualmente plausiveis nao podem ser reapontadas sozinhas.
        var c1 = new LocalGameUtils.RelocationMatch { PastaCandidata = @"E:\A", Pontos = 70, Motivo = "x", Confianca = "Alta" };
        var c2 = new LocalGameUtils.RelocationMatch { PastaCandidata = @"E:\B", Pontos = 65, Motivo = "y", Confianca = "Alta" };
        var empate = LocalGameUtils.MelhorCandidato(new List<LocalGameUtils.RelocationMatch> { c1, c2 });
        Check("empate tecnico derruba para Media", empate != null && !empate.Confiavel);
        Check("empate tecnico avisa no motivo", empate != null && empate.Motivo.Contains("confira antes"));

        Console.WriteLine();
        Console.WriteLine(string.Format("Resultado: {0}/{1} passaram, {2} falha(s).", total - failures, total, failures));
        return failures == 0 ? 0 : 1;
    }
}
