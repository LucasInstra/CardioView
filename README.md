# CardioView

Monitor multiparâmetro e visualizador de ECG em WPF (.NET 10). Simula sinais vitais em tempo real com alarmes, tendência e captura de tela, e permite abrir gravações reais do banco **MIT-BIH** (`*.hea` / `*.dat` / `*.atr`) para análise de batimentos e ritmo.

A interface é totalmente em português.

---

## Funcionalidades

### Monitor (tela principal)

- Sinais em tempo real: **ECG1**, **SpO2**, **P1**, **P2**, **EtCO2**.
- Válvulas vitais: **FC (bpm)**, **SpO2 (%)**, **PNI (mmHg)**, **FR (rpm)**, **T1/T2 (°C)**, **ΔT**, **ST**, **PAM**.
- Estados do paciente: Normal, Exercício, Taquicardia, Bradicardia, Hipóxia, Febre — cada um com alvos vitais próprios.
- Sistema de alarmes com limites (FC máx/mín, SpO2 mín, PNI máx/mín, RESP, TEMP, EtCO2), indicador piscante e bipe sonoro.
- Overlay **AJUSTES** para editar limites e opções (som, sistema de alarmes).
- **TENDÊNCIA**: registro das últimas leituras.
- **CAPTURA**: salva PNG do monitor em `Documentos\Capturas`.
- **MARCAR**: insere marcação no log de tendência.
- **PNI** manual e **AUTO** (ciclo automático de ~30 s).
- **CONGELAR/RETOMAR**: pausa a simulação.

### Visualizador de ECG (gravações MIT-BIH)

- Abre arquivos de cabeçalho `*.hea` (suporta o formato de dados **212** e múltiplos sinais/derivações).
- Reprodução automática ao carregar; **PAUSAR / REPRODUZIR / REINICIAR**.
- Seletor de derivação (ex.: MLII, V1).
- Leitura de anotações do arquivo `*.atr` (formato *annot(5)*): batimentos (N, V, Q…), ruído, artefatos e mudanças de ritmo.
- **FC** calculada pelas anotações do `.atr` (fallback: detecção QRS).
- Marcadores com **símbolo em negrito** sobre o traço; passe o mouse sobre uma tag para ver o significado.
- Botão **LEGENDAS**: ao clicar, mostra à direita o significado das tags presentes no arquivo.
- Botão **DIAGNÓSTICO**: análise automática das anotações (`.atr`) e sugestão de possíveis condições — contagem de batimentos por tipo, PVCs/APCs, salvas, marcadores de ritmo `(N`, `(VT`, `(AFIB`…, FC e ruído — com nível de gravidade (INFO / ATENÇÃO / CRÍTICO) e resumo em pt-BR.
  - Achados repetidos são **agrupados por condição** com contagem de episódios (×N), duração total e horário do 1º episódio (ex.: `3 episódios · total 4 min 20 s · 1º às 00:00:10`).
  - O resumo **lidera pelas condições críticas** e cita o ritmo de base em segundo plano (ex.: *"Episódios de taquicardia ventricular; Extrassístoles ventriculares (PVC); em ritmo sinusal normal"*).
- Faixa inferior (**overview**) com todo o registro: clique/arraste para navegar (seek).
- **TAGS: LIG./DESL.** para alternar a exibição dos marcadores.

---

## Requisitos

- [.NET 10 SDK](https://dotnet.microsoft.com/) (Windows)
- Windows 10/11

## Compilar e executar

```bash
dotnet build
dotnet run
```

Ou abra `CardioView.csproj` no Visual Studio e execute.

## Como usar

1. **Visualizador de ECG**: clique em **CARREGAR ECG**, escolha um arquivo `*.hea` (ex.: `mitbih\105.hea`). O traço começa a tocar; use **LEGENDAS** para ver o significado das tags e a faixa inferior para pular no registro.
2. **Monitor**: ao abrir, a simulação já está rodando. Use os botões do topo para mudar o estado do paciente e os botões da barra inferior para ajustes, PNI, captura, tendência, marcação, alarmes, congelar e ir ao ECG.


## Dados de exemplo

A pasta [`mitbih\`](mitbih/) contém o registro **105** do banco MIT-BIH (30 minutos, 360 Hz, derivações MLII + V1, 2526 batimentos normais, 41 PVC), incluindo `105.hea`, `105.dat` e `105.atr`.

> Fonte: [PhysioNet MIT-BIH Arrhythmia Database](https://physionet.org/content/mitdb/).

## Estrutura do projeto

```
CardioView/
├── Controls/          WaveformControl, AnnotationOverview, AnnotationPalette
├── Converters/        Conversores de exibição (estados)
├── Models/            Patient, PatientState, VitalSigns
├── Services/          Simulação, alarmes, leitor MIT-BIH (.hea/.atr), QRS, análise de ritmo, configurações
├── Simulation/        Geradores de formas de onda (ECG, SpO2, PA, EtCO2) e simulador
├── ViewModels/        MonitorViewModel, EcgViewModel
├── Views/             MainWindow (monitor), EcgViewerWindow (ECG)
├── mitbih/            Registro 105 (dados de exemplo)
└── CardioView.csproj
```

## Formato MIT-BIH

- **Cabeçalho** (`*.hea`): campos de formato, ganho, resolução e descrição (campos opcionais como baseline/unidades são tolerados).
- **Dados** (`*.dat`): decodificação do formato **212** (2 sinais em 3 bytes).
- **Anotações** (`*.atr`): formato *annot(5)* — códigos 1–41 (N, L, R, a, V, F, J, A, S, E, j, /, Q, ruído, artefato, mudança de ritmo, etc.) com significados em pt-BR.

## Configurações

Os limites de alarme e opções ficam em `%AppData%\CardioView\settings.json` (criado automaticamente).

---

> **Aviso:** aplicativo apenas para simulação e estudo. Valores, formas de onda e alarmes **não** representam dados médicos reais.
