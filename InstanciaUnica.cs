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
    /// Abre o mutex sem tomar a posse dele: se ele não existia, foi criado
    /// agora e <c>criado</c> vem true — o que significa que ninguém está
    /// rodando. Se ele existia, alguém o está segurando.
    ///
    /// <para>
    /// Quando não dá para saber (o mutex é de outro nível de permissão),
    /// a resposta é <b>true</b>: sem certeza, a operação que mexe no
    /// arquivo do histórico não acontece. Perder uma opportunity é melhor
    /// que mexer no arquivo que outro programa tem aberto.
    /// </para>
    /// </summary>
    public static bool EstaRodando()
    {
        Mutex? sonda = null;

        try
        {
            bool criado;
            sonda = new Mutex(false, Nome, out criado);

            // Criado agora: não havia ninguém com a trava. A sonda é
            // descartada logo abaixo, e o mutex some junto.
            return !criado;
        }
        catch (UnauthorizedAccessException)
        {
            // Criado por outra instância com outro nível de permissão:
            // existe uma instância rodando.
            return true;
        }
        catch (Exception)
        {
            // Sem conseguir saber, o app não arrisca mexer no banco.
            return true;
        }
        finally
        {
            sonda?.Dispose();
        }
    }
}
