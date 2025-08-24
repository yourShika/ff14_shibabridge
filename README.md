<!-- ShibaBridge README (reines HTML, ohne CSS) -->

<h1 id="shibridge">ShibaBridge</h1>
<p>Synchronisierte Looks &amp; Emotes für <strong>Final Fantasy XIV</strong> via <strong>Dalamud</strong> – datenschutzfreundlich, opt-in und gruppenbasiert.</p>

<blockquote>
  <p><strong>Kurzfassung:</strong> ShibaBridge teilt auf Wunsch dein aktuelles Outfit (Glamour), deine lokalen Penumbra/Glamourer-Presets und – optional – Emotes/Animationen mit freigegebenen Kontakten. Es werden <strong>keine Mod-Dateien verteilt</strong>, nur die nötigen Referenzen/Metadaten.</p>
</blockquote>

<hr />

<h2 id="inhalt">Inhaltsverzeichnis</h2>
<ul>
  <li><a href="#features">Features</a></li>
  <li><a href="#how">So funktioniert’s</a></li>
  <li><a href="#install">Installation</a></li>
  <li><a href="#start">Erste Schritte</a></li>
  <li><a href="#privacy">Datenschutz &amp; Sicherheit</a></li>
  <li><a href="#faq">FAQ</a></li>
  <li><a href="#roadmap">Roadmap</a></li>
  <li><a href="#contrib">Beitragende</a></li>
  <li><a href="#disclaimer">Haftung &amp; Hinweise</a></li>
  <li><a href="#license">Lizenz</a></li>
</ul>

<hr />

<h2 id="features">Features</h2>
<ul>
  <li><strong>Aussehen-Sync:</strong> Outfit, Farbstoffe, Accessoires und Charakter-Optik werden für freigegebene Kontakte sichtbar.</li>
  <li><strong>Penumbra/Glamourer-Brücke:</strong> Mapping auf lokale Presets/Mods – ohne Original-Assets zu übertragen.</li>
  <li><strong>Emote/Animation-Sync (optional):</strong> Für Screenshots, Events und Roleplay.</li>
  <li><strong>Gruppen &amp; Schlüssel:</strong> Opt-in-Freigaben, Sichtbarkeiten (Freunde/Party/FC), Widerruf jederzeit.</li>
  <li><strong>Ressourcenschonend:</strong> Asynchron, Deduplizierung, lokales Caching.</li>
  <li><strong>Privacy-Werkzeuge:</strong> Panic-Toggle, Blacklist, Offline-Status, Auto-Timeouts.</li>
</ul>

<p><strong>Was ShibaBridge NICHT tut</strong></p>
<ul>
  <li>verteilt <strong>keine</strong> Mod-Dateien</li>
  <li>verändert <strong>keine</strong> Spiel-Assets</li>
  <li>greift <strong>nicht</strong> ohne Zustimmung auf Daten zu</li>
</ul>

<hr />

<h2 id="how">So funktioniert’s</h2>
<pre><code>[Dein Client]
  ├─ Erfasst: aktueller Look + (optional) Emote-Status
  ├─ Erstellt: schlanke Metadaten + Mapping auf deine lokalen Presets
  └─ Sendet: nur an Kontakte/Groups mit gültigem Schlüssel

[Empfänger-Client]
  ├─ Prüft: Freigabe &amp; Sichtbarkeit
  └─ Rekonstruiert Ansicht mit eigenen lokalen Mods/Presets
</code></pre>

<ul>
  <li><strong>Minimaldaten:</strong> Nur Namen/IDs von Presets, Slots, Farbcodes, Flags (z. B. „Emote aktiv“).</li>
  <li><strong>Kein Asset-Transfer:</strong> Empfänger benötigt kompatible lokale Mods/Presets, sonst Fallbacks.</li>
  <li><strong>Kontrolliert:</strong> Jederzeit pausierbar oder pro Kontakt einschränkbar.</li>
</ul>

<hr />

<h2 id="install">Installation</h2>
<p><em>Voraussetzung: <strong>XIVLauncher</strong> &amp; <strong>Dalamud</strong>, sowie <strong>Penumbra</strong> und <strong>Glamourer</strong>.</em></p>

