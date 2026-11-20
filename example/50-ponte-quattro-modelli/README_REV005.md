# 50 REV005 — stazioni di verifica e sezioni notevoli (BridGHe 0.6.0)

Aprire **[50-ponte-quattro-modelli_REV005.ghx](50-ponte-quattro-modelli_REV005.ghx)** con la Release 0.6.0. Il file deriva dal REV004, conservato, con il comando `--example-stations 50`. Geometria, sezioni, carichi, fasi e combinazioni sono quelli del REV004.

## Cosa cambia

| Dove | REV004 | REV005 |
|---|---|---|
| Super Element Check | instabilità locali sugli ingressi del check; 5 stazioni per asta | **Super Element Check Options**: le stesse instabilità e un cursore «stazioni di analisi per asta» a **2**, cioè i due nodi di ogni asta (con aste da 1 m, una verifica ogni metro) |
| Stazioni Straus7 | 5 per asta, fisse | **Beam Output Stations** dopo Bridge FEM Members, con un cursore a **5** come prima: l'utente sceglie |
| Detail Check «Sezioni e pioli» | tutte le stazioni di tutte le travi | solo le **sezioni notevoli**: Bridge Notable Stations (estremi e metà di ogni tratto, appoggi, massimo positivo di campata, più i punti degli irrigiditori), poi Bridge Detail Stations |
| Irrigiditori ai giunti e agli appoggi | leggono dal Detail Check | invariati: leggono dal Detail Check ridotto, che contiene i loro punti |
| Grafici di tensioni e taglio | dal Detail Check | dal Super Element Check, perché coprono tutta la trave |

## Verifica

Calcolo completo con Straus7 (prima esecuzione della prova di salvataggio e riapertura), REV004 con la 0.5.0 contro REV005 con la 0.6.0:

| | Tempo della definizione | Picco di memoria | Righe dei risultati |
|---|---|---|---|
| REV004 | 107 s | 11,9 GB | 68 184 |
| REV005 | 70 s | 5,1 GB | 16 776 |

I massimi di trave sono gli stessi:
- in tutte le verifiche del Super Element Check;
- nel Detail Check;
- negli irrigiditori agli appoggi e ai giunti.

Le verifiche degli irrigiditori sono lo stesso numero (432 e 864).
