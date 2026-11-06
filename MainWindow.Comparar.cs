using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;

namespace TranalSentinel;

/// <summary>
/// Página TRANAL COMPARAR: levar os resultados de benchmark de um PC para o
/// outro e comparar.
///
/// <para>
/// O arquivo exportado é um JSON com as specs da máquina de origem e os
/// resultados. Ao importar, o app compara com os resultados desta máquina e
/// avisa quando as duas são diferentes demais para a comparação valer
/// (CPU e placa de vídeo diferentes, por exemplo).
/// </para>
/// </summary>
public partial class MainWindow
{
    /// <summary>As specs desta máquina (consultadas uma vez, o WMI não é rápido).</summary>
    private BenchMaquina? _compararMaquinaLocal;

    /// <summary>Prepara a página (chamada quando o menu "Comparar" é aberto).</summary>
    private void InitComparar()
    {
        if (CompararStatus == null) return;

        CompararStatus.Text =
            "O arquivo exportado leva os resultados e a identificação do PC de origem. Nada sai do computador: o arquivo é seu, e você decide para quem vai.";

        // A consulta ao WMI é lenta: começa a buscar as specs desta máquina
        // assim que a página abre, para o clique não esperar por ela.
        if (_compararMaquinaLocal == null)
        {
            _ = Task.Run(() =>
            {
                try
                {
                    BenchMaquina maquina = BenchIntercambio.MaquinaAtual();
                    Dispatcher.Invoke(() => _compararMaquinaLocal = maquina);
                }
                catch
                {
                    // A janela pode ter fechado antes da consulta terminar:
                    // sem cache, o export volta a consultar as specs.
                }
            });
        }
    }

