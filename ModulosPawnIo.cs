using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;

namespace TranalSentinel;

/// <summary>O que fazer com um módulo PawnIO já existente no disco.</summary>
public enum AcaoModuloPawnIo
{
    /// <summary>Usar o arquivo que está no disco.</summary>
    UsarArquivoDoDisco,

    /// <summary>Gravar de novo no disco o módulo que vem embutido no executável.</summary>
    ExtrairEmbutido
}

/// <summary>
/// Regra única dos módulos PawnIO: onde eles ficam, quando o arquivo do disco
/// vale mais que o embutido e como o usuário troca a versão do módulo.
///
/// <para>
/// Por que o módulo vai para o disco: a LGPL-2.1 obriga que o usuário possa
/// trocar um arquivo binário distribuído por outro sem recompilar nada.
/// Por que fica uma cópia separada do executável: o TRANAL SENTINEL não
/// modifica os módulos, ele só os carrega no driver PawnIO.
/// </para>
/// </summary>
public static class ModulosPawnIo
{
    /// <summary>Recurso do módulo PawnIO do AMD FCH (AM4).</summary>
    public const string RecursoModuloAmd = "TranalSentinel.Assets.PawnIO.SmbusPIIX4.bin";

    /// <summary>Recurso do módulo PawnIO da Intel (ICH/PCH).</summary>
    public const string RecursoModuloIntel = "TranalSentinel.Assets.PawnIO.SmbusI801.bin";

    /// <summary>Recurso do texto da licença LGPL dos módulos.</summary>
    public const string RecursoLicenca = "TranalSentinel.Assets.PawnIO.COPYING";

    /// <summary>Recurso do aviso de origem e licença dos módulos.</summary>
    public const string RecursoAviso = "TranalSentinel.Assets.PawnIO.NOTICE.txt";

    /// <summary>Nome do arquivo de licença na pasta dos módulos.</summary>
    public const string NomeLicenca = "COPYING";

    /// <summary>Nome do arquivo de aviso na pasta dos módulos.</summary>
    public const string NomeAviso = "NOTICE.txt";

    /// <summary>Nome do arquivo que guarda o hash do que foi extraído.</summary>
    public const string NomeRegistroHashes = "modulos.hash";

    /// <summary>
    /// Pasta onde os módulos ficam extraídos:
    /// <c>%LOCALAPPDATA%\TranalSentinel\PawnIO</c>.
    /// </summary>
    public static string Pasta => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TranalSentinel", "PawnIO");

    /// <summary>Caminho de um arquivo dentro da pasta dos módulos.</summary>
    public static string Caminho(string nomeArquivo) => Path.Combine(Pasta, nomeArquivo);

    /// <summary>
    /// Devolve os bytes do módulo PawnIO a usar.
    ///
    /// <para>
    /// A ordem é sempre a mesma: primeiro o arquivo que está no disco (se o
    /// usuário trocou, quem vale é o dele), depois o embutido no executável.
    /// </para>
    /// </summary>
    /// <param name="nomeArquivo">Nome do arquivo na pasta dos módulos.</param>
    /// <param name="nomeRecurso">Nome do recurso embutido no executável.</param>
    public static byte[]? Obter(string nomeArquivo, string nomeRecurso)
    {
        byte[]? embutido = CarregarRecurso(nomeRecurso);
        if (embutido == null || embutido.Length == 0) return null;

        string pasta = Pasta;
        string caminho = Caminho(nomeArquivo);

        byte[]? doDisco = null;
        try
        {
            if (File.Exists(caminho))
            {
                byte[]? lido = File.ReadAllBytes(caminho);
                if (lido != null && lido.Length > 0) doDisco = lido;
            }
        }
        catch { }

        string? hashRegistrado = LerHashRegistrado(nomeArquivo);

        if (Decidir(doDisco, embutido, hashRegistrado) == AcaoModuloPawnIo.ExtrairEmbutido)
        {
            try
            {
                Directory.CreateDirectory(pasta);
                GravarArquivo(caminho, embutido);
                RegistrarHash(nomeArquivo, CalcularHash(embutido));
            }
            catch
            {
                // Sem permissão para gravar no disco: o embutido em memória serve.
            }

            ExtrairLicencaEAviso(pasta);
            return embutido;
        }

        ExtrairLicencaEAviso(pasta);
        return doDisco;
    }

