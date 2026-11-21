# Verifiche 0.2.0

## Risultati locali

- 62 test di logica/sicurezza superati: include consenso, snapshot SHA-256, INF cambiato dalla scansione, dipendenze cambiate, traversal, flussi NTFS, duplicati, dimensioni, annullamento, ZIP slip, link simbolici ZIP e blocco dell'installazione se il punto di ripristino richiesto non viene creato.
- 19 controlli WMI/SetupAPI/UI superati: include matching reale di un INF inbox sui dispositivi presenti, popolamento della tabella DriverPack, stati dei pulsanti e schermate delle schede DriverPack/Ripristino.
- Build Release con zero avvisi/errori; EXE x64 self-contained e installer Inno Setup 6 generati.
- Il nuovo logo viene renderizzato dalle stesse geometrie usate nell'app; icona ICO con sette dimensioni (16–256), SVG per sfondi chiari/scuri e PNG trasparenti.

Il test WMI/SetupAPI non installa driver e non crea punti di ripristino. I fixture sono copie di INF pubblici di Windows; non includono file driver redistribuiti.

## Fonte SDIO

Il dominio ufficiale SDIO non è risolvibile dall'ambiente locale di sviluppo. Un tentativo di download alternativo da SourceForge ha restituito HTML, non uno ZIP: non è stato estratto né eseguito. Il codice rifiuta questi casi tramite il digest dell'archivio e supporta l'importazione dello ZIP ufficiale verificato. Il test di rete CI registra lo stato reale della fonte in `sdio-source-results.json`, senza eseguire il motore esterno.

## Limiti del collaudo

L'installazione effettiva di driver e la creazione effettiva di un punto di ripristino richiedono un ambiente Windows di prova con privilegi UAC, Protezione sistema attiva e un backup/snapshot. Non sono state eseguite sul PC dell'utente; il processo di test disponibile gira senza token amministrativo. Sono testati i blocchi di sicurezza quando la creazione fallisce o Windows riutilizza un punto esistente, senza annunciare falsi successi.

Non sono stati collaudati il download dell'intero catalogo SDIO, l'installazione tramite SDIO, una matrice completa di hardware OEM, tutte le configurazioni Windows 10/11/WSUS o il rollback di un driver dopo riavvio. Gli EXE CosmoDriver non hanno firma Authenticode; gli aggiornamenti dell'app hanno manifest RSA firmato come nella versione 0.1.0.
