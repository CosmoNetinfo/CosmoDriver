# Verifiche della versione 0.1.0

Eseguite il 21 novembre 2026.

| Verifica | Esito |
| --- | --- |
| Compilazione Release con avvisi trattati come errori | PASS, nessun avviso |
| Test protocollo, stati sconosciuti, firme, hash, manifest, consenso installazione, URL produttori | 38 PASS |
| WMI reale e UI: filtri, selezione, annullamento, pulsanti, esportazione abilitata solo con dati validi, rendering desktop/compatto | 14 PASS |
| Pubblicazione EXE Windows x64 self-contained | PASS |
| Installer: installazione per utente in cartella di prova, hash del binario, esecuzione UI, disinstallazione e pulizia voce registro | PASS |
| ZIP portatile: contenuti previsti | PASS |
| Autoupdate: manifest firmato con chiave di produzione, sostituzione su copie isolate, backup, secondo aggiornamento, rifiuto payload alterato | PASS |
| Ricerca reale Windows Update sul PC locale | PASS, una proposta restituita; nessun download o installazione driver |
| Pipeline Windows su GitHub: build, test, WMI/UI e installer/disinstallazione | PASS sui relativi step della [run iniziale](https://github.com/CosmoNetinfo/CosmoDriver/actions/runs/44903762015) |

Il test reale ha individuato una firma `DBNull` nell'inventario WMI: il caso è stato corretto e coperto da un test di regressione.

## Verifiche ancora necessarie su hardware dedicato

L'installazione effettiva di un driver, l'interazione UAC con utenti standard e credenziali diverse, l'eventuale riavvio richiesto, gli ambienti WSUS aziendali, i supporti USB in sola lettura e una matrice completa Windows 10/11 non sono stati collaudati sul dispositivo dell'utente. La loro copertura non è implicata dai test automatici.

Prima di impiegare l'installazione diretta in produzione, eseguire il test in VM o su un PC dedicato con snapshot/backup e un driver realmente proposto da Windows Update. Verificare annullamento UAC, conferma licenza, installazione, rilettura dell'inventario e segnalazione di riavvio. Il processo di installazione non va terminato forzatamente dopo l'avvio.

Il manifest firmato protegge la catena di aggiornamento dell'app; installer ed EXE non hanno firma Authenticode. Il primo download può mostrare un avviso SmartScreen di editore sconosciuto.
