using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

namespace TranalSentinel;

/// <summary>
/// Le os registradores do System Management Network (SMN) do AMD pelo módulo
/// PawnIO <c>AMDFamily17</c>.
///
/// <para>
/// O módulo é do projeto PawnIO.Modules e conversa com o processador pelo
/// barramento de configuração do FCH, exatamente como o ryzen_smu faz no
/// Linux. Ele cobre as famílias 17h a 1Ah (Zen 2, Zen 3 e Zen 4).
/// </para>
/// <para>
/// <b>Esta classe só lê.</b> O módulo também exporta funções de escrita
/// (<c>ioctl_write_msr</c> e afins); nenhuma delas é usada aqui, em lugar
/// nenhum. Nenhum registro do processador é alterado por esta feature.
/// </para>
/// <para>
/// Cada handle do driver PawnIO guarda o seu próprio módulo carregado, então
/// o leitor de SMN convive com o leitor de SMBus: cada um abre a sua própria
/// conexão e os dois ficam ativos ao mesmo tempo.
/// </para>
/// </remarks>
public sealed class SmnPawnIo : IDisposable
{
    /// <summary>
    /// Mutex que serializa o acesso ao SMN entre programas. O próprio módulo
    /// avisa que ele deve ser usado sob esse mutex, porque o acesso ao
    /// barramento de configuração é global na máquina.
    /// </summary>
    internal const string MutexSmn = @"Global\Access_PCI";

    private readonly IPawnIoTransporte _transporte;
    private readonly Mutex? _mutex;
    private bool _descartado;

    private SmnPawnIo(IPawnIoTransporte transporte, Mutex? mutex)
    {
        _transporte = transporte;
        _mutex = mutex;
    }

    /// <summary>
    /// Abre o acesso ao SMN com o módulo do AMD. Devolve null quando o driver
    /// não está instalado ou quando este processador não é compatível.
    /// </summary>
    /// <param name="modulo">Conteúdo do módulo AMDFamily17.bin (null quando não está disponível).</param>
    /// <param name="erro">Motivo da falha, quando não deu para abrir.</param>
    public static SmnPawnIo? Abrir(byte[]? modulo, out string? erro)
    {
        erro = null;

        if (modulo == null || modulo.Length == 0)
        {
            erro = "O módulo PawnIO do AMD não está embutido nesta instalação.";
            return null;
        }

        var transporte = new PawnIoTransporte();
        if (!transporte.Abrir())
        {
            transporte.Dispose();
            erro = "O driver PawnIO não está instalado neste computador.";
            return null;
        }

        if (!transporte.CarregarModulo(modulo))
        {
            // O módulo recusou: o processador não é de uma família que ele
            // conheça (o módulo cobre 17h a 1Ah).
            transporte.Dispose();
            erro = "Este processador não é compatível com a leitura de SMN.";
            return null;
        }

        return new SmnPawnIo(transporte, CriarMutex());
    }

    /// <summary>
    /// Lê um registrador do SMN.
    /// </summary>
    /// <param name="endereco">Endereço do registrador (por exemplo 0x50200).</param>
    /// <param name="valor">Valor lido.</param>
    public bool Ler(uint endereco, out uint valor)
    {
        uint lido = 0;

        bool ok = ComTrava(() =>
        {
            if (!_transporte.Executar("ioctl_read_smn", new long[] { endereco }, 1, out long[] saida))
            {
                return false;
            }

            lido = (uint)saida[0];
            return true;
        });

        valor = lido;
        return ok;
    }

    /// <summary>
    /// Lê vários registradores de uma vez, sob uma única aquisição do mutex.
    ///
    /// <para>
    /// Não é só eficiência: a leitura de um registrador do SMN são duas
    /// transações PCI separadas (escrever o endereço no índice e ler o dado).
    /// Se o mutex fosse tomado a cada registrador, outra ferramenta poderia
    /// trocar o índice no meio da leitura — e o app juntaria o clock de um
    /// instante com os tempos de outro.
    /// </para>
    /// </summary>
    /// <param name="enderecos">Endereços a ler.</param>
    /// <param name="valores">Onde cada valor lido é guardado.</param>
    public bool LerBloco(IEnumerable<uint> enderecos, IDictionary<uint, uint> valores)
    {
        ArgumentNullException.ThrowIfNull(enderecos);
        ArgumentNullException.ThrowIfNull(valores);

        return ComTrava(() =>
        {
            foreach (uint endereco in enderecos)
            {
                if (!_transporte.Executar("ioctl_read_smn", new long[] { endereco }, 1, out long[] saida))
                {
                    valores[endereco] = RegistrosSmn.SemDado;
                    continue;
                }

                valores[endereco] = (uint)saida[0];
            }

            return true;
        });
    }

    /// <summary>Executa uma ação protegida pelo mutex do SMN.</summary>
    public T ComTrava<T>(Func<T> acao)
    {
        // Sem mutex (por falta de permissão para criá-lo), seguimos assim
        // mesmo: o pior que acontece é uma leitura cruzar com a de outro
        // programa, o que devolve um valor antigo em vez de um errado.
        if (_mutex == null) return acao();

        bool entrou = false;
        try
        {
            try
            {
                // O mutex pode estar abandonado por outra ferramenta: nesse
                // caso o Windows entrega a posse a quem espera, e ela tem que
                // ser devolvida no finally — senão este thread fica dono do
                // mutex e trava as outras ferramentas.
                entrou = _mutex.WaitOne(TimeSpan.FromSeconds(5));
            }
            catch (AbandonedMutexException)
            {
                entrou = true;
            }

            return acao();
        }
        finally
        {
            if (entrou) _mutex.ReleaseMutex();
        }
    }

    public void Dispose()
    {
        if (_descartado) return;
        _descartado = true;

        _mutex?.Dispose();
        _transporte.Dispose();
    }

    /// <summary>
    /// Cria o mutex do SMN. Quando o sistema recusa a criação (permissão),
    /// o app segue sem ele — melhor ler do que não ler.
    /// </summary>
    private static Mutex? CriarMutex()
    {
        try
        {
            return new Mutex(false, MutexSmn);
        }
        catch (UnauthorizedAccessException)
        {
            return null;
        }
        catch (IOException)
        {
            return null;
        }
    }
}
