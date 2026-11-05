using System;
using System.Threading;
using System.Windows;
using TranalSentinel;
using Xunit;

namespace TranalSentinel.Tests;

/// <summary>
/// Teste da janela de suspensão de serviços.
///
/// <para>
/// O que este teste cobre: a janela <b>carrega</b> (XAML, recursos e
/// estilos). Um StaticResource com o nome errado só estouraria na mão do
/// usuário, ao abrir a janela — e derrubaria o app.
/// </para>
///
/// <para>
/// A janela é criada numa thread de STA (o WPF exige), e o teste não
/// depende da máquina: a lista vem vazia.
/// </para>
/// </summary>
public class JanelaServicosTestes
{
    [Fact]
    public void JanelaServicos_AbreSemErro()
    {
        Exception? erro = null;
        var thread = new Thread(() =>
        {
            try
            {
                var app = new Application();
                app.Resources.MergedDictionaries.Add(new ResourceDictionary
                {
                    Source = new Uri("pack://application:,,,/TranalSentinel;component/Theme.xaml", UriKind.Absolute)
                });

                var janela = new JanelaServicos(new AppSettings(), Array.Empty<ServicoInfo>());
                janela.Close();
            }
            catch (Exception ex)
            {
                erro = ex;
            }
        });
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        thread.Join();

        Assert.Null(erro);
    }
}