<ol>
  <li>
    <p><strong>ShibaBridge-Repo in Dalamud hinzufügen</strong><br />
    Dalamud → <em>Settings</em> → <em>Experimental → Custom Plugin Repositories</em>:</p>
    <pre><code>https://raw.githubusercontent.com/USER/REPO/branch/repo.json
</code></pre>
  </li>
  <li>
    <p><strong>Plugin installieren</strong><br />
    <code>/xlplugins</code> öffnen → nach <strong>ShibaBridge</strong> suchen → <strong>Install</strong>.</p>
  </li>
  <li>
    <p><strong>Laden &amp; öffnen</strong><br />
    Client neu starten oder Dalamud reloaden, dann Config via:</p>
    <pre><code>/shiba
</code></pre>
  </li>
</ol>

<p><strong>Releases:</strong> <a href="https://github.com/USER/REPO/releases">Zum Download</a><br />
<strong>Quellcode:</strong> <a href="https://github.com/USER/REPO">GitHub Repository</a></p>

<h3 id="repojson">Beispiel <code>repo.json</code> (Dalamud-Feed)</h3>
<pre><code>{
  "Author": "ShibaBridge Team",
  "Name": "ShibaBridge",
  "Punchline": "Synchronisierte Looks &amp; Emotes für FFXIV",
  "Description": "Opt-in Sync von Glamour/Presets (Penumbra/Glamourer) &amp; optionalen Emotes zwischen freigegebenen Kontakten.",
  "RepoUrl": "https://github.com/USER/REPO",
  "ApplicableVersion": "any",
  "Tags": [ "ffxiv", "dalamud", "glamour", "penumbra", "sync" ],
  "DalamudApiLevel": 10,
  "AssemblyVersion": "0.1.0",
  "Changelog": "Erstveröffentlichung",
  "DownloadLinkInstall": "https://github.com/USER/REPO/releases/download/v0.1.0/ShibaBridge.zip",
  "DownloadLinkTesting": "",
  "DownloadLinkUpdate": "https://github.com/USER/REPO/releases/download/v0.1.0/ShibaBridge.zip",
  "IconUrl": "https://raw.githubusercontent.com/USER/REPO/branch/ShibaBridge/shibridge_icon_512.png",
  "ImageUrls": [ "https://raw.githubusercontent.com/USER/REPO/branch/assets/screen1.png" ]
}
</code></pre>

<hr />

<h2 id="start">Erste Schritte</h2>
<ol>
  <li><strong>Gruppe erstellen</strong> und <strong>Schlüssel</strong> generieren/teilen.</li>
  <li><strong>Rechte setzen:</strong>
    <ul>
      <li>Outfit-Sync an/aus</li>
      <li>Emote-Sync an/aus</li>
      <li>Sichtbarkeit: Freunde | Party | FC | Benutzerdefiniert</li>
    </ul>
  </li>
  <li><strong>Testen:</strong> Outfit wechseln oder Emote ausführen → freigegebene Kontakte sehen die Änderung live.</li>
</ol>

<p><strong>Tipps</strong></p>
<ul>
  <li>Profile pro Job/Rolle verwenden.</li>
  <li>Fallback-Regeln aktivieren, falls beim Empfänger Presets fehlen.</li>
  <li>Blacklist für Namen/IDs pflegen, die nie geteilt werden.</li>
</ul>

<hr />

<h2 id="privacy">Datenschutz &amp; Sicherheit</h2>
<ul>
  <li><strong>Opt-in:</strong> Nichts wird ohne ausdrückliche Freigabe geteilt.</li>
  <li><strong>Granular:</strong> Per-Kontakt/Gruppe aktivierbar, jederzeit widerrufbar.</li>
  <li><strong>Minimalprinzip:</strong> Nur erforderliche Referenzen/Parameter.</li>
  <li><strong>Transparenz:</strong> Lokales Live-Log &amp; optionale Diagnoseansicht.</li>
  <li><strong>Panik-Taste:</strong> Sofortiger Stopp aller Übertragungen.</li>
  <li><strong>Zeitlimits:</strong> Session-Timeouts &amp; Auto-Pause bei Inaktivität.</li>
</ul>

<hr />