    private async void BtnCompararExportar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Exportar resultados de benchmark",
            Filter = "Benchmarks TRANAL (*.tranalbench.json)|*.tranalbench.json|Todos os arquivos (*.*)|*.*",
            DefaultExt = ".tranalbench.json",
            FileName = BenchIntercambio.NomeSugerido()
        };

        if (dlg.ShowDialog(this) != true) return;

        CompararStatus.Text = "Exportando…";

        // Tirada da tela do app: se o histórico mudar enquanto o arquivo é
        // montado, o arquivo leva os resultados de um instante só.
        List<BenchRun> renders = _settings.BenchRuns.ToList();
        List<BenchRecord> registros = _settings.BenchRecords.ToList();

        BenchArquivo arquivo;
        try
        {
            // O WMI e a escrita do arquivo demoram: fora da thread da
            // interface, senão a janela congela sem dar nenhum retorno.
            arquivo = await Task.Run(() =>
            {
                BenchArquivo montado = BenchIntercambio.Montar(renders, registros, MaquinaLocal());

                BenchIntercambio.Gravar(montado, dlg.FileName);
                return montado;
            });
        }
        catch (Exception ex)
        {
            CompararStatus.Text = "Falhou ao exportar: " + ex.Message;
            return;
        }

        string texto = $"Exportado: {arquivo.Resultados.Count} resultado(s) para {dlg.FileName}. " +
                       "Leve esse arquivo para o outro PC e importe lá.";

        // Benchmark que falhou vai no arquivo sem nota — o outro PC precisa
        // saber que o teste rodou lá e deu errado.
        int semNota = arquivo.Resultados.Count(r => !BenchResultado.NotaUtilizavel(r.Nota));
        if (semNota > 0)
        {
            texto += $" {semNota} benchmark(s) falharam e foram exportados sem nota.";
        }

        if (arquivo.Resultados.Count == 0)
        {
            texto += " Nenhum benchmark foi rodado nesta máquina ainda: rode o TRANAL BENCH antes de exportar.";
        }

        CompararStatus.Text = texto;
    }

    private async void BtnCompararImportar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Importar resultados de outro PC",
            Filter = "Benchmarks TRANAL (*.tranalbench.json)|*.tranalbench.json|Todos os arquivos (*.*)|*.*"
        };

        if (dlg.ShowDialog(this) != true) return;

        CompararStatus.Text = "Lendo o arquivo…";

        BenchArquivo? arquivo;
        string? erro;
        int descartados;
        List<BenchResultado> locais;
        BenchMaquina maquinaLocal;

        try
        {
            List<BenchRun> renders = _settings.BenchRuns.ToList();
            List<BenchRecord> registros = _settings.BenchRecords.ToList();

            // Fora da thread da interface: ler o arquivo e consultar o WMI
            // pode levar segundos, e a janela não pode travar nesse meio.
            (arquivo, erro, descartados, locais, maquinaLocal) = await Task.Run(() =>
            {
                BenchArquivo? lido = BenchIntercambio.Ler(dlg.FileName, out string? motivo, out int descartadosLidos);
                List<BenchResultado> resultados = BenchIntercambio.Resultados(renders, registros);
                return (lido, motivo, descartadosLidos, resultados, MaquinaLocal());
            });
        }
        catch (Exception ex)
        {
            CompararStatus.Text = "Falhou ao importar: " + ex.Message;
            return;
        }

        if (arquivo == null)
        {
            CompararStatus.Text = erro ?? "Não deu para ler o arquivo.";
            CardCompararMaquinas.Visibility = Visibility.Collapsed;
            CardCompararTabela.Visibility = Visibility.Collapsed;
            return;
        }

        RelatorioComparacao relatorio = BenchComparacao.Comparar(maquinaLocal, arquivo, locais);

        MostrarComparacao(relatorio);

        if (descartados > 0)
        {
            CompararStatus.Text +=
                $" · {descartados} resultado(s) sem identificação de teste foram ignorados (não dá para compará-los).";
        }

        if (locais.Count == 0)
        {
            CompararStatus.Text += " Esta máquina ainda não rodou nenhum benchmark: rode o TRANAL BENCH para ter com o que comparar.";
        }
    }

    /// <summary>As specs desta máquina (consultadas uma vez).</summary>
    private BenchMaquina MaquinaLocal() => _compararMaquinaLocal ??= BenchIntercambio.MaquinaAtual();

    /// <summary>Preenche a tela com o relatório da comparação.</summary>
    private void MostrarComparacao(RelatorioComparacao relatorio)
    {
        CompararLocalTexto.Text = DescreverMaquina(relatorio.MaquinaLocal);
        CompararImportadoTexto.Text = DescreverMaquina(relatorio.MaquinaImportada);

        CompararAvisos.Text = relatorio.Diferencas.Count == 0
            ? "As duas máquinas têm o mesmo hardware: a comparação vale direto."
            : "Atenção — as máquinas não são iguais, então a diferença de nota não é só de hardware:" +
              Environment.NewLine + "· " + string.Join(Environment.NewLine + "· ", relatorio.Diferencas);

        CompararTabela.ItemsSource = relatorio.Linhas.Select(VmComparacao).ToList();

        var observacoes = new List<string>();
        if (relatorio.SoLocal.Count > 0)
        {
            observacoes.Add($"Só existem nesta máquina: {string.Join(", ", relatorio.SoLocal)}.");
        }

        if (relatorio.SoImportado.Count > 0)
        {
            observacoes.Add($"Só existem no PC importado: {string.Join(", ", relatorio.SoImportado)}.");
        }

        if (relatorio.SemNotaLocal.Count > 0)
        {
            observacoes.Add($"Nesta máquina o benchmark falhou (sem nota): {string.Join(", ", relatorio.SemNotaLocal)}.");
        }

        if (relatorio.SemNotaImportado.Count > 0)
        {
            observacoes.Add($"No PC importado o benchmark falhou (sem nota): {string.Join(", ", relatorio.SemNotaImportado)}.");
        }

        observacoes.Add($"Comparado o melhor resultado de cada teste (a variação de uma execução para a outra é de poucos por cento).");
        CompararObservacao.Text = string.Join(" ", observacoes);

        CardCompararMaquinas.Visibility = Visibility.Visible;
        CardCompararTabela.Visibility = Visibility.Visible;

        // Sem data e sem app de origem no arquivo: o app diz que não sabe,
        // em vez de mostrar 01/01/0001. A frase é a mesma que a CLI usa.
        CompararStatus.Text =
            $"{relatorio.Linhas.Count} teste(s) comparado(s) · arquivo de {BenchIntercambio.DescreverOrigem(relatorio.AppOrigem, relatorio.ExportadoEm)}";
    }

    /// <summary>Descreve uma máquina em linhas curtas.</summary>
    private static string DescreverMaquina(BenchMaquina maquina)
    {
        var linhas = new List<string> { maquina.Nome };

        if (!string.IsNullOrWhiteSpace(maquina.Cpu)) linhas.Add(maquina.Cpu);
        if (!string.IsNullOrWhiteSpace(maquina.Gpu)) linhas.Add(maquina.Gpu);
        if (maquina.MemoriaGb > 0) linhas.Add($"{MemoriaTexto.Numero(maquina.MemoriaGb)} GB de memória");
        if (!string.IsNullOrWhiteSpace(maquina.Sistema)) linhas.Add(maquina.Sistema);

        return string.Join(Environment.NewLine, linhas);
    }

    /// <summary>Converte uma linha da regra em linha de tela.</summary>
    private static BenchComparacaoLinhaVm VmComparacao(LinhaComparacao linha)
    {
        string detalhe = $"{linha.ResumoLocal} (este PC) · {linha.ResumoImportado} (outro PC)";
        if (linha.DataLocal.Year > 2000 && linha.DataImportada.Year > 2000)
        {
            detalhe += $" · {linha.DataLocal:dd/MM/yyyy} contra {linha.DataImportada:dd/MM/yyyy}";
        }

        return new BenchComparacaoLinhaVm
        {
            Teste = linha.Teste,
            Detalhe = detalhe,
            NotaLocal = MemoriaTexto.Numero(linha.NotaLocal),
            NotaImportada = MemoriaTexto.Numero(linha.NotaImportada),
            Diferenca = $"{linha.Veredito} ({MemoriaTexto.Numero(linha.DiferencaPercentual)}%)"
        };
    }
}
