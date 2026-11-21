# DriverPack e punti di ripristino · 0.2.0

## Due modalità distinte

**INF locali, dentro CosmoDriver.** Seleziona una cartella contenente driver già estratti. L'app individua candidati per i dispositivi presenti, compresi quelli senza driver; Windows SetupAPI verifica ID hardware/compatibili, sezione INF, sistema operativo e architettura. Non vengono inventate corrispondenze confrontando soltanto nomi o numeri di versione.

I candidati mostrano dispositivo, descrizione, versione e firma. Un candidato non significa che il driver attuale sia obsoleto. L'installazione è abilitata solo per pacchetti con firma verificata da SetupAPI (inclusi editori Authenticode già attendibili). Non vengono forzati editori sconosciuti, signature enforcement, downgrade o compatibilità. L'hash dell'INF viene conservato dalla scansione alla conferma; i file del pacchetto vengono fotografati con SHA-256, copiati in staging con ACL amministratori/SYSTEM e verificati di nuovo. `DiInstallDriverW` con flag zero lascia a Windows la scelta se applicare il driver ai dispositivi compatibili. Il risultato distingue l'aggiunta del pacchetto dall'effettiva sostituzione di ogni dispositivo.

Seleziona la cartella di un singolo pacchetto completo. Per l'installazione vengono inclusi i file nella cartella dell'INF e nelle sue sottocartelle: dipendenze esterne non vengono prelevate automaticamente. Limiti: 30.000 elementi, 2 GB per snapshot di installazione, 3.000 INF e 128 MB complessivi di testo per una scansione. Percorsi di rete, junction, link simbolici, traversal e flussi alternativi NTFS vengono rifiutati. I pacchetti `.7z` SDIO devono essere gestiti con il motore SDIO oppure estratti prima dell'analisi locale.

**Catalogo esteso, nella finestra originale SDIO.** CosmoDriver può scaricare e avviare il motore originale **Snappy Driver Installer Origin 2.0.8.891**, oppure importare il relativo ZIP ufficiale. Il motore non viene incorporato nell'EXE, rinominato o presentato come codice CosmoNet. Gestisce i suoi indici, matching, download DriverPack e uso offline nella propria interfaccia. Non viene avviata una installazione automatica tramite script. I risultati SDIO non vengono importati come falsi risultati del motore nativo: alla fine esegui una nuova scansione in CosmoDriver.

Il pacchetto SDIO viene scaricato soltanto dall'URL ufficiale e confrontato con uno SHA-256 fissato nell'app. Un archivio alterato o di versione differente non viene eseguito. Puoi selezionare lo ZIP da una chiavetta se il download non è disponibile. Ogni avvio usa una nuova estrazione dei byte verificati; indici, pacchetti e log restano nella libreria selezionata. La versione del motore verificato si aggiorna con una successiva release di CosmoDriver; gli aggiornamenti dei DriverPack sono gestiti da SDIO. Nessun mirror sconosciuto viene usato come fallback.

Il download diretto può essere indisponibile per DNS/rete o manutenzione del sito. La UI mostra l'errore e consente l'importazione dello ZIP ufficiale. Il test CI della fonte registra esplicitamente successo o indisponibilità; non esegue il motore. Il download non è requisito per usare il motore INF locale o Windows Update.

SDIO può usare BitTorrent e scaricare decine di GB se scegli l'intero catalogo; l'app lo segnala prima dell'apertura. Puoi scegliere soltanto indici e pacchetti necessari nella finestra SDIO. I suoi log e dati sono soggetti al comportamento del progetto originale.

## Ripristino

La scheda **Ripristino** offre creazione del punto, apertura di Protezione sistema e apertura del ripristino Windows. La creazione usa la classe WMI `SystemRestore` tramite processo UAC, senza eseguire PowerShell, abilitare automaticamente la protezione o cambiare il limite di frequenza nel registro. L'app verifica che un nuovo numero di sequenza con la descrizione richiesta sia realmente comparso. Un ritorno API di successo con un vecchio punto riutilizzato non viene annunciato come nuovo punto creato.

Nelle conferme di installazione Windows Update/INF, **Crea prima un punto di ripristino** è selezionato inizialmente. Se la creazione fallisce o non è verificabile, l'installazione non parte. Puoi disattivare esplicitamente questa opzione prima di confermare. Prima di aprire SDIO puoi scegliere lo stesso controllo; se richiesto e fallito, SDIO non viene avviato.

Protezione sistema deve essere attiva. Windows può saltare un nuovo punto se ne esiste uno recente (normalmente entro 24 ore). Windows Server non supporta questa funzione. Un punto non è un backup dei documenti e non garantisce il recupero di ogni problema hardware.

## Fonti e terze parti

- [SDIO: sito e download dell'autore](https://www.glenn.delahoy.com/snappy-driver-installer-origin/).
- [Codice originale SDIO, GPLv3](https://sourceforge.net/p/snappy-driver-installer-origin/code/).
- [Manifest del pacchetto SDIO mantenuto su Chocolatey, con hash della versione fissata](https://community.chocolatey.org/packages/sdio/2.0.8.891). Il digest è un riferimento di integrità per una versione specifica, non una firma Authenticode dell'EXE.
- [Microsoft: selezione e ranking dei driver](https://learn.microsoft.com/en-us/windows-hardware/drivers/install/overview-of-the-driver-selection-process).
- [Microsoft: DiInstallDriverW](https://learn.microsoft.com/en-us/windows/win32/api/newdev/nf-newdev-diinstalldriverw).
- [Microsoft: creazione e frequenza dei punti di ripristino](https://learn.microsoft.com/en-us/windows/win32/sr/createrestorepoint-systemrestore).

CosmoDriver resta un progetto MIT indipendente. SDIO viene scaricato separatamente dall'utente e mantiene nome, interfaccia, licenza e copyright originali. I driver appartengono ai rispettivi produttori e mantengono le loro licenze. Nessun pacchetto driver o binario SDIO viene redistribuito negli installer CosmoDriver.
