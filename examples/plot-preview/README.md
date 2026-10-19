# Preview dei grafici nel canvas

Aprire `plot-preview.ghx` con la Release aggiornata. Sono presenti due SVG già memorizzati nel parametro **SVG demo**; lo slider sceglie il grafico 0 o 1. Il nuovo componente **Bridge Plot Preview** mostra il diagramma direttamente nel canvas. Doppio clic sull'immagine apre la finestra ingrandita.

I valori sono sintetici, scelti per mostrare curve, inviluppo e progressive: non provengono da un calcolo strutturale. Per i propri risultati sostituire **SVG demo** con l'uscita `SVG` di Bridge Force/Stress/Shear/Stud Plot. Premere **Draw** nel plot o attivare `Keep Running`. Non è necessario scrivere file SVG o PNG.

Generazione e prova di riapertura: `dotnet run --project tests/BridGHe.Smoke -c Release -- --regression plot-preview --write-plot-example`.
