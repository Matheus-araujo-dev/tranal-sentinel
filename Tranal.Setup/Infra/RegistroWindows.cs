using Microsoft.Win32;

namespace TranalSetup.Infra;

/// <summary>
/// Acesso real ao registro do Windows (HKCU e HKLM de 64 bits).
///
/// Tudo o que é gravado passa por <see cref="GravarTexto"/>/<see cref="GravarNumero"/>,
/// que sobrescrevem o valor: é isso que faz instalar duas vezes não duplicar nada.
/// </summary>
public sealed class RegistroWindows : IRegistro
{
    /// <inheritdoc />
    public bool ExisteChave(ChaveRegistro chave, string caminho)
    {
        using var sub = Abrir(chave, caminho, writable: false);
        return sub is not null;
    }

    /// <inheritdoc />
    public string? LerTexto(ChaveRegistro chave, string caminho, string nome)
    {
        using var sub = Abrir(chave, caminho, writable: false);
        return sub?.GetValue(nome)?.ToString();
    }

    /// <inheritdoc />
    public void GravarTexto(ChaveRegistro chave, string caminho, string nome, string valor)
    {
        using var sub = Criar(chave, caminho);
        sub.SetValue(nome, valor, RegistryValueKind.String);
    }

    /// <inheritdoc />
    public void GravarNumero(ChaveRegistro chave, string caminho, string nome, int valor)
    {
        using var sub = Criar(chave, caminho);
        sub.SetValue(nome, valor, RegistryValueKind.DWord);
    }

    /// <inheritdoc />
    public void RemoverValor(ChaveRegistro chave, string caminho, string nome)
    {
        using var sub = Abrir(chave, caminho, writable: true);
        sub?.DeleteValue(nome, throwOnMissingValue: false);
    }

    /// <inheritdoc />
    public void RemoverChave(ChaveRegistro chave, string caminho)
    {
        using var baseKey = RegistryKey.OpenBaseKey(Hive(chave), Visao(chave));
        baseKey.DeleteSubKeyTree(caminho, throwOnMissingSubKey: false);
    }

    private static RegistryKey? Abrir(ChaveRegistro chave, string caminho, bool writable)
    {
        using var baseKey = RegistryKey.OpenBaseKey(Hive(chave), Visao(chave));
        return baseKey.OpenSubKey(caminho, writable);
    }

    private static RegistryKey Criar(ChaveRegistro chave, string caminho)
    {
        using var baseKey = RegistryKey.OpenBaseKey(Hive(chave), Visao(chave));
        return baseKey.CreateSubKey(caminho, writable: true)
            ?? throw new InvalidOperationException($"Não foi possível criar a chave de registro: {caminho}");
    }

    private static RegistryHive Hive(ChaveRegistro chave)
        => chave == ChaveRegistro.Usuario ? RegistryHive.CurrentUser : RegistryHive.LocalMachine;

    // A desinstalação e a entrada de "Apps e recursos" ficam em HKLM: em 64 bits
    // elas precisam da visão de 64 bits, senão o Windows cria a chave espelhada.
    private static RegistryView Visao(ChaveRegistro chave)
        => chave == ChaveRegistro.Usuario ? RegistryView.Default : RegistryView.Registry64;
}
