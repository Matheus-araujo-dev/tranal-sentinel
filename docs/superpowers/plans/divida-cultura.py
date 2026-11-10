"""Conta os numeros que ainda dependem da cultura do Windows.

O criterio esta no plano da v0.31 (secao "Divida conhecida"): buraco de
interpolacao {expressao:formato} cujo formato e numerico e sensivel a cultura
(0.x com uma ou mais casas decimais, N, P e F), contado por ocorrencia, nos
.cs do projeto do app (TranalSentinel.csproj) — que exclui a suite de testes,
o Tranal.Setup/ e o Tranal.Setup.Tests/.

Uso:
    python divida-cultura.py            # a arvore de trabalho
    python divida-cultura.py head       # o HEAD da branch
    python divida-cultura.py <commit>   # um commit especifico

Nao precisa de nenhum pacote: so git e a biblioteca padrao.
"""
import collections
import os
import re
import subprocess
import sys

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.dirname(
    os.path.abspath(__file__)))))

BURACO = re.compile(r"\{([^{}]*)\}")
FORMATO = re.compile(r"^(?:0\.[0#]+|N[0-9]*|P[0-9]*|F[0-9]*)$")


def arquivos_do_app():
    lista = subprocess.run(["git", "-C", ROOT, "ls-files", "*.cs"],
                           capture_output=True, text=True, check=True).stdout.splitlines()
    # O app e o projeto da raiz; os testes e o setup tem pasta propria.
    return [f for f in lista if "/" not in f]


def conta(texto):
    total = 0
    for m in BURACO.finditer(texto):
        conteudo = m.group(1)
        if ":" not in conteudo:
            continue
        expr, formato = conteudo.split(":", 1)
        if expr.strip() == "":
            continue
        if FORMATO.match(formato.strip()):
            total += 1
    return total


def main():
    origem = sys.argv[1] if len(sys.argv) > 1 else "work"
    achados = []
    for arq in arquivos_do_app():
        if origem == "work":
            with open(os.path.join(ROOT, arq), "rb") as f:
                texto = f.read().decode("utf-8-sig", errors="replace")
        else:
            bruto = subprocess.run(["git", "-C", ROOT, "show", origem + ":" + arq],
                                   capture_output=True, check=True).stdout
            texto = bruto.decode("utf-8-sig", errors="replace")
        n = conta(texto)
        if n:
            achados.append((arq, n))
    achados.sort()
    total = sum(n for _, n in achados)
    for arq, n in achados:
        print("%4d  %s" % (n, arq))
    print("TOTAL %d ocorrencias em %d arquivos (origem %s)" % (total, len(achados), origem))


main()
