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

        // ---- Nome de executavel sozinho nao aponta jogo (bug do lancador de DRM) ----
        // "Sword Art Online Fractured Daydream" foi dado como movido para a pasta de
        // "MARVEL Tokon Fighting Souls" porque os dois usam start_protected_game.exe (Denuvo).
        // Nome de arquivo repetido no disco inteiro e coincidencia de embalagem, nao prova.
        Console.WriteLine("[So o nome do executavel nao basta]");

        Check("start_protected_game e tratado como nome generico",
            LocalGameUtils.IsGenericExeName(@"E:\Jogos\X\start_protected_game.exe"));
        Check("gamelaunchhelper e tratado como nome generico",
            LocalGameUtils.IsGenericExeName(@"E:\Jogos\X\gamelaunchhelper.exe"));

        var lancadorDeDrm = LocalGameUtils.AvaliarRelocalizacao(
            "Sword Art Online Fractured Daydream",
            @"E:\SWORD-ART-ONLINE-Fractured-Daydream", @"E:\SWORD-ART-ONLINE-Fractured-Daydream\start_protected_game.exe", 0,
            @"J:\Games\MARVEL Tokon Fighting Souls", @"J:\Games\MARVEL Tokon Fighting Souls\start_protected_game.exe", 0);
        Check("mesmo lancador de DRM nao vira candidato", lancadorDeDrm == null);

        // Executavel proprio, mas nada mais casando: fica marcado para quem chama descartar.
        var soONomeDoExe = LocalGameUtils.AvaliarRelocalizacao(
            "Foo Bar",
            @"D:\Jogos\Foo Bar", @"D:\Jogos\Foo Bar\foobar.exe", 0,
            @"E:\Jogos\Baz Qux", @"E:\Jogos\Baz Qux\foobar.exe", 0);
        Check("mesmo exe sem mais nada e marcado como so-nome-do-exe",
            soONomeDoExe != null && soONomeDoExe.SomenteNomeDoExe);
        Check("exe unico no disco tira a marca de so-nome-do-exe",
            LocalGameUtils.ReforcarPorExclusividade(soONomeDoExe, @"D:\Jogos\Foo Bar\foobar.exe", 1).SomenteNomeDoExe == false);

        var exeMaisNome = LocalGameUtils.AvaliarRelocalizacao(
            "Hades",
            @"D:\Jogos\Hades", @"D:\Jogos\Hades\Hades.exe", 0,
            @"E:\Jogos\Hades", @"E:\Jogos\Hades\Hades.exe", 0);
        Check("exe com nome de pasta batendo nao e so-nome-do-exe",
            exeMaisNome != null && !exeMaisNome.SomenteNomeDoExe);

        // ---- PastaMonitoradaDe ----
        Console.WriteLine("[PastaMonitoradaDe]");
        var monitoradas = new List<string> { @"F:\Fighting", @"E:\Jogos", @"F:\Fighting\Indies" };
        Eq("acha a pasta monitorada do caminho", @"F:\Fighting",
            LocalGameUtils.PastaMonitoradaDe(@"F:\Fighting\Jump Force\JUMP_FORCE.exe", monitoradas));
        Eq("a pasta mais especifica vence", @"F:\Fighting\Indies",
            LocalGameUtils.PastaMonitoradaDe(@"F:\Fighting\Indies\Celeste", monitoradas));
        Check("caminho fora das monitoradas devolve null",
            LocalGameUtils.PastaMonitoradaDe(@"J:\Games\X", monitoradas) == null);
        Check("caminho vazio devolve null", LocalGameUtils.PastaMonitoradaDe("", monitoradas) == null);
        Check("lista null devolve null", LocalGameUtils.PastaMonitoradaDe(@"F:\Fighting\X", null) == null);

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


        // ---- Executavel que mudou dentro da MESMA pasta (bug relatado na 0.9.0) ----
        // O jogo continuava marcado como instalado apontando para um .exe inexistente, e o
        // Playnite falhava no Jogar. A pasta certa era descartada por estar "ocupada" pelo
        // proprio jogo, e nada pontuava o fato de a pasta ser a mesma.
        Console.WriteLine("[Executavel mudou na mesma pasta]");

        var mesmaPasta = LocalGameUtils.AvaliarRelocalizacao("Dark Souls III",
            @"D:\Jogos\Dark Souls III", @"D:\Jogos\Dark Souls III\DarkSoulsIII.exe", 0,
            @"D:\Jogos\Dark Souls III", @"D:\Jogos\Dark Souls III\Game\DS3.exe", 0);
        Check("mesma pasta com outro exe e reconhecida", mesmaPasta != null);
        Check("mesma pasta marca a flag", mesmaPasta != null && mesmaPasta.MesmaPasta);
        Check("mesma pasta tem confianca alta", mesmaPasta != null && mesmaPasta.Confiavel);
        Check("mesma pasta explica no motivo",
            mesmaPasta != null && mesmaPasta.Motivo.Contains("a pasta continua a mesma"));

        // Barra final e diferenca de caixa nao podem virar "outra pasta".
        var mesmaPastaBarra = LocalGameUtils.AvaliarRelocalizacao("Dark Souls III",
            @"D:\Jogos\Dark Souls III\", @"D:\Jogos\Dark Souls III\ds3.exe", 0,
            @"d:\jogos\dark souls iii", @"d:\jogos\dark souls iii\bin\ds3.exe", 0);
        Check("barra final e caixa nao criam pasta diferente",
            mesmaPastaBarra != null && mesmaPastaBarra.MesmaPasta);
        Check("EhMesmaPasta ignora barra final",
            LocalGameUtils.EhMesmaPasta(@"D:\A\B\", @"D:\A\B"));
        Check("EhMesmaPasta com null e false",
            !LocalGameUtils.EhMesmaPasta(null, @"D:\A"));
        Check("EhMesmaPasta distingue pastas de verdade",
            !LocalGameUtils.EhMesmaPasta(@"D:\A\B", @"D:\A\C"));

        // A propria pasta nao pode ser rebaixada por outra parecida no disco.
        var propria = new LocalGameUtils.RelocationMatch { PastaCandidata = @"D:\Jogos\X", Pontos = 65, Motivo = "m", Confianca = "Alta", MesmaPasta = true };
        var vizinha = new LocalGameUtils.RelocationMatch { PastaCandidata = @"E:\Jogos\X", Pontos = 63, Motivo = "n", Confianca = "Alta" };
        var vencedorPropria = LocalGameUtils.MelhorCandidato(new List<LocalGameUtils.RelocationMatch> { vizinha, propria });
        Check("pasta propria vence o empate tecnico",
            vencedorPropria != null && vencedorPropria.MesmaPasta && vencedorPropria.Confiavel);
        Check("pasta propria nao ganha aviso de empate",
            vencedorPropria != null && !vencedorPropria.Motivo.Contains("confira antes"));

        // ---- NaoEhOJogo / MelhorExeDaPasta ----
        Console.WriteLine("[Escolha do executavel dentro da pasta]");
        Check("unins000 nao e o jogo", LocalGameUtils.NaoEhOJogo(@"D:\J\unins000.exe"));
        Check("uninstall nao e o jogo", LocalGameUtils.NaoEhOJogo(@"D:\J\UnInstall.EXE"));
        Check("vcredist nao e o jogo", LocalGameUtils.NaoEhOJogo(@"D:\J\vcredist_x64.exe"));
        Check("dxsetup nao e o jogo", LocalGameUtils.NaoEhOJogo(@"D:\J\DXSETUP.exe"));
        Check("crash handler nao e o jogo", LocalGameUtils.NaoEhOJogo(@"D:\J\UnityCrashHandler64.exe"));
        Check("o jogo em si passa", !LocalGameUtils.NaoEhOJogo(@"D:\J\eldenring.exe"));
        Check("null nao e o jogo", LocalGameUtils.NaoEhOJogo(null));

        var pastaComLixo = new List<string> {
            @"D:\Jogos\Elden Ring\unins000.exe",
            @"D:\Jogos\Elden Ring\_CommonRedist\vcredist_x64.exe",
            @"D:\Jogos\Elden Ring\Game\eldenring.exe"
        };
        Check("o desinstalador nunca e proposto",
            LocalGameUtils.MelhorExeDaPasta("Elden Ring", @"D:\Jogos\Elden Ring", @"D:\Jogos\Elden Ring\eldenring.exe",
                @"D:\Jogos\Elden Ring", pastaComLixo) == @"D:\Jogos\Elden Ring\Game\eldenring.exe");

        Check("mesmo nome de arquivo ganha de tudo",
            LocalGameUtils.MelhorExeDaPasta("Jogo Qualquer", @"D:\A\Jogo", @"D:\A\Jogo\bin\alvo.exe",
                @"E:\B\Jogo", new List<string> { @"E:\B\Jogo\outro.exe", @"E:\B\Jogo\bin\alvo.exe" })
            == @"E:\B\Jogo\bin\alvo.exe");

        Check("sem o exe antigo vale o nome do jogo",
            LocalGameUtils.MelhorExeDaPasta("Hollow Knight", null, null,
                @"E:\B\HK", new List<string> { @"E:\B\HK\launcher.exe", @"E:\B\HK\hollow knight.exe" })
            == @"E:\B\HK\hollow knight.exe");

        Check("raiz ganha de exe enterrado quando o resto empata",
            LocalGameUtils.MelhorExeDaPasta("Nome Que Nao Casa", null, null,
                @"E:\B\J", new List<string> { @"E:\B\J\Engine\Binaries\Win64\tool.exe", @"E:\B\J\jogo.exe" })
            == @"E:\B\J\jogo.exe");

        Check("pasta so com ferramenta nao devolve nada",
            LocalGameUtils.MelhorExeDaPasta("Jogo", null, null,
                @"E:\B\J", new List<string> { @"E:\B\J\unins000.exe", @"E:\B\J\setup.exe" }) == null);
        Check("lista vazia de exe devolve null",
            LocalGameUtils.MelhorExeDaPasta("Jogo", null, null, @"E:\B\J", new List<string>()) == null);
        Check("lista null de exe devolve null",
            LocalGameUtils.MelhorExeDaPasta("Jogo", null, null, @"E:\B\J", null) == null);

        // ---- EhArquivoDeRom ----
        //
        // O critério tem que ser o MESMO que o Playnite usa para importar. Sendo mais frouxo, a
        // aba de emuladores acusa de "fora da biblioteca" arquivo que o Playnite nunca importaria
        // — e manda a pessoa procurar um problema que não existe.
        Console.WriteLine();
        Console.WriteLine("[EhArquivoDeRom]");

        var extPs2 = new List<string> { "iso", "chd", "bin" };

        Check("extensao declarada pelo perfil entra",
            LocalGameUtils.EhArquivoDeRom(@"D:\Roms\PS2\jogo.iso", extPs2));
        Check("o Playnite guarda a extensao SEM ponto, e com ponto tambem casa",
            LocalGameUtils.EhArquivoDeRom(@"D:\Roms\PS2\jogo.chd", new List<string> { ".chd" }));
        Check("maiuscula nao muda nada",
            LocalGameUtils.EhArquivoDeRom(@"D:\Roms\PS2\JOGO.ISO", extPs2));
        Check("fora da lista declarada NAO entra",
            !LocalGameUtils.EhArquivoDeRom(@"D:\Roms\PS2\capa.png", extPs2));
        Check("nem outra extensao de rom, se o perfil nao declarou",
            !LocalGameUtils.EhArquivoDeRom(@"D:\Roms\PS2\jogo.nsp", extPs2));

        // Sem extensão declarada a regra inverte: aceita tudo menos o lixo conhecido. É o único
        // caminho honesto — uma lista de permitidos escrita por mim erraria em todo console que
        // eu não conheço, e formatos de ROM são milhares.
        Check("sem perfil declarando, extensao de rom conhecida conta",
            LocalGameUtils.EhArquivoDeRom(@"D:\Roms\N64\jogo.z64", null));
        Check("sem perfil declarando, extensao desconhecida tambem conta",
            LocalGameUtils.EhArquivoDeRom(@"D:\Roms\X\jogo.formatoqueeunaoconheco", new List<string>()));
        Check("save nunca conta como rom",
            !LocalGameUtils.EhArquivoDeRom(@"D:\Roms\N64\jogo.sav", null));
        Check("imagem de capa nunca conta como rom",
            !LocalGameUtils.EhArquivoDeRom(@"D:\Roms\N64\capa.jpg", null));
        Check("o proprio emulador nunca conta como rom",
            !LocalGameUtils.EhArquivoDeRom(@"D:\Roms\N64\project64.exe", null));

        // "<none>" na definição do Playnite (Xenia) é arquivo sem extensão.
        var extXenia = new List<string> { "iso", "xex", "<none>" };
        Check("<none> aceita arquivo sem extensao",
            LocalGameUtils.EhArquivoDeRom(@"D:\Roms\X360\default", extXenia));
        Check("<none> nao vira extensao literal",
            !LocalGameUtils.EhArquivoDeRom(@"D:\Roms\X360\jogo.<none>", extXenia));

        // Perfil que importa por script (shadPS4, RPCS3): jogo e a pasta com o arquivo de boot;
        // o resto (.XVAG aos milhares) nao e jogo. Foi o que fez o shadPS4 acusar 44.978 "fora".
        var boot = new List<string>(LocalGameUtils.NomesDeBootConhecidos);
        Check("script: eboot.bin conta",
            LocalGameUtils.EhArquivoDeBoot(@"G:\PS4\Jogo\eboot.bin", boot));
        Check("script: maiuscula nao muda nada",
            LocalGameUtils.EhArquivoDeBoot(@"G:\PS3\Jogo\PS3_GAME\USRDIR\EBOOT.BIN", boot));
        Check("script: audio do jogo NAO conta",
            !LocalGameUtils.EhArquivoDeBoot(@"G:\PS4\Jogo\sound\ZM_SPAWNFIGHT.XVAG", boot));
        Check("script: sem lista de nomes nada conta",
            !LocalGameUtils.EhArquivoDeBoot(@"G:\PS4\Jogo\eboot.bin", new List<string>()));

        // Jogo de CD: o .cue e o jogo; os .bin por faixa sao dado + MUSICA. Contar as faixas
        // fazia o DuckStation acusar dez "jogos" por disco.
        Console.WriteLine();
        Console.WriteLine("[ArquivosSubordinados]");
        var arquivosCd = new List<string> {
            @"D:\Roms\PS1\Tekken 3.cue",
            @"D:\Roms\PS1\Tekken 3 (Track 01).bin",
            @"D:\Roms\PS1\Tekken 3 (Track 02).bin",
            @"D:\Roms\PS1\Outro.chd",
            @"D:\Roms\PS1\Colecao.m3u",
            @"D:\Roms\PS1\Disco2.chd"
        };
        Func<string, string[]> leitor = (caminho) =>
        {
            if (caminho.EndsWith(".cue")) return new[] {
                "FILE \"Tekken 3 (Track 01).bin\" BINARY",
                "  TRACK 01 MODE2/2352",
                "FILE \"Tekken 3 (Track 02).bin\" BINARY",
                "  TRACK 02 AUDIO" };
            if (caminho.EndsWith(".m3u")) return new[] { "# comentario", "Disco2.chd", "" };
            return null;
        };
        var sub = LocalGameUtils.ArquivosSubordinados(arquivosCd, leitor);
        Check("faixa de dado referenciada pelo cue e subordinada",
            sub.Contains(LocalGameUtils.NormalizePath(@"D:\Roms\PS1\Tekken 3 (Track 01).bin")));
        Check("faixa de audio referenciada pelo cue e subordinada",
            sub.Contains(LocalGameUtils.NormalizePath(@"D:\Roms\PS1\Tekken 3 (Track 02).bin")));
        Check("disco listado no m3u e subordinado",
            sub.Contains(LocalGameUtils.NormalizePath(@"D:\Roms\PS1\Disco2.chd")));
        Check("o proprio cue NAO e subordinado",
            !sub.Contains(LocalGameUtils.NormalizePath(@"D:\Roms\PS1\Tekken 3.cue")));
        Check("chd solto continua sendo jogo",
            !sub.Contains(LocalGameUtils.NormalizePath(@"D:\Roms\PS1\Outro.chd")));
        Check("leitor que falha nao derruba a regra",
            LocalGameUtils.ArquivosSubordinados(arquivosCd, (c) => null).Count == 0);
        Check("arquivo sem extensao nao conta",
            !LocalGameUtils.EhArquivoDeRom(@"D:\Roms\N64\LEIAME", null));
        Check("caminho vazio nao conta",
            !LocalGameUtils.EhArquivoDeRom("", extPs2));
        Check("caminho null nao conta",
            !LocalGameUtils.EhArquivoDeRom(null, extPs2));

        // ---- NomeDeRomParaExibicao ----
        Console.WriteLine();
        Console.WriteLine("[NomeDeRomParaExibicao]");

        Eq("tira so a extensao", "Chrono Cross (USA) (Disc 1)",
            LocalGameUtils.NomeDeRomParaExibicao(@"D:\Roms\PSX\Chrono Cross (USA) (Disc 1).chd"));
        // Região e revisão entre parênteses ficam: em nome de ROM isso é informação, e é o que
        // separa dois arquivos do mesmo jogo. Passar por uma limpeza de nome de repack
        // colapsaria os dois num nome só.
        Eq("regiao e revisao sobrevivem", "Zelda (USA) (Rev 1)",
            LocalGameUtils.NomeDeRomParaExibicao(@"D:\Roms\Zelda (USA) (Rev 1).z64"));
        Eq("ponto no meio do nome nao vira extensao", "Sonic 3 & Knuckles",
            LocalGameUtils.NomeDeRomParaExibicao(@"D:\Roms\Sonic 3 & Knuckles.md"));
        Eq("caminho vazio devolve vazio", "", LocalGameUtils.NomeDeRomParaExibicao(""));

        // ---- CaminhoParaAbrirNoExplorador ----
        //
        // O caso que importa é o que NÃO abre: pasta de HD desligado. Sem a checagem o Explorador
        // abre "Este Computador" e a pessoa conclui que o botão quebrou, em vez de descobrir que
        // o disco sumiu — que é a informação de verdade.
        Console.WriteLine();
        Console.WriteLine("[CaminhoParaAbrirNoExplorador]");

        string erroAbrir;
        Check("pasta que existe devolve caminho e nenhum erro",
            LocalGameUtils.CaminhoParaAbrirNoExplorador(Environment.GetFolderPath(Environment.SpecialFolder.System), out erroAbrir) != null
            && erroAbrir == null);

        Check("pasta inexistente devolve null",
            LocalGameUtils.CaminhoParaAbrirNoExplorador(@"Z:\PastaQueNaoExiste\Nunca", out erroAbrir) == null);
        Check("e o motivo fala em HD desligado",
            erroAbrir != null && erroAbrir.IndexOf("HD desligado", StringComparison.OrdinalIgnoreCase) >= 0);

        Check("caminho vazio devolve null com motivo",
            LocalGameUtils.CaminhoParaAbrirNoExplorador("", out erroAbrir) == null && !string.IsNullOrEmpty(erroAbrir));
        Check("caminho null devolve null com motivo",
            LocalGameUtils.CaminhoParaAbrirNoExplorador(null, out erroAbrir) == null && !string.IsNullOrEmpty(erroAbrir));
        Check("caminho invalido nao explode, devolve motivo",
            LocalGameUtils.CaminhoParaAbrirNoExplorador("::nao<>e|caminho", out erroAbrir) == null && !string.IsNullOrEmpty(erroAbrir));

        Console.WriteLine();
        Console.WriteLine(string.Format("Resultado: {0}/{1} passaram, {2} falha(s).", total - failures, total, failures));
        return failures == 0 ? 0 : 1;
    }
}
