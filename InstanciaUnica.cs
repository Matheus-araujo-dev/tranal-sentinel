using System.Threading;

namespace TranalSentinel;

/// <summary>
/// A trava de instância única do TRANAL SENTINEL: só um app pode ficar
/// rodando por vez na máquina.
///
/// <para>
/// A regra mora aqui — em um lugar só — porque duas coisas precisam dela:
/// o app (que não sobe duas vezes) e a linha de comando do banco (que só
/// repara o histórico com o app fechado).
///
/// <para>
/// O nome do mutex e o <c>Local\</c> (a sessão do Windows) são a mesma
/// coisa que a regra do AGENTS.md manda usar: <c>Local\TranalSentinel.SingleInstance</c>.
/// </para>
/// </summary>
public static class InstanciaUnica
{
    /// <summary>
    /// O nome do mutex. É o mesmo em toda parte — o app, a linha de
    /// comando e a documentação.
    /// </summary>
    public const string Nome = @"Local\TranalSentinel.SingleInstance";

    /// <summary>
    /// Segura a trava de instância única.
    ///
    /// <para>
    /// Devolve o mutex quando esta é a primeira instância (e o app pode
    /// subir), e null quando já existe outra rodando (o app não sobe e
    /// apenas traz a janela da outra para a frente).
    /// </para>
    /// </summary>
    public static Mutex? Segurar()
    {
        Mutex? trava = null;

        try
        {
            // initialOwner = true: a trava pertence a esta instância desde
            // já. O segundo app que abrir encontra a trava ocupada.
            trava = new Mutex(true, Nome, out bool primeiro);

            if (primeiro) return trava;

            // A trava já existia: outra instância está rodando, e esta não
            // precisa do handle — só de saber que não pode subir.
            trava.Dispose();
            return null;
        }
        catch (UnauthorizedAccessException)
        {
            // Criado por outra instância com outro nível de permissão
            // (o app instalado roda como administrador). Existe uma
            // instância rodando — e esta não pode falar com ela.
            trava?.Dispose();
            return null;
        }
    }

    /// <summary>
    /// True quando já existe uma instância do TRANAL SENTINEL rodando
    /// nesta sessão do Windows.
    ///
    /// <para>
    /// A sonda <b>não cria</b> o mutex: ela só olha se ele existe. Criar
    /// seria perigoso — o app que subisse na mesma hora encontraria a
    /// trava já criada por outra chamada e acharia que outra instância
    /// estava rodando (ele não sobe, sem mensagem).
    ///
    /// <para>
    /// Quando não dá para saber (o mutex é de outro nível de permissão),
    /// a resposta é <b>true</b>: sem certeza, a operação que mexe no
    /// arquivo do histórico não acontece. Perder uma oportunidade é melhor
    /// que mexer no arquivo que outro programa tem aberto.
    /// </para>
    /// </summary>
    public static bool EstaRodando()
    {
        try
        {
            if (!Mutex.TryOpenExisting(Nome, out Mutex? existente)) return false;

            // O mutex existe: alguém o está segurando. O handle é só uma
            // sondagem — ele é devolvido logo em seguida.
            existente.Dispose();
            return true;
        }
        catch (UnauthorizedAccessException)
        {
            // Existe uma instância rodando (o app instalado roda como
            // administrador), e esta não consegue nem abrir a trava.
            return true;
        }
        catch (Exception)
        {
            // Sem conseguir saber, o app não arrisca mexer no banco.
            return true;
        }
    }
}