    /// <summary>
    /// Decide o que fazer com o módulo. É a regra pura, sem disco: o que está
    /// no disco só vale mais quando o usuário mexeu nele.
    ///
    /// <para>
    /// O hash registrado é o do que foi extraído na última vez. Quando o
    /// arquivo do disco bate com ele, o arquivo é o nosso: se o executável
    /// trouxer outro módulo, reescrevemos. Quando não bate, o usuário trocou:
    /// o que está no disco vale mais e não é tocado.
    /// </para>
    /// </summary>
    /// <param name="emDisco">Conteúdo do arquivo na pasta dos módulos (null se não existe).</param>
    /// <param name="embutido">Conteúdo do módulo que vem no executável.</param>
    /// <param name="hashRegistrado">Hash do último módulo extraído (null se nunca extraído).</param>
    public static AcaoModuloPawnIo Decidir(byte[]? emDisco, byte[] embutido, string? hashRegistrado)
    {
        ArgumentNullException.ThrowIfNull(embutido);

        if (emDisco == null || emDisco.Length == 0)
            return AcaoModuloPawnIo.ExtrairEmbutido;

        string hashDisco = CalcularHash(emDisco);

        // Nunca foi extraído por nós (ou o registro sumiu): o que está no
        // disco é do usuário e manda.
        if (string.IsNullOrEmpty(hashRegistrado))
            return AcaoModuloPawnIo.UsarArquivoDoDisco;

        // O usuário trocou o arquivo: vale o que está no disco.
        if (!string.Equals(hashDisco, hashRegistrado, StringComparison.OrdinalIgnoreCase))
            return AcaoModuloPawnIo.UsarArquivoDoDisco;

        // É o nosso arquivo: só reescreve quando o executável trouxe outro módulo.
        return string.Equals(hashDisco, CalcularHash(embutido), StringComparison.OrdinalIgnoreCase)
            ? AcaoModuloPawnIo.UsarArquivoDoDisco
            : AcaoModuloPawnIo.ExtrairEmbutido;
    }

    /// <summary>Hash SHA-256 de um conteúdo, em hexadecimal.</summary>
    public static string CalcularHash(byte[] dados)
    {
        ArgumentNullException.ThrowIfNull(dados);
        return Convert.ToHexString(SHA256.HashData(dados));
    }

    /// <summary>Carrega um recurso embutido no executável.</summary>
    private static byte[]? CarregarRecurso(string nome)
    {
        Assembly assembly = typeof(ModulosPawnIo).Assembly;

        using Stream? fluxo = assembly.GetManifestResourceStream(nome);
        if (fluxo == null) return null;

        using var memoria = new MemoryStream();
        fluxo.CopyTo(memoria);
        return memoria.ToArray();
    }

    /// <summary>Escreve um arquivo de forma segura (temporário e renomeia).</summary>
    private static void GravarArquivo(string caminho, byte[] dados)
    {
        string temporario = caminho + ".tmp";
        File.WriteAllBytes(temporario, dados);
        File.Move(temporario, caminho, overwrite: true);
    }

    /// <summary>Deixa a licença e o aviso junto dos módulos (a LGPL exige).</summary>
    private static void ExtrairLicencaEAviso(string pasta)
    {
        GravarRecursoSeFaltar(pasta, NomeLicenca, RecursoLicenca);
        GravarRecursoSeFaltar(pasta, NomeAviso, RecursoAviso);
    }

    /// <summary>Grava um recurso embutido no disco, só se o arquivo ainda não existir.</summary>
    private static void GravarRecursoSeFaltar(string pasta, string nomeArquivo, string nomeRecurso)
    {
        try
        {
            string destino = Path.Combine(pasta, nomeArquivo);
            if (File.Exists(destino)) return;

            byte[]? dados = CarregarRecurso(nomeRecurso);
            if (dados == null || dados.Length == 0) return;

            Directory.CreateDirectory(pasta);
            GravarArquivo(destino, dados);
        }
        catch { }
    }

    /// <summary>Hash registrado do que foi extraído para um módulo (null se não houver).</summary>
    private static string? LerHashRegistrado(string nomeArquivo)
    {
        foreach (KeyValuePair<string, string> par in LerHashes())
        {
            if (string.Equals(par.Key, nomeArquivo, StringComparison.OrdinalIgnoreCase))
                return par.Value;
        }

        return null;
    }

    /// <summary>Registro de hashes do que foi extraído (um por módulo).</summary>
    private static Dictionary<string, string> LerHashes()
    {
        var mapa = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        try
        {
            string arquivo = Path.Combine(Pasta, NomeRegistroHashes);
            if (!File.Exists(arquivo)) return mapa;

            foreach (string linha in File.ReadAllLines(arquivo))
            {
                int separador = linha.IndexOf('=');
                if (separador <= 0) continue;
                mapa[linha[..separador].Trim()] = linha[(separador + 1)..].Trim();
            }
        }
        catch { }

        return mapa;
    }

    /// <summary>Registra o hash do módulo que acabou de ser extraído.</summary>
    private static void RegistrarHash(string nomeArquivo, string hash)
    {
        try
        {
            Dictionary<string, string> mapa = LerHashes();
            mapa[nomeArquivo] = hash;

            var linhas = new List<string>();
            foreach (KeyValuePair<string, string> par in mapa)
                linhas.Add($"{par.Key}={par.Value}");

            Directory.CreateDirectory(Pasta);
            string caminho = Path.Combine(Pasta, NomeRegistroHashes);
            File.WriteAllText(caminho, string.Join(Environment.NewLine, linhas) + Environment.NewLine,
                              new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        }
        catch { }
    }
}