<h2 id="faq">FAQ</h2>

<details>
  <summary>Verteilt ShibaBridge Mod-Dateien?</summary>
  <p>Nein. Es werden nur Metadaten/Referenzen übertragen. Empfänger benötigen eigene lokale Mods/Presets.</p>
</details>

<details>
  <summary>Brauche ich Penumbra &amp; Glamourer?</summary>
  <p>Für die Outfit/Optik-Funktionen ja. ShibaBridge mappt auf deine lokalen Presets.</p>
</details>

<details>
  <summary>Kann ich Emotes synchronisieren?</summary>
  <p>Ja, wenn du es explizit aktivierst und deine Gruppe zustimmt. Jederzeit deaktivierbar.</p>
</details>

<details>
  <summary>Was passiert, wenn ein Preset beim Empfänger fehlt?</summary>
  <p>Es greift eine Fallback-Logik (Standard-Look / ähnliche Items), sodass die Szene konsistent bleibt.</p>
</details>

<details>
  <summary>Beeinflusst das meine FPS?</summary>
  <p>Der Sync ist asynchron und gecached. In der Praxis sind die Auswirkungen gering; bei Bedarf kannst du Limits setzen.</p>
</details>

<hr />

<h2 id="roadmap">Roadmap</h2>
<ul>
  <li><strong>Geplant</strong>
    <ul>
      <li>Auto-Freigabe für Party-Mitglieder</li>
      <li>Per-Slot-Sync (nur Kopf/Weapon/etc.)</li>
      <li>Job-basierte Sync-Profile</li>
    </ul>
  </li>
  <li><strong>In Arbeit</strong>
    <ul>
      <li>Bessere Konfliktlösung bei fehlenden Presets</li>
      <li>Robustere Emote-Synchronität bei hoher Latenz</li>
    </ul>
  </li>
  <li><strong>Erledigt</strong>
    <ul>
      <li>Gruppen &amp; Schlüsselverwaltung</li>
      <li>Opt-in-Freigaben</li>
      <li>Basis-Aussehen-Sync</li>
    </ul>
  </li>
</ul>

<hr />

<h2 id="contrib">Beitragende</h2>
<p>Beiträge sind willkommen!</p>
<ul>
  <li>Issue eröffnen: <a href="https://github.com/USER/REPO/issues">Issues</a></li>
  <li>PR einreichen (mit kurzen Notizen im PR-Text)</li>
  <li>Features/Use-Cases im Bereich <em>Discussions</em> diskutieren</li>
</ul>

<p><strong>Dev-Setup (Kurz)</strong></p>
<pre><code># Repo klonen
git clone https://github.com/USER/REPO.git
cd REPO

# Abhängigkeiten (abhängig von eurer Build-Chain)
# dotnet workload install ...
# oder:
# ./build.ps1
</code></pre>

<hr />

<h2 id="disclaimer">Haftung &amp; Hinweise</h2>
<p>ShibaBridge ist ein <strong>inoffizielles</strong> Drittanbieter-Plugin. Die Nutzung kann gegen Richtlinien des Spielbetreibers verstoßen und erfolgt <strong>auf eigenes Risiko</strong>. Prüfe die für deine Region geltenden Regeln und handle verantwortungsvoll. Dieses Projekt steht in <strong>keiner Verbindung zu SQUARE ENIX</strong>.</p>

<hr />

<h2 id="license">Lizenz</h2>
<p>Dieses Projekt steht unter der <strong>MIT-Lizenz</strong> (oder wähle eine passende Lizenz). Siehe <a href="./LICENSE"><code>LICENSE</code></a>.</p>

<hr />

<p><strong>Links</strong><br />
<a href="https://github.com/USER/REPO">Repository</a> •
<a href="https://github.com/USER/REPO/releases">Releases</a></p>

<hr />
<h2 id="development">Development</h2>
<p><strong>Build Plugin:</strong> <code>dotnet build ShibaBridge/ShibaBridge.csproj</code></p>
<p><strong>Run Server:</strong> <code>cd Server/ShibaBridge.Server && dotnet run</code></p>
<p><strong>Docker:</strong> <code>docker build -t shibabridge-server -f Server/Dockerfile .</code></p>
