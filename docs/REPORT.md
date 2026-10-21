# Report Word delle verifiche

`Bridge Report`, in **BridGHe / 13-Results**, genera un DOCX a partire dai risultati GPC e da un template Word. Non richiede Word installato, non avvia Straus e non ricalcola le verifiche. La generazione avviene soltanto premendo **Generate**.

## Collegamenti essenziali

1. Collegare il **Results** completo di Section Check, Super Element Check o Bridge Detail Check. Il componente riceve l'intero albero, conserva i percorsi e produce un solo documento.
2. Impostare **Template** con un `.docx` o `.dotx` predisposto con i segnaposto descritti sotto.
3. Impostare **File** con il DOCX di destinazione, diverso dal template.
4. Collegare una **Value List a Topics**: si trasforma automaticamente in una checklist con gli argomenti disponibili.
5. Se disponibili, collegare il modello assemblato a **Model** e le uscite SVG dei plot a **SVG**.
6. Compilare Title, Project, Author e Revision; premere Generate.

I percorsi relativi sono risolti rispetto alla definizione GH salvata. Per una definizione non salvata servono percorsi assoluti. **Overwrite** è disattivato per impostazione iniziale; per aggiornare una relazione esistente chiuderla in Word e abilitarlo esplicitamente. Il template non può essere usato come destinazione. Alla riapertura della definizione non viene scritto alcun documento e File resta vuoto fino al successivo Generate.

## Argomenti selezionabili

| Topics | Contenuto | Segnaposto |
|---|---|---|
| 0 | Identificazione del modello e dei risultati | `{{BG.Model}}` |
| 1 | Sezioni, materiali, opzioni e dettagli attivi | `{{BG.Sections}}` |
| 2 | Fasi costruttive e coefficienti memorizzati nel modello | `{{BG.Phases}}` |
| 3 | WR governanti per voce e verifiche senza esito numerico | `{{BG.Checks}}` |
| 4 | Schede dei risultati governanti per famiglia, con azioni concomitanti | `{{BG.Critical}}` |
| 5 | Grafici SVG collegati, incorporati come immagini | `{{BG.Plots}}` |
| 6 | Tutte le voci per ogni risultato | `{{BG.Appendix}}` |

Senza collegamenti a Topics sono selezionati 0–5. La checklist conserva le scelte alla riapertura; deselezionando tutte le voci rimane il solo **riepilogo di copertura**, sempre presente. Il tabulato completo è escluso inizialmente per contenere la lunghezza del documento. I dati numerici completi rimangono comunque incorporati nel DOCX.

La selezione dei capitoli riguarda l'impaginazione, non filtra le verifiche usate per il riepilogo. Per limitare l'ambito del report selezionare consapevolmente i risultati a monte.

## Preparazione di un template Word

Partire da [Template_BridGHe.docx](../example/48-report-word/Template_BridGHe.docx), oppure usare il proprio documento con logo e intestazioni.

Sono previsti due modi alternativi:

- **Un blocco unico:** inserire `{{BG.Report}}` su un paragrafo autonomo. Il componente inserisce il riepilogo e i capitoli selezionati nell'ordine standard.
- **Ordine personalizzato:** inserire `{{BG.Summary}}` e i segnaposto dei capitoli su paragrafi separati, nell'ordine desiderato. Il componente genera anche i titoli. I blocchi dei capitoli deselezionati vengono rimossi.

Non mescolare i due modi. Il riepilogo `{{BG.Summary}}` è obbligatorio nel secondo modo. Ogni capitolo selezionato deve avere il proprio segnaposto: la sua assenza produce un errore, non un'omissione silenziosa. I blocchi devono trovarsi nel corpo del documento, fuori dalle tabelle e dai controlli contenuto. Non inserirli in un paragrafo che contiene un'interruzione di sezione. Segnaposto sconosciuti o blocchi duplicati sono segnalati.

I segnaposto testuali possono essere inseriti in paragrafi, tabelle, intestazioni e piè di pagina:

| Segnaposto | Valore |
|---|---|
| `{{BG.Title}}` | Titolo |
| `{{BG.Project}}` | Progetto o commessa |
| `{{BG.Author}}` | Autore dichiarato, senza firma automatica |
| `{{BG.Revision}}` | Revisione |
| `{{BG.Date}}` | Data e ora di esportazione con offset locale |
| `{{BG.ReportId}}` | Identificativo univoco dell'esportazione |

I nomi distinguono maiuscole e minuscole. Word può dividere il testo di un segnaposto in più run: il generatore gestisce anche questo caso. Testi personali, figure esistenti, margini, stili, intestazioni e piè di pagina vengono conservati. I template con macro non sono supportati.

Per configurare l'aspetto modificare gli stili **Normal**, **Title**, **Heading 1**, **Heading 2** e **Caption**. Lo stile di paragrafo **BGTableText** controlla il testo delle tabelle generate; il template fornito usa 10 pt. Le tabelle generate hanno bordi grigio chiaro, intestazioni azzurre ripetute e colonne proporzionate allo spazio disponibile. I titoli automatici non sono numerati: la numerazione si può applicare agli stili Heading del proprio template.

## Significato dei risultati

Il report comprende i punti tensionali e le verifiche locali presenti nel risultato GPC. Usa i WR nativi, inclusi quelli convenzionali dei controlli qualitativi; non ricostruisce indiscriminatamente il rapporto domanda/resistenza.

- Un WR attivo finito è classificato secondo il limite 1.
- Un WR assente o non finito è **Non disponibile**, mai zero.
- Un punto dichiarato inattivo da GPC è **Non attivo**.
- Una verifica assente non viene dichiarata **Non applicabile**: tale informazione non è disponibile in modo uniforme nei risultati attuali.

Il riepilogo riporta massimi disponibili e conteggi delle lacune separatamente. I casi governanti sono distinti per configurazione, fase, stato limite e famiglia/voce; conserva tutte le parità. Domanda, limite, azioni e strain appartengono allo stesso risultato. Configurazioni con dettagli o opzioni diversi non vengono accorpate.

Il modello opzionale fornisce sequenza costruttiva, casi e coefficienti. Viene controllata l'appartenenza degli ID trave, delle stazioni locali e delle combinazioni, ma i risultati attuali non contengono un'impronta del FEM sorgente che certifichi la revisione del modello. I grafici SVG non contengono una provenienza numerica verificabile: collegare quelli prodotti dallo stesso flusso.

## Tracciabilità e limiti della prima versione

Ogni DOCX include **bridghe/report.json**, accessibile aprendo il documento come archivio ZIP. Contiene identificativo e data, impronta SHA256 del template e dei dati, argomenti scelti, versioni delle librerie, configurazioni, azioni, WR, provenienza, note e segnalazioni. I numeri sono conservati senza l'arrotondamento di presentazione; i valori non finiti sono stringhe JSON esplicite. L'archivio descrive il report e non è un modello da ricalcolare automaticamente.

Il generatore non ricostruisce lo schema globale del ponte né tutte le formule della libreria. Inserire schema, vincoli e spiegazioni progettuali nel template; le schede riportano i valori nativi disponibili e le note GPC. Non certifica completezza normativa o verifiche non implementate. Il ritiro resta quello già presente nei risultati: nessuna aggiunta implicita.

L'uscita del componente è **DOCX**. Per il PDF usare l'esportazione di Word; su Windows con Word installato è disponibile anche `tools/render_word_report.ps1 -InputPath <report.docx> -OutputPath <report.pdf>`. Questo comando usa un'istanza Word nascosta e non è una dipendenza del plugin.

[Esempio 48 e procedura riproducibile](../example/48-report-word/README.md).
