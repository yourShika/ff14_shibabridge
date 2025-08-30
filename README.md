<!-- Project README in pure HTML -->

<h1 id="shibabridge">ShibaBridge</h1>
<p>ShibaBridge synchronizes glamour, Penumbra/Glamourer presets, and optional emotes between Final Fantasy XIV players using the Dalamud plugin framework.</p>

<h2 id="toc">Quick Links</h2>
<ul>
  <li><a href="#overview">Overview</a></li>
  <li><a href="#status">Project Status</a></li>
  <li><a href="#features">Features</a></li>
  <li><a href="#roadmap">Roadmap</a></li>
  <li><a href="#api">API</a></li>
  <li><a href="#build">Build</a></li>
  <li><a href="#license">License</a></li>
</ul>
<h2 id="overview">Overview</h2>
<p>The plugin bridges character appearance data through a lightweight ShibaBridge API. It leverages external Dalamud plugins like Penumbra and Glamourer to map incoming references to local presets. The production server that powers the API remains private to discourage large scale cloning, but the shared contract is available in the <code>ShibaBridgeAPI</code> project.</p>

<h2 id="status">Project Status</h2>
<p>This project is not a competition with Snowcloack Sync or Lightless Sync to build the best Mare clone. It primarily serves as a learning exercise for understanding Dalamud for future projects. Whether the server component will be released is still undecided, but the plugin will be kept up to date with new Dalamud versions and receive ongoing quality-of-life improvements.</p>

<h2 id="features">Features</h2>
<ul>
  <li>Synchronize outfits, dyes, accessories, and character appearance.</li>
  <li>Optional emote and animation synchronization.</li>
  <li>Opt-in sharing with group keys and built-in privacy controls.</li>
</ul>

<h2 id="roadmap">Roadmap</h2>
<ul>
  <li>Improved compression and performance.</li>
  <li>High-performance server for API communication so large communities do not require powerful hardware.</li>
  <li>Cross-server communication and synchronization, enabling multiple servers to run in parallel.</li>
  <li>Dashboard for server management with start/stop controls, admin notifications, and more.</li>
  <li>Customizable uploads such as color changes.</li>
  <li>Name colors for different roles (admin, moderator, etc.).</li>
  <li>Additional enhancements as inspiration strikes.</li>
</ul>

<h2 id="api">API</h2>
<p>Client and server communicate via REST and SignalR routes defined in <code>ShibaBridgeAPI</code>. The repository exposes data transfer objects and route constants, while the actual server implementation is intentionally not distributed.</p>
<p><strong>Note:</strong> The backend server that communicates with the API and plugin will not be released publicly. This private deployment helps prevent wide-scale copying and rehosting of the plugin.</p>

<h2 id="build">Building the Plugin</h2>
<ol>
  <li>Install the .NET SDK (version 9.0 or later) and a local Dalamud setup.</li>
  <li>Clone this repository.</li>
  <li>Execute <code>dotnet build ShibaBridge.sln</code> to build the plugin.</li>
</ol>

<h2 id="license">License</h2>
<p>This project is licensed under the <a href="./LICENSE">MIT License</a>.</p>

<h2 id="contributing">Contributing</h2>
<p>Issues and pull requests are welcome. Please open an issue to discuss new ideas or improvements.</p>

