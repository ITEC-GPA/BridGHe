# 99 REV007 — il mobile nelle verifiche (BridGHe 0.7.0)

Aprire **[flusso-acciaio-misto_REV007.ghx](flusso-acciaio-misto_REV007.ghx)** con la Release 0.7.0. Il file deriva dal REV006, che resta, con il comando `--example-envelope 99`. Modelli, carichi, combinazioni e stazioni sono quelli del REV006.

## Cosa cambia

**Bridge Moving Envelope** fra la tabella delle combinazioni mobili del tandem e Build BridGHe Model, con la Value List «Mobile nelle verifiche» impostata su **Posizioni**: i risultati del REV006. Le altre scelte servono al dimensionamento:
- **Inviluppo**: gli 8 angoli N±, V±, M± in ogni stazione, sempre a favore di sicurezza;
- **Concomitanti**: massimo e minimo di N, V, M con le sollecitazioni della stessa posizione.

## Verifica (nativo con Straus7)

| | Righe dei risultati | Utilizzo massimo delle tensioni | Rapporto massimo delle verifiche locali |
|---|---:|---:|---:|
| Posizioni | 5.952, identiche al REV006 | 0,17140 | 0,99760 |
| Inviluppo | 2.688 | 0,17140, stesso punto | 0,99760 |
| Concomitanti | 2.304 | 0,17140, stesso punto | 0,99760 |
