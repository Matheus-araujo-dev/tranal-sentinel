namespace TranalSetup.Dominio;

/// <summary>
/// Valores da entrada de desinstalação em "Apps e recursos" (HKLM).
/// Fica separado do registro de verdade para que o conteúdo seja conferível sem
/// precisar escrever nada no Windows.
/// </summary>
public sealed record EntradaDesinstalacao(
    /// <summary>Nome exibido em "Apps e recursos".</summary>
    string DisplayName,
    /// <summary>Versão exibida (vem do executável do payload).</summary>
    string DisplayVersion,
    /// <summary>Editor exibido.</summary>
    string Publisher,
    /// <summary>Pasta de instalação.</summary>
    string InstallLocation,
    /// <summary>Ícone exibido na lista de programas.</summary>
    string DisplayIcon,
    /// <summary>Comando que roda a desinstalação.</summary>
    string UninstallString);
