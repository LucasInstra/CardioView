# Como o ECG funciona no CardioView

Este documento explica, em linguagem acessível, como o CardioView lê um ECG
real, como ele **detecta os batimentos**, como chega ao **diagnóstico de
ritmo** ("a doença"), o que **significa cada vital** do monitor e como as
**formas de onda são simuladas**.

> **Aviso:** tudo aqui é didático. O aplicativo é apenas para simulação e
> estudo — valores, formas de onda e alarmes **não** representam dados médicos
> reais e **não substituem avaliação médica**.

---

## 1. Visão geral

O CardioView tem dois modos:

| Modo | O que faz |
|------|-----------|
| **Monitor** | Simula sinais vitais em tempo real (ECG, SpO2, pressão, EtCO2), com alarmes e tendência. |
| **Visualizador de ECG** | Abre gravações reais do banco MIT-BIH e analisa o ritmo. |

O "cérebro" da análise está em `Services/`:

- `MitBih.cs` — leitura dos arquivos `.hea` / `.dat`
- `AnnotationReader.cs` — leitura das anotações `.atr`
- `QrsDetector.cs` — detecção de complexos QRS
- `RhythmAnalyzer.cs` — o algoritmo que sugere condições de ritmo
- `InterpretationService.cs` — o texto clínico em linguagem simples
- `ReportService.cs` — gera o PDF

---

## 2. O formato MIT-BIH

O banco MIT-BIH guarda cada registro em três arquivos:

### `.hea` (cabeçalho)

Primeira linha: nome do registro, número de sinais, **taxa de amostragem** e
**total de amostras**. As linhas seguintes descrevem cada derivação:
nome do arquivo, formato, ganho, baseline, unidades e resolução.

Exemplo (`105.hea`): 2 sinais, 360 Hz, derivações MLII e V1.

### `.dat` (dados)

Os valores do ECG são armazenados como inteiros (ADC). O CardioView suporta
três formatos:

| Formato | Descrição |
|---------|-----------|
| **212** | 2 sinais em 3 bytes (12 bits por amostra) — o padrão do MIT-BIH |
| **16**  | 2 bytes por amostra (16 bits) |
| **80**  | 1 byte por amostra (8 bits com sinal) |

Para converter para milivolts, usa-se a fórmula:

```
mV = (valor_ADC − centro) / ganho
```

onde `centro` é o ADC-zero do cabeçalho e `ganho` é o ganho (ex.: 200 ADC/mV).
Depois, a média é subtraída para remover o desvio de linha de base.

### `.atr` (anotações)

Formato *annot(5)*: cada anotação é uma palavra de 2 bytes com o **código**
nos 6 bits mais altos e o **intervalo** (em amostras) nos 10 bits restantes.
Há códigos especiais (59–63: SKIP, NUM, SUB, CHN, AUX) e códigos de batimento
(1–41). A tabela completa de significados está em `AnnotationReader.cs`
(ex.: `N` normal, `V` PVC, `A` APC, `+` mudança de ritmo).

---

## 3. Detecção de complexos QRS

`QrsDetector.cs` implementa uma versão simplificada do clássico algoritmo de
**Pan–Tompkins**. O objetivo é achar a posição de cada batimento em um sinal
bruto. Etapas, na ordem:

1. **Passa-baixa (média móvel)** — janela de `sampleRate/40` amostras
   (~9 ms), suaviza o traçado.
2. **Derivada** — `d[i] = low[i] − low[i−1]`, realça as subidas rápidas do QRS.
3. **Quadrado** — `sq[i] = d[i]²`, torna tudo positivo e amplifica os picos.
4. **Integração por janela móvel** — janela de `sampleRate/8` (~45 ms),
   produz uma "curva de energia" suave.
5. **Limiar** — `threshold = 0.35 × valor máximo` da curva integrada.
6. **Detecção de pico** — um máximo local acima do limiar, respeitando um
   **período refratário de 200 ms** (evita dupla contagem do mesmo complexo).

Esse detector é usado como *fallback* de FC quando o `.atr` não tem anotações
de batimento.

---

## 4. Análise de ritmo — o algoritmo do "diagnóstico"

`RhythmAnalyzer.cs` recebe as anotações e produz um `RhythmReport` com
achados (`findings`) e um resumo. Passo a passo:

### 4.1 Frequência cardíaca

A partir da lista de batimentos anotados, calcula os **intervalos RR** (tempo
entre batimentos consecutivos, em ms). Filtra os intervalos fisiológicos
(200–3000 ms, ou 20–300 bpm) e obtém:

