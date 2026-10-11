# TumMakro

Ett litet Windows-program för musens två tumknappar. Ett tryck kopierar, nästa tryck på samma knapp klistrar in.

| Knapp | Första trycket | Nästa tryck |
|---|---|---|
| Tumknapp 1, normalt Bakåt | Kopierar sökvägen till markerade filer eller mappar i Utforskaren | Klistrar in den sparade sökvägen |
| Tumknapp 2, normalt Framåt | Skickar Ctrl+C | Skickar Ctrl+V |

Efter inklistringen börjar samma knapp om med kopiering. Du kan byta plats på funktionerna, pausa och återställa båda knapparna i programfönstret.

## Hämta och kör

Hämta **TumMakro.exe** från [senaste utgåvan](https://github.com/sp1e/TumMakro/releases/latest). Dubbelklicka för att öppna programmet. Fönstrets kryss döljer programmet till ikonen vid klockan; högerklicka på ikonen och välj **Avsluta** för att stoppa det.

Programmet kräver 64-bitars Windows och .NET Framework 4.x. Sökvägskopieringen använder Windows 11-genvägen Ctrl+Shift+C i Utforskaren. Markera filer eller mappar i innehållslistan före första trycket. Vid flera markerade filer får du en sökväg per rad. Inför inklistringen tar programmet bort omgivande citattecken och återställer den sparade sökvägen i urklippet.

Ctrl+C/Ctrl+V-makrot använder det vanliga Windows-urklippet. Om du kopierar annat mellan trycken klistrar du in det nya innehållet. Utan markerat innehåll kan Ctrl+C lämna gammalt innehåll i urklippet.

## Starta automatiskt vid Windows-inloggning

Hämta också **Installera-autostart.ps1** från utgåvan och lägg den i samma mapp som TumMakro.exe. Kör följande kommando i PowerShell från den mappen:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Installera-autostart.ps1
```

Skriptet kopierar programmet till `%LOCALAPPDATA%\TumMakro\TumMakro.exe` och lägger till autostart för det aktuella Windows-kontot. Programmet startar med `--background`: fönstret är dolt, makrona är aktiva och ikonen finns vid klockan. Dubbelklicka på ikonen för att öppna inställningarna. Windows kan lägga ikonen under pilen för dolda ikoner.

För att ta bort autostarten och behålla programfilerna:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Installera-autostart.ps1 -Avinstallera
```

Autostarten använder värdet `TumMakro` under `HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Run`. Den kräver inga administratörsrättigheter. Du kan också inaktivera autostarten i Windows inställningar för startappar.

## Razer Synapse och begränsningar

Tumknapparna behöver rapporteras som Windows extraknappar 1 och 2, vanligen **Mouse Button 4 / Back** och **Mouse Button 5 / Forward**. Om Synapse skickar en tangent eller ett eget makro i stället behöver du återställa knappens standardfunktion. Raden **Senaste tryck** i TumMakro visar vilken knapp Windows rapporterar.

Medan programmet är aktivt ersätter makrona tumknapparnas vanliga funktioner i hela Windows. Övriga musknappar fungerar som vanligt. Sökvägsmakrot stöder Utforskaren, men inte Öppna/Spara-dialoger eller skrivbordets ikoner. Windows kan blockera tangenttryckningar till program som körs med högre behörighet.

TumMakro gör inga nätverksanrop och sparar inget kopierat innehåll på disk. Det sparar knappordningen i `%LOCALAPPDATA%\TumMakro\settings.txt`. Programmet innehåller de två färdiga makrona ovan; det är ingen generell redigerare för valfria tangentsekvenser.

## Bygg och kontrollera

Källkoden finns i `src/TumMakro.cs`. Windows .NET Framework-kompilator bygger programmet utan externa bibliotek:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\Bygg.ps1
.\TumMakro.exe --self-test .\self-test.txt
.\TumMakro.exe --self-test-background .\background-test.txt
```

Makrotestet behöver ett interaktivt Windows-skrivbord och öppnar ett eget testfönster. Det provar Windows musavläsning och tangentbordsinmatning med simulerade musknappar. Sökvägstestet simulerar Utforskarens Ctrl+Shift+C i testfönstret. Bakgrundstestet kontrollerar dolt fönster, ikon och aktiv musavläsning samt att fönstret kan öppnas och döljas. Testerna ersätter inte ett praktiskt test med din mus och Synapse-profil.

## Referenser

- [Microsofts tangentbordsgenvägar för Windows](https://support.microsoft.com/en-us/windows/keyboard-shortcuts-in-windows-dcc61a57-8ff0-cffe-9796-cb9706c75eec)
- [Microsoft: musens extraknappar](https://learn.microsoft.com/en-us/windows/win32/learnwin32/mouse-clicks)
- [Microsoft: SendInput och behörighetsgränser](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-sendinput)
