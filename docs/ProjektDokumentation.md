# ShibaBridge – Projektübersicht

## Ziel des Projekts
ShibaBridge synchronisiert Outfits, Mod‑Presets und optional Emotes zwischen Final Fantasy XIV‑Spielern, die sich gegenseitig freigeben. Die Lösung setzt auf **Dalamud** als Plugin‑Plattform und nutzt externe Plugins wie **Penumbra** und **Glamourer**, um lokale Modifikationen referenzieren zu können.

## Aufbau des Repositories
```
.
├── Glamourer.Api      – Submodul: Wrapper für die Glamourer‑IPC‑Schnittstelle
├── Penumbra.Api       – Submodul: Wrapper für die Penumbra‑IPC‑Schnittstelle
├── Server/            – ASP.NET Core Server für Registrierung, Pairing & Dateiaustausch
├── ShibaBridge/       – Dalamud‑Plugin (Client)
├── ShibaBridgeAPI/    – Gemeinsame API‑Bibliothek (DTOs, Routen, SignalR‑Contracts)
└── ShibaBridge.sln    – Solution-Datei mit Plugin und API
```

### ShibaBridge (Plugin)
* Einstiegspunkt: [`Plugin.cs`](../ShibaBridge/Plugin.cs)
* Registriert eine Vielzahl von Diensten über **Microsoft.Extensions.Hosting**.
* Kommuniziert per REST und SignalR mit dem Server (`ApiController`, `HubFactory`).
* Nutzt Penumbra- und Glamourer‑APIs, um lokale Mods auf empfangene Daten abzubilden.
* Enthält UI‑Komponenten zur Konfiguration und zum Anzeigen von Profilen/Logs.

### ShibaBridge.API
* Stellt serialisierbare Datenklassen wie [`CharacterData`](../ShibaBridgeAPI/ShibaBridge.API/Data/CharacterData.cs) bereit.
* Definiert DTOs für Dateiübertragungen und Routen‑Konstanten (`Routes/*`).
* Wird sowohl vom Plugin als auch vom Server referenziert, um Schema‑Kompatibilität sicherzustellen.

### Server
* Minimaler ASP.NET‑Core‑Server mit REST‑Endpunkten und einer SignalR‑Hub‑Instanz.
* Controller: `AuthController` (Registrierung/Login), `PairingController` (Spieler koppeln), `FileController` (temporärer Dateispeicher).
* Services (`AuthService`, `PairingService`, `FileTransferService`) nutzen einfache In‑Memory‑Implementierungen – als Platzhalter für echte Persistenz.
* Konfiguration über `appsettings*.json`; optional als Docker‑Container nutzbar (`Server/Dockerfile`).

### Externe API‑Wrapper
* **Glamourer.Api** und **Penumbra.Api** sind schreibgeschützte Submodule, die IPC‑Schnittstellen der entsprechenden Plugins kapseln.
* Dienen der Interaktion mit den im Client vorhandenen Mod‑Daten, ohne selbst Mod‑Assets zu verwalten.

## Funktionsweise ("Wie?")
1. **Client sammelt lokale Daten** über Penumbra, Glamourer und weitere Plugin‑Schnittstellen.
2. **Metadaten werden generiert** (z. B. Liste der Mod‑Dateien, Farb‑Codes, Emote‑Status) und über HTTP/SignalR an einen freigegebenen Kontakt gesendet.
3. **Server vermittelt** lediglich zwischen den Clients: Registrierung, Pairing sowie temporärer Dateiaustausch. Persistente Speicherung findet nicht statt.
4. **Empfängerclient rekonstruiert** das Aussehen mit seinen lokalen Mods. Fehlen Presets, greifen Fallback‑Mechanismen.

## Warum dieser Ansatz?
* **Datenschutz:** Es werden nur Referenzen übertragen, keine Mod‑Dateien.
* **Opt‑in:** Spielende entscheiden pro Gruppe/Kontakt, was geteilt wird.
* **Leichtgewichtig:** Asynchrones Caching und File‑Hashes verhindern redundante Übertragungen.
* **Modular:** Server‑Komponenten lassen sich austauschen oder erweitern (z. B. echte Authentifizierung, persistente Datenbank).

## Entwicklung & Build
```bash
# Plugin & API kompilieren
dotnet build ShibaBridge.sln

# Server lokal starten
cd Server/ShibaBridge.Server
dotnet run

# Docker‑Variante des Servers
docker build -t shibabridge-server -f Server/Dockerfile .
docker run -p 8080:8080 shibabridge-server
```

## Weiterführende Hinweise
* Ausführliche Nutzer‑Informationen und Installationsanleitung befinden sich in der [README](../README.md).
* Der Server ist nur ein Demonstrations‑Backend. Für produktive Nutzung sind Persistenz, Authentifizierung und Security‑Audits notwendig.