```
FC mín = 60000 / RR máximo
FC máx = 60000 / RR mínimo
FC média = 60000 / RR médio
```

### 4.2 Contagem por tipo de batimento

Conta quantos batimentos de cada código existem. Os mais importantes:

| Código | Símbolo | Significado |
|--------|---------|-------------|
| 1  | N | Batimento normal |
| 5  | V | Contração ventricular prematura (PVC) |
| 8  | A | Contração atrial prematura (APC) |
| 2/3 | L/R | Bloqueio de ramo esquerdo/direito |
| 41 | ? | PVC R-sobre-T (R-on-T) |

### 4.3 Salvas ventriculares

Percorre os batimentos contando sequências consecutivas de `V`:

- 2 seguidos → **dupleto**
- 3 seguidos → **tripleto**
- ≥ 3 seguidos → **salva de taquicardia ventricular** (não sustentada)

### 4.4 Bigeminismo

Detecta alternância regular `V-N-V-N…` (pelo menos 6 batimentos) — o "batimento
de pulso falho" que alterna com um normal.

### 4.5 Segmentos de ritmo

As anotações de **mudança de ritmo** (código 28) trazem um texto auxiliar
(`(N`, `(VT`, `(AFIB`, `(AFL`, `(SBR`, …). Cada marcador é mapeado para um
nome e uma gravidade:

| Marcador | Condição | Gravidade |
|----------|----------|-----------|
| `(N`     | Ritmo sinusal normal | INFO |
| `(AFIB`  | Fibrilação atrial | ATENÇÃO |
| `(SBR`   | Bradicardia sinusal | ATENÇÃO |
| `(VT`    | Taquicardia ventricular | CRÍTICO |
| `(VFL`   | Flutter ventricular | CRÍTICO |

### 4.6 Geração dos achados

O algoritmo monta uma lista de `RhythmFinding` (condição + evidência +
gravidade), por exemplo:

- PVC → `"41 de 2526 batimentos (1.6%)"`, gravidade **CRÍTICO** se ≥ 10%,
  senão **ATENÇÃO**
- R-on-T → sempre **CRÍTICO**
- Bloqueios de ramo → **ATENÇÃO**
- APC, batimentos de escape, fusão → **INFO**
- Bradicardia/taquicardia → **ATENÇÃO** (quando não há marcador de ritmo)

No fim, achados com a mesma condição são **fundidos** (contagem de episódios)
e ordenados por gravidade decrescente.

### 4.7 Resumo

O resumo **lidera pelas condições críticas** e menciona o ritmo de base em
segundo plano. Exemplo:

> "Possível condição: Episódios de taquicardia ventricular; Extrassístoles
> ventriculares (PVC); em ritmo sinusal normal"

---

## 5. Interpretação clínica

`InterpretationService.cs` transforma os achados em **parágrafos didáticos**:

1. **Quadro geral** — total de batimentos e FC média/mín/máx.
2. **Destaques** — lista os achados críticos e de atenção com números.
3. **Interpretação** — regras que associam achados a possíveis causas:

   - Irritabilidade ventricular (PVC, VT) → cardiopatia isquêmica,
     miocardiopatia, distúrbios eletrolíticos (hipocalemia).
   - Fibrilação/flutter atrial → avaliação de risco tromboembólico.
   - Extrassístoles atriais → geralmente benignas, podem preceder fibrilação.
   - Bloqueios de ramo → doença estrutural (isquêmica, hipertensiva).
   - Batimentos de escape → falha do marca-passo natural (bloqueio AV).
   - Bradicardia → disfunção sinusal, bloqueio AV, medicações.

4. **Gravidade/recomendação** — CRÍTICO → avaliação imediata; ATENÇÃO →
   acompanhamento; nada → seguimento habitual.
5. **Nota de ruído** — se houver mudanças de qualidade/artefatos.

---

## 6. O monitor multiparâmetro — o que cada vital significa

`MonitorViewModel.cs` exibe os valores simulados. Referências e significados:

