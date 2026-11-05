using System;
using System.IO;
using System.Text;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Testes da regra dos módulos PawnIO: quando o arquivo que está no disco
/// vale mais que o módulo embutido no executável.
///
/// <para>
/// A regra é pura de propósito: quem decide não toca no disco do usuário.
/// </para>
/// </summary>
public class ModulosPawnIoTestes
{
    private static readonly byte[] Embutido = Encoding.ASCII.GetBytes("modulo-pawnio-versao-1");
    private static readonly byte[] EmbutidoNovo = Encoding.ASCII.GetBytes("modulo-pawnio-versao-2");
    private static readonly byte[] DoUsuario = Encoding.ASCII.GetBytes("modulo-trocado-pelo-usuario");

    [Fact]
    public void SemArquivoNoDisco_ExtraiOEmbutido()
    {
        AcaoModuloPawnIo acao = ModulosPawnIo.Decidir(null, Embutido, null);

        Assert.Equal(AcaoModuloPawnIo.ExtrairEmbutido, acao);
    }

    [Fact]
    public void ArquivoVazioNoDisco_ExtraiOEmbutido()
    {
        AcaoModuloPawnIo acao = ModulosPawnIo.Decidir(Array.Empty<byte>(), Embutido, null);

        Assert.Equal(AcaoModuloPawnIo.ExtrairEmbutido, acao);
    }

    [Fact]
    public void SemHashRegistrado_UsaOArquivoDoDisco()
    {
        // Nunca foi extraído por nós: o que está no disco é do usuário.
        AcaoModuloPawnIo acao = ModulosPawnIo.Decidir(DoUsuario, Embutido, null);

        Assert.Equal(AcaoModuloPawnIo.UsarArquivoDoDisco, acao);
    }

    [Fact]
    public void UsuarioTrocouOArquivo_UsaODoDisco()
    {
        string hashRegistrado = ModulosPawnIo.CalcularHash(Embutido);

        AcaoModuloPawnIo acao = ModulosPawnIo.Decidir(DoUsuario, Embutido, hashRegistrado);

        Assert.Equal(AcaoModuloPawnIo.UsarArquivoDoDisco, acao);
    }

    [Fact]
    public void ArquivoEOMesmoEmbutido_NaoReescreve()
    {
        string hashRegistrado = ModulosPawnIo.CalcularHash(Embutido);

        AcaoModuloPawnIo acao = ModulosPawnIo.Decidir(Embutido, Embutido, hashRegistrado);

        Assert.Equal(AcaoModuloPawnIo.UsarArquivoDoDisco, acao);
    }

    [Fact]
    public void ExecutavelTrouxeOutroModulo_ReescreveODoDisco()
    {
        // O arquivo do disco é o nosso, mas o executável agora traz outro módulo.
        string hashRegistrado = ModulosPawnIo.CalcularHash(Embutido);

        AcaoModuloPawnIo acao = ModulosPawnIo.Decidir(Embutido, EmbutidoNovo, hashRegistrado);

        Assert.Equal(AcaoModuloPawnIo.ExtrairEmbutido, acao);
    }

    [Fact]
    public void HashEDeterministicoEDiferenteParaConteudosDiferentes()
    {
        string a = ModulosPawnIo.CalcularHash(Embutido);
        string b = ModulosPawnIo.CalcularHash(Embutido);
        string c = ModulosPawnIo.CalcularHash(EmbutidoNovo);

        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
        Assert.Equal(64, a.Length);
    }

    [Fact]
    public void PastaDosModulosFicaNoLocalAppData()
    {
        string local = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);

        Assert.StartsWith(local, ModulosPawnIo.Pasta, StringComparison.OrdinalIgnoreCase);
        Assert.EndsWith(Path.Combine("TranalSentinel", "PawnIO"), ModulosPawnIo.Pasta, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(Path.Combine(ModulosPawnIo.Pasta, "SmbusPIIX4.bin"), ModulosPawnIo.Caminho("SmbusPIIX4.bin"));
    }
}
