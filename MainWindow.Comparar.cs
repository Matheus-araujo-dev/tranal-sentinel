using System;
using System.Collections.Generic;
using System.Linq;
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
    }

    private void BtnCompararExportar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new SaveFileDialog
        {
            Title = "Exportar resultados de benchmark",
            Filter = "Benchmarks TRANAL (*.tranalbench.json)|*.tranalbench.json|Todos os arquivos (*.*)|*.*",
            DefaultExt = ".tranalbench.json",
            FileName = BenchIntercambio.NomeSugerido()
        };

        if (dlg.ShowDialog(this) != true) return;

        try
        {
            BenchArquivo arquivo = BenchIntercambio.Montar(
                _settings.BenchRuns,
                _settings.BenchRecords,
                MaquinaLocal());

            BenchIntercambio.Gravar(arquivo, dlg.FileName);

            CompararStatus.Text =
                $"Exportado: {arquivo.Resultados.Count} resultado(s) para {dlg.FileName}. " +
                "Leve esse arquivo para o outro PC e importe lá.";
        }
        catch (Exception ex)
        {
            CompararStatus.Text = "Falhou ao exportar: " + ex.Message;
        }
    }

    private void BtnCompararImportar_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog
        {
            Title = "Importar resultados de outro PC",
            Filter = "Benchmarks TRANAL (*.tranalbench.json)|*.tranalbench.json|Todos os arquivos (*.*)|*.*"
        };

        if (dlg.ShowDialog(this) != true) return;

        BenchArquivo? arquivo = BenchIntercambio.Ler(dlg.FileName, out string? erro);
        if (arquivo == null)
        {
            CompararStatus.Text = erro ?? "Não deu para ler o arquivo.";
            CardCompararMaquinas.Visibility = Visibility.Collapsed;
            CardCompararTabela.Visibility = Visibility.Collapsed;
            return;
        }

        List<BenchResultado> locais = BenchIntercambio.Resultados(_settings.BenchRuns, _settings.BenchRecords);
        RelatorioComparacao relatorio = BenchComparacao.Comparar(MaquinaLocal(), arquivo, locais);

        MostrarComparacao(relatorio);

        if (locais.Count == 0)
        {
            CompararStatus.Text +=
                " Esta máquina ainda não rodou nenhum benchmark: rode o TRANAL BENCH para ter com o que comparar.";
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

        observacoes.Add($"Comparado o melhor resultado de cada teste (a variação de uma execução para a outra é de poucos por cento).");
        CompararObservacao.Text = string.Join(" ", observacoes);

        CardCompararMaquinas.Visibility = Visibility.Visible;
        CardCompararTabela.Visibility = Visibility.Visible;

        CompararStatus.Text =
            $"{relatorio.Linhas.Count} teste(s) comparado(s) · arquivo de {relatorio.AppOrigem} · exportado em {relatorio.ExportadoEm:dd/MM/yyyy HH:mm}";
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