| Vital | Unidade | O que representa |
|-------|---------|------------------|
| **FC** (frequência cardíaca) | bpm | Batimentos por minuto |
| **SpO₂** (saturação de oxigênio) | % | Fração de hemoglobina oxigenada |
| **PNI** (pressão não invasiva) | mmHg | Sistólica/Diastólica/PAM |
| **PAM** (pressão arterial média) | mmHg | `(Sistólica + 2×Diastólica) / 3` |
| **RESP/FR** (frequência respiratória) | rpm | Respirações por minuto |
| **T1 / T2** (temperatura) | °C | Dois sensores (ex.: central/periférica) |
| **ΔT** | °C | Diferença entre T1 e T2 |
| **EtCO₂** (CO₂ expirado) | mmHg | Capnografia — ventilação/eliminação de CO₂ |
| **FiCO₂** (CO₂ inspirado) | mmHg | CO₂ na inspiração |
| **ST** | mm | Elevação/depressão do segmento ST (isquemia) |
| **P1 / P2** | mmHg | Pressões invasivas (curvas) |

> Nota: `ST` é um valor fixo de demonstração (`"2.3"`), não é medido de fato.

### Estados do paciente

Cada estado tem alvos vitais próprios (`PatientSimulator.cs`):

| Estado | FC | SpO₂ | RESP | PNI | Temp |
|--------|-----|------|------|-----|------|
| Normal | 96 | 98 | 34 | 122/84 | 36,9 |
| Exercício | 125 | 97 | 30 | 145/85 | 37,2 |
| Taquicardia | 140 | 98 | 22 | 120/80 | 36,8 |
| Bradicardia | 50 | 97 | 14 | 115/75 | 36,5 |
| Hipóxia | 115 | **86** | 24 | 125/80 | 36,9 |
| Febre | 120 | 97 | 20 | 130/80 | **38,8** |

### Alarmes (limites padrão)

`AlarmService.cs` compara cada vital com limites alto/baixo:

| Vital | Baixo | Alto |
|-------|-------|------|
| FC | 55 | 120 |
| SpO₂ | 90 | — |
| PNI (sistólica) | 80 | 165 |
| RESP | 8 | 45 |
| Temp | 35,0 | 38,5 |
| EtCO₂ | 20 | 130 |

---

## 7. Geração das formas de onda

`Simulation/` gera os traços com base em uma **fase** que avança a cada
amostra. O período depende do vital alvo (ex.: `beat = 60/FC`).

### ECG (`EcgWaveformGenerator.cs`)

O complexo ECG é a soma de **quatro gaussianas** (onda P, QRS, onda T) mais
duas senoides de alta frequência (ruído muscular):

```
v = G(u, 0.18, 0.03, -0.12)   // onda P
  + G(u, 0.30, 0.012, 1.30)   // complexo QRS
  + G(u, 0.35, 0.014, -0.30)  // porção negativa
  + G(u, 0.50, 0.055, 0.38)   // onda T
  + 0.008·sin(2π·12·u)
  + 0.004·sin(2π·31·u)
```

onde `u` é a posição dentro do ciclo cardíaco (0 a 1) e
`G(u, c, s, a) = a·exp(−0.5·((u−c)/s)²)`.

### SpO₂ (`Spo2WaveformGenerator.cs`)

Pulso com envelope exponencial decrescente (como a fotopletismografia):

```
pulse = sin(2π·2.2·u) · exp(−5.5·u)
```

### Pressão arterial (`BloodPressureWaveformGenerator.cs`)

Subida rápida (sístole) seguida de queda exponencial com uma pequena
"ondícula dicrótica":

```
u < 0.1 → subida linear
senão   → exp(−4·(u−0.1)) − 0.16·exp(−60·(u−0.30)²)
```

### EtCO₂ (`Co2WaveformGenerator.cs`)

A forma clássica da capnografia: platô inspiratório nulo, subida rápida,
**platô alveolar** (quase reto com leve ondulação) e descida rápida.

---

## 8. Pipeline completo (resumo)

```
arquivo .hea ──► MitBihReader.Load ──► sinais em mV (double[])
     │
     ├── .dat (formato 212/16/80) ─► DecodeSignal
     └── .atr (annot 5) ────────────► MitBihAnnotations.Load

sinais ──► QrsDetector.DetectPeaks ──► posição dos QRS (fallback)

anotações ──► RhythmAnalyzer.Analyze ──► RhythmReport
                    │                        │
                    │                        ├─ Findings (condição/evidência/gravidade)
                    │                        └─ Summary
                    ▼
             InterpretationService.Build ──► parágrafos clínicos
                    │
                    ▼
             ReportService.BuildEcgPdf ──► PDF (A4)
```

---

## 9. Limitações

- A análise é **baseada em regras fixas** (não é aprendizado de máquina).
- Depende da qualidade das anotações do `.atr`; sem `.atr`, só há detecção QRS
  e contagem de FC.
- Valores do monitor são **simulados** e não refletem fisiologia real.
- Nada aqui substitui avaliação médica profissional.
