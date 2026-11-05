using System.IO;
using TranalSetup.Dominio;
using TranalSetup.Infra;

namespace TranalSetup.Tests;

/// <summary>
/// Registro falso: guarda tudo em memória, como o registro de verdade faria.
/// É o que permite testar a idempotência sem escrever no Windows.
/// </summary>
public sealed class RegistroFalso : IRegistro
{
    private readonly Dictionary<(ChaveRegistro Chave, string Caminho), Dictionary<string, object>> _valores = new();

    /// <summary>Quantos valores existem na chave (e na sub-chave) informada.</summary>
    public int QuantosValores(ChaveRegistro chave, string caminho)
        => _valores.TryGetValue((chave, caminho), out var d) ? d.Count : 0;

    /// <summary>Nomes dos valores da chave.</summary>
    public IReadOnlyCollection<string> Nomes(ChaveRegistro chave, string caminho)
        => _valores.TryGetValue((chave, caminho), out var d) ? d.Keys.ToArray() : Array.Empty<string>();

    /// <summary>Se esta chave está selecionada, a gravação nela falha (para testar o caminho de erro).</summary>
    public string? FalharAoGravarNestaChave { get; set; }

    /// <inheritdoc />
    public bool ExisteChave(ChaveRegistro chave, string caminho) => _valores.ContainsKey((chave, caminho));

    /// <inheritdoc />
    public string? LerTexto(ChaveRegistro chave, string caminho, string nome)
        => _valores.TryGetValue((chave, caminho), out var d) && d.TryGetValue(nome, out var v)
            ? v?.ToString()
            : null;

    /// <inheritdoc />
    public void GravarTexto(ChaveRegistro chave, string caminho, string nome, string valor)
    {
        Gravar(chave, caminho, nome, valor);
    }

    /// <inheritdoc />
    public void GravarNumero(ChaveRegistro chave, string caminho, string nome, int valor)
    {
        Gravar(chave, caminho, nome, valor);
    }

    /// <inheritdoc />
    public void RemoverValor(ChaveRegistro chave, string caminho, string nome)
    {
        if (_valores.TryGetValue((chave, caminho), out var d)) d.Remove(nome);
    }

    /// <inheritdoc />
    public void RemoverChave(ChaveRegistro chave, string caminho)
    {
        _valores.Remove((chave, caminho));
    }

    private void Gravar(ChaveRegistro chave, string caminho, string nome, object valor)
    {
        if (FalharAoGravarNestaChave is not null
            && caminho.Contains(FalharAoGravarNestaChave, StringComparison.OrdinalIgnoreCase))
            throw new UnauthorizedAccessException($"Falha simulada ao gravar em {caminho}");

        var chaveCompleta = (chave, caminho);
        if (!_valores.TryGetValue(chaveCompleta, out var d))
        {
            d = new Dictionary<string, object>();
            _valores[chaveCompleta] = d;
        }
        d[nome] = valor;
    }
}

/// <summary>
/// Atalho falso: registra o atalho em memória e cria um arquivo vazio no disco,
/// para que "existe" e "não existe" signifiquem a mesma coisa para o serviço.
/// </summary>
public sealed class AtalhoFalso : IAtalho
{
    private readonly Dictionary<string, DadosAtalho> _atalhos = new();

    /// <summary>Atalhos criados, por caminho.</summary>
    public IReadOnlyDictionary<string, DadosAtalho> Atalhos => _atalhos;

    /// <summary>Quantos atalhos existem (serve para provar que não duplicou).</summary>
    public int Quantidade => _atalhos.Count;

    /// <summary>Se este atalho deve falhar ao ser criado (usado para testar o caminho de erro).</summary>
    public string? FalharAoCriarEsteCaminho { get; set; }
    /// <inheritdoc />
    public void Criar(string caminho, DadosAtalho dados)
    {
        if (FalharAoCriarEsteCaminho is not null
            && caminho.Contains(FalharAoCriarEsteCaminho, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Falha simulada ao criar o atalho: {caminho}");

        string? pasta = Path.GetDirectoryName(caminho);
        if (!string.IsNullOrEmpty(pasta)) Directory.CreateDirectory(pasta);
        File.WriteAllText(caminho, "atalho de teste");
        _atalhos[caminho] = dados;
    }

    /// <inheritdoc />
    public bool Existe(string caminho) => _atalhos.ContainsKey(caminho);

    /// <inheritdoc />
    public void Remover(string caminho)
    {
        _atalhos.Remove(caminho);
        if (File.Exists(caminho)) File.Delete(caminho);
    }
}

/// <summary>Log falso: guarda as linhas para o teste conferir o que foi registrado.</summary>
public sealed class LogFalso : ILog
{
    /// <summary>Linhas registradas, na ordem.</summary>
    public List<string> Linhas { get; } = new();

    /// <inheritdoc />
    public void Registrar(string mensagem) => Linhas.Add(mensagem);
}

/// <summary>
/// Tarefa agendada falsa: guarda a tarefa em memória, como o Agendador de
/// Tarefas faria. É o que permite testar a inicialização automática sem
/// registrar nada no Windows de verdade.
/// </summary>
public sealed class TarefaFalso : ITarefaAgendada
{
    private readonly Dictionary<string, (string Caminho, string Argumento)> _tarefas = new();

    /// <summary>Tarefas criadas, por nome.</summary>
    public IReadOnlyDictionary<string, (string Caminho, string Argumento)> Tarefas => _tarefas;

    /// <summary>Se esta tarefa deve falhar ao ser criada (usado para testar o caminho de erro).</summary>
    public string? FalharAoCriarEstaTarefa { get; set; }

    /// <inheritdoc />
    public void Criar(string nome, string caminhoExecutavel, string argumento)
    {
        if (FalharAoCriarEstaTarefa is not null
            && nome.Contains(FalharAoCriarEstaTarefa, StringComparison.OrdinalIgnoreCase))
            throw new IOException($"Falha simulada ao criar a tarefa: {nome}");

        _tarefas[nome] = (caminhoExecutavel, argumento);
    }

    /// <inheritdoc />
    public bool Existe(string nome) => _tarefas.ContainsKey(nome);

    /// <inheritdoc />
    public void Remover(string nome) => _tarefas.Remove(nome);
}
