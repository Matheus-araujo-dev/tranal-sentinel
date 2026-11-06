using System;

namespace TranalSentinel;

/// <summary>
/// Amostra a janela em frente de tempos em tempos e registra a troca de
/// programa.
///
/// <para>
/// O evento registrado é a TROCA de programa, não cada instante: trocar de
/// aba do navegador a cada segundo não interessa, e o que interessa é
/// "quanto tempo o chrome ficou em frente". A duração de cada uso é a
/// distância até a próxima troca.
/// </para>
///
/// <para>
/// A leitura da janela em frente é barata (uma chamada de API), mas não é
/// de graça: por isso o amostrador só olha a tela de <see cref="Intervalo"/>
/// em <see cref="Intervalo"/>, no máximo uma vez por intervalo.
/// </para>
/// </summary>
public static class UsoAmostrador
{
    /// <summary>De quanto em quanto tempo a janela em frente é olhada.</summary>
    public static TimeSpan Intervalo => TimeSpan.FromSeconds(5);

    private static readonly object Tranca = new();

    private static DateTime _ultimaLeitura = DateTime.MinValue;
    private static string _ultimoProcesso = "";

    /// <summary>
    /// Olha a janela em frente e registra a troca, se houve.
    ///
    /// <para>
    /// Nunca lança: o histórico de uso é um extra e não pode derrubar o
    /// app.
    /// </para>
    /// </summary>
    public static void Amostrar(DateTime agora)
    {
        try
        {
            lock (Tranca)
            {
                if (agora - _ultimaLeitura < Intervalo) return;
                _ultimaLeitura = agora;
            }

            var troca = UsoJanela.LerEmDestaque(agora);
            if (troca is null) return;

            lock (Tranca)
            {
                // Mesma janela em frente: não é troca, é a mesma sessão
                // continuando.
                if (string.Equals(_ultimoProcesso, troca.Processo, StringComparison.OrdinalIgnoreCase))
                    return;

                _ultimoProcesso = troca.Processo;
            }

            UsoHistorico.Gravar(troca);
        }
        catch
        {
            // Janela protegida, arquivo trancado, disco cheio: o histórico de
            // uso é acessório e não pode derrubar o app.
        }
    }

    /// <summary>
    /// Esquece o programa atual. Só os testes usam isto: sem isso, um teste
    /// que rode depois de outro não registraria a troca.
    /// </summary>
    internal static void ZerarEstado()
    {
        lock (Tranca)
        {
            _ultimaLeitura = DateTime.MinValue;
            _ultimoProcesso = "";
        }
    }
}
